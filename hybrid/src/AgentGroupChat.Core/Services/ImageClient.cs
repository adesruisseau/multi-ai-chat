using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentGroupChat.Core.Models.Domain;

namespace AgentGroupChat.Core.Services;

public sealed record ImageGenerationResult(string Message, byte[] ImageBytes, string MimeType);

public sealed class ImageClient
{
    private readonly HttpClient _httpClient;

    public ImageClient(HttpClient httpClient) => _httpClient = httpClient;

    public Task<ImageGenerationResult> GenerateTestImageAsync(
        ImageConnection connection,
        ImageModel model,
        string prompt,
        CancellationToken ct = default)
    {
        if (connection is null)
            throw new ArgumentNullException(nameof(connection));
        if (model is null)
            throw new ArgumentNullException(nameof(model));
        if (string.IsNullOrWhiteSpace(connection.Endpoint))
            throw new InvalidOperationException("Set an endpoint before testing this image connection.");
        if (string.IsNullOrWhiteSpace(prompt))
            throw new InvalidOperationException("Enter a non-empty test prompt before generating an image.");

        return connection.Transport switch
        {
            ImageTransports.ComfyUI => GenerateComfyUiImageAsync(connection, model, prompt.Trim(), ct),
            ImageTransports.EasyDiffusion => GenerateEasyDiffusionImageAsync(connection, model, prompt.Trim(), ct),
            ImageTransports.OpenAiImages => GenerateOpenAiCompatibleImageAsync(connection, model, prompt.Trim(), ct),
            _ => throw new InvalidOperationException($"Image transport '{connection.Transport}' is not supported yet."),
        };
    }

