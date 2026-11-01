using System.Text.RegularExpressions;

namespace XNotch.Services.AI;

/// <summary>
/// Redacts sensitive API keys, secrets, tokens, and private paths before passing context to AI providers.
/// </summary>
public static class ContextSanitizer
{
    private static readonly Regex SecretPatterns = new(
        @"(?:sk-[a-zA-Z0-9]{20,}|ghp_[a-zA-Z0-9]{20,}|AIza[0-9A-Za-z-_]{35}|bearer\s+[a-zA-Z0-9_\-\.]{20,}|password\s*[:=]\s*[""'][^""']+[""'])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return SecretPatterns.Replace(input, "[REDACTED_SECRET]");
    }
}
