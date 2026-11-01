using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using XNotch.Models;
using XNotch.Plugins;

namespace QuickNotesPlugin;

[XNotchPlugin("com.xnotch.quicknotes", "Quick Notes", "1.0.0", "X-Notch Team")]
public class QuickNotesPlugin : PluginBase, IUIPlugin, IConfigurablePlugin
{
    public override string Id => "com.xnotch.quicknotes";
    public override string Name => "Quick Notes";
    public override string Version => "1.0.0";
    public override string Author => "X-Notch Team";
    public override string Description => "Instant scratchpad & task notes right inside the dynamic island with auto-save & clipboard copy.";
    public override string MinimumXNotchVersion => "0.7.0";

    private string _noteContent = "";
    private string _saveFilePath = "";
    private TextBox? _noteBox;
    private TextBlock? _closedLabel;
    private TextBlock? _statusText;
    private DispatcherTimer? _saveDebounceTimer;
    private int _fontSize = 12;

    public override async Task InitializeAsync(IPluginContext context)
    {
        await base.InitializeAsync(context);

        try
        {
            var dataDir = context.GetPluginDataPath(Id);
            Directory.CreateDirectory(dataDir);
            _saveFilePath = Path.Combine(dataDir, "quicknotes.txt");

            if (File.Exists(_saveFilePath))
            {
                _noteContent = await File.ReadAllTextAsync(_saveFilePath);
            }
            else
            {
                _noteContent = "- [ ] Review project updates\n- [ ] Try out X-Notch Liquid Glass theme\n💡 Quick thought: Awesome dynamic island!";
            }
        }
        catch (Exception ex)
        {
            context.Log($"QuickNotes initialization note: {ex.Message}", PluginLogLevel.Warning);
        }

        _saveDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveDebounceTimer.Tick += (s, e) =>
        {
            _saveDebounceTimer.Stop();
            SaveNotesToDisk();
        };
    }

    public UIElement? GetUIElement(UIPluginLocation location)
    {
        return location switch
        {
            UIPluginLocation.ClosedContent => BuildClosedContent(),
            UIPluginLocation.OpenContent => BuildOpenContent(isVertical: false),
            UIPluginLocation.VerticalOpenContent => BuildOpenContent(isVertical: true),
            UIPluginLocation.CustomTab => BuildOpenContent(isVertical: false),
            _ => null
        };
    }

    public void OnNotchStateChanged(NotchState newState)
    {
        if (newState == NotchState.Open && _noteBox != null)
        {
            _noteBox.Text = _noteContent;
            UpdateCounters();
        }
    }