    private async Task<ImageGenerationResult> GenerateEasyDiffusionImageAsync(
    ImageConnection connection,
    ImageModel model,
    string prompt,
    CancellationToken ct)
    {
        var endpoint = NormalizeBaseUrl(connection.Endpoint) + "/render";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    prompt,
                    use_stable_diffusion_model = model.ModelId,
                    negative_prompt = model.NegativePrompt,
                    width = ClampDimension(model.Width),
                    height = ClampDimension(model.Height),
                    num_outputs = 1,
                    num_inference_steps = model.Steps,
                    guidance_scale = model.GuidanceScale,
                    sampler_name = "euler_a",
                }),
                Encoding.UTF8,
                "application/json")
        };

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"EasyDiffusion request failed: {Truncate(body, 280)}");

        using var doc = JsonDocument.Parse(body);

        var taskId = doc.RootElement.GetProperty("task");

        var imageInfo = await WaitForEasyDiffusionImageAsync(connection.Endpoint, taskId.ToString(), ct);

        string dataUri = imageInfo.Images.First().Url;
        string base64 = dataUri.Substring(dataUri.IndexOf(",") + 1);
        byte[] bytes = Convert.FromBase64String(base64);

        return new ImageGenerationResult(
            $"Generated image with EasyDiffusion task {taskId}.",
            bytes,
            GuessMimeType(bytes, imageInfo.Images.FirstOrDefault()?.FileName));
    }

    private async Task<byte[]> DownloadEasyDiffusionImageAsync(
    string baseUrl,
    EasyDiffusionInfo info,
    CancellationToken ct)
    {
        var image = info.Images.FirstOrDefault()
            ?? throw new InvalidOperationException("No image returned.");

        var url = image.Url;

        // handle relative URLs like /image/tmp/123.png
        if (url.StartsWith("/"))
            url = baseUrl.TrimEnd('/') + url;

        return await _httpClient.GetByteArrayAsync(url, ct);
    }

    private async Task<ImageGenerationResult> GenerateOpenAiCompatibleImageAsync(
        ImageConnection connection,
        ImageModel model,
        string prompt,
        CancellationToken ct)
    {
        var endpoint = BuildOpenAiImagesEndpoint(connection.Endpoint);
        var size = $"{ClampDimension(model.Width)}x{ClampDimension(model.Height)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                model = string.IsNullOrWhiteSpace(model.ModelId) ? "gpt-image-1" : model.ModelId.Trim(),
                prompt,
                size,
                n = 1,
                response_format = "b64_json",
            }), Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrWhiteSpace(connection.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey.Trim());

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Image request failed: {Truncate(body, 280)}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array
            || data.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Image provider returned no image payload.");
        }

        var first = data[0];
        byte[] imageBytes;

        if (first.TryGetProperty("b64_json", out var b64Json)
            && b64Json.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(b64Json.GetString()))
        {
            imageBytes = Convert.FromBase64String(b64Json.GetString()!);
        }
        else if (first.TryGetProperty("url", out var url)
            && url.ValueKind == JsonValueKind.String
            && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var imageUri))
        {
            imageBytes = await _httpClient.GetByteArrayAsync(imageUri, ct);
        }
        else
        {
            throw new InvalidOperationException("Image provider response did not include b64_json or a downloadable url.");
        }

        return new ImageGenerationResult(
            $"Generated a test image with '{model.Name}' at {size}.",
            imageBytes,
            GuessMimeType(imageBytes, null));
    }

    private async Task<ImageGenerationResult> GenerateComfyUiImageAsync(
        ImageConnection connection,
        ImageModel model,
        string prompt,
        CancellationToken ct)
    {
        var baseUrl = NormalizeBaseUrl(connection.Endpoint);
        var workflow = await LoadComfyWorkflowAsync(model, prompt, ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/prompt")
        {
            Content = new StringContent(new JsonObject
            {
                ["prompt"] = workflow,
                ["client_id"] = Guid.NewGuid().ToString("N"),
            }.ToJsonString(), Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"ComfyUI request failed: {Truncate(body, 280)}");

        using var promptResponse = JsonDocument.Parse(body);
        if (!promptResponse.RootElement.TryGetProperty("prompt_id", out var promptIdElement)
            || string.IsNullOrWhiteSpace(promptIdElement.GetString()))
        {
            throw new InvalidOperationException("ComfyUI did not return a prompt id.");
        }

        var promptId = promptIdElement.GetString()!;
        var imageInfo = await WaitForComfyImageAsync(baseUrl, promptId, ct);
        var imageBytes = await DownloadComfyImageAsync(baseUrl, imageInfo, ct);

        return new ImageGenerationResult(
            $"Generated a ComfyUI test image with '{model.Name}' (prompt {promptId}).",
            imageBytes,
            GuessMimeType(imageBytes, imageInfo.FileName));
    }

    private async Task<JsonNode> LoadComfyWorkflowAsync(ImageModel model, string prompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.WorkflowId))
        {
            throw new InvalidOperationException(
                "ComfyUI models need a workflow. Paste an API workflow JSON document or a local file path into Workflow / Profile.");
        }

        var workflowSource = model.WorkflowId.Trim();
        var rawWorkflow = workflowSource.StartsWith("{") || workflowSource.StartsWith("[")
            ? workflowSource
            : File.Exists(workflowSource)
                ? await File.ReadAllTextAsync(workflowSource, ct)
                : throw new InvalidOperationException("ComfyUI workflow was not valid JSON and no file was found at the provided path.");

        var workflow = JsonNode.Parse(rawWorkflow) ?? throw new InvalidOperationException("Unable to parse the ComfyUI workflow JSON.");
        var replacements = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["{{prompt}}"] = prompt,
            ["{{negative_prompt}}"] = model.NegativePrompt.Trim(),
            ["{{model}}"] = model.ModelId.Trim(),
            ["{{width}}"] = ClampDimension(model.Width),
            ["{{height}}"] = ClampDimension(model.Height),
            ["{{steps}}"] = model.Steps ?? 28,
            ["{{guidance_scale}}"] = model.GuidanceScale ?? 7.0,
            ["{{seed}}"] = Random.Shared.Next(1, int.MaxValue),
        };

        return ReplaceWorkflowPlaceholders(workflow, replacements);
    }
    private static string GetLastJsonObject(string body)
    {
        var lastStart = body.LastIndexOf("{\"status\"", StringComparison.Ordinal);

        if (lastStart < 0)
            throw new InvalidOperationException("No final status object found.");

        return body[lastStart..];
    }
    
    private async Task<EasyDiffusionInfo> WaitForEasyDiffusionImageAsync(string baseUrl, string promptId, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {

            using var response = await _httpClient.GetAsync($"{baseUrl}image/stream/{promptId}", ct);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Easy Diffusion lookup failed.");
            var finalJson = "";
            try
            {
                 finalJson = GetLastJsonObject(body);
                using var doc = JsonDocument.Parse(finalJson);

                var imageInfo = TryExtractEasyDiffusionInfo(doc.RootElement);
                if (imageInfo is not null && IsFinalStatus(imageInfo.Status))
                    return imageInfo;
            }
            catch
            {
                //fix me 
            }
            await Task.Delay(3000, ct);
        }
        throw new TimeoutException("Timed out waiting for Easy Diffusion to finish the image request");
    }

    private async Task<ComfyImageInfo> WaitForComfyImageAsync(string baseUrl, string promptId, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            using var response = await _httpClient.GetAsync($"{baseUrl}/history/{Uri.EscapeDataString(promptId)}", ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"ComfyUI history lookup failed: {Truncate(body, 280)}");

            using var doc = JsonDocument.Parse(body);
            var imageInfo = TryExtractComfyImageInfo(doc.RootElement);
            if (imageInfo is not null)
                return imageInfo;

            await Task.Delay(3000, ct);
        }

        throw new TimeoutException("Timed out waiting for ComfyUI to finish the test image request.");
    }

    private async Task<byte[]> DownloadComfyImageAsync(string baseUrl, ComfyImageInfo imageInfo, CancellationToken ct)
    {
        var query = new StringBuilder($"{baseUrl}/view?filename={Uri.EscapeDataString(imageInfo.FileName)}");
        if (!string.IsNullOrWhiteSpace(imageInfo.Subfolder))
            query.Append($"&subfolder={Uri.EscapeDataString(imageInfo.Subfolder)}");
        query.Append($"&type={Uri.EscapeDataString(string.IsNullOrWhiteSpace(imageInfo.Type) ? "output" : imageInfo.Type)}");

        using var response = await _httpClient.GetAsync(query.ToString(), ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"ComfyUI image download failed: {Truncate(body, 280)}");
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static JsonNode ReplaceWorkflowPlaceholders(JsonNode node, IReadOnlyDictionary<string, object?> replacements)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(x => x.Key).ToList())
                {
                    if (obj[key] is JsonNode child)
                    {
                        var replaced = ReplaceWorkflowPlaceholders(child, replacements);

                        if (!ReferenceEquals(child, replaced))
                            obj[key] = replaced;
                    }
                }
                return obj;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonNode child)
                    {
                        var replaced = ReplaceWorkflowPlaceholders(child, replacements);

                        if (!ReferenceEquals(child, replaced))
                            array[i] = replaced;
                    }
                }
                return array;

            case JsonValue value when value.TryGetValue<string>(out var text):
                return ReplaceWorkflowValue(text, replacements);

            default:
                return node;
        }
    }

    private static JsonNode ReplaceWorkflowValue(string text, IReadOnlyDictionary<string, object?> replacements)
    {
        foreach (var replacement in replacements)
        {
            if (string.Equals(text, replacement.Key, StringComparison.Ordinal))
                return CreateJsonValue(replacement.Value);
        }

        var replaced = text;
        foreach (var replacement in replacements)
            replaced = replaced.Replace(replacement.Key, replacement.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);

        return JsonValue.Create(replaced);
    }

    private static JsonNode CreateJsonValue(object? value) => value switch
    {
        null => JsonValue.Create((string?)null)!,
        int number => JsonValue.Create(number)!,
        double number => JsonValue.Create(number)!,
        float number => JsonValue.Create(number)!,
        long number => JsonValue.Create(number)!,
        bool flag => JsonValue.Create(flag)!,
        _ => JsonValue.Create(value.ToString())!,
    };

    private static EasyDiffusionInfo? TryExtractEasyDiffusionInfo(JsonElement root)
    {
        var status = root.TryGetProperty("status", out var statusProp)
            ? statusProp.GetString()
            : null;

        var progress = root.TryGetProperty("progress", out var progressProp) &&
                       progressProp.TryGetDouble(out var p)
            ? p
            : (double?)null;

        // Case 1: images[]
        if (root.TryGetProperty("images", out var imagesProp) &&
            imagesProp.ValueKind == JsonValueKind.Array &&
            imagesProp.GetArrayLength() > 0)
        {
            var images = new List<EasyDiffusionImage>();

            foreach (var img in imagesProp.EnumerateArray())
            {
                var url = img.TryGetProperty("url", out var urlProp)
                    ? urlProp.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(url))
                    continue;

                var fileName = img.TryGetProperty("file_name", out var fnProp)
                    ? fnProp.GetString()
                    : null;

                images.Add(new EasyDiffusionImage(url!, fileName));
            }

            if (images.Count > 0)
            {
                return new EasyDiffusionInfo(
                    status ?? "completed",
                    progress,
                    images
                );
            }
        }

        // Case 2: output[]
        if (root.TryGetProperty("output", out var outputProp) &&
            outputProp.ValueKind == JsonValueKind.Array &&
            outputProp.GetArrayLength() > 0)
        {
            var images = new List<EasyDiffusionImage>();

            foreach (var img in outputProp.EnumerateArray())
            {
                var url = img.TryGetProperty("data", out var urlProp)
                    ? urlProp.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(url))
                    continue;

                images.Add(new EasyDiffusionImage(url!, null));
            }

            if (images.Count > 0)
            {
                return new EasyDiffusionInfo(
                    status ?? "completed",
                    progress,
                    images
                );
            }
        }

        return null;
    }

    private static ComfyImageInfo? TryExtractComfyImageInfo(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var promptEntry in root.EnumerateObject())
        {
            if (!promptEntry.Value.TryGetProperty("outputs", out var outputs)
                || outputs.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var nodeOutput in outputs.EnumerateObject())
            {
                if (!nodeOutput.Value.TryGetProperty("images", out var images)
                    || images.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var image in images.EnumerateArray())
                {
                    if (!image.TryGetProperty("filename", out var filename)
                        || filename.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(filename.GetString()))
                    {
                        continue;
                    }

                    return new ComfyImageInfo(
                        filename.GetString()!,
                        image.TryGetProperty("subfolder", out var subfolder) && subfolder.ValueKind == JsonValueKind.String
                            ? subfolder.GetString() ?? string.Empty
                            : string.Empty,
                        image.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                            ? type.GetString() ?? "output"
                            : "output");
                }
            }
        }

        return null;
    }

    private static string BuildOpenAiImagesEndpoint(string endpoint)
    {
        var normalized = NormalizeBaseUrl(endpoint);
        if (normalized.EndsWith("/images/generations", StringComparison.OrdinalIgnoreCase))
            return normalized;
        if (normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return normalized + "/images/generations";
        return normalized + "/v1/images/generations";
    }

    private static string NormalizeBaseUrl(string endpoint) => endpoint.Trim().TrimEnd('/');

    private static int ClampDimension(int value) => Math.Clamp(value <= 0 ? 1024 : value, 256, 2048);

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "No response body.";

        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "...";
    }

    private static string GuessMimeType(byte[] imageBytes, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension == ".jpg" || extension == ".jpeg") return "image/jpeg";
            if (extension == ".webp") return "image/webp";
            if (extension == ".gif") return "image/gif";
            if (extension == ".png") return "image/png";
        }

        if (imageBytes.Length >= 12
            && imageBytes[0] == 0x52 && imageBytes[1] == 0x49 && imageBytes[2] == 0x46 && imageBytes[3] == 0x46
            && imageBytes[8] == 0x57 && imageBytes[9] == 0x45 && imageBytes[10] == 0x42 && imageBytes[11] == 0x50)
        {
            return "image/webp";
        }

        if (imageBytes.Length >= 4
            && imageBytes[0] == 0x89 && imageBytes[1] == 0x50 && imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
        {
            return "image/png";
        }

        if (imageBytes.Length >= 2 && imageBytes[0] == 0xFF && imageBytes[1] == 0xD8)
            return "image/jpeg";

        return "image/png";
    }

    private static bool IsFinalStatus(string? status)
    {
        return status is "completed" or "succeeded" or "done" or "finished";
    }

    private sealed record ComfyImageInfo(string FileName, string Subfolder, string Type);

    public sealed record EasyDiffusionInfo(string Status,double? Progress,IReadOnlyList<EasyDiffusionImage> Images);

    public sealed record EasyDiffusionImage(string Url,string? FileName);
}