using System;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using XNotch.Models;

namespace XNotch.Helpers;

/// <summary>
/// Visual presentation attributes for a Dynamic Island notification card.
/// </summary>
public sealed class NotificationVisualInfo
{
    public string Glyph { get; init; } = "🔔";
    public Brush BackgroundBrush { get; init; } = Brushes.Transparent;
    public Brush BorderBrush { get; init; } = Brushes.Transparent;
    public Brush ForegroundBrush { get; init; } = Brushes.White;
    public Color AccentColor { get; init; } = Color.FromRgb(0x0A, 0x84, 0xFF);
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public ImageSource? AppLogoSource { get; init; }
}

/// <summary>
/// Analyzes incoming Windows toast notifications and resolves authentic,
/// vibrantly colored Apple Dynamic Island icons, background badges, specular rims,
/// and matched ambient backlighting.
/// </summary>
public static class NotificationVisualResolver
{
    private static LinearGradientBrush CreateBadgeGradient(Color topColor, Color bottomColor)
    {
        var brush = new LinearGradientBrush(topColor, bottomColor, 0.0)
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1)
        };
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush CreateSolidBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static NotificationVisualInfo Resolve(NotificationItem item)
    {
        var visual = ResolveInternal(item);
        var logo = AppLogoResolver.ResolveAppLogo(item);
        if (logo != null)
        {
            return new NotificationVisualInfo
            {
                Glyph = visual.Glyph,
                BackgroundBrush = visual.BackgroundBrush,
                BorderBrush = visual.BorderBrush,
                ForegroundBrush = visual.ForegroundBrush,
                AccentColor = visual.AccentColor,
                Title = visual.Title,
                Body = visual.Body,
                AppLogoSource = logo
            };
        }
        return visual;
    }

    private static NotificationVisualInfo ResolveInternal(NotificationItem item)
    {
        string app = (item.AppName ?? "").Trim().ToLowerInvariant();
        string aumid = (item.AppUserModelId ?? "").Trim().ToLowerInvariant();
        string title = (item.Title ?? "").Trim();
        string body = (item.Body ?? "").Trim();
        string combined = $"{app} {aumid} {title.ToLowerInvariant()} {body.ToLowerInvariant()}";

        // Display title fallback
        string displayTitle = !string.IsNullOrWhiteSpace(title) 
            ? title 
            : (!string.IsNullOrWhiteSpace(item.AppName) ? item.AppName : "Notification");

        // 1. WhatsApp
        if (app.Contains("whatsapp") || aumid.Contains("whatsapp"))
        {
            return Build(
                glyph: "💬",
                topColor: Color.FromRgb(0x25, 0xD3, 0x66),
                bottomColor: Color.FromRgb(0x12, 0x8C, 0x7E),
                accentColor: Color.FromRgb(0x25, 0xD3, 0x66),
                displayTitle, body);
        }

        // 2. Telegram
        if (app.Contains("telegram") || aumid.Contains("telegram"))
        {
            return Build(
                glyph: "✈️",
                topColor: Color.FromRgb(0x2A, 0xAB, 0xEE),
                bottomColor: Color.FromRgb(0x1E, 0x88, 0xE5),
                accentColor: Color.FromRgb(0x2A, 0xAB, 0xEE),
                displayTitle, body);
        }

        // 3. Discord
        if (app.Contains("discord") || aumid.Contains("discord"))
        {
            return Build(
                glyph: "💬",
                topColor: Color.FromRgb(0x58, 0x65, 0xF2),
                bottomColor: Color.FromRgb(0x47, 0x52, 0xC4),
                accentColor: Color.FromRgb(0x58, 0x65, 0xF2),
                displayTitle, body);
        }

        // 4. Slack
        if (app.Contains("slack") || aumid.Contains("slack"))
        {
            return Build(
                glyph: "💬",
                topColor: Color.FromRgb(0xE0, 0x1E, 0x5A),
                bottomColor: Color.FromRgb(0xB7, 0x15, 0x40),
                accentColor: Color.FromRgb(0xE0, 0x1E, 0x5A),
                displayTitle, body);
        }

        // 5. Microsoft Teams
        if (app.Contains("teams") || aumid.Contains("teams"))
        {
            return Build(
                glyph: "👥",
                topColor: Color.FromRgb(0x62, 0x64, 0xA7),
                bottomColor: Color.FromRgb(0x46, 0x47, 0x75),
                accentColor: Color.FromRgb(0x62, 0x64, 0xA7),
                displayTitle, body);
        }

        // 6. Signal
        if (app.Contains("signal") || aumid.Contains("signal"))
        {
            return Build(
                glyph: "💬",
                topColor: Color.FromRgb(0x3A, 0x76, 0xF0),
                bottomColor: Color.FromRgb(0x2C, 0x5E, 0xB5),
                accentColor: Color.FromRgb(0x3A, 0x76, 0xF0),
                displayTitle, body);
        }

        // 7. Phone Link / Messages / SMS
        if (app.Contains("phone") || app.Contains("message") || aumid.Contains("phone") || aumid.Contains("message"))
        {
            return Build(
                glyph: "💬",
                topColor: Color.FromRgb(0x30, 0xD1, 0x58),
                bottomColor: Color.FromRgb(0x24, 0x8A, 0x3D),
                accentColor: Color.FromRgb(0x30, 0xD1, 0x58),
                displayTitle, body);
        }

        // 8. Outlook & Microsoft Office Mail
        if (app.Contains("outlook") || aumid.Contains("outlook"))
        {
            return Build(
                glyph: "✉️",
                topColor: Color.FromRgb(0x00, 0x78, 0xD4),
                bottomColor: Color.FromRgb(0x00, 0x5A, 0x9E),
                accentColor: Color.FromRgb(0x00, 0x78, 0xD4),
                displayTitle, body);
        }

        // 9. Generic Mail / Gmail / Thunderbird
        if (app.Contains("mail") || app.Contains("thunderbird") || aumid.Contains("mail") || combined.Contains("email"))
        {
            Color top = app.Contains("gmail") ? Color.FromRgb(0xEA, 0x43, 0x35) : Color.FromRgb(0x00, 0x7A, 0xFF);
            Color bottom = app.Contains("gmail") ? Color.FromRgb(0xC5, 0x22, 0x1F) : Color.FromRgb(0x00, 0x51, 0xA8);
            return Build(
                glyph: "✉️",
                topColor: top,
                bottomColor: bottom,
                accentColor: top,
                displayTitle, body);
        }

        // 10. Calendar / Google Calendar
        if (app.Contains("calendar") || aumid.Contains("calendar") || combined.Contains("meeting") || combined.Contains("event"))
        {
            return Build(
                glyph: "📅",
                topColor: Color.FromRgb(0xFF, 0x3B, 0x30),
                bottomColor: Color.FromRgb(0xD7, 0x00, 0x15),
                accentColor: Color.FromRgb(0xFF, 0x3B, 0x30),
                displayTitle, body);
        }

        // 11. Reminders / To-Do / Tasks / Focus
        if (app.Contains("todo") || app.Contains("to do") || app.Contains("reminder") || app.Contains("task") || app.Contains("focus") || aumid.Contains("todo"))
        {
            return Build(
                glyph: "☑️",
                topColor: Color.FromRgb(0xFF, 0x95, 0x00),
                bottomColor: Color.FromRgb(0xC9, 0x70, 0x00),
                accentColor: Color.FromRgb(0xFF, 0x95, 0x00),
                displayTitle, body);
        }

        // 12. Spotify
        if (app.Contains("spotify") || aumid.Contains("spotify"))
        {
            return Build(
                glyph: "🎵",
                topColor: Color.FromRgb(0x1D, 0xB9, 0x54),
                bottomColor: Color.FromRgb(0x16, 0x8B, 0x40),
                accentColor: Color.FromRgb(0x1D, 0xB9, 0x54),
                displayTitle, body);
        }

        // 13. Apple Music / Music
        if (app.Contains("music") || aumid.Contains("music") || app.Contains("itunes"))
        {
            return Build(
                glyph: "🎵",
                topColor: Color.FromRgb(0xFC, 0x3C, 0x44),
                bottomColor: Color.FromRgb(0xD6, 0x22, 0x2A),
                accentColor: Color.FromRgb(0xFC, 0x3C, 0x44),
                displayTitle, body);
        }

        // 14. YouTube / Video / Media
        if (app.Contains("youtube") || aumid.Contains("youtube") || app.Contains("netflix") || app.Contains("vlc"))
        {
            return Build(
                glyph: "▶️",
                topColor: Color.FromRgb(0xFF, 0x00, 0x00),
                bottomColor: Color.FromRgb(0xB3, 0x00, 0x00),
                accentColor: Color.FromRgb(0xFF, 0x00, 0x00),
                displayTitle, body);
        }

        // 15. AI / Coding Agents (Claude, Cursor, Copilot, Antigravity, Cline, etc.)
        if (app.Contains("claude") || app.Contains("cursor") || app.Contains("copilot") || app.Contains("antigravity") || app.Contains("agent") || combined.Contains("ai"))
        {
            return Build(
                glyph: "⚡",
                topColor: Color.FromRgb(0xFF, 0x9F, 0x0A),
                bottomColor: Color.FromRgb(0xD9, 0x77, 0x06),
                accentColor: Color.FromRgb(0xFF, 0x9F, 0x0A),
                displayTitle, body);
        }

        // 16. Visual Studio / VS Code
        if (app.Contains("visual studio") || app.Contains("vs code") || app == "code" || aumid.Contains("vscode"))
        {
            return Build(
                glyph: "💻",
                topColor: Color.FromRgb(0x00, 0x7A, 0xCC),
                bottomColor: Color.FromRgb(0x00, 0x5B, 0x99),
                accentColor: Color.FromRgb(0x00, 0x7A, 0xCC),
                displayTitle, body);
        }

        // 17. GitHub / Git
        if (app.Contains("github") || aumid.Contains("github") || combined.Contains("git pull") || combined.Contains("git push") || combined.Contains("commit"))
        {
            return Build(
                glyph: "🐙",
                topColor: Color.FromRgb(0xAF, 0x52, 0xDE),
                bottomColor: Color.FromRgb(0x79, 0x28, 0xCA),
                accentColor: Color.FromRgb(0xAF, 0x52, 0xDE),
                displayTitle, body);
        }

        // 18. Terminal / PowerShell / Command Prompt / WSL
        if (app.Contains("terminal") || app.Contains("powershell") || app.Contains("cmd") || aumid.Contains("terminal"))
        {
            return Build(
                glyph: "⚡",
                topColor: Color.FromRgb(0x34, 0xD3, 0x99),
                bottomColor: Color.FromRgb(0x05, 0x96, 0x69),
                accentColor: Color.FromRgb(0x34, 0xD3, 0x99),
                displayTitle, body);
        }

        // 19. Microsoft Edge
        if (app.Contains("edge") || aumid.Contains("edge"))
        {
            return Build(
                glyph: "🌐",
                topColor: Color.FromRgb(0x00, 0x78, 0xD4),
                bottomColor: Color.FromRgb(0x00, 0xB4, 0xD8),
                accentColor: Color.FromRgb(0x00, 0x78, 0xD4),
                displayTitle, body);
        }

        // 20. Google Chrome
        if (app.Contains("chrome") || aumid.Contains("chrome"))
        {
            return Build(
                glyph: "🌐",
                topColor: Color.FromRgb(0x42, 0x85, 0xF4),
                bottomColor: Color.FromRgb(0x1A, 0x73, 0xE8),
                accentColor: Color.FromRgb(0x42, 0x85, 0xF4),
                displayTitle, body);
        }

        // 21. Firefox
        if (app.Contains("firefox") || aumid.Contains("firefox"))
        {
            return Build(
                glyph: "🦊",
                topColor: Color.FromRgb(0xFF, 0x71, 0x39),
                bottomColor: Color.FromRgb(0xC8, 0x32, 0x00),
                accentColor: Color.FromRgb(0xFF, 0x71, 0x39),
                displayTitle, body);
        }

        // 22. Brave
        if (app.Contains("brave") || aumid.Contains("brave"))
        {
            return Build(
                glyph: "🦁",
                topColor: Color.FromRgb(0xFB, 0x54, 0x2B),
                bottomColor: Color.FromRgb(0xC7, 0x38, 0x12),
                accentColor: Color.FromRgb(0xFB, 0x54, 0x2B),
                displayTitle, body);
        }

        // 23. Windows Security / Defender
        if (app.Contains("security") || app.Contains("defender") || app.Contains("antivirus") || combined.Contains("threat") || combined.Contains("protection"))
        {
            return Build(
                glyph: "🛡️",
                topColor: Color.FromRgb(0x30, 0xD1, 0x58),
                bottomColor: Color.FromRgb(0x1E, 0x82, 0x38),
                accentColor: Color.FromRgb(0x30, 0xD1, 0x58),
                displayTitle, body);
        }

        // 24. Windows Update / Settings / Control Panel
        if (app.Contains("update") || app.Contains("setting") || aumid.Contains("settings") || combined.Contains("windows update"))
        {
            return Build(
                glyph: "⚙️",
                topColor: Color.FromRgb(0x64, 0xD2, 0xFF),
                bottomColor: Color.FromRgb(0x0A, 0x84, 0xFF),
                accentColor: Color.FromRgb(0x64, 0xD2, 0xFF),
                displayTitle, body);
        }

        // 25. Downloads / File Explorer
        if (app.Contains("download") || app.Contains("explorer") || aumid.Contains("explorer") || combined.Contains("downloaded"))
        {
            return Build(
                glyph: "📥",
                topColor: Color.FromRgb(0x63, 0x66, 0xF1),
                bottomColor: Color.FromRgb(0x8B, 0x5C, 0xF6),
                accentColor: Color.FromRgb(0x63, 0x66, 0xF1),
                displayTitle, body);
        }

        // 26. Alarms & Clock / Timer
        if (app.Contains("clock") || app.Contains("alarm") || app.Contains("timer") || combined.Contains("timer expired"))
        {
            return Build(
                glyph: "⏰",
                topColor: Color.FromRgb(0xFF, 0x9F, 0x0A),
                bottomColor: Color.FromRgb(0xD9, 0x77, 0x06),
                accentColor: Color.FromRgb(0xFF, 0x9F, 0x0A),
                displayTitle, body);
        }

        // 27. Snipping Tool / Screenshot
        if (app.Contains("snip") || app.Contains("screenshot") || aumid.Contains("screenclip"))
        {
            return Build(
                glyph: "✂️",
                topColor: Color.FromRgb(0xBF, 0x5A, 0xF2),
                bottomColor: Color.FromRgb(0x8E, 0x44, 0xAD),
                accentColor: Color.FromRgb(0xBF, 0x5A, 0xF2),
                displayTitle, body);
        }

        // 28. Keyword-based matching
        if (combined.Contains("call") || combined.Contains("incoming") || combined.Contains("ringing"))
        {
            return Build(
                glyph: "📞",
                topColor: Color.FromRgb(0x30, 0xD1, 0x58),
                bottomColor: Color.FromRgb(0x24, 0x8A, 0x3D),
                accentColor: Color.FromRgb(0x30, 0xD1, 0x58),
                displayTitle, body);
        }
        if (combined.Contains("error") || combined.Contains("failed") || combined.Contains("critical"))
        {
            return Build(
                glyph: "⚠️",
                topColor: Color.FromRgb(0xFF, 0x45, 0x3A),
                bottomColor: Color.FromRgb(0xD7, 0x00, 0x15),
                accentColor: Color.FromRgb(0xFF, 0x45, 0x3A),
                displayTitle, body);
        }
        if (combined.Contains("success") || combined.Contains("completed") || combined.Contains("done"))
        {
            return Build(
                glyph: "✅",
                topColor: Color.FromRgb(0x30, 0xD1, 0x58),
                bottomColor: Color.FromRgb(0x24, 0x8A, 0x3D),
                accentColor: Color.FromRgb(0x30, 0xD1, 0x58),
                displayTitle, body);
        }

        // 29. Default Dynamic Island notification (Apple Accent Blue)
        return Build(
            glyph: "🔔",
            topColor: Color.FromRgb(0x0A, 0x84, 0xFF),
            bottomColor: Color.FromRgb(0x00, 0x56, 0xB3),
            accentColor: Color.FromRgb(0x0A, 0x84, 0xFF),
            displayTitle, body);
    }

    private static NotificationVisualInfo Build(
        string glyph,
        Color topColor,
        Color bottomColor,
        Color accentColor,
        string title,
        string body)
    {
        var borderBrush = new LinearGradientBrush(
            Color.FromArgb(120, 0xFF, 0xFF, 0xFF),
            Color.FromArgb(30, 0xFF, 0xFF, 0xFF),
            90.0);
        borderBrush.Freeze();

        return new NotificationVisualInfo
        {
            Glyph = glyph,
            BackgroundBrush = CreateBadgeGradient(topColor, bottomColor),
            BorderBrush = borderBrush,
            ForegroundBrush = Brushes.White,
            AccentColor = accentColor,
            Title = title,
            Body = body
        };
    }
}
