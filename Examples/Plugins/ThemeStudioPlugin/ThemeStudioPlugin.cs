using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using XNotch.Models;
using XNotch.Plugins;
using XNotch.Services;

namespace ThemeStudioPlugin;

public record ThemePreset(
    string Name,
    string Icon,
    string AccentHex,
    string BackgroundHex,
    string TextHex,
    string Description
);

[XNotchPlugin("com.xnotch.themestudio", "Theme Studio", "1.0.0", "X-Notch Team")]
public class ThemeStudioPlugin : PluginBase, IUIPlugin, IConfigurablePlugin
{
    public override string Id => "com.xnotch.themestudio";
    public override string Name => "Theme Studio";
    public override string Version => "1.0.0";
    public override string Author => "X-Notch Team";
    public override string Description => "Curated color themes, ambient neon presets, and dynamic live styling for X-Notch.";
    public override string MinimumXNotchVersion => "0.7.0";

    public static readonly IReadOnlyList<ThemePreset> Presets = new List<ThemePreset>
    {
        new("Cyberpunk Neon", "⚡", "#00F0FF", "#0A0E1A", "#FFFFFF", "Vibrant neon cyan & electric dark indigo"),
        new("Aurora Borealis", "🌌", "#00FFA3", "#0E1524", "#F0FDF4", "Emerald flare over arctic polar midnight"),
        new("Sunset Gold", "🌅", "#FF8C00", "#1C0B12", "#FFF1F2", "Warm golden amber & crimson coral evening"),
        new("OLED Pure Black", "⬛", "#007AFF", "#000000", "#FFFFFF", "Zero-power abyss black with macOS ice blue"),
        new("Matrix Terminal", "💻", "#00FF66", "#041407", "#E0FFE5", "Retro phosphor green & deep matrix black"),
        new("Liquid Lavender", "🔮", "#C084FC", "#170F2C", "#FAF5FF", "Ethereal violet mist & dreamy twilight glass")
    };

    private ThemeService? _themeService;
    private AppSettings? _appSettings;
    private int _selectedPresetIndex;
    private Ellipse? _closedDot;
    private TextBlock? _closedText;
    private StackPanel? _closedContainer;
    private StackPanel? _openPanel;
    private TextBlock? _statusLabel;
    private string? _saveFilePath;

    public override async Task InitializeAsync(IPluginContext context)
    {
        await base.InitializeAsync(context);

        _themeService = context.ThemeService;
        _appSettings = context.Settings;

        try
        {
            var dataDir = context.GetPluginDataPath(Id);
            Directory.CreateDirectory(dataDir);
            _saveFilePath = System.IO.Path.Combine(dataDir, "active_preset.txt");

            if (File.Exists(_saveFilePath))
            {
                var savedName = File.ReadAllText(_saveFilePath).Trim();
                for (int i = 0; i < Presets.Count; i++)
                {
                    if (string.Equals(Presets[i].Name, savedName, StringComparison.OrdinalIgnoreCase))
                    {
                        _selectedPresetIndex = i;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            context.Log($"ThemeStudio initialization notice: {ex.Message}", PluginLogLevel.Warning);
        }
    }

    public UIElement? GetUIElement(UIPluginLocation location)
    {
        return location switch
        {
            UIPluginLocation.ClosedContent => BuildClosedContent(),
            UIPluginLocation.OpenContent => BuildOpenContent(),
            UIPluginLocation.Settings => BuildOpenContent(),
            _ => null
        };
    }

    public void OnNotchStateChanged(NotchState newState)
    {
        // Visual updates on open if needed
    }

    private UIElement BuildClosedContent()
    {
        var preset = Presets[_selectedPresetIndex];
        var accentColor = ParseColor(preset.AccentHex, Colors.Cyan);

        _closedContainer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0),
            Cursor = Cursors.Hand,
            ToolTip = $"Theme Studio: {preset.Name} (Click to open)"
        };

        _closedDot = new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = new SolidColorBrush(accentColor),
            Margin = new Thickness(0, 0, 4, 0)
        };

        _closedText = new TextBlock
        {
            Text = preset.Icon,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };

        _closedContainer.Children.Add(_closedDot);
        _closedContainer.Children.Add(_closedText);

        return _closedContainer;
    }

    private UIElement BuildOpenContent()
    {
        _openPanel = new StackPanel
        {
            Margin = new Thickness(14, 10, 14, 10)
        };

        // Header
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleBlock = new StackPanel { Orientation = Orientation.Horizontal };
        titleBlock.Children.Add(new TextBlock
        {
            Text = "🎨 Theme Studio",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });

        _statusLabel = new TextBlock
        {
            Text = "Active: " + Presets[_selectedPresetIndex].Name,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_statusLabel, 1);

        headerGrid.Children.Add(titleBlock);
        headerGrid.Children.Add(_statusLabel);
        _openPanel.Children.Add(headerGrid);

        // Preset cards container (2 rows of 3 presets)
        var wrapPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };

