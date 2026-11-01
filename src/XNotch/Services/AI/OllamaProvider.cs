using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XNotch.Services.AI;

/// <summary>
/// Local-first AI provider communicating with an Ollama daemon running on http://localhost:11434.
/// Completely offline, private, and free.
/// </summary>
public sealed class OllamaProvider : IAIProvider
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public string ProviderName => "Ollama (Local)";
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen2.5-coder";

    public bool IsAvailable { get; private set; }

    public async Task<bool> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await HttpClient.GetAsync($"{BaseUrl}/api/version", ct);
            IsAvailable = resp.IsSuccessStatusCode;
            return IsAvailable;
        }
        catch
        {
            IsAvailable = false;
            return false;
        }
    }

    public async Task<string> GenerateCompletionAsync(string prompt, CancellationToken ct = default)
    {
        string sanitizedPrompt = ContextSanitizer.Sanitize(prompt);

        var payload = new
        {
            model = Model,
            prompt = sanitizedPrompt,
            stream = false
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        try
        {
            using var resp = await HttpClient.PostAsync($"{BaseUrl}/api/generate", content, ct);
            if (!resp.IsSuccessStatusCode)
                return $"Ollama request failed: {resp.StatusCode}";

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("response", out var rProp))
            {
                return rProp.GetString() ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            return $"Ollama unavailable: {ex.Message}";
        }

        return string.Empty;
    }
}
