using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using NAudio.Wave;
using LocalSpeechSynthesizer = System.Speech.Synthesis.SpeechSynthesizer;
using LocalSpeakCompletedEventArgs = System.Speech.Synthesis.SpeakCompletedEventArgs;

namespace AgentGroupChat;

public partial class MainWindow
{
    private sealed record SpeechSegment(string DisplayText, string SpeechText);
    private sealed record KokoroSpeechAudio(
        string BaseUrl,
        int CharacterCount,
        byte[] RawResponseBody,
        byte[] NormalizedResponseBody,
        WaveFormat WaveFormat,
        byte[] PlaybackBytes,
        TimeSpan PlaybackDuration,
        Stopwatch TotalStopwatch,
        long HeadersReceivedMilliseconds,
        long BodyReadMilliseconds,
        long AudioReadyMilliseconds);

    private Task QueueSpeech(string voiceName, string content, CancellationToken cancellationToken)
        => QueueSpeech(null, voiceName, content, cancellationToken);

    private Task QueueSpeech(ChatMessage? message, string voiceName, string content, CancellationToken cancellationToken)
    {
        if (!TextToSpeechEnabled)
        {
            return Task.CompletedTask;
        }

        var provider = TextToSpeechProviderSelection;

        var spokenSegments = BuildSpeechSegments(content, provider);
        if (spokenSegments.Count == 0)
        {
            return Task.CompletedTask;
        }

        lock (_speechLock)
        {
            var speechTask = _speechQueue.ContinueWith(
                _ => SpeakTextSegmentsAsync(spokenSegments, message, voiceName, provider, cancellationToken),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();

            _speechQueue = speechTask;
            return speechTask;
        }
    }

    private async Task NarrateUserMessageAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        if (!TextToSpeechEnabled)
        {
            return;
        }

        var priorStatus = StatusTextBlock.Text;
        try
        {
            StatusTextBlock.Text = $"{message.Author} speaking";
            await QueueSpeech(message, ResolveTextToSpeechVoiceForMessage(message), message.Content, cancellationToken);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested && string.Equals(StatusTextBlock.Text, $"{message.Author} speaking", StringComparison.Ordinal))
            {
                StatusTextBlock.Text = priorStatus;
            }
        }
    }

    private async Task ReplayTranscriptFromMessageAsync(ChatMessage startMessage)
    {
        var startIndex = Messages.IndexOf(startMessage);
        if (startIndex < 0)
        {
            return;
        }

        CancelTranscriptReplay();
        StopSpeaking();

        if (_isRunning)
        {
            AddSystemMessage("Stopping the current run to replay narration from the selected message.");
            _runCancellationTokenSource?.Cancel();
            if (_activeRunTask is not null)
            {
                try
                {
                    await _activeRunTask;
                }
                catch
                {
                }
            }
        }

        var replayMessages = Messages
            .Skip(startIndex)
            .Where(message => !string.IsNullOrWhiteSpace(message.Content) && !string.Equals(message.Content, "Thinking...", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (replayMessages.Count == 0)
        {
            AddSystemMessage("No finished transcript messages were available to replay from that point.");
            return;
        }

        var replayCancellationTokenSource = new CancellationTokenSource();
        _transcriptReplayCancellationTokenSource = replayCancellationTokenSource;
        var cancellationToken = replayCancellationTokenSource.Token;
        var priorStatus = StatusTextBlock.Text;

        try
        {
            AddSystemMessage($"Replaying narration from {replayMessages[0].Author} at {replayMessages[0].TimestampText}.");

            foreach (var message in replayMessages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                StatusTextBlock.Text = $"{message.Author} speaking";
                await QueueSpeech(message, ResolveTextToSpeechVoiceForMessage(message), message.Content, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_transcriptReplayCancellationTokenSource, replayCancellationTokenSource))
            {
                _transcriptReplayCancellationTokenSource.Dispose();
                _transcriptReplayCancellationTokenSource = null;
            }
            else
            {
                replayCancellationTokenSource.Dispose();
            }

            if (!_isRunning)
            {
                StatusTextBlock.Text = string.Equals(priorStatus, "Stopping", StringComparison.Ordinal)
                    ? "Ready"
                    : priorStatus;
            }
        }
    }

    private string ResolveTextToSpeechVoiceForMessage(ChatMessage message)
    {
        if (string.Equals(message.Author, "You", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return SelectedTheme?.Agents
            .FirstOrDefault(agent => string.Equals(agent.Name, message.Author, StringComparison.OrdinalIgnoreCase))
            ?.TextToSpeechVoice
            ?? string.Empty;
    }

    private async Task SpeakTextSegmentsAsync(IReadOnlyList<SpeechSegment> segments, ChatMessage? message, string voiceName, string provider, CancellationToken cancellationToken)
    {
        try
        {
            if (string.Equals(provider, "Kokoro", StringComparison.OrdinalIgnoreCase))
            {
                await SpeakWithPrefetchedKokoroSegmentsAsync(segments, message, voiceName, cancellationToken);
                return;
            }

            foreach (var segment in segments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await SetSpeechHighlightAsync(message, segment.DisplayText, cancellationToken);
                await SpeakTextAsync(segment.SpeechText, voiceName, provider, cancellationToken);
            }
        }
        finally
        {
            await ClearSpeechHighlightAsync(message);
        }
    }

    private async Task SpeakWithPrefetchedKokoroSegmentsAsync(IReadOnlyList<SpeechSegment> segments, ChatMessage? message, string voiceName, CancellationToken cancellationToken)
    {
        if (segments.Count == 0)
        {
            return;
        }

        if (segments.Count == 1)
        {
            var singleAudio = await FetchKokoroSpeechAudioAsync(segments[0].SpeechText, voiceName, cancellationToken);
            if (singleAudio is not null)
            {
                await SetSpeechHighlightAsync(message, segments[0].DisplayText, cancellationToken);
                await PlayKokoroSpeechAudioAsync(singleAudio, cancellationToken);
            }

            return;
        }

        Task<KokoroSpeechAudio?>? currentAudioTask = FetchKokoroSpeechAudioAsync(segments[0].SpeechText, voiceName, cancellationToken);
        var transitionLeadDuration = TimeSpan.FromMilliseconds(50);

        try
        {
            for (var index = 0; index < segments.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Task<KokoroSpeechAudio?>? nextAudioTask = null;
                if (index + 1 < segments.Count)
                {
                    nextAudioTask = FetchKokoroSpeechAudioAsync(segments[index + 1].SpeechText, voiceName, cancellationToken);
                }

                var currentAudio = currentAudioTask is null
                    ? null
                    : await currentAudioTask;
                if (currentAudio is null)
                {
                    currentAudioTask = nextAudioTask;
                    continue;
                }

                var bufferedWaveProvider = EnsureKokoroPlaybackSession(currentAudio.WaveFormat);
                await SetSpeechHighlightAsync(message, segments[index].DisplayText, cancellationToken);
                bufferedWaveProvider.AddSamples(currentAudio.PlaybackBytes, 0, currentAudio.PlaybackBytes.Length);

                var playbackStartedMilliseconds = currentAudio.TotalStopwatch.ElapsedMilliseconds;
                EnsureKokoroPlaybackRunning();
                UpdateSpeechPlaybackState(isActive: true, isPaused: false);

                if (nextAudioTask is not null)
                {
                    currentAudioTask = WaitForKokoroSegmentBoundaryAndFetchNextAsync(
                        bufferedWaveProvider,
                        nextAudioTask,
                        transitionLeadDuration,
                        cancellationToken);

                    if (currentAudio.TotalStopwatch.IsRunning)
                    {
                        currentAudio.TotalStopwatch.Stop();
                    }

                    AddLog(
                        LogCategory.Speech,
                        "TTS.Kokoro",
                        $"Kokoro completed in {currentAudio.TotalStopwatch.ElapsedMilliseconds} ms.",
                        BuildKokoroTimingDetail(
                            currentAudio.BaseUrl,
                            currentAudio.CharacterCount,
                            currentAudio.RawResponseBody.Length,
                            currentAudio.NormalizedResponseBody.Length,
                            currentAudio.HeadersReceivedMilliseconds,
                            currentAudio.BodyReadMilliseconds,
                            currentAudio.AudioReadyMilliseconds,
                            playbackStartedMilliseconds,
                            currentAudio.TotalStopwatch.ElapsedMilliseconds),
                        currentAudio.TotalStopwatch.ElapsedMilliseconds);

                    continue;
                }

                await WaitForPiperBufferToDrainAsync(bufferedWaveProvider, cancellationToken);

                if (currentAudio.TotalStopwatch.IsRunning)
                {
                    currentAudio.TotalStopwatch.Stop();
                }

                AddLog(
                    LogCategory.Speech,
                    "TTS.Kokoro",
                    $"Kokoro completed in {currentAudio.TotalStopwatch.ElapsedMilliseconds} ms.",
                    BuildKokoroTimingDetail(
                        currentAudio.BaseUrl,
                        currentAudio.CharacterCount,
                        currentAudio.RawResponseBody.Length,
                        currentAudio.NormalizedResponseBody.Length,
                        currentAudio.HeadersReceivedMilliseconds,
                        currentAudio.BodyReadMilliseconds,
                        currentAudio.AudioReadyMilliseconds,
                        playbackStartedMilliseconds,
                        currentAudio.TotalStopwatch.ElapsedMilliseconds),
                    currentAudio.TotalStopwatch.ElapsedMilliseconds);
            }
        }
        finally
        {
            UpdateSpeechPlaybackState(isActive: false, isPaused: false);
        }
    }

    private static async Task<KokoroSpeechAudio?> WaitForKokoroSegmentBoundaryAndFetchNextAsync(
        BufferedWaveProvider bufferedWaveProvider,
        Task<KokoroSpeechAudio?> nextAudioTask,
        TimeSpan transitionLeadDuration,
        CancellationToken cancellationToken)
    {
        while (!nextAudioTask.IsCompleted || bufferedWaveProvider.BufferedDuration > transitionLeadDuration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(PiperBufferPollingInterval, cancellationToken);
        }

        return await nextAudioTask;
    }

    private Task SetSpeechHighlightAsync(ChatMessage? message, string segmentText, CancellationToken cancellationToken)
    {
        if (message is null)
        {
            return Task.CompletedTask;
        }

        return Dispatcher.InvokeAsync(() =>
        {
            if (_activeSpeechMessage is not null && !ReferenceEquals(_activeSpeechMessage, message))
            {
                _activeSpeechMessage.ClearSpeechHighlight();
            }

            _activeSpeechMessage = message;
            message.SetSpeechHighlight(segmentText);
            FocusTranscriptMessage(message);
        }, DispatcherPriority.Background, cancellationToken).Task;
    }

    private Task ClearSpeechHighlightAsync(ChatMessage? message)
    {
        if (message is null)
        {
            return Task.CompletedTask;
        }

        return Dispatcher.InvokeAsync(() =>
        {
            message.ClearSpeechHighlight();
            if (ReferenceEquals(_activeSpeechMessage, message))
            {
                _activeSpeechMessage = null;
            }
        }, DispatcherPriority.Background).Task;
    }

    private void ClearActiveSpeechHighlight()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ClearActiveSpeechHighlight, DispatcherPriority.Background);
            return;
        }

        _activeSpeechMessage?.ClearSpeechHighlight();
        _activeSpeechMessage = null;
    }

    private async Task SpeakTextAsync(string content, string voiceName, string provider, CancellationToken cancellationToken)
    {
        if (string.Equals(provider, "Kokoro", StringComparison.OrdinalIgnoreCase))
        {
            await SpeakWithKokoroAsync(content, voiceName, cancellationToken);
            return;
        }

        if (string.Equals(provider, "Piper", StringComparison.OrdinalIgnoreCase))
        {
            await SpeakWithPiperAsync(content, voiceName, cancellationToken);
            return;
        }

        await SpeakWithLocalSynthesizerAsync(content, voiceName, cancellationToken);
    }

    private async Task SpeakWithKokoroAsync(string content, string voiceName, CancellationToken cancellationToken)
    {
        var audio = await FetchKokoroSpeechAudioAsync(content, voiceName, cancellationToken);
        if (audio is null)
        {
            return;
        }

        await PlayKokoroSpeechAudioAsync(audio, cancellationToken);
    }

    private async Task<KokoroSpeechAudio?> FetchKokoroSpeechAudioAsync(string content, string voiceName, CancellationToken cancellationToken)
    {
        const string logSource = "TTS.Kokoro";
        var totalStopwatch = Stopwatch.StartNew();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var settings = _connectionSettingsStore.Load();
            if (!TryCreateKokoroSpeechRequest(settings, content, voiceName, out var request, out var error))
            {
                totalStopwatch.Stop();
                AddLog(LogCategory.Speech, logSource, $"Kokoro unavailable: {error}");
                Debug.WriteLine($"Kokoro TTS unavailable: {error}");
                return null;
            }

            AddLog(
                LogCategory.Speech,
                logSource,
                $"Synthesizing {content.Length} characters with Kokoro.",
                $"Endpoint: {BuildKokoroSpeechEndpoint(settings.KokoroBaseUrl)}\nProvider: Kokoro");

            using var ownedRequest = request;
            using var registration = cancellationToken.Register(StopSpeaking);
            using var response = await _httpClient.SendAsync(ownedRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var headersReceivedMilliseconds = totalStopwatch.ElapsedMilliseconds;
            var responseBody = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var bodyReadMilliseconds = totalStopwatch.ElapsedMilliseconds;

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = TryDecodeUtf8(responseBody);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorBody)
                    ? $"Kokoro speech request failed ({(int)response.StatusCode})."
                    : $"Kokoro speech request failed ({(int)response.StatusCode}): {errorBody}");
            }

            var normalizedResponseBody = NormalizeKokoroWaveResponse(responseBody);
            var playbackBytes = ReadWavePayloadBytes(normalizedResponseBody, out var waveFormat, out var playbackDuration);
            ApplyKokoroSegmentEdgeFade(playbackBytes, waveFormat);
            var audioReadyMilliseconds = totalStopwatch.ElapsedMilliseconds;

            return new KokoroSpeechAudio(
                settings.KokoroBaseUrl,
                content.Length,
                responseBody,
                normalizedResponseBody,
                waveFormat,
                playbackBytes,
                playbackDuration,
                totalStopwatch,
                headersReceivedMilliseconds,
                bodyReadMilliseconds,
                audioReadyMilliseconds);
        }
        catch (OperationCanceledException)
        {
            totalStopwatch.Stop();
            AddLog(
                LogCategory.Speech,
                logSource,
                $"Kokoro canceled after {totalStopwatch.ElapsedMilliseconds} ms.",
                $"Characters: {content.Length}\nElapsed: {totalStopwatch.ElapsedMilliseconds} ms",
                totalStopwatch.ElapsedMilliseconds);
            return null;
        }
        catch (Exception exception)
        {
            totalStopwatch.Stop();
            AddLog(
                LogCategory.Speech,
                logSource,
                $"Kokoro failed after {totalStopwatch.ElapsedMilliseconds} ms.",
                $"Characters: {content.Length}\nElapsed: {totalStopwatch.ElapsedMilliseconds} ms",
                totalStopwatch.ElapsedMilliseconds);
            AddLog(LogCategory.Speech, logSource, $"Kokoro speech failed: {exception.Message}");
            Debug.WriteLine($"Kokoro TTS error: {exception.Message}");
            return null;
        }
    }

    private async Task PlayKokoroSpeechAudioAsync(KokoroSpeechAudio audio, CancellationToken cancellationToken)
    {
        const string logSource = "TTS.Kokoro";

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bufferedWaveProvider = EnsureKokoroPlaybackSession(audio.WaveFormat);
            bufferedWaveProvider.AddSamples(audio.PlaybackBytes, 0, audio.PlaybackBytes.Length);

            UpdateSpeechPlaybackState(isActive: true, isPaused: false);
            EnsureKokoroPlaybackRunning();
            var playbackStartedMilliseconds = audio.TotalStopwatch.ElapsedMilliseconds;
            await WaitForPiperBufferToDrainAsync(bufferedWaveProvider, cancellationToken);

            audio.TotalStopwatch.Stop();
            AddLog(
                LogCategory.Speech,
                logSource,
                $"Kokoro completed in {audio.TotalStopwatch.ElapsedMilliseconds} ms.",
                BuildKokoroTimingDetail(
                    audio.BaseUrl,
                    audio.CharacterCount,
                    audio.RawResponseBody.Length,
                    audio.NormalizedResponseBody.Length,
                    audio.HeadersReceivedMilliseconds,
                    audio.BodyReadMilliseconds,
                    audio.AudioReadyMilliseconds,
                    playbackStartedMilliseconds,
                    audio.TotalStopwatch.ElapsedMilliseconds),
                audio.TotalStopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            if (audio.TotalStopwatch.IsRunning)
            {
                audio.TotalStopwatch.Stop();
            }

            AddLog(
                LogCategory.Speech,
                logSource,
                $"Kokoro canceled after {audio.TotalStopwatch.ElapsedMilliseconds} ms.",
                $"Characters: {audio.CharacterCount}\nElapsed: {audio.TotalStopwatch.ElapsedMilliseconds} ms",
                audio.TotalStopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            if (audio.TotalStopwatch.IsRunning)
            {
                audio.TotalStopwatch.Stop();
            }

            AddLog(
                LogCategory.Speech,
                logSource,
                $"Kokoro failed after {audio.TotalStopwatch.ElapsedMilliseconds} ms.",
                $"Characters: {audio.CharacterCount}\nElapsed: {audio.TotalStopwatch.ElapsedMilliseconds} ms",
                audio.TotalStopwatch.ElapsedMilliseconds);
            AddLog(LogCategory.Speech, logSource, $"Kokoro speech failed: {exception.Message}");
            Debug.WriteLine($"Kokoro TTS error: {exception.Message}");
        }
        finally
        {
            UpdateSpeechPlaybackState(isActive: false, isPaused: false);
        }
    }

    private BufferedWaveProvider EnsureKokoroPlaybackSession(WaveFormat waveFormat)
    {
        lock (_speechLock)
        {
            if (_kokoroBufferedWaveProvider is not null)
            {
                if (_kokoroWaveFormat is null || !AreEquivalentWaveFormats(_kokoroWaveFormat, waveFormat))
                {
                    DisposeKokoroPlaybackSession();
                }
            }

            if (_kokoroBufferedWaveProvider is null)
            {
                _kokoroBufferedWaveProvider = new BufferedWaveProvider(waveFormat)
                {
                    BufferDuration = TimeSpan.FromSeconds(30),
                    DiscardOnBufferOverflow = false,
                    ReadFully = true,
                };

                _kokoroWaveOut = new WaveOutEvent();
                _kokoroWaveOut.Init(_kokoroBufferedWaveProvider);
                _kokoroWaveFormat = waveFormat;
            }

            return _kokoroBufferedWaveProvider;
        }
    }

    private void EnsureKokoroPlaybackRunning()
    {
        lock (_speechLock)
        {
            if (_kokoroWaveOut is null)
            {
                return;
            }

            if (_kokoroWaveOut.PlaybackState != PlaybackState.Playing)
            {
                _kokoroWaveOut.Play();
            }
        }
    }

    private void DisposeKokoroPlaybackSession()
    {
        try
        {
            _kokoroWaveOut?.Stop();
        }
        catch
        {
        }

        _kokoroWaveOut?.Dispose();
        _kokoroWaveOut = null;
        _kokoroBufferedWaveProvider = null;
        _kokoroWaveFormat = null;
    }

    private static string BuildKokoroTimingDetail(
        string baseUrl,
        int characterCount,
        int rawResponseBytes,
        int normalizedResponseBytes,
        long headersReceivedMilliseconds,
        long bodyReadMilliseconds,
        long audioReadyMilliseconds,
        long playbackStartedMilliseconds,
        long totalElapsedMilliseconds)
    {
        var synthesisMilliseconds = headersReceivedMilliseconds;
        var downloadMilliseconds = Math.Max(0, bodyReadMilliseconds - headersReceivedMilliseconds);
        var preparationMilliseconds = Math.Max(0, audioReadyMilliseconds - bodyReadMilliseconds);
        var waitBeforePlaybackMilliseconds = Math.Max(0, playbackStartedMilliseconds - audioReadyMilliseconds);
        var playbackMilliseconds = Math.Max(0, totalElapsedMilliseconds - playbackStartedMilliseconds);

        return string.Join(
            Environment.NewLine,
            $"Endpoint: {BuildKokoroSpeechEndpoint(baseUrl)}",
            $"Characters: {characterCount}",
            $"Raw audio bytes: {rawResponseBytes}",
            $"Normalized audio bytes: {normalizedResponseBytes}",
            $"Headers received: {synthesisMilliseconds} ms",
            $"Body read: {downloadMilliseconds} ms",
            $"Audio prepared: {preparationMilliseconds} ms",
            $"Wait before playback: {waitBeforePlaybackMilliseconds} ms",
            $"Playback duration: {playbackMilliseconds} ms");
    }

    private static byte[] ReadWavePayloadBytes(byte[] normalizedResponseBody, out WaveFormat waveFormat, out TimeSpan playbackDuration)
    {
        using var audioStream = new MemoryStream(normalizedResponseBody, writable: false);
        using var waveReader = new WaveFileReader(audioStream);
        using var pcmStream = new MemoryStream();
        var buffer = new byte[8192];
        int bytesRead;

        while ((bytesRead = waveReader.Read(buffer, 0, buffer.Length)) > 0)
        {
            pcmStream.Write(buffer, 0, bytesRead);
        }

        waveFormat = waveReader.WaveFormat;
        playbackDuration = waveReader.TotalTime;
        return pcmStream.ToArray();
    }

    private static void ApplyKokoroSegmentEdgeFade(byte[] playbackBytes, WaveFormat waveFormat)
    {
        if (playbackBytes.Length == 0)
        {
            return;
        }

        if (!TryGetKokoroFadeFrameCount(playbackBytes, waveFormat, out var fadeFrameCount))
        {
            return;
        }

        if (waveFormat.Encoding == WaveFormatEncoding.Pcm && waveFormat.BitsPerSample == 16)
        {
            ApplyKokoroPcm16EdgeFade(playbackBytes, waveFormat, fadeFrameCount);
            return;
        }

        if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat && waveFormat.BitsPerSample == 32)
        {
            ApplyKokoroFloat32EdgeFade(playbackBytes, waveFormat, fadeFrameCount);
        }
    }

    private static bool TryGetKokoroFadeFrameCount(byte[] playbackBytes, WaveFormat waveFormat, out int fadeFrameCount)
    {
        fadeFrameCount = 0;

        if (waveFormat.BlockAlign <= 0 || waveFormat.SampleRate <= 0)
        {
            return false;
        }

        var totalFrameCount = playbackBytes.Length / waveFormat.BlockAlign;
        if (totalFrameCount < 8)
        {
            return false;
        }

        var desiredFadeFrames = (int)Math.Round(waveFormat.SampleRate * KokoroSegmentEdgeFadeDuration.TotalSeconds);
        fadeFrameCount = Math.Clamp(desiredFadeFrames, 1, Math.Max(1, totalFrameCount / 2));
        return fadeFrameCount > 0;
    }

    private static void ApplyKokoroPcm16EdgeFade(byte[] playbackBytes, WaveFormat waveFormat, int fadeFrameCount)
    {
        var bytesPerSample = waveFormat.BitsPerSample / 8;
        var bytesPerFrame = waveFormat.BlockAlign;
        var channels = Math.Max(1, waveFormat.Channels);
        var totalFrameCount = playbackBytes.Length / bytesPerFrame;

        for (var frameIndex = 0; frameIndex < fadeFrameCount; frameIndex++)
        {
            var fadeInGain = (frameIndex + 1d) / fadeFrameCount;
            var fadeOutGain = (fadeFrameCount - frameIndex - 1d) / fadeFrameCount;
            var startFrameOffset = frameIndex * bytesPerFrame;
            var endFrameOffset = (totalFrameCount - fadeFrameCount + frameIndex) * bytesPerFrame;

            for (var channelIndex = 0; channelIndex < channels; channelIndex++)
            {
                var startSampleOffset = startFrameOffset + (channelIndex * bytesPerSample);
                var endSampleOffset = endFrameOffset + (channelIndex * bytesPerSample);
                ApplyPcm16Gain(playbackBytes, startSampleOffset, fadeInGain);
                ApplyPcm16Gain(playbackBytes, endSampleOffset, fadeOutGain);
            }
        }
    }

    private static void ApplyKokoroFloat32EdgeFade(byte[] playbackBytes, WaveFormat waveFormat, int fadeFrameCount)
    {
        var bytesPerSample = waveFormat.BitsPerSample / 8;
        var bytesPerFrame = waveFormat.BlockAlign;
        var channels = Math.Max(1, waveFormat.Channels);
        var totalFrameCount = playbackBytes.Length / bytesPerFrame;

        for (var frameIndex = 0; frameIndex < fadeFrameCount; frameIndex++)
        {
            var fadeInGain = (float)((frameIndex + 1d) / fadeFrameCount);
            var fadeOutGain = (float)((fadeFrameCount - frameIndex - 1d) / fadeFrameCount);
            var startFrameOffset = frameIndex * bytesPerFrame;
            var endFrameOffset = (totalFrameCount - fadeFrameCount + frameIndex) * bytesPerFrame;

            for (var channelIndex = 0; channelIndex < channels; channelIndex++)
            {
                var startSampleOffset = startFrameOffset + (channelIndex * bytesPerSample);
                var endSampleOffset = endFrameOffset + (channelIndex * bytesPerSample);
                ApplyFloat32Gain(playbackBytes, startSampleOffset, fadeInGain);
                ApplyFloat32Gain(playbackBytes, endSampleOffset, fadeOutGain);
            }
        }
    }

    private static void ApplyPcm16Gain(byte[] buffer, int offset, double gain)
    {
        var sample = BitConverter.ToInt16(buffer, offset);
        var scaled = (short)Math.Clamp((int)Math.Round(sample * gain), short.MinValue, short.MaxValue);
        var encoded = BitConverter.GetBytes(scaled);
        buffer[offset] = encoded[0];
        buffer[offset + 1] = encoded[1];
    }

    private static void ApplyFloat32Gain(byte[] buffer, int offset, float gain)
    {
        var sample = BitConverter.ToSingle(buffer, offset);
        var encoded = BitConverter.GetBytes(sample * gain);
        Buffer.BlockCopy(encoded, 0, buffer, offset, encoded.Length);
    }

    private static bool AreEquivalentWaveFormats(WaveFormat left, WaveFormat right)
    {
        return left.Encoding == right.Encoding
            && left.SampleRate == right.SampleRate
            && left.Channels == right.Channels
            && left.BitsPerSample == right.BitsPerSample
            && left.BlockAlign == right.BlockAlign
            && left.AverageBytesPerSecond == right.AverageBytesPerSecond;
    }

    private async Task SpeakWithLocalSynthesizerAsync(string content, string voiceName, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var synthesizer = GetSpeechSynthesizer();
            ConfigureSpeechSynthesizer(synthesizer, voiceName);

            var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<LocalSpeakCompletedEventArgs>? completedHandler = null;
            completedHandler = (_, args) =>
            {
                if (completedHandler is not null)
                {
                    synthesizer.SpeakCompleted -= completedHandler;
                }

                if (args.Cancelled)
                {
                    completionSource.TrySetCanceled(cancellationToken);
                }
                else if (args.Error is not null)
                {
                    completionSource.TrySetException(args.Error);
                }
                else
                {
                    completionSource.TrySetResult();
                }
            };

            using var registration = cancellationToken.Register(StopSpeaking);
            lock (_speechLock)
            {
                synthesizer.SpeakCompleted += completedHandler;
                UpdateSpeechPlaybackState(isActive: true, isPaused: false);
                synthesizer.SpeakAsync(content);
            }

            await completionSource.Task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"TTS error: {exception.Message}");
        }
        finally
        {
            UpdateSpeechPlaybackState(isActive: false, isPaused: false);
        }
    }

    private async Task SpeakWithPiperAsync(string content, string voiceName, CancellationToken cancellationToken)
    {
        Process? process = null;
        WaveOutEvent? waveOut = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetPiperConfiguration(voiceName, out var executablePath, out var modelPath, out var sampleRate, out var configurationError))
            {
                Debug.WriteLine($"Piper TTS unavailable: {configurationError}");
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
            };
            startInfo.ArgumentList.Add("-m");
            startInfo.ArgumentList.Add(modelPath);
            startInfo.ArgumentList.Add("--output_raw");
            startInfo.ArgumentList.Add("--length_scale");
            startInfo.ArgumentList.Add(GetPiperLengthScale().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--sentence_silence");
            startInfo.ArgumentList.Add(GetPiperSentenceSilenceSeconds().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

            process = new Process { StartInfo = startInfo };
            var playbackReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var playbackCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 1))
            {
                BufferDuration = TimeSpan.FromSeconds(30),
                DiscardOnBufferOverflow = false,
                ReadFully = true,
            };

            waveOut = new WaveOutEvent();
            waveOut.Init(bufferedWaveProvider);
            waveOut.PlaybackStopped += (_, args) =>
            {
                if (args.Exception is not null)
                {
                    playbackCompleted.TrySetException(args.Exception);
                    return;
                }

                playbackCompleted.TrySetResult();
            };

            lock (_speechLock)
            {
                _piperProcess = process;
                _piperWaveOut = waveOut;
            }

            process.Start();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            var standardOutputTask = StreamPiperAudioAsync(process.StandardOutput.BaseStream, bufferedWaveProvider, playbackReady, cancellationToken);

            using var registration = cancellationToken.Register(StopSpeaking);

            await process.StandardInput.WriteAsync(content);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();

            var hasAudio = await playbackReady.Task.ConfigureAwait(false);
            if (hasAudio)
            {
                UpdateSpeechPlaybackState(isActive: true, isPaused: false);
                waveOut.Play();
            }

            await process.WaitForExitAsync(cancellationToken);
            await standardOutputTask;
            var standardError = await standardErrorTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(standardError)
                    ? $"Piper exited with code {process.ExitCode}."
                    : standardError.Trim());
            }

            if (!hasAudio)
            {
                throw new InvalidOperationException("Piper did not produce audio output.");
            }

            await WaitForPiperBufferToDrainAsync(bufferedWaveProvider, cancellationToken);
            if (waveOut.PlaybackState != PlaybackState.Stopped)
            {
                waveOut.Stop();
            }

            await playbackCompleted.Task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Piper TTS error: {exception.Message}");
        }
        finally
        {
            lock (_speechLock)
            {
                if (ReferenceEquals(_piperWaveOut, waveOut))
                {
                    _piperWaveOut = null;
                }

                _piperProcess = null;
            }

            UpdateSpeechPlaybackState(isActive: false, isPaused: false);
            waveOut?.Dispose();
            process?.Dispose();
        }
    }

    private async Task StreamPiperAudioAsync(Stream sourceStream, BufferedWaveProvider bufferedWaveProvider, TaskCompletionSource<bool> playbackReady, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var receivedAudio = false;

        try
        {
            while (true)
            {
                while (bufferedWaveProvider.BufferedDuration >= PiperMaximumBufferedAudioDuration)
                {
                    if (receivedAudio && !playbackReady.Task.IsCompleted)
                    {
                        playbackReady.TrySetResult(true);
                    }

                    await Task.Delay(PiperBufferPollingInterval, cancellationToken);
                }

                var bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (bytesRead <= 0)
                {
                    break;
                }

                bufferedWaveProvider.AddSamples(buffer, 0, bytesRead);
                if (!receivedAudio)
                {
                    receivedAudio = true;
                }

                if (receivedAudio
                    && bufferedWaveProvider.BufferedDuration >= PiperInitialPlaybackBufferDuration
                    && !playbackReady.Task.IsCompleted)
                {
                    playbackReady.TrySetResult(true);
                }
            }
        }
        finally
        {
            if (receivedAudio)
            {
                playbackReady.TrySetResult(true);
            }
            else
            {
                playbackReady.TrySetResult(false);
            }
        }
    }

    private static async Task WaitForPiperBufferToDrainAsync(BufferedWaveProvider bufferedWaveProvider, CancellationToken cancellationToken)
    {
        while (bufferedWaveProvider.BufferedBytes > 0)
        {
            await Task.Delay(PiperBufferPollingInterval, cancellationToken);
        }
    }

    private bool TryGetPiperConfiguration(string voiceName, out string executablePath, out string modelPath, out int sampleRate, out string error)
    {
        var settings = _connectionSettingsStore.Load();
        if (!string.Equals(settings.TextToSpeechProvider, "Piper", StringComparison.OrdinalIgnoreCase))
        {
            executablePath = string.Empty;
            modelPath = string.Empty;
            sampleRate = 0;
            error = "Piper is not selected.";
            return false;
        }

        executablePath = settings.PiperExecutablePath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            modelPath = string.Empty;
            sampleRate = 0;
            error = $"Piper executable not found at '{settings.PiperExecutablePath}'.";
            return false;
        }

        var requestedVoice = string.IsNullOrWhiteSpace(voiceName) ? _defaultTextToSpeechVoice : voiceName.Trim();
        modelPath = ResolvePiperModelPath(settings.PiperModelsDirectory, requestedVoice);
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            modelPath = string.Empty;
            sampleRate = 0;
            error = $"Piper model '{requestedVoice}' was not found under '{settings.PiperModelsDirectory}'.";
            return false;
        }

        if (!TryReadPiperSampleRate(modelPath, out sampleRate))
        {
            error = $"Unable to read Piper model sample rate from '{modelPath}.json'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private LocalSpeechSynthesizer GetSpeechSynthesizer()
    {
        lock (_speechLock)
        {
            _speechSynthesizer ??= new LocalSpeechSynthesizer();
            return _speechSynthesizer;
        }
    }

    private void ConfigureSpeechSynthesizer(LocalSpeechSynthesizer synthesizer, string voiceName)
    {
        lock (_speechLock)
        {
            synthesizer.Rate = TextToSpeechRate;

            var requestedVoice = voiceName?.Trim() ?? string.Empty;
            var desiredVoice = !string.IsNullOrWhiteSpace(requestedVoice)
                && AvailableTextToSpeechVoices.Any(voice => string.Equals(voice, requestedVoice, StringComparison.OrdinalIgnoreCase))
                    ? requestedVoice
                    : _defaultTextToSpeechVoice;

            if (!string.IsNullOrWhiteSpace(desiredVoice)
                && !string.Equals(synthesizer.Voice.Name, desiredVoice, StringComparison.OrdinalIgnoreCase))
            {
                synthesizer.SelectVoice(desiredVoice);
            }
        }
    }

    private double GetPiperLengthScale()
    {
        return Math.Clamp(1.0 - (TextToSpeechRate * 0.08), 0.6, 1.4);
    }

    private double GetPiperSentenceSilenceSeconds()
    {
        return 0.24;
    }

    private void StopSpeaking()
    {
        lock (_speechLock)
        {
            _speechQueue = Task.CompletedTask;
            _speechSynthesizer?.SpeakAsyncCancelAll();

            try
            {
                _kokoroWaveOut?.Stop();
            }
            catch
            {
            }

            try
            {
                if (_piperProcess is { HasExited: false })
                {
                    _piperProcess.Kill(true);
                }
            }
            catch
            {
            }

            try
            {
                _piperWaveOut?.Stop();
            }
            catch
            {
            }

            DisposeKokoroPlaybackSession();
        }

        ClearActiveSpeechHighlight();
        UpdateSpeechPlaybackState(isActive: false, isPaused: false);
    }

    private bool TryToggleSpeechPause(out bool isPaused, out string feedbackMessage)
    {
        isPaused = false;
        feedbackMessage = string.Empty;

        try
        {
            lock (_speechLock)
            {
                if (_kokoroWaveOut is not null && _kokoroWaveOut.PlaybackState != PlaybackState.Stopped)
                {
                    if (_kokoroWaveOut.PlaybackState == PlaybackState.Playing)
                    {
                        _kokoroWaveOut.Pause();
                        isPaused = true;
                    }
                    else
                    {
                        _kokoroWaveOut.Play();
                    }

                    UpdateSpeechPlaybackState(isActive: true, isPaused: isPaused);
                    return true;
                }

                if (_piperWaveOut is not null && _piperWaveOut.PlaybackState != PlaybackState.Stopped)
                {
                    if (_piperWaveOut.PlaybackState == PlaybackState.Playing)
                    {
                        _piperWaveOut.Pause();
                        isPaused = true;
                    }
                    else
                    {
                        _piperWaveOut.Play();
                    }

                    UpdateSpeechPlaybackState(isActive: true, isPaused: isPaused);
                    return true;
                }

                if (_speechSynthesizer is not null)
                {
                    if (_speechSynthesizer.State == System.Speech.Synthesis.SynthesizerState.Speaking)
                    {
                        _speechSynthesizer.Pause();
                        isPaused = true;
                        UpdateSpeechPlaybackState(isActive: true, isPaused: true);
                        return true;
                    }

                    if (_speechSynthesizer.State == System.Speech.Synthesis.SynthesizerState.Paused)
                    {
                        _speechSynthesizer.Resume();
                        UpdateSpeechPlaybackState(isActive: true, isPaused: false);
                        return true;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            feedbackMessage = $"Unable to toggle speech pause: {exception.Message}";
            return false;
        }

        feedbackMessage = "No active speech playback is available to pause or resume.";
        return false;
    }

    private void UpdateSpeechPlaybackState(bool isActive, bool isPaused)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplySpeechPlaybackState(isActive, isPaused);
            return;
        }

        Dispatcher.BeginInvoke(() => ApplySpeechPlaybackState(isActive, isPaused), DispatcherPriority.Background);
    }

    private void ApplySpeechPlaybackState(bool isActive, bool isPaused)
    {
        var activeChanged = SetProperty(ref _isSpeechActive, isActive, nameof(CanPauseSpeech));
        var pausedChanged = SetProperty(ref _isSpeechPaused, isPaused, nameof(PauseButtonText));
        if (activeChanged || pausedChanged)
        {
            OnPropertyChanged(nameof(CanPauseSpeech));
            OnPropertyChanged(nameof(PauseButtonText));
        }
    }

    private static string NormalizeSpeechContent(string content, string provider)
    {
        if (string.IsNullOrWhiteSpace(content) || string.Equals(content, "(empty response)", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var cleanedContent = StripSpeechFormatting(content);

        var normalizedBody = string.Equals(provider, "Piper", StringComparison.OrdinalIgnoreCase)
            ? NormalizePiperSpeechContent(cleanedContent)
            : NormalizeStandardSpeechContent(cleanedContent);

        return string.IsNullOrWhiteSpace(normalizedBody)
            ? string.Empty
            : normalizedBody;
    }

    private static string StripSpeechFormatting(string content)
    {
        var cleaned = Regex.Replace(content, @"(?<!\S)\*+(?=\S)|(?<=\S)\*+(?!\S)", string.Empty);
        cleaned = cleaned.Replace("*", string.Empty);
        return cleaned;
    }

    private static IReadOnlyList<SpeechSegment> BuildSpeechSegments(string content, string provider)
    {
        if (string.Equals(provider, "Piper", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedContent = NormalizeSpeechContent(content, provider);
            if (string.IsNullOrWhiteSpace(normalizedContent))
            {
                return Array.Empty<SpeechSegment>();
            }

            return BuildPiperSpeechSegments(normalizedContent)
                .Select(segment => new SpeechSegment(segment, segment))
                .ToArray();
        }

        if (string.Equals(provider, "Kokoro", StringComparison.OrdinalIgnoreCase))
        {
            return BuildKokoroSpeechSegments(content);
        }

        var normalizedStandardContent = NormalizeStandardSpeechContent(StripSpeechFormatting(content));
        if (string.IsNullOrWhiteSpace(normalizedStandardContent))
        {
            return Array.Empty<SpeechSegment>();
        }

        var trimmedContent = content.Trim();
        return new[] { new SpeechSegment(string.IsNullOrWhiteSpace(trimmedContent) ? normalizedStandardContent : trimmedContent, normalizedStandardContent) };
    }

    private static IReadOnlyList<SpeechSegment> BuildKokoroSpeechSegments(string content)
    {
        if (string.IsNullOrWhiteSpace(content)
            || string.Equals(content, "(empty response)", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<SpeechSegment>();
        }

        var segments = SplitSpeechSentences(content)
            .Select(segment => new SpeechSegment(segment, NormalizeStandardSpeechContent(StripSpeechFormatting(segment))))
            .Where(segment => !string.IsNullOrWhiteSpace(segment.SpeechText))
            .ToList();

        if (segments.Count > 0)
        {
            return segments;
        }

        var normalizedContent = NormalizeStandardSpeechContent(StripSpeechFormatting(content));
        if (string.IsNullOrWhiteSpace(normalizedContent))
        {
            return Array.Empty<SpeechSegment>();
        }

        return new[] { new SpeechSegment(content.Trim(), normalizedContent) };
    }

    private static string NormalizeStandardSpeechContent(string content)
    {
        return Regex.Replace(content, @"[\r\n\t]+", " ").Trim();
    }

    private static IReadOnlyList<string> SplitSpeechSentences(string content)
    {
        var segments = new List<string>();
        var segmentStartIndex = 0;

        for (var index = 0; index < content.Length; index++)
        {
            if (!IsSentenceTerminator(content[index]))
            {
                continue;
            }

            var segmentEndIndex = index + 1;
            while (segmentEndIndex < content.Length && IsSentenceTerminator(content[segmentEndIndex]))
            {
                segmentEndIndex++;
            }

            while (segmentEndIndex < content.Length && "\"')]}".Contains(content[segmentEndIndex]))
            {
                segmentEndIndex++;
            }

            if (segmentEndIndex < content.Length && !char.IsWhiteSpace(content[segmentEndIndex]))
            {
                continue;
            }

            var sentence = content[segmentStartIndex..segmentEndIndex].Trim();
            if (!string.IsNullOrWhiteSpace(sentence))
            {
                segments.Add(sentence);
            }

            segmentStartIndex = segmentEndIndex;
        }

        if (segmentStartIndex < content.Length)
        {
            var trailingSentence = content[segmentStartIndex..].Trim();
            if (!string.IsNullOrWhiteSpace(trailingSentence))
            {
                segments.Add(trailingSentence);
            }
        }

        return segments;
    }

    private static bool IsSentenceTerminator(char value)
        => value is '.' or '!' or '?';

    private static string NormalizePiperSpeechContent(string content)
    {
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        normalized = Regex.Replace(normalized, @"\n{2,}", ". ");
        normalized = Regex.Replace(normalized, @"\n+", ". ");
        normalized = Regex.Replace(normalized, @"(?<=\S)(?:\s+[-–—]\s*|\s*[-–—]\s+)(?=\S)", ". ");
        normalized = Regex.Replace(normalized, @"\s*([,;:])\s*", "$1 ");
        normalized = Regex.Replace(normalized, @"\s*([.!?])\s*", "$1 ");
        normalized = Regex.Replace(normalized, @"\s{2,}", " ");
        return normalized.Trim();
    }

    private static IReadOnlyList<string> BuildPiperSpeechSegments(string content)
    {
        var sentenceUnits = SplitPiperSpeechUnits(content);
        if (sentenceUnits.Count <= 1 && content.Length <= PiperMaximumSpeechChunkCharacters)
        {
            return new[] { content };
        }

        var segments = new List<string>();
        var builder = new StringBuilder();
        var unitCount = 0;

        foreach (var unit in sentenceUnits)
        {
            var trimmedUnit = unit.Trim();
            if (string.IsNullOrWhiteSpace(trimmedUnit))
            {
                continue;
            }

            if (trimmedUnit.Length > PiperMaximumSpeechChunkCharacters)
            {
                foreach (var clause in SplitOversizedPiperUnit(trimmedUnit))
                {
                    AppendPiperSpeechSegment(clause, segments, builder, ref unitCount);
                }

                continue;
            }

            var separator = builder.Length == 0 ? string.Empty : " ";
            var wouldExceedLength = builder.Length + separator.Length + trimmedUnit.Length > PiperMaximumSpeechChunkCharacters;
            var wouldExceedUnitCount = unitCount >= PiperMaximumSpeechChunkUnits;
            if (builder.Length > 0 && (wouldExceedLength || wouldExceedUnitCount))
            {
                segments.Add(builder.ToString().Trim());
                builder.Clear();
                unitCount = 0;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(trimmedUnit);
            unitCount++;
        }

        if (builder.Length > 0)
        {
            segments.Add(builder.ToString().Trim());
        }

        return segments;
    }

    private static List<string> SplitPiperSpeechUnits(string content)
    {
        return Regex
            .Split(content, @"(?<=[.!?])\s+")
            .SelectMany(unit => SplitOversizedPiperUnit(unit))
            .Where(unit => !string.IsNullOrWhiteSpace(unit))
            .ToList();
    }

    private static IEnumerable<string> SplitOversizedPiperUnit(string unit)
    {
        var clauseBuilder = new StringBuilder();
        foreach (var clause in Regex.Split(unit, @"(?<=[,;:])\s+"))
        {
            var trimmedClause = clause.Trim();
            if (string.IsNullOrWhiteSpace(trimmedClause))
            {
                continue;
            }

            if (trimmedClause.Length > PiperMaximumSpeechChunkCharacters)
            {
                if (clauseBuilder.Length > 0)
                {
                    yield return clauseBuilder.ToString().Trim();
                    clauseBuilder.Clear();
                }

                foreach (var phrase in SplitPiperClauseByWords(trimmedClause))
                {
                    yield return phrase;
                }

                continue;
            }

            var separator = clauseBuilder.Length == 0 ? string.Empty : " ";
            if (clauseBuilder.Length > 0 && clauseBuilder.Length + separator.Length + trimmedClause.Length > PiperMaximumSpeechChunkCharacters)
            {
                yield return clauseBuilder.ToString().Trim();
                clauseBuilder.Clear();
            }

            if (clauseBuilder.Length > 0)
            {
                clauseBuilder.Append(' ');
            }

            clauseBuilder.Append(trimmedClause);
        }

        if (clauseBuilder.Length > 0)
        {
            yield return clauseBuilder.ToString().Trim();
        }
    }

    private static IEnumerable<string> SplitPiperClauseByWords(string clause)
    {
        var phraseBuilder = new StringBuilder();
        foreach (var word in clause.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = phraseBuilder.Length == 0 ? string.Empty : " ";
            if (phraseBuilder.Length > 0 && phraseBuilder.Length + separator.Length + word.Length > PiperMaximumSpeechChunkCharacters)
            {
                yield return phraseBuilder.ToString().Trim();
                phraseBuilder.Clear();
            }

            if (phraseBuilder.Length > 0)
            {
                phraseBuilder.Append(' ');
            }

            phraseBuilder.Append(word);
        }

        if (phraseBuilder.Length > 0)
        {
            yield return phraseBuilder.ToString().Trim();
        }
    }

    private static void AppendPiperSpeechSegment(string segment, List<string> segments, StringBuilder builder, ref int unitCount)
    {
        var trimmedSegment = segment.Trim();
        if (string.IsNullOrWhiteSpace(trimmedSegment))
        {
            return;
        }

        var separator = builder.Length == 0 ? string.Empty : " ";
        var wouldExceedLength = builder.Length + separator.Length + trimmedSegment.Length > PiperMaximumSpeechChunkCharacters;
        var wouldExceedUnitCount = unitCount >= PiperMaximumSpeechChunkUnits;
        if (builder.Length > 0 && (wouldExceedLength || wouldExceedUnitCount))
        {
            segments.Add(builder.ToString().Trim());
            builder.Clear();
            unitCount = 0;
        }

        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        builder.Append(trimmedSegment);
        unitCount++;
    }

    private void LoadTextToSpeechOptions()
    {
        _ = LoadTextToSpeechOptionsAsync();
    }

    private async Task LoadTextToSpeechOptionsAsync()
    {
        try
        {
            var settings = _connectionSettingsStore.Load();
            List<string> voices;
            if (string.Equals(settings.TextToSpeechProvider, "Kokoro", StringComparison.OrdinalIgnoreCase))
            {
                voices = LoadKokoroTextToSpeechVoices(settings);
            }
            else if (string.Equals(settings.TextToSpeechProvider, "Piper", StringComparison.OrdinalIgnoreCase))
            {
                voices = LoadPiperTextToSpeechVoices(settings);
            }
            else
            {
                voices = LoadLocalTextToSpeechVoices();
            }

            AvailableTextToSpeechVoices.Clear();
            foreach (var voice in voices)
            {
                AvailableTextToSpeechVoices.Add(voice);
            }

            if (string.IsNullOrWhiteSpace(_defaultTextToSpeechVoice) && AvailableTextToSpeechVoices.Count > 0)
            {
                _defaultTextToSpeechVoice = AvailableTextToSpeechVoices[0];
            }

            OnPropertyChanged(nameof(HasTextToSpeechVoices));
            ApplyTextToSpeechSettings(settings);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to enumerate TTS voices: {exception.Message}");
        }
    }

    private List<string> LoadPiperTextToSpeechVoices(ConnectionSettings settings)
    {
        var voices = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.PiperModelsDirectory) || !Directory.Exists(settings.PiperModelsDirectory))
        {
            _defaultTextToSpeechVoice = string.Empty;
            return voices;
        }

        try
        {
            voices.AddRange(
                Directory
                    .EnumerateFiles(settings.PiperModelsDirectory, "*.onnx", SearchOption.AllDirectories)
                    .Select(path => NormalizePiperVoiceName(settings.PiperModelsDirectory, path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to enumerate Piper voices: {exception.Message}");
        }

        _defaultTextToSpeechVoice = voices.FirstOrDefault() ?? string.Empty;
        return voices;
    }

    private List<string> LoadKokoroTextToSpeechVoices(ConnectionSettings settings)
    {
        var voices = settings.KokoroVoices
            .Where(voice => !string.IsNullOrWhiteSpace(voice))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(voice => voice, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!voices.Any(voice => string.Equals(voice, settings.KokoroVoice, StringComparison.OrdinalIgnoreCase)))
        {
            voices.Insert(0, settings.KokoroVoice);
        }

        _defaultTextToSpeechVoice = settings.KokoroVoice;
        return voices;
    }

    private List<string> LoadLocalTextToSpeechVoices()
    {
        var voices = new List<string>();

        try
        {
            using var speechSynthesizer = new LocalSpeechSynthesizer();
            _defaultTextToSpeechVoice = speechSynthesizer.Voice?.Name ?? string.Empty;
            voices.AddRange(
                speechSynthesizer
                    .GetInstalledVoices()
                    .Select(item => item.VoiceInfo.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to enumerate local TTS voices: {exception.Message}");
        }

        return voices;
    }

    private void ApplyTextToSpeechSettings(ConnectionSettings settings)
    {
        SetProperty(ref _textToSpeechProviderSelection, settings.TextToSpeechProvider, nameof(TextToSpeechProviderSelection));
        SetProperty(ref _textToSpeechEnabled, settings.TextToSpeechEnabled, nameof(TextToSpeechEnabled));
        SetProperty(ref _textToSpeechRate, Math.Clamp(settings.TextToSpeechRate, -5, 5), nameof(TextToSpeechRate));
        OnPropertyChanged(nameof(TextToSpeechRateLabel));
    }

    private void PersistTextToSpeechSettings()
    {
        try
        {
            var settings = _connectionSettingsStore.Load();
            settings.TextToSpeechProvider = TextToSpeechProviderSelection;
            settings.TextToSpeechEnabled = TextToSpeechEnabled;
            settings.TextToSpeechRate = TextToSpeechRate;
            _connectionSettingsStore.Save(settings);
            ConnectionSettingsSummary = BuildConnectionSettingsSummary(settings);
            MarkSetupGuideTtsConfigured();
        }
        catch (Exception exception)
        {
            ConnectionSettingsSummary = exception.Message;
        }
    }

    private static string NormalizeTextToSpeechProvider(string? provider)
    {
        if (string.Equals(provider?.Trim(), "Kokoro", StringComparison.OrdinalIgnoreCase))
        {
            return "Kokoro";
        }

        if (string.Equals(provider?.Trim(), "Piper", StringComparison.OrdinalIgnoreCase))
        {
            return "Piper";
        }

        return "Local";
    }

    private static string NormalizePiperVoiceName(string modelsDirectory, string modelPath)
    {
        var relativePath = Path.GetRelativePath(modelsDirectory, modelPath).Replace('\\', '/');
        return relativePath.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)
            ? relativePath[..^5]
            : relativePath;
    }

    private static string ResolvePiperModelPath(string modelsDirectory, string voiceName)
    {
        if (string.IsNullOrWhiteSpace(modelsDirectory) || string.IsNullOrWhiteSpace(voiceName))
        {
            return string.Empty;
        }

        var candidate = voiceName.Trim().Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(candidate))
        {
            return candidate.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) ? candidate : candidate + ".onnx";
        }

        var combined = Path.Combine(modelsDirectory, candidate);
        if (File.Exists(combined))
        {
            return combined;
        }

        if (File.Exists(combined + ".onnx"))
        {
            return combined + ".onnx";
        }

        return string.Empty;
    }

    private static bool TryReadPiperSampleRate(string modelPath, out int sampleRate)
    {
        var configPath = modelPath + ".json";
        if (!File.Exists(configPath))
        {
            sampleRate = 0;
            return false;
        }

        try
        {
            using var stream = File.OpenRead(configPath);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("audio", out var audioElement)
                && audioElement.TryGetProperty("sample_rate", out var sampleRateElement)
                && sampleRateElement.TryGetInt32(out sampleRate)
                && sampleRate > 0)
            {
                return true;
            }
        }
        catch
        {
        }

        sampleRate = 0;
        return false;
    }

    private string GetSuggestedTextToSpeechVoice(int index)
    {
        if (AvailableTextToSpeechVoices.Count == 0)
        {
            return string.Empty;
        }

        return AvailableTextToSpeechVoices[index % AvailableTextToSpeechVoices.Count];
    }

    protected override void OnClosed(EventArgs e)
    {
        PersistConversationSession(SelectedTheme);
        UnsubscribeFromAppearanceSync();
        StopSpeaking();
        lock (_speechLock)
        {
            _speechSynthesizer?.Dispose();
            _speechSynthesizer = null;
            DisposeKokoroPlaybackSession();
            _piperWaveOut?.Dispose();
            _piperWaveOut = null;
            _piperProcess?.Dispose();
            _piperProcess = null;
        }

        base.OnClosed(e);
    }

    private bool TryCreateKokoroSpeechRequest(ConnectionSettings settings, string content, string voiceName, out HttpRequestMessage request, out string error)
    {
        if (!string.Equals(settings.TextToSpeechProvider, "Kokoro", StringComparison.OrdinalIgnoreCase))
        {
            request = null!;
            error = "Kokoro is not selected.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.KokoroBaseUrl))
        {
            request = null!;
            error = "Add kokoroBaseUrl to the local settings file.";
            return false;
        }

        var endpoint = BuildKokoroSpeechEndpoint(settings.KokoroBaseUrl);
        var desiredVoice = string.IsNullOrWhiteSpace(voiceName) ? settings.KokoroVoice : voiceName.Trim();
        if (settings.KokoroVoices.Count > 0
            && !settings.KokoroVoices.Any(configuredVoice => string.Equals(configuredVoice, desiredVoice, StringComparison.OrdinalIgnoreCase)))
        {
            desiredVoice = settings.KokoroVoice;
        }

        if (string.IsNullOrWhiteSpace(desiredVoice))
        {
            desiredVoice = "af_heart";
        }

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(settings.KokoroModel) ? "kokoro" : settings.KokoroModel,
            input = content,
            voice = desiredVoice,
            response_format = "wav",
            speed = GetKokoroEffectiveSpeed(settings),
            lang_code = ResolveKokoroLanguageCode(settings, desiredVoice),
        };

        request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"),
        };

        error = string.Empty;
        return true;
    }

    private double GetKokoroEffectiveSpeed(ConnectionSettings settings)
    {
        var baseSpeed = double.IsFinite(settings.KokoroSpeed)
            ? settings.KokoroSpeed
            : 1.0;

        var sliderOffset = TextToSpeechRate * 0.1;
        return Math.Clamp(baseSpeed + sliderOffset, 0.5, 2.0);
    }

    private static string ResolveKokoroLanguageCode(ConnectionSettings settings, string voiceName)
    {
        if (!string.IsNullOrWhiteSpace(voiceName))
        {
            var separatorIndex = voiceName.IndexOf('_');
            if (separatorIndex > 0)
            {
                var candidate = voiceName[..separatorIndex];
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate[..1].ToLowerInvariant();
                }
            }
        }

        return string.IsNullOrWhiteSpace(settings.KokoroLanguageCode)
            ? "a"
            : settings.KokoroLanguageCode.Trim().ToLowerInvariant();
    }

    private static string BuildKokoroSpeechEndpoint(string baseUrl)
    {
        return baseUrl.EndsWith("/v1/audio/speech", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl.TrimEnd('/')}/v1/audio/speech";
    }

    private static byte[] NormalizeKokoroWaveResponse(byte[] payload)
    {
        if (payload.Length < 12
            || !HasAscii(payload, 0, "RIFF")
            || !HasAscii(payload, 8, "WAVE"))
        {
            return payload;
        }

        byte[]? normalized = null;

        if (ReadUInt32LittleEndian(payload, 4) == uint.MaxValue)
        {
            normalized = CloneIfNeeded(payload, normalized);
            WriteUInt32LittleEndian(normalized, 4, (uint)Math.Max(0, normalized.Length - 8));
        }

        var dataChunkOffset = FindAscii(payload, "data");
        if (dataChunkOffset >= 0
            && dataChunkOffset + 8 <= payload.Length
            && ReadUInt32LittleEndian(payload, dataChunkOffset + 4) == uint.MaxValue)
        {
            normalized = CloneIfNeeded(payload, normalized);
            WriteUInt32LittleEndian(normalized, dataChunkOffset + 4, (uint)Math.Max(0, normalized.Length - dataChunkOffset - 8));
        }

        return normalized ?? payload;
    }

    private static byte[] CloneIfNeeded(byte[] original, byte[]? clone)
    {
        return clone ?? (byte[])original.Clone();
    }

    private static uint ReadUInt32LittleEndian(byte[] payload, int offset)
    {
        return BitConverter.ToUInt32(payload, offset);
    }

    private static void WriteUInt32LittleEndian(byte[] payload, int offset, uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, payload, offset, bytes.Length);
    }

    private static bool HasAscii(byte[] payload, int offset, string value)
    {
        if (offset < 0 || offset + value.Length > payload.Length)
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (payload[offset + index] != value[index])
            {
                return false;
            }
        }

        return true;
    }

    private static int FindAscii(byte[] payload, string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > payload.Length)
        {
            return -1;
        }

        for (var offset = 0; offset <= payload.Length - value.Length; offset++)
        {
            if (HasAscii(payload, offset, value))
            {
                return offset;
            }
        }

        return -1;
    }

    private static string TryDecodeUtf8(byte[] payload)
    {
        try
        {
            return Encoding.UTF8.GetString(payload).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
}