    private UIElement BuildClosedContent()
    {
        var preview = GetFirstLinePreview(_noteContent);

        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0),
            Cursor = Cursors.Hand,
            ToolTip = "Quick Notes (Click to open)"
        };

        var icon = new TextBlock
        {
            Text = "📝",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };

        _closedLabel = new TextBlock
        {
            Text = preview,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 90,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        stack.Children.Add(icon);
        stack.Children.Add(_closedLabel);

        return stack;
    }

    private UIElement BuildOpenContent(bool isVertical)
    {
        var mainContainer = new Grid
        {
            Margin = new Thickness(14, 10, 14, 10)
        };

        mainContainer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        mainContainer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Row 0: Header with action toolbar
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleBlock = new StackPanel { Orientation = Orientation.Horizontal };
        titleBlock.Children.Add(new TextBlock
        {
            Text = "📝 Quick Notes",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(toolbar, 1);

        var btnCopy = CreateMiniButton("📋 Copy", "Copy entire note to clipboard");
        btnCopy.Click += (s, e) =>
        {
            if (!string.IsNullOrEmpty(_noteContent))
            {
                Clipboard.SetText(_noteContent);
                ShowStatusMessage("Copied to clipboard! ✓");
            }
        };

        var btnTodo = CreateMiniButton("☑ Todo", "Insert checklist item");
        btnTodo.Click += (s, e) =>
        {
            InsertTextAtCursor("- [ ] ");
        };

        var btnTimestamp = CreateMiniButton("⏱ Time", "Insert timestamp");
        btnTimestamp.Click += (s, e) =>
        {
            InsertTextAtCursor($"[{DateTime.Now:hh:mm tt}] ");
        };

        var btnClear = CreateMiniButton("🗑️ Clear", "Clear note content");
        btnClear.Click += (s, e) =>
        {
            if (_noteBox != null)
                _noteBox.Text = "";
            _noteContent = "";
            SaveNotesToDisk();
            ShowStatusMessage("Cleared ✓");
        };

        toolbar.Children.Add(btnCopy);
        toolbar.Children.Add(btnTodo);
        toolbar.Children.Add(btnTimestamp);
        toolbar.Children.Add(btnClear);

        headerGrid.Children.Add(titleBlock);
        headerGrid.Children.Add(toolbar);
        Grid.SetRow(headerGrid, 0);
        mainContainer.Children.Add(headerGrid);

        // Row 1: The Note TextBox
        var boxBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            MinHeight = isVertical ? 180 : 100,
            MaxHeight = isVertical ? 350 : 180
        };

        _noteBox = new TextBox
        {
            Text = _noteContent,
            FontSize = _fontSize,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Segoe UI, Cascadia Code, Consolas")
        };

        _noteBox.TextChanged += (s, e) =>
        {
            _noteContent = _noteBox.Text;
            UpdateCounters();
            _saveDebounceTimer?.Stop();
            _saveDebounceTimer?.Start();
        };

        boxBorder.Child = _noteBox;
        Grid.SetRow(boxBorder, 1);
        mainContainer.Children.Add(boxBorder);

        // Row 2: Status & Word Count Footer
        var footerGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _statusText = new TextBlock
        {
            Text = "Auto-saved to local disk ✓",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_statusText, 0);

        var countText = new TextBlock
        {
            Text = CalculateCountersText(_noteContent),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(countText, 1);

        footerGrid.Children.Add(_statusText);
        footerGrid.Children.Add(countText);
        Grid.SetRow(footerGrid, 2);
        mainContainer.Children.Add(footerGrid);

        return mainContainer;
    }

    private void InsertTextAtCursor(string snippet)
    {
        if (_noteBox == null) return;

        int index = _noteBox.CaretIndex;
        if (index < 0 || index > _noteBox.Text.Length) index = _noteBox.Text.Length;

        _noteBox.Text = _noteBox.Text.Insert(index, snippet);
        _noteBox.CaretIndex = index + snippet.Length;
        _noteBox.Focus();
    }

    private void UpdateCounters()
    {
        if (_closedLabel != null)
            _closedLabel.Text = GetFirstLinePreview(_noteContent);
    }

    private void ShowStatusMessage(string message)
    {
        if (_statusText != null)
        {
            _statusText.Text = message;
        }
    }

    private void SaveNotesToDisk()
    {
        try
        {
            if (!string.IsNullOrEmpty(_saveFilePath))
            {
                File.WriteAllText(_saveFilePath, _noteContent);
                ShowStatusMessage("Auto-saved to local disk ✓");
            }
        }
        catch { }
    }

    private static string GetFirstLinePreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Empty note";
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return "Empty note";
        var first = lines[0].TrimStart('-', '[', ']', ' ', '*');
        return string.IsNullOrWhiteSpace(first) ? "Quick Note" : first;
    }

    public static string CalculateCountersText(string text)
    {
        int chars = text?.Length ?? 0;
        int words = string.IsNullOrWhiteSpace(text) ? 0 : Regex.Matches(text, @"\b\w+\b").Count;
        return $"{words} words | {chars} chars";
    }

    private static Button CreateMiniButton(string content, string tooltip)
    {
        return new Button
        {
            Content = content,
            ToolTip = tooltip,
            FontSize = 10,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(7, 3, 7, 3),
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
    }

    // --- IConfigurablePlugin ---

    public PluginConfigurationDefinition GetConfigurationDefinition()
    {
        return new PluginConfigurationDefinition
        {
            Title = "Quick Notes Configuration",
            Description = "Configure font size and defaults for the dynamic island scratchpad.",
            StatusText = $"Current Font Size: {_fontSize}pt",
            IsConfigured = true,
            RequiresConfiguration = false,
            Sections = new[]
            {
                new PluginConfigurationSection
                {
                    Title = "Editor Options",
                    Description = "Customize appearance and formatting.",
                    Fields = new[]
                    {
                        new PluginConfigurationField
                        {
                            Key = "FontSize",
                            Label = "Editor Font Size",
                            HelpText = "Select the font size for the scratchpad (10, 12, 14, or 16).",
                            FieldType = PluginConfigurationFieldType.Choice,
                            Value = _fontSize.ToString(),
                            Options = new[]
                            {
                                new PluginConfigurationOption { Value = "10", Label = "Compact (10pt)" },
                                new PluginConfigurationOption { Value = "12", Label = "Standard (12pt)" },
                                new PluginConfigurationOption { Value = "14", Label = "Large (14pt)" },
                                new PluginConfigurationOption { Value = "16", Label = "Extra Large (16pt)" }
                            }
                        }
                    }
                }
            }
        };
    }

    public Task<PluginConfigurationResult> ApplyConfigurationAsync(IReadOnlyDictionary<string, string?> values)
    {
        if (values.TryGetValue("FontSize", out var sizeStr) && int.TryParse(sizeStr, out var size) && size >= 8 && size <= 24)
        {
            _fontSize = size;
            if (_noteBox != null)
                _noteBox.FontSize = size;
            return Task.FromResult(PluginConfigurationResult.Ok($"Font size updated to {size}pt"));
        }

        return Task.FromResult(PluginConfigurationResult.Ok("Settings saved."));
    }
}
