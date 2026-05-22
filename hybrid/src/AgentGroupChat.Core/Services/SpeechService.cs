using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NAudio.Wave;
using LocalSpeechSynthesizer = System.Speech.Synthesis.SpeechSynthesizer;
using LocalSpeakCompletedEventArgs = System.Speech.Synthesis.SpeakCompletedEventArgs;

namespace AgentGroupChat.Core.Services;

public sealed class SpeechService : IDisposable
{
    public record SpeechSettings(
        bool Enabled,
        string Provider,     // "Local", "Piper", "Kokoro"
        int Rate,            // -5 to +5
        string KokoroBaseUrl,
        string KokoroModel,
        string KokoroVoice,
        string KokoroLangCode,
        double KokoroSpeed,
        string PiperExePath,
        string PiperModelsDir);

    private const int PiperMaxChunkChars = 260;
    private const int PiperMaxChunkUnits = 3;
    private static readonly TimeSpan EdgeFadeDuration = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan PiperInitialBuffer = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan PiperMaxBuffer = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    private readonly HttpClient _httpClient;
    private readonly object _lock = new();

    private Task _speechQueue = Task.CompletedTask;
    private WaveOutEvent? _kokoroWaveOut;
    private BufferedWaveProvider? _kokoroBuffer;
    private WaveFormat? _kokoroFormat;
    private Process? _piperProcess;
    private WaveOutEvent? _piperWaveOut;
    private LocalSpeechSynthesizer? _localSynth;

    public bool IsSpeaking { get; private set; }
    public bool IsPaused { get; private set; }

    public event Action? OnPlaybackStateChanged;

