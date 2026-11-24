using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Markup;

namespace XNotch.Helpers;

/// <summary>
/// Localization, NotchPixel-style: the English text is the key, each language is one embedded
/// JSON file (Assets/Localization/strings.&lt;code&gt;.json). A missing entry falls back to English,
/// so an incomplete translation never shows a blank or a key.
/// </summary>
public static class Loc
{
    /// <summary>Languages shipped, as (code, native name).</summary>
    public static readonly (string Code, string Name)[] Languages =
    [
        ("en", "English"), ("es", "Español"), ("fr", "Français"), ("de", "Deutsch"),
        ("hi", "हिन्दी"), ("ja", "日本語"), ("zh-Hans", "简体中文"),
    ];

    private static Dictionary<string, string> _table = [];

    public static string Current { get; private set; } = "en";

    /// <summary>"auto" (or null) follows Windows' display language.</summary>
    public static void Init(string? language)
    {
        Current = Resolve(language, CultureInfo.CurrentUICulture);
        _table = Current == "en" ? [] : Load(Current) ?? [];
    }

    public static string Resolve(string? language, CultureInfo ui)
    {
        if (!string.IsNullOrWhiteSpace(language) && language != "auto" && Languages.Any(l => l.Code == language)) return language;
        // zh-CN / zh-SG / zh-Hans-* → zh-Hans; otherwise match the two-letter code.
        if (ui.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return ui.Name.Contains("Hant") || ui.Name is "zh-TW" or "zh-HK" or "zh-MO" ? "en" : "zh-Hans";
        var two = ui.TwoLetterISOLanguageName;
        return Languages.Any(l => l.Code == two) ? two : "en";
    }

    public static Dictionary<string, string>? Load(string code)
    {
        using var s = typeof(Loc).Assembly.GetManifestResourceStream($"XNotch.Localization.strings.{code}.json");
        if (s == null) return null;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(s);
    }

    /// <summary>Translates English text; <paramref name="args"/> fill {0}, {1}… after translation.</summary>
    public static string T(string english, params object?[] args)
    {
        var text = _table.TryGetValue(english, out var t) && !string.IsNullOrEmpty(t) ? t : english;
        if (args.Length == 0) return text;
        try { return string.Format(CultureInfo.CurrentCulture, text, args); }
        catch (FormatException) { return string.Format(CultureInfo.CurrentCulture, english, args); } // broken translation: English
    }
}

/// <summary>XAML: <c>Text="{l:L 'Clear All'}"</c>. Resolved once when the view loads.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LExtension : MarkupExtension
{
    public LExtension() { }
    public LExtension(string text) => Text = text;

    [ConstructorArgument("text")]
    public string Text { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}
