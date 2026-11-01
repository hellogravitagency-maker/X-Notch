using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XNotch.Services.AI;

/// <summary>
/// OpenAI-compatible provider supporting any API implementing /v1/chat/completions
/// (OpenAI, Groq, OpenRouter, LocalAI, vLLM).
/// </summary>
public sealed class OpenAICompatibleProvider : IAIProvider
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public string ProviderName => "OpenAI-Compatible";
    public string EndpointUrl { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";

    public bool IsAvailable => !string.IsNullOrWhiteSpace(ApiKey);

    public async Task<string> GenerateCompletionAsync(string prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            return "API Key not configured.";

        string sanitized = ContextSanitizer.Sanitize(prompt);

        var payload = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "user", content = sanitized }
            },
            temperature = 0.3
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        try
        {
            using var resp = await HttpClient.SendAsync(request, ct);
            if (!resp.IsSuccessStatusCode)
                return $"API request failed: {resp.StatusCode}";

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
                {
                    return content.GetString() ?? string.Empty;
                }
            }
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }

        return string.Empty;
    }
}