    public SpeechService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Queues speech for the given content. Returns a Task that completes when playback finishes.
    /// </summary>
    public Task SpeakAsync(SpeechSettings settings, string content, string voiceName, CancellationToken ct)
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(content))
            return Task.CompletedTask;

        var segments = BuildSegments(content, settings.Provider);
        if (segments.Count == 0)
            return Task.CompletedTask;

        lock (_lock)
        {
            var task = _speechQueue.ContinueWith(
                _ => SpeakSegmentsAsync(segments, voiceName, settings, ct),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
            _speechQueue = task;
            return task;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _speechQueue = Task.CompletedTask;
            _localSynth?.SpeakAsyncCancelAll();
            try { _kokoroWaveOut?.Stop(); } catch { }
            try { if (_piperProcess is { HasExited: false }) _piperProcess.Kill(true); } catch { }
            try { _piperWaveOut?.Stop(); } catch { }
            DisposeKokoroSession();
        }
        SetPlaybackState(false, false);
    }

    public bool TogglePause()
    {
        lock (_lock)
        {
            if (_kokoroWaveOut is not null && _kokoroWaveOut.PlaybackState != PlaybackState.Stopped)
            {
                if (_kokoroWaveOut.PlaybackState == PlaybackState.Playing)
                { _kokoroWaveOut.Pause(); SetPlaybackState(true, true); return true; }
                else
                { _kokoroWaveOut.Play(); SetPlaybackState(true, false); return true; }
            }
            if (_piperWaveOut is not null && _piperWaveOut.PlaybackState != PlaybackState.Stopped)
            {
                if (_piperWaveOut.PlaybackState == PlaybackState.Playing)
                { _piperWaveOut.Pause(); SetPlaybackState(true, true); return true; }
                else
                { _piperWaveOut.Play(); SetPlaybackState(true, false); return true; }
            }
            if (_localSynth is not null)
            {
                if (_localSynth.State == System.Speech.Synthesis.SynthesizerState.Speaking)
                { _localSynth.Pause(); SetPlaybackState(true, true); return true; }
                if (_localSynth.State == System.Speech.Synthesis.SynthesizerState.Paused)
                { _localSynth.Resume(); SetPlaybackState(true, false); return true; }
            }
        }
        return false;
    }

    // ─── Private pipeline ──────────────────────────────────────────

    private async Task SpeakSegmentsAsync(IReadOnlyList<(string Display, string Speech)> segments, string voiceName, SpeechSettings s, CancellationToken ct)
    {
        try
        {
            if (s.Provider.Equals("Kokoro", StringComparison.OrdinalIgnoreCase))
            { await SpeakKokoroPrefetchAsync(segments, voiceName, s, ct); return; }

            foreach (var seg in segments)
            {
                ct.ThrowIfCancellationRequested();
                await SpeakOneAsync(seg.Speech, voiceName, s, ct);
            }
        }
        finally
        {
            SetPlaybackState(false, false);
        }
    }

    private Task SpeakOneAsync(string text, string voice, SpeechSettings s, CancellationToken ct)
    {
        if (s.Provider.Equals("Kokoro", StringComparison.OrdinalIgnoreCase))
            return SpeakKokoroSingleAsync(text, voice, s, ct);
        if (s.Provider.Equals("Piper", StringComparison.OrdinalIgnoreCase))
            return SpeakPiperAsync(text, voice, s, ct);
        return SpeakLocalAsync(text, voice, s, ct);
    }

    // ─── Kokoro ────────────────────────────────────────────────────

    private async Task SpeakKokoroPrefetchAsync(IReadOnlyList<(string Display, string Speech)> segments, string voice, SpeechSettings s, CancellationToken ct)
    {
        if (segments.Count == 1)
        {
            var audio = await FetchKokoroAsync(segments[0].Speech, voice, s, ct);
            if (audio is not null) await PlayKokoroAsync(audio, ct);
            return;
        }

        var currentFetch = FetchKokoroAsync(segments[0].Speech, voice, s, ct);

        for (var i = 0; i < segments.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            Task<byte[]?>? nextFetch = null;
            if (i + 1 < segments.Count)
                nextFetch = FetchKokoroAsync(segments[i + 1].Speech, voice, s, ct);

            var audio = currentFetch is null ? null : await currentFetch;
            if (audio is null) { currentFetch = nextFetch; continue; }

            var (pcm, fmt) = DecodeWav(audio);
            ApplyEdgeFade(pcm, fmt);
            var buf = EnsureKokoroSession(fmt);
            buf.AddSamples(pcm, 0, pcm.Length);
            EnsureKokoroPlaying();
            SetPlaybackState(true, false);

            if (nextFetch is not null)
            {
                // Wait until current segment nearly drained AND next is fetched
                while (!nextFetch.IsCompleted || buf.BufferedDuration > TimeSpan.FromMilliseconds(50))
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(PollInterval, ct);
                }
                currentFetch = nextFetch;
            }
            else
            {
                await WaitForDrainAsync(buf, ct);
            }
        }
    }

    private async Task SpeakKokoroSingleAsync(string text, string voice, SpeechSettings s, CancellationToken ct)
    {
        var audio = await FetchKokoroAsync(text, voice, s, ct);
        if (audio is not null) await PlayKokoroAsync(audio, ct);
    }

    private async Task<byte[]?> FetchKokoroAsync(string text, string voice, SpeechSettings s, CancellationToken ct)
    {
        try
        {
            var desiredVoice = string.IsNullOrWhiteSpace(voice) ? s.KokoroVoice : voice.Trim();
            if (string.IsNullOrWhiteSpace(desiredVoice)) desiredVoice = "af_heart";

            var langCode = ResolveLangCode(s, desiredVoice);
            var speed = Math.Clamp((double.IsFinite(s.KokoroSpeed) ? s.KokoroSpeed : 1.0) + s.Rate * 0.1, 0.5, 2.0);
            var endpoint = s.KokoroBaseUrl.TrimEnd('/') + "/v1/audio/speech";

            var payload = JsonSerializer.Serialize(new
            {
                model = string.IsNullOrWhiteSpace(s.KokoroModel) ? "kokoro" : s.KokoroModel,
                input = text,
                voice = desiredVoice,
                response_format = "wav",
                speed,
                lang_code = langCode,
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var body = await response.Content.ReadAsByteArrayAsync(ct);

            if (!response.IsSuccessStatusCode) return null;
            return NormalizeWav(body);
        }
        catch (OperationCanceledException) { return null; }
        catch { return null; }
    }

    private async Task PlayKokoroAsync(byte[] wavData, CancellationToken ct)
    {
        try
        {
            var (pcm, fmt) = DecodeWav(wavData);
            ApplyEdgeFade(pcm, fmt);
            var buf = EnsureKokoroSession(fmt);
            buf.AddSamples(pcm, 0, pcm.Length);
            SetPlaybackState(true, false);
            EnsureKokoroPlaying();
            await WaitForDrainAsync(buf, ct);
        }
        catch (OperationCanceledException) { }
        catch { }
        finally { SetPlaybackState(false, false); }
    }

    private BufferedWaveProvider EnsureKokoroSession(WaveFormat fmt)
    {
        lock (_lock)
        {
            if (_kokoroBuffer is not null && (_kokoroFormat is null || !FmtEqual(_kokoroFormat, fmt)))
                DisposeKokoroSession();

            if (_kokoroBuffer is null)
            {
                _kokoroBuffer = new BufferedWaveProvider(fmt) { BufferDuration = TimeSpan.FromSeconds(30), DiscardOnBufferOverflow = false, ReadFully = true };
                _kokoroWaveOut = new WaveOutEvent();
                _kokoroWaveOut.Init(_kokoroBuffer);
                _kokoroFormat = fmt;
            }
            return _kokoroBuffer;
        }
    }

    private void EnsureKokoroPlaying()
    {
        lock (_lock)
        {
            if (_kokoroWaveOut?.PlaybackState != PlaybackState.Playing)
                _kokoroWaveOut?.Play();
        }
    }

    private void DisposeKokoroSession()
    {
        try { _kokoroWaveOut?.Stop(); } catch { }
        _kokoroWaveOut?.Dispose();
        _kokoroWaveOut = null;
        _kokoroBuffer = null;
        _kokoroFormat = null;
    }

    // ─── Piper ─────────────────────────────────────────────────────

    private async Task SpeakPiperAsync(string text, string voice, SpeechSettings s, CancellationToken ct)
    {
        Process? proc = null;
        WaveOutEvent? waveOut = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            var exePath = s.PiperExePath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return;

            var modelPath = ResolvePiperModel(s.PiperModelsDir, voice);
            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath)) return;

            var sampleRate = ReadPiperSampleRate(modelPath);
            var lengthScale = Math.Clamp(1.0 - s.Rate * 0.08, 0.6, 1.4);

            var psi = new ProcessStartInfo
            {
                FileName = exePath, UseShellExecute = false,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exePath) ?? ".",
            };
            psi.ArgumentList.Add("-m"); psi.ArgumentList.Add(modelPath);
            psi.ArgumentList.Add("--output_raw");
            psi.ArgumentList.Add("--length_scale"); psi.ArgumentList.Add(lengthScale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--sentence_silence"); psi.ArgumentList.Add("0.24");

            proc = new Process { StartInfo = psi };
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var buf = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 1))
            { BufferDuration = TimeSpan.FromSeconds(30), DiscardOnBufferOverflow = false, ReadFully = true };

            waveOut = new WaveOutEvent();
            waveOut.Init(buf);
            lock (_lock) { _piperProcess = proc; _piperWaveOut = waveOut; }

            proc.Start();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            var streamTask = StreamPiperAsync(proc.StandardOutput.BaseStream, buf, ready, ct);

            await proc.StandardInput.WriteAsync(text);
            await proc.StandardInput.FlushAsync();
            proc.StandardInput.Close();

            if (await ready.Task)
            { SetPlaybackState(true, false); waveOut.Play(); }

            await proc.WaitForExitAsync(ct);
            await streamTask;
            await WaitForDrainAsync(buf, ct);
            if (waveOut.PlaybackState != PlaybackState.Stopped) waveOut.Stop();
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            lock (_lock) { if (ReferenceEquals(_piperWaveOut, waveOut)) _piperWaveOut = null; _piperProcess = null; }
            SetPlaybackState(false, false);
            waveOut?.Dispose(); proc?.Dispose();
        }
    }

    private static async Task StreamPiperAsync(Stream src, BufferedWaveProvider buf, TaskCompletionSource<bool> ready, CancellationToken ct)
    {
        var b = new byte[8192]; var got = false;
        try
        {
            while (true)
            {
                while (buf.BufferedDuration >= PiperMaxBuffer)
                { if (got && !ready.Task.IsCompleted) ready.TrySetResult(true); await Task.Delay(PollInterval, ct); }
                var n = await src.ReadAsync(b.AsMemory(0, b.Length), ct);
                if (n <= 0) break;
                buf.AddSamples(b, 0, n);
                if (!got) got = true;
                if (got && buf.BufferedDuration >= PiperInitialBuffer && !ready.Task.IsCompleted) ready.TrySetResult(true);
            }
        }
        finally { ready.TrySetResult(got); }
    }

    // ─── Local (System.Speech) ─────────────────────────────────────

    private async Task SpeakLocalAsync(string text, string voice, SpeechSettings s, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var synth = GetLocalSynth();
            synth.Rate = s.Rate;
            if (!string.IsNullOrWhiteSpace(voice))
            {
                try { synth.SelectVoice(voice); } catch { }
            }

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<LocalSpeakCompletedEventArgs>? handler = null;
            handler = (_, args) =>
            {
                if (handler is not null) synth.SpeakCompleted -= handler;
                if (args.Cancelled) tcs.TrySetCanceled(ct);
                else if (args.Error is not null) tcs.TrySetException(args.Error);
                else tcs.TrySetResult();
            };

            using var reg = ct.Register(Stop);
            lock (_lock) { synth.SpeakCompleted += handler; SetPlaybackState(true, false); synth.SpeakAsync(text); }
            await tcs.Task;
        }
        catch (OperationCanceledException) { }
        catch { }
        finally { SetPlaybackState(false, false); }
    }

    private LocalSpeechSynthesizer GetLocalSynth()
    {
        lock (_lock) { _localSynth ??= new LocalSpeechSynthesizer(); return _localSynth; }
    }

    // ─── WAV helpers ───────────────────────────────────────────────

    private static byte[] NormalizeWav(byte[] data)
    {
        if (data.Length < 12) return data;
        if (data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F') return data;
        byte[]? norm = null;
        if (BitConverter.ToUInt32(data, 4) == uint.MaxValue)
        { norm ??= (byte[])data.Clone(); BitConverter.GetBytes((uint)Math.Max(0, data.Length - 8)).CopyTo(norm, 4); }
        var di = FindChunk(data, "data");
        if (di >= 0 && di + 8 <= data.Length && BitConverter.ToUInt32(data, di + 4) == uint.MaxValue)
        { norm ??= (byte[])data.Clone(); BitConverter.GetBytes((uint)Math.Max(0, data.Length - di - 8)).CopyTo(norm, di + 4); }
        return norm ?? data;
    }

    private static int FindChunk(byte[] data, string id)
    {
        for (var i = 0; i <= data.Length - id.Length; i++)
        {
            var match = true;
            for (var j = 0; j < id.Length; j++) { if (data[i + j] != id[j]) { match = false; break; } }
            if (match) return i;
        }
        return -1;
    }

    private static (byte[] pcm, WaveFormat fmt) DecodeWav(byte[] data)
    {
        using var ms = new MemoryStream(data, false);
        using var reader = new WaveFileReader(ms);
        using var pcmMs = new MemoryStream();
        var buf = new byte[8192]; int n;
        while ((n = reader.Read(buf, 0, buf.Length)) > 0) pcmMs.Write(buf, 0, n);
        return (pcmMs.ToArray(), reader.WaveFormat);
    }

    private static void ApplyEdgeFade(byte[] pcm, WaveFormat fmt)
    {
        if (pcm.Length == 0 || fmt.BlockAlign <= 0 || fmt.SampleRate <= 0) return;
        var totalFrames = pcm.Length / fmt.BlockAlign;
        if (totalFrames < 8) return;
        var fadeFrames = Math.Clamp((int)Math.Round(fmt.SampleRate * EdgeFadeDuration.TotalSeconds), 1, Math.Max(1, totalFrames / 2));

        if (fmt.Encoding == WaveFormatEncoding.Pcm && fmt.BitsPerSample == 16)
        {
            for (var f = 0; f < fadeFrames; f++)
            {
                var gi = (f + 1.0) / fadeFrames;
                var go = (fadeFrames - f - 1.0) / fadeFrames;
                for (var ch = 0; ch < fmt.Channels; ch++)
                {
                    FadePcm16(pcm, f * fmt.BlockAlign + ch * 2, gi);
                    FadePcm16(pcm, (totalFrames - fadeFrames + f) * fmt.BlockAlign + ch * 2, go);
                }
            }
        }
        else if (fmt.Encoding == WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample == 32)
        {
            for (var f = 0; f < fadeFrames; f++)
            {
                var gi = (float)((f + 1.0) / fadeFrames);
                var go = (float)((fadeFrames - f - 1.0) / fadeFrames);
                for (var ch = 0; ch < fmt.Channels; ch++)
                {
                    FadeFloat32(pcm, f * fmt.BlockAlign + ch * 4, gi);
                    FadeFloat32(pcm, (totalFrames - fadeFrames + f) * fmt.BlockAlign + ch * 4, go);
                }
            }
        }
    }

    private static void FadePcm16(byte[] buf, int off, double gain)
    {
        var s = BitConverter.ToInt16(buf, off);
        BitConverter.GetBytes((short)Math.Clamp((int)Math.Round(s * gain), short.MinValue, short.MaxValue)).CopyTo(buf, off);
    }

    private static void FadeFloat32(byte[] buf, int off, float gain)
    {
        BitConverter.GetBytes(BitConverter.ToSingle(buf, off) * gain).CopyTo(buf, off);
    }

    private static bool FmtEqual(WaveFormat a, WaveFormat b) =>
        a.Encoding == b.Encoding && a.SampleRate == b.SampleRate && a.Channels == b.Channels
        && a.BitsPerSample == b.BitsPerSample && a.BlockAlign == b.BlockAlign;

    private static async Task WaitForDrainAsync(BufferedWaveProvider buf, CancellationToken ct)
    {
        while (buf.BufferedBytes > 0) { await Task.Delay(PollInterval, ct); }
    }

    // ─── Piper model helpers ───────────────────────────────────────

    private static string? ResolvePiperModel(string modelsDir, string voice)
    {
        if (string.IsNullOrWhiteSpace(modelsDir) || !Directory.Exists(modelsDir)) return null;
        var name = string.IsNullOrWhiteSpace(voice) ? null : voice.Trim();
        if (name is not null)
        {
            var exact = Path.Combine(modelsDir, name.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) ? name : name + ".onnx");
            if (File.Exists(exact)) return exact;
        }
        return Directory.EnumerateFiles(modelsDir, "*.onnx").FirstOrDefault();
    }

    private static int ReadPiperSampleRate(string modelPath)
    {
        var jsonPath = modelPath + ".json";
        if (!File.Exists(jsonPath)) return 22050;
        try
        {
            var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (doc.RootElement.TryGetProperty("audio", out var audio) && audio.TryGetProperty("sample_rate", out var sr))
                return sr.GetInt32();
        }
        catch { }
        return 22050;
    }

    // ─── Speech text segmentation ──────────────────────────────────

    private static IReadOnlyList<(string Display, string Speech)> BuildSegments(string content, string provider)
    {
        if (provider.Equals("Kokoro", StringComparison.OrdinalIgnoreCase))
            return BuildKokoroSegments(content);
        if (provider.Equals("Piper", StringComparison.OrdinalIgnoreCase))
        {
            var norm = NormalizePiper(StripFormatting(content));
            if (string.IsNullOrWhiteSpace(norm)) return [];
            return BuildPiperChunks(norm).Select(c => (c, c)).ToArray();
        }
        var std = NormalizeStandard(StripFormatting(content));
        if (string.IsNullOrWhiteSpace(std)) return [];
        return [(content.Trim(), std)];
    }

    private static IReadOnlyList<(string Display, string Speech)> BuildKokoroSegments(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return [];
        var segs = SplitSentences(content)
            .Select(s => (Display: s, Speech: NormalizeStandard(StripFormatting(s))))
            .Where(s => !string.IsNullOrWhiteSpace(s.Speech))
            .ToList();
        if (segs.Count > 0) return segs;
        var fallback = NormalizeStandard(StripFormatting(content));
        return string.IsNullOrWhiteSpace(fallback) ? [] : [(content.Trim(), fallback)];
    }

    private static string StripFormatting(string s) => Regex.Replace(s, @"(?<!\S)\*+(?=\S)|(?<=\S)\*+(?!\S)", "").Replace("*", "");
    private static string NormalizeStandard(string s) => Regex.Replace(s, @"[\r\n\t]+", " ").Trim();
    private static string NormalizePiper(string s)
    {
        var n = s.Replace("\r\n", "\n").Replace('\r', '\n');
        n = Regex.Replace(n, @"\n{2,}", ". ");
        n = Regex.Replace(n, @"\n+", ". ");
        n = Regex.Replace(n, @"\s{2,}", " ");
        return n.Trim();
    }

    private static IReadOnlyList<string> SplitSentences(string content)
    {
        var result = new List<string>(); var start = 0;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] is not ('.' or '!' or '?')) continue;
            var end = i + 1;
            while (end < content.Length && content[end] is '.' or '!' or '?') end++;
            while (end < content.Length && "\"')]}".Contains(content[end])) end++;
            if (end < content.Length && !char.IsWhiteSpace(content[end])) continue;
            var s = content[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
            start = end;
        }
        if (start < content.Length) { var t = content[start..].Trim(); if (!string.IsNullOrWhiteSpace(t)) result.Add(t); }
        return result;
    }

    private static IReadOnlyList<string> BuildPiperChunks(string content)
    {
        var units = Regex.Split(content, @"(?<=[.!?])\s+").Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        var chunks = new List<string>(); var sb = new StringBuilder(); var uc = 0;
        foreach (var unit in units)
        {
            var t = unit.Trim();
            if (sb.Length > 0 && (sb.Length + 1 + t.Length > PiperMaxChunkChars || uc >= PiperMaxChunkUnits))
            { chunks.Add(sb.ToString().Trim()); sb.Clear(); uc = 0; }
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(t); uc++;
        }
        if (sb.Length > 0) chunks.Add(sb.ToString().Trim());
        return chunks;
    }

    private static string ResolveLangCode(SpeechSettings s, string voice)
    {
        if (!string.IsNullOrWhiteSpace(voice))
        {
            var sep = voice.IndexOf('_');
            if (sep > 0) return voice[..1].ToLowerInvariant();
        }
        return string.IsNullOrWhiteSpace(s.KokoroLangCode) ? "a" : s.KokoroLangCode.Trim().ToLowerInvariant();
    }

    private void SetPlaybackState(bool active, bool paused)
    {
        IsSpeaking = active; IsPaused = paused;
        OnPlaybackStateChanged?.Invoke();
    }

    public void Dispose()
    {
        Stop();
        _localSynth?.Dispose();
        _localSynth = null;
    }
}
