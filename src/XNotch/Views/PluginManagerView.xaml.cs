using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using XNotch.Plugins;
using Brush = System.Windows.Media.Brush;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace XNotch.Views;

public partial class PluginManagerView : UserControl
{
    private readonly PluginManager? _pluginManager;
    private readonly PluginLibraryService? _libraryService;
    private readonly string? _initialPluginId;
    private readonly Dictionary<string, FrameworkElement> _configurationEditors = new();
    private bool _hasTriedLibraryRefresh;
    private bool _initialConfigurationHandled;
    private IConfigurablePlugin? _activeConfigurablePlugin;
    private PluginConfigurationDefinition? _activeConfiguration;
    private string _selectedCategory = "All";
    private readonly string[] _categories = new[] { "All", "Productivity", "Theme", "Animation", "Integration", "System Utility", "Widget" };

    public PluginManagerView()
    {
        InitializeComponent();
    }

    public PluginManagerView(PluginManager pluginManager, PluginLibraryService libraryService, string? initialPluginId = null) : this()
    {
        _pluginManager = pluginManager;
        _libraryService = libraryService;
        _initialPluginId = initialPluginId;

        CloseButton.Click += (_, _) => Window.GetWindow(this)?.Close();
        CloseConfigurationButton.Click += (_, _) => CloseConfigurationPanel();
        ApplyConfigurationButton.Click += async (_, _) => await ApplyConfigurationAsync();
        RefreshLibraryButton.Click += async (_, _) => await RefreshLibrary();
        InstalledBrowseButton.Click += (_, _) => { if (TabBrowse != null) TabBrowse.IsChecked = true; };
        OpenPluginsFolderButton.Click += (_, _) => OpenPluginsFolder();

        SearchBox.TextChanged += (_, _) => ApplyBrowseFilter();
        SearchBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyBrowseFilter(); };

        Loaded += async (_, _) => await EnsureLibraryLoadedAsync();