        for (int i = 0; i < Presets.Count; i++)
        {
            int index = i;
            var p = Presets[i];
            var isSelected = (index == _selectedPresetIndex);

            var cardBorder = new Border
            {
                Width = 140,
                Height = 48,
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(ParseColor(p.BackgroundHex, Color.FromRgb(20, 20, 20))),
                BorderBrush = isSelected
                    ? new SolidColorBrush(ParseColor(p.AccentHex, Colors.White))
                    : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                Cursor = Cursors.Hand,
                Padding = new Thickness(8, 6, 8, 6)
            };

            var cardStack = new StackPanel { Orientation = Orientation.Horizontal };

            var iconCircle = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(ParseColor(p.AccentHex, Colors.Cyan)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0),
                Child = new TextBlock
                {
                    Text = p.Icon,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            cardStack.Children.Add(iconCircle);

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = p.Name,
                FontSize = 11,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
                Foreground = isSelected ? Brushes.White : new SolidColorBrush(Color.FromRgb(220, 220, 220))
            });
            textStack.Children.Add(new TextBlock
            {
                Text = p.AccentHex,
                FontSize = 9,
                Foreground = new SolidColorBrush(ParseColor(p.AccentHex, Colors.Gray))
            });
            cardStack.Children.Add(textStack);

            cardBorder.Child = cardStack;

            cardBorder.MouseLeftButtonUp += (s, e) =>
            {
                SelectAndApplyPreset(index);
            };

            wrapPanel.Children.Add(cardBorder);
        }

        _openPanel.Children.Add(wrapPanel);

        // Actions
        var actionsGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var descriptionBlock = new TextBlock
        {
            Text = Presets[_selectedPresetIndex].Description,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(descriptionBlock, 0);

        var resetBtn = new Button
        {
            Content = "Reset Dark",
            FontSize = 11,
            Padding = new Thickness(12, 4, 12, 4),
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        Grid.SetColumn(resetBtn, 1);
        resetBtn.Click += (s, e) =>
        {
            _themeService?.Apply(AppTheme.Dark);
            if (_statusLabel != null)
                _statusLabel.Text = "Reset to Default Dark";
        };

        actionsGrid.Children.Add(descriptionBlock);
        actionsGrid.Children.Add(resetBtn);
        _openPanel.Children.Add(actionsGrid);

        return _openPanel;
    }

    private void SelectAndApplyPreset(int index)
    {
        if (index < 0 || index >= Presets.Count) return;

        _selectedPresetIndex = index;
        var preset = Presets[index];

        if (_appSettings != null)
        {
            _appSettings.Theme = AppTheme.Custom;
            _appSettings.CustomAccentColor = preset.AccentHex;
            _appSettings.CustomBackgroundColor = preset.BackgroundHex;
            _appSettings.CustomTextColor = preset.TextHex;
        }

        _themeService?.Apply(
            AppTheme.Custom,
            useLiquidGlass: false,
            customAccent: preset.AccentHex,
            customBg: preset.BackgroundHex,
            customText: preset.TextHex);

        if (_closedDot != null)
            _closedDot.Fill = new SolidColorBrush(ParseColor(preset.AccentHex, Colors.Cyan));
        if (_closedText != null)
            _closedText.Text = preset.Icon;
        if (_closedContainer != null)
            _closedContainer.ToolTip = $"Theme Studio: {preset.Name} (Click to open)";
        if (_statusLabel != null)
            _statusLabel.Text = "Applied: " + preset.Name;

        try
        {
            if (!string.IsNullOrEmpty(_saveFilePath))
                File.WriteAllText(_saveFilePath, preset.Name);
        }
        catch { }

        // Refresh open panel visuals
        BuildOpenContent();
    }

    public PluginConfigurationDefinition GetConfigurationDefinition()
    {
        var options = new List<PluginConfigurationOption>();
        for (int i = 0; i < Presets.Count; i++)
        {
            options.Add(new PluginConfigurationOption
            {
                Value = i.ToString(),
                Label = $"{Presets[i].Icon} {Presets[i].Name} ({Presets[i].AccentHex})"
            });
        }

        return new PluginConfigurationDefinition
        {
            Title = "Theme Studio Configuration",
            Description = "Configure default styling and startup presets for X-Notch.",
            StatusText = $"Active Theme: {Presets[_selectedPresetIndex].Name}",
            IsConfigured = true,
            RequiresConfiguration = false,
            Sections = new[]
            {
                new PluginConfigurationSection
                {
                    Title = "Startup Preset",
                    Description = "Choose which dynamic theme to apply when X-Notch starts up.",
                    Fields = new[]
                    {
                        new PluginConfigurationField
                        {
                            Key = "StartupPresetIndex",
                            Label = "Active Theme Preset",
                            HelpText = "Select your favorite preset from Cyberpunk, Aurora, Sunset, OLED, Matrix or Lavender.",
                            FieldType = PluginConfigurationFieldType.Choice,
                            Value = _selectedPresetIndex.ToString(),
                            Options = options
                        }
                    }
                }
            }
        };
    }

    public Task<PluginConfigurationResult> ApplyConfigurationAsync(IReadOnlyDictionary<string, string?> values)
    {
        if (values.TryGetValue("StartupPresetIndex", out var indexStr) &&
            int.TryParse(indexStr, out var index) &&
            index >= 0 && index < Presets.Count)
        {
            SelectAndApplyPreset(index);
            return Task.FromResult(PluginConfigurationResult.Ok($"Applied theme: {Presets[index].Name}"));
        }

        return Task.FromResult(PluginConfigurationResult.Ok("Configuration saved."));
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            hex = hex.Trim().TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex[..2], 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return Color.FromRgb(r, g, b);
            }
            if (hex.Length == 8)
            {
                byte a = Convert.ToByte(hex[..2], 16);
                byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                byte b = Convert.ToByte(hex.Substring(6, 2), 16);
                return Color.FromArgb(a, r, g, b);
            }
        }
        catch { }
        return fallback;
    }
}