        LoadPlugins();
    }

    private void LoadPlugins()
    {
        PluginListPanel.Children.Clear();

        if (_pluginManager == null)
            return;

        var queuedUpdateCount = _pluginManager.LoadedPlugins.Count(HasQueuedUpdate);
        var availableUpdateCount = _pluginManager.LoadedPlugins.Count(HasAvailableUpdate);
        var needsSetupCount = _pluginManager.LoadedPlugins.Count(plugin => RequiresSetup(plugin, out _));
        var noticeCount = queuedUpdateCount + availableUpdateCount + needsSetupCount;

        InstalledCountText.Text = noticeCount > 0
            ? $"{_pluginManager.LoadedPlugins.Count} plugin{(_pluginManager.LoadedPlugins.Count == 1 ? string.Empty : "s")} installed - {noticeCount} action{(noticeCount == 1 ? string.Empty : "s")} needed"
            : $"{_pluginManager.LoadedPlugins.Count} plugin{(_pluginManager.LoadedPlugins.Count == 1 ? string.Empty : "s")} installed";

        foreach (var plugin in _pluginManager.LoadedPlugins.OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            PluginListPanel.Children.Add(CreatePluginCard(plugin));
        }

        if (_pluginManager.LoadedPlugins.Count == 0)
        {
            PluginListPanel.Children.Add(CreateEmptyState(
                "No plugins installed yet.",
                "Browse our catalog to add widgets, themes, and tools to your notch."));
        }

        UpdateTabCounters();

        if (!_initialConfigurationHandled && !string.IsNullOrWhiteSpace(_initialPluginId))
        {
            _initialConfigurationHandled = true;
            OpenConfigurationForPlugin(_initialPluginId!);
        }
    }

    private Border CreatePluginCard(IPlugin plugin)
    {
        var queuedUpdate = GetQueuedManifest(plugin);
        var availableUpdate = queuedUpdate == null ? GetAvailableUpdateManifest(plugin) : null;
        var configurablePlugin = plugin as IConfigurablePlugin;
        var configDefinition = configurablePlugin?.GetConfigurationDefinition();
        var requiresSetup = configDefinition is { RequiresConfiguration: true, IsConfigured: false };
        var uninstallQueued = _pluginManager?.IsUninstallQueued(plugin.Id) == true;

        var card = new Border
        {
            Background = GetSurfaceBrush(),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var infoStack = new StackPanel();

        var topRow = new Grid();
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = plugin.Name,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = GetPrimaryBrush()
        };
        Grid.SetColumn(title, 0);
        topRow.Children.Add(title);

        var version = new TextBlock
        {
            Text = queuedUpdate != null
                ? $"v{plugin.Version} -> v{queuedUpdate.Version}"
                : availableUpdate != null
                    ? $"v{plugin.Version} -> v{availableUpdate.Version}"
                    : $"v{plugin.Version}",
            FontSize = 11,
            Foreground = queuedUpdate != null || availableUpdate != null ? GetAccentBrush() : GetSecondaryBrush(),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(version, 1);
        topRow.Children.Add(version);

        infoStack.Children.Add(topRow);

        infoStack.Children.Add(new TextBlock
        {
            Text = $"by {plugin.Author}",
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = GetMutedBrush()
        });

        infoStack.Children.Add(new TextBlock
        {
            Text = plugin.Description,
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = GetSecondaryBrush()
        });

        var tags = new WrapPanel
        {
            Margin = new Thickness(0, 12, 0, 0)
        };
        AddPluginTag(tags, plugin is IUIPlugin ? "UI" : null);
        AddPluginTag(tags, plugin is IServicePlugin ? "Service" : null);
        AddPluginTag(tags, plugin is IAnimationPlugin ? "Animation" : null);
        AddPluginTag(tags, $"ID {plugin.Id}");
        if (uninstallQueued)
        {
            AddPluginTag(tags, "Removes on restart", GetAccentSoftBrush(), GetErrorBrush());
        }
        if (requiresSetup)
        {
            AddPluginTag(tags, "Setup required", GetAccentSoftBrush(), GetAccentBrush());
        }
        else if (configurablePlugin != null)
        {
            AddPluginTag(tags, "Configurable", GetTagBrush(), GetSecondaryBrush());
        }

        infoStack.Children.Add(tags);

        if (configDefinition != null && !string.IsNullOrWhiteSpace(configDefinition.StatusText))
        {
            infoStack.Children.Add(new TextBlock
            {
                Text = configDefinition.StatusText,
                FontSize = 10,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = requiresSetup ? GetAccentBrush() : GetMutedBrush()
            });
        }

        Grid.SetColumn(infoStack, 0);
        layout.Children.Add(infoStack);

        var controlStack = new StackPanel
        {
            Margin = new Thickness(18, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        if (configurablePlugin != null)
        {
            controlStack.Children.Add(new TextBlock
            {
                Text = requiresSetup ? "Needs setup" : "Configure",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = requiresSetup ? GetAccentBrush() : GetMutedBrush()
            });

            var configureButton = new Button
            {
                Content = requiresSetup ? "Set Up" : "Configure",
                MinWidth = 94,
                Margin = new Thickness(0, 0, 0, 12),
                Style = TryGetStyle(requiresSetup ? "PluginPrimaryButton" : "PluginSecondaryButton")
            };
            configureButton.Click += (_, _) => OpenConfiguration(configurablePlugin);
            controlStack.Children.Add(configureButton);
        }

        if (queuedUpdate != null)
        {
            controlStack.Children.Add(new TextBlock
            {
                Text = "Restart needed",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = GetAccentBrush()
            });

            var restartButton = new Button
            {
                Content = "Restart",
                MinWidth = 86,
                Margin = new Thickness(0, 0, 0, 12),
                Style = TryGetStyle("PluginSecondaryButton")
            };
            restartButton.Click += (_, _) => PluginBrowserWindow.RestartApp();
            controlStack.Children.Add(restartButton);
        }
        else if (availableUpdate != null)
        {
            controlStack.Children.Add(new TextBlock
            {
                Text = "Update available",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = GetAccentBrush()
            });

            var updateButton = new Button
            {
                Content = "Update",
                MinWidth = 86,
                Margin = new Thickness(0, 0, 0, 12),
                Style = TryGetStyle("PluginSecondaryButton")
            };
            updateButton.Click += async (_, _) => await UpdatePluginAsync(plugin, availableUpdate, updateButton);
            controlStack.Children.Add(updateButton);
        }

        var enabledState = _pluginManager?.IsPluginEnabled(plugin.Id) ?? false;
        var stateLabel = new TextBlock
        {
            Text = enabledState ? "On" : "Off",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = enabledState ? GetAccentBrush() : GetMutedBrush(),
            FontWeight = FontWeights.SemiBold
        };
        controlStack.Children.Add(stateLabel);

        var switchToggle = new CheckBox
        {
            IsChecked = enabledState,
            Style = TryGetStyle("PluginSwitchStyle")
        };

        switchToggle.Checked += async (_, _) =>
        {
            stateLabel.Text = "On";
            stateLabel.Foreground = GetAccentBrush();
            if (_pluginManager != null)
            {
                await _pluginManager.EnablePluginAsync(plugin.Id);
            }
        };

        switchToggle.Unchecked += async (_, _) =>
        {
            stateLabel.Text = "Off";
            stateLabel.Foreground = GetMutedBrush();
            if (_pluginManager != null)
            {
                await _pluginManager.DisablePluginAsync(plugin.Id);
            }
        };

        controlStack.Children.Add(switchToggle);

        var uninstallButton = new Button
        {
            Content = uninstallQueued ? "Cancel removal" : "Uninstall",
            MinWidth = 94,
            Margin = new Thickness(0, 14, 0, 0),
            Style = TryGetStyle("PluginGhostButton")
        };
        uninstallButton.Click += async (_, _) => await ToggleUninstallAsync(plugin);
        controlStack.Children.Add(uninstallButton);

        Grid.SetColumn(controlStack, 1);
        layout.Children.Add(controlStack);

        card.Child = layout;
        return card;
    }

    private Border CreateEmptyState(string title, string body)
    {
        var card = new Border
        {
            Background = GetSurfaceBrush(),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(24),
            Margin = new Thickness(0, 2, 0, 0)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = GetPrimaryBrush(),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = body,
            Margin = new Thickness(0, 8, 0, 16),
            FontSize = 12,
            Foreground = GetSecondaryBrush(),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center
        });

        var browseButton = new Button
        {
            Content = "Browse Available Plugins",
            Style = TryGetStyle("PluginPrimaryButton"),
            HorizontalAlignment = HorizontalAlignment.Center,
            MinWidth = 180
        };
        browseButton.Click += (_, _) =>
        {
            if (TabBrowse != null)
                TabBrowse.IsChecked = true;
        };
        stack.Children.Add(browseButton);

        card.Child = stack;
        return card;
    }

    private async System.Threading.Tasks.Task<bool> EnsureLibraryLoadedAsync(bool forceRefresh = false, bool showErrors = false)
    {
        if (_libraryService == null)
            return false;

        if (!forceRefresh && (_hasTriedLibraryRefresh || _libraryService.AvailablePlugins.Count > 0))
        {
            InitCategoryFilters();
            ApplyBrowseFilter();
            UpdateTabCounters();
            LoadPlugins();
            return _libraryService.AvailablePlugins.Count > 0;
        }

        _hasTriedLibraryRefresh = true;

        var originalContent = RefreshLibraryButton.Content;
        RefreshLibraryButton.IsEnabled = false;
        RefreshLibraryButton.Content = "Refreshing...";

        try
        {
            var success = await _libraryService.RefreshLibraryAsync();
            InitCategoryFilters();
            ApplyBrowseFilter();
            UpdateTabCounters();
            LoadPlugins();

            if (!success && showErrors)
            {
                var detail = _libraryService.LastError ?? "Unknown error";
                MessageBox.Show($"Failed to refresh plugin library.\n\n{detail}", "Plugin Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return success;
        }
        finally
        {
            RefreshLibraryButton.IsEnabled = true;
            RefreshLibraryButton.Content = originalContent;
        }
    }

    private void AddPluginTag(System.Windows.Controls.Panel host, string? label)
    {
        AddPluginTag(host, label, GetTagBrush(), GetSecondaryBrush());
    }

    private static void AddPluginTag(System.Windows.Controls.Panel host, string? label, Brush background, Brush foreground)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;

        var pill = new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 8, 8)
        };
        pill.Child = new TextBlock
        {
            Text = label,
            FontSize = 10,
            Foreground = foreground
        };
        host.Children.Add(pill);
    }

    private async System.Threading.Tasks.Task RefreshLibrary()
    {
        var success = await EnsureLibraryLoadedAsync(forceRefresh: true, showErrors: true);
        if (success)
        {
            MessageBox.Show("Plugin library refreshed successfully!", "Plugin Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            if (_activeConfigurablePlugin != null)
            {
                OpenConfiguration(_activeConfigurablePlugin);
            }
        }
    }

    private void BrowsePlugins()
    {
        if (TabBrowse != null)
        {
            TabBrowse.IsChecked = true;
        }
    }

    private void OnTabSelectionChanged(object sender, RoutedEventArgs e)
    {
        bool isInstalled = TabInstalled?.IsChecked == true;
        if (InstalledSubheader != null)
            InstalledSubheader.Visibility = isInstalled ? Visibility.Visible : Visibility.Collapsed;
        if (InstalledContentGrid != null)
            InstalledContentGrid.Visibility = isInstalled ? Visibility.Visible : Visibility.Collapsed;
        if (BrowseSubheader != null)
            BrowseSubheader.Visibility = !isInstalled ? Visibility.Visible : Visibility.Collapsed;
        if (BrowseContentGrid != null)
            BrowseContentGrid.Visibility = !isInstalled ? Visibility.Visible : Visibility.Collapsed;

        if (!isInstalled)
        {
            ApplyBrowseFilter();
        }
    }

    private void UpdateTabCounters()
    {
        int installedCount = _pluginManager?.LoadedPlugins.Count ?? 0;
        int availableCount = _libraryService?.AvailablePlugins.Count ?? 0;

        if (TabInstalled != null)
            TabInstalled.Content = $"Installed ({installedCount})";
        if (TabBrowse != null)
            TabBrowse.Content = $"Browse Catalog ({availableCount})";
    }

    private void InitCategoryFilters()
    {
        if (CategoryFilterPanel == null)
            return;

        CategoryFilterPanel.Children.Clear();

        foreach (var category in _categories)
        {
            var isSelected = string.Equals(_selectedCategory, category, StringComparison.OrdinalIgnoreCase);

            var button = new Button
            {
                Content = category,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(14, 5, 14, 5),
                FontSize = 11,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = isSelected ? GetAccentSoftBrush() : GetSurfaceBrush(),
                Foreground = isSelected ? GetAccentBrush() : GetSecondaryBrush(),
                BorderThickness = new Thickness(0),
                Tag = category
            };

            var style = new Style(typeof(Button));
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            borderFactory.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentPresenter);

            template.VisualTree = borderFactory;
            style.Setters.Add(new Setter(Button.TemplateProperty, template));
            button.Style = style;

            button.Click += (_, _) =>
            {
                _selectedCategory = category;
                InitCategoryFilters();
                ApplyBrowseFilter();
            };

            CategoryFilterPanel.Children.Add(button);
        }
    }

    private void ApplyBrowseFilter()
    {
        if (_libraryService == null || BrowseGridPanel == null)
            return;

        var query = SearchBox?.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var plugins = _libraryService.AvailablePlugins.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_selectedCategory) && !_selectedCategory.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedSelected = _selectedCategory.Replace(" ", "").ToLowerInvariant();
            plugins = plugins.Where(p =>
                p.Category.ToString().ToLowerInvariant().Contains(normalizedSelected));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            plugins = plugins.Where(p =>
                (p.Name != null && p.Name.ToLowerInvariant().Contains(query)) ||
                (p.Description != null && p.Description.ToLowerInvariant().Contains(query)) ||
                (p.Author != null && p.Author.ToLowerInvariant().Contains(query)) ||
                p.Category.ToString().ToLowerInvariant().Contains(query));
        }

        var list = plugins
            .OrderByDescending(p => p.IsVerified)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        BrowseGridPanel.Children.Clear();

        if (ResultsText != null)
        {
            ResultsText.Text = list.Count == 0
                ? "No matching plugins"
                : $"{list.Count} plugin{(list.Count == 1 ? string.Empty : "s")} available";
        }

        if (list.Count == 0)
        {
            if (BrowseEmptyStatePanel != null)
            {
                BrowseEmptyTitleText.Text = _libraryService.AvailablePlugins.Count == 0
                    ? "Plugin library is empty or offline."
                    : "No plugins match your filter.";
                BrowseEmptyBodyText.Text = _libraryService.AvailablePlugins.Count == 0
                    ? "Click 'Refresh' to re-fetch the library catalog."
                    : "Try a different search keyword or switch the category filter.";
                BrowseEmptyStatePanel.Visibility = Visibility.Visible;
            }
        }
        else
        {
            if (BrowseEmptyStatePanel != null)
                BrowseEmptyStatePanel.Visibility = Visibility.Collapsed;

            foreach (var manifest in list)
            {
                BrowseGridPanel.Children.Add(CreateBrowseCard(manifest));
            }
        }
    }

    private Border CreateBrowseCard(PluginManifest manifest)
    {
        var isInstalled = IsPluginInstalled(manifest.Id);

        var card = new Border
        {
            Width = 285,
            Margin = new Thickness(0, 0, 14, 14),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(20),
            Background = GetSurfaceBrush()
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header: Name & Verified badge
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = manifest.Name,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = GetPrimaryBrush()
        };
        Grid.SetColumn(title, 0);
        header.Children.Add(title);

        if (manifest.IsVerified)
        {
            var verifiedTag = CreateTag("Verified", GetAccentSoftBrush(), GetAccentBrush());
            Grid.SetColumn(verifiedTag, 1);
            header.Children.Add(verifiedTag);
        }

        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        // Subtitle: Version and author
        var meta = new TextBlock
        {
            Text = $"v{manifest.Version} • by {manifest.Author}",
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = GetMutedBrush()
        };
        Grid.SetRow(meta, 1);
        layout.Children.Add(meta);

        // Description
        var description = new TextBlock
        {
            Text = manifest.Description,
            FontSize = 11.5,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 44,
            Foreground = GetSecondaryBrush()
        };
        Grid.SetRow(description, 2);
        layout.Children.Add(description);

        // Tags: Category and Min version
        var tags = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        tags.Children.Add(CreateTag(manifest.Category.ToString(), GetTagBrush(), GetSecondaryBrush()));
        if (!string.IsNullOrWhiteSpace(manifest.MinimumXNotchVersion))
        {
            tags.Children.Add(CreateTag($"Min v{manifest.MinimumXNotchVersion}", GetTagBrush(), GetMutedBrush()));
        }
        Grid.SetRow(tags, 3);
        layout.Children.Add(tags);

        // Actions: Install or Installed / Configure
        var actions = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var installButton = new Button
        {
            MinWidth = 88,
            Height = 32
        };

        if (isInstalled)
        {
            installButton.Content = "Installed";
            installButton.Style = TryGetStyle("PluginSecondaryButton");
            installButton.IsEnabled = false;
        }
        else
        {
            installButton.Content = "Install";
            installButton.Style = TryGetStyle("PluginPrimaryButton");
            installButton.Click += async (_, _) => await InstallPluginAsync(manifest, installButton);
        }

        Grid.SetColumn(installButton, 0);
        actions.Children.Add(installButton);

        // View/Details or Configure button
        if (isInstalled)
        {
            var configurable = _pluginManager?.LoadedPlugins
                .OfType<IConfigurablePlugin>()
                .FirstOrDefault(p => string.Equals(p.Id, manifest.Id, StringComparison.OrdinalIgnoreCase));

            if (configurable != null)
            {
                var configBtn = new Button
                {
                    Content = "Configure",
                    MinWidth = 76,
                    Height = 32,
                    Margin = new Thickness(8, 0, 0, 0),
                    Style = TryGetStyle("PluginGhostButton")
                };
                configBtn.Click += (_, _) =>
                {
                    if (TabInstalled != null)
                        TabInstalled.IsChecked = true;
                    OpenConfigurationForPlugin(manifest.Id);
                };
                Grid.SetColumn(configBtn, 1);
                actions.Children.Add(configBtn);
            }
        }
        else if (!string.IsNullOrWhiteSpace(manifest.Homepage))
        {
            var openPageButton = new Button
            {
                Content = "Details",
                MinWidth = 68,
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                Style = TryGetStyle("PluginGhostButton")
            };
            openPageButton.Click += (_, _) => OpenExternal(manifest.Homepage);
            Grid.SetColumn(openPageButton, 1);
            actions.Children.Add(openPageButton);
        }

        Grid.SetRow(actions, 4);
        layout.Children.Add(actions);

        card.Child = layout;
        return card;
    }

    private static Border CreateTag(string text, Brush background, Brush foreground)
    {
        var pill = new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 6, 6)
        };
        pill.Child = new TextBlock
        {
            Text = text,
            FontSize = 10,
            Foreground = foreground
        };
        return pill;
    }

    private async System.Threading.Tasks.Task InstallPluginAsync(PluginManifest manifest, Button button)
    {
        button.IsEnabled = false;
        button.Content = "Installing...";

        try
        {
            var targetDir = _pluginManager?.GetPluginsDirectory();
            if (targetDir == null || _libraryService == null)
            {
                MessageBox.Show("Plugin manager is not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                button.IsEnabled = true;
                button.Content = "Install";
                return;
            }

            var progress = new Progress<double>(value =>
            {
                button.Content = $"{Math.Round(value):0}%";
            });

            var filePath = await _libraryService.DownloadPluginAsync(manifest.Id, targetDir, progress);
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                if (_pluginManager != null)
                {
                    try
                    {
                        await _pluginManager.LoadPluginFromFileAsync(filePath);
                    }
                    catch { }
                }

                button.Content = "Installed";
                button.Style = TryGetStyle("PluginSecondaryButton");
                button.IsEnabled = false;

                LoadPlugins();
                UpdateTabCounters();
                ApplyBrowseFilter();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to install plugin: {ex.Message}", "Install Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            button.IsEnabled = true;
            button.Content = "Install";
            button.Style = TryGetStyle("PluginPrimaryButton");
        }
    }

    private bool IsPluginInstalled(string pluginId)
    {
        if (_pluginManager?.LoadedPlugins.Any(p => string.Equals(p.Id, pluginId, StringComparison.OrdinalIgnoreCase)) == true)
            return true;

        var dir = _pluginManager?.GetPluginsDirectory();
        if (!string.IsNullOrWhiteSpace(dir))
        {
            var pluginDir = Path.Combine(dir, pluginId);
            if (Directory.Exists(pluginDir))
            {
                return File.Exists(Path.Combine(pluginDir, "manifest.json"))
                    || Directory.EnumerateFiles(pluginDir, "*.dll", SearchOption.TopDirectoryOnly).Any()
                    || Directory.EnumerateFiles(pluginDir, "*.pending", SearchOption.TopDirectoryOnly).Any();
            }
        }

        return false;
    }

    private static void OpenExternal(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void OpenPluginsFolder()
    {
        if (_pluginManager == null)
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = _pluginManager.GetPluginsDirectory(),
            UseShellExecute = true
        });
    }

    public void OpenConfigurationForPlugin(string pluginId)
    {
        if (_pluginManager == null)
            return;

        if (TabInstalled != null && TabInstalled.IsChecked != true)
        {
            TabInstalled.IsChecked = true;
        }

        var plugin = _pluginManager.LoadedPlugins
            .OfType<IConfigurablePlugin>()
            .FirstOrDefault(candidate => string.Equals(candidate.Id, pluginId, StringComparison.OrdinalIgnoreCase));

        if (plugin != null)
        {
            OpenConfiguration(plugin);
        }
    }

    private void OpenConfiguration(IConfigurablePlugin plugin)
    {
        _activeConfigurablePlugin = plugin;
        _activeConfiguration = plugin.GetConfigurationDefinition();
        _configurationEditors.Clear();
        ConfigurationFieldHost.Children.Clear();
        ConfigurationApplyResultText.Visibility = Visibility.Collapsed;
        ConfigurationApplyResultText.Text = string.Empty;
        ConfigurationApplyResultText.Foreground = GetPositiveBrush();

        ConfigurationTitleText.Text = _activeConfiguration.Title;
        ConfigurationBodyText.Text = _activeConfiguration.Description;
        ConfigurationStatusText.Text = _activeConfiguration.StatusText;
        ApplyConfigurationButton.Content = _activeConfiguration.RequiresConfiguration && !_activeConfiguration.IsConfigured
            ? "Apply and Finish Setup"
            : "Apply Configuration";

        foreach (var section in _activeConfiguration.Sections)
        {
            ConfigurationFieldHost.Children.Add(CreateSectionTitle(section.Title, section.Description));

            foreach (var field in section.Fields)
            {
                ConfigurationFieldHost.Children.Add(CreateFieldEditor(field));
            }
        }

        ConfigurationColumn.Width = new GridLength(336);
        ConfigurationPanelBorder.Visibility = Visibility.Visible;
    }

    private void CloseConfigurationPanel()
    {
        _activeConfigurablePlugin = null;
        _activeConfiguration = null;
        _configurationEditors.Clear();
        ConfigurationFieldHost.Children.Clear();
        ConfigurationApplyResultText.Visibility = Visibility.Collapsed;
        ConfigurationPanelBorder.Visibility = Visibility.Collapsed;
        ConfigurationColumn.Width = new GridLength(0);
    }

    private UIElement CreateSectionTitle(string title, string description)
    {
        var stack = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 14)
        };

        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = GetPrimaryBrush()
        });

        if (!string.IsNullOrWhiteSpace(description))
        {
            stack.Children.Add(new TextBlock
            {
                Text = description,
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Foreground = GetMutedBrush()
            });
        }

        return stack;
    }

    private UIElement CreateFieldEditor(PluginConfigurationField field)
    {
        var stack = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 14)
        };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Children.Add(new TextBlock
        {
            Text = field.IsRequired ? $"{field.Label} *" : field.Label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = GetPrimaryBrush()
        });

        if (!string.IsNullOrWhiteSpace(field.HelpText))
        {
            var helpButton = new Button
            {
                Content = "?",
                ToolTip = field.HelpText,
                Margin = new Thickness(8, 0, 0, 0),
                Style = TryGetStyle("PluginHelpButton")
            };
            Grid.SetColumn(helpButton, 1);
            header.Children.Add(helpButton);
        }

        stack.Children.Add(header);

        var editor = CreateEditor(field);
        editor.Margin = new Thickness(0, 8, 0, 0);
        _configurationEditors[field.Key] = editor;
        stack.Children.Add(editor);

        if (!string.IsNullOrWhiteSpace(field.Placeholder))
        {
            stack.Children.Add(new TextBlock
            {
                Text = field.Placeholder,
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Foreground = GetMutedBrush()
            });
        }

        return stack;
    }

    private FrameworkElement CreateEditor(PluginConfigurationField field)
    {
        return field.FieldType switch
        {
            PluginConfigurationFieldType.Multiline => new TextBox
            {
                Text = field.Value,
                Style = TryGetStyle("PluginFieldMultilineTextBox")
            },
            PluginConfigurationFieldType.Choice => CreateChoiceEditor(field),
            _ => new TextBox
            {
                Text = field.Value,
                Style = TryGetStyle("PluginFieldTextBox")
            }
        };
    }

    private FrameworkElement CreateChoiceEditor(PluginConfigurationField field)
    {
        var comboBox = new ComboBox
        {
            Style = TryGetStyle("PluginFieldComboBox"),
            SelectedValuePath = "Value",
            DisplayMemberPath = "Label"
        };

        foreach (var option in field.Options)
        {
            comboBox.Items.Add(option);
        }

        comboBox.SelectedValue = field.Value;
        if (comboBox.SelectedIndex < 0 && comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }

        return comboBox;
    }

    private async System.Threading.Tasks.Task ApplyConfigurationAsync()
    {
        if (_activeConfigurablePlugin == null || _activeConfiguration == null)
            return;

        ApplyConfigurationButton.IsEnabled = false;
        var originalContent = ApplyConfigurationButton.Content;
        ApplyConfigurationButton.Content = "Applying...";
        ConfigurationApplyResultText.Visibility = Visibility.Collapsed;

        try
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _configurationEditors)
            {
                values[pair.Key] = ReadEditorValue(pair.Value);
            }

            var result = await _activeConfigurablePlugin.ApplyConfigurationAsync(values);
            ConfigurationApplyResultText.Text = result.Message;
            ConfigurationApplyResultText.Foreground = result.Success ? GetPositiveBrush() : GetErrorBrush();
            ConfigurationApplyResultText.Visibility = Visibility.Visible;

            if (!result.Success)
                return;

            OpenConfiguration(_activeConfigurablePlugin);
            LoadPlugins();
        }
        catch (Exception ex)
        {
            ConfigurationApplyResultText.Text = ex.Message;
            ConfigurationApplyResultText.Foreground = GetErrorBrush();
            ConfigurationApplyResultText.Visibility = Visibility.Visible;
        }
        finally
        {
            ApplyConfigurationButton.IsEnabled = true;
            ApplyConfigurationButton.Content = originalContent;
        }
    }

    private static string ReadEditorValue(FrameworkElement editor)
    {
        return editor switch
        {
            TextBox textBox => textBox.Text,
            ComboBox comboBox => comboBox.SelectedValue?.ToString()
                ?? (comboBox.SelectedItem as PluginConfigurationOption)?.Value
                ?? string.Empty,
            _ => string.Empty
        };
    }

    private async System.Threading.Tasks.Task UpdatePluginAsync(IPlugin plugin, PluginManifest manifest, Button updateButton)
    {
        if (_libraryService == null || _pluginManager == null)
            return;

        updateButton.IsEnabled = false;
        var originalContent = updateButton.Content;

        try
        {
            var targetDir = _pluginManager.GetPluginsDirectory();
            var progress = new Progress<double>(value =>
            {
                updateButton.Content = $"{Math.Round(value):0}%";
            });

            var filePath = await _libraryService.DownloadPluginAsync(plugin.Id, targetDir, progress);
            if (filePath == null)
            {
                throw new InvalidOperationException($"Failed to update {plugin.Name}.");
            }

            updateButton.Content = "Queued";
            LoadPlugins();
            PluginBrowserWindow.ShowRestartPopup($"{plugin.Name} will update to v{manifest.Version}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to update plugin: {ex.Message}", "Plugin Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            updateButton.IsEnabled = true;
            updateButton.Content = originalContent;
        }
    }

    private async System.Threading.Tasks.Task ToggleUninstallAsync(IPlugin plugin)
    {
        if (_pluginManager == null)
            return;

        if (_pluginManager.IsUninstallQueued(plugin.Id))
        {
            _pluginManager.CancelUninstall(plugin.Id);
            LoadPlugins();
            return;
        }

        var confirm = MessageBox.Show(
            $"Uninstall {plugin.Name}?\n\nThe plugin is disabled immediately and its files are removed the next time X-Notch starts.",
            "Uninstall Plugin",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        if (!_pluginManager.QueueUninstall(plugin.Id))
        {
            MessageBox.Show($"Couldn't queue {plugin.Name} for removal — see plugin.log.",
                "Plugin Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await _pluginManager.DisablePluginAsync(plugin.Id);
        LoadPlugins();
        PluginBrowserWindow.ShowRestartPopup($"{plugin.Name} will be removed.");
    }

    private bool HasAvailableUpdate(IPlugin plugin)
    {
        return GetAvailableUpdateManifest(plugin) != null;
    }

    private bool HasQueuedUpdate(IPlugin plugin)
    {
        return GetQueuedManifest(plugin) != null;
    }

    private bool RequiresSetup(IPlugin plugin, out PluginConfigurationDefinition? definition)
    {
        definition = (plugin as IConfigurablePlugin)?.GetConfigurationDefinition();
        return definition is { RequiresConfiguration: true, IsConfigured: false };
    }

    private PluginManifest? GetQueuedManifest(IPlugin plugin)
    {
        if (_pluginManager == null || _libraryService == null)
            return null;

        var installedManifest = _libraryService.ReadInstalledManifest(plugin.Id, _pluginManager.GetPluginsDirectory());
        if (installedManifest == null)
            return null;

        return CompareVersions(installedManifest.Version, plugin.Version) > 0
            ? installedManifest
            : null;
    }

    private PluginManifest? GetAvailableManifest(string pluginId)
    {
        return _libraryService?.GetAvailablePlugin(pluginId);
    }

    private PluginManifest? GetAvailableUpdateManifest(IPlugin plugin)
    {
        var availableManifest = GetAvailableManifest(plugin.Id);
        if (availableManifest == null)
            return null;

        if (CompareVersions(availableManifest.Version, plugin.Version) <= 0)
            return null;

        var queuedManifest = GetQueuedManifest(plugin);
        if (queuedManifest != null && CompareVersions(queuedManifest.Version, availableManifest.Version) >= 0)
            return null;

        return availableManifest;
    }

    private static int CompareVersions(string left, string right)
    {
        if (Version.TryParse(left, out var leftVersion) && Version.TryParse(right, out var rightVersion))
            return leftVersion.CompareTo(rightVersion);

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        Window.GetWindow(this)?.DragMove();
    }

    private Brush GetPrimaryBrush() => GetBrush("TextPrimaryBrush", Color.FromRgb(255, 255, 255));
    private Brush GetSecondaryBrush() => GetBrush("TextSecondaryBrush", Color.FromRgb(204, 204, 204));
    private Brush GetMutedBrush() => GetBrush("TextMutedBrush", Color.FromRgb(136, 136, 136));
    private Brush GetSurfaceBrush() => GetBrush("SegmentedBgBrush", Color.FromRgb(26, 26, 28));
    private Brush GetTagBrush() => GetBrush("HoverOverlayBrush", Color.FromArgb(24, 255, 255, 255));
    private Brush GetAccentBrush() => GetBrush("PluginAccentBrush", Color.FromRgb(10, 132, 255));
    private Brush GetAccentSoftBrush() => GetBrush("PluginAccentSoftBrush", Color.FromArgb(31, 10, 132, 255));
    private Brush GetPositiveBrush() => GetBrush("PluginPositiveBrush", Color.FromRgb(52, 199, 89));
    private Brush GetErrorBrush() => GetBrush("PluginNegativeBrush", Color.FromRgb(255, 107, 107));

    private Brush GetBrush(string resourceKey, Color fallback)
    {
        if (TryFindResource(resourceKey) is Brush localBrush)
            return localBrush;

        if (Application.Current?.TryFindResource(resourceKey) is Brush applicationBrush)
            return applicationBrush;

        return new SolidColorBrush(fallback);
    }

    private Style? TryGetStyle(string resourceKey)
    {
        return TryFindResource(resourceKey) as Style
            ?? Application.Current?.TryFindResource(resourceKey) as Style;
    }
}
