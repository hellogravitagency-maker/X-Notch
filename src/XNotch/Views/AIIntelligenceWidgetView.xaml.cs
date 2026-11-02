using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XNotch.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace XNotch.Views;

public partial class AIIntelligenceWidgetView : UserControl
{
    private NotchServices? _services;

    public AIIntelligenceWidgetView()
    {
        InitializeComponent();
        PromptInputBox.TextChanged += (s, e) =>
        {
            PlaceholderText.Visibility = string.IsNullOrWhiteSpace(PromptInputBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        };
    }

    public void Bind(NotchServices services)
    {
        _services = services;
        CheckProviderAvailability();
    }

    private async void CheckProviderAvailability()
    {
        if (_services == null) return;

        bool ollamaReady = await _services.Ollama.CheckAvailabilityAsync();
        Dispatcher.Invoke(() =>
        {
            if (ollamaReady)
            {
                ProviderNameText.Text = "Ollama Active";
                StatusDot.Fill = (System.Windows.Media.Brush)FindResource("AccentSuccessBrush");
            }
            else
            {
                ProviderNameText.Text = "Local Engine";
                StatusDot.Fill = (System.Windows.Media.Brush)FindResource("AccentCyanBrush");
            }
        });
    }

    private void OnPromptKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SubmitPrompt();
        }
    }

    private void OnSendClick(object sender, RoutedEventArgs e)
    {
        SubmitPrompt();
    }

    private async void SubmitPrompt()
    {
        string prompt = PromptInputBox.Text.Trim();
        if (string.IsNullOrEmpty(prompt) || _services == null) return;

        PromptInputBox.Text = string.Empty;
        OutputTextBlock.Text = "Thinking...";

        try
        {
            string response;
            if (await _services.Ollama.CheckAvailabilityAsync())
            {
                response = await _services.Ollama.GenerateCompletionAsync(prompt);
            }
            else
            {
                // Fallback smart local response generator based on system telemetry and workspace status
                int projCount = _services.ProjectDiscovery.DiscoveredProjects.Count;
                int taskCount = _services.TaskDiscovery.DiscoveredTasks.Count;
                var hw = _services.Hardware.CurrentSnapshot;
                double cpu = hw?.CpuUsagePercent ?? 0;
                double ramUsed = hw?.RamUsedGb ?? 0;
                double ramTotal = hw?.RamTotalGb ?? 16;
                double ramPercent = hw?.RamUsagePercent ?? 0;

                response = $"[Local Agent Intelligence]\n• Monitored Workspaces: {projCount} active repositories\n• Pending Discovered Tasks: {taskCount}\n• System Load: CPU {cpu:F0}% | RAM {ramUsed:F1}/{ramTotal:F0} GB ({ramPercent:F0}%)\n• Status: Ready for commands and automations.";
            }

            Dispatcher.Invoke(() => OutputTextBlock.Text = response);
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => OutputTextBlock.Text = $"Error: {ex.Message}");
        }
    }

    private async void OnDailyBriefingClick(object sender, RoutedEventArgs e)
    {
        if (_services == null) return;

        OutputTextBlock.Text = "Generating Daily Executive Briefing...";
        try
        {
            var report = await _services.Briefing.GenerateBriefingAsync(
                _services.ProjectDiscovery.DiscoveredProjects,
                _services.TaskDiscovery.DiscoveredTasks,
                _services.Hardware.CurrentSnapshot);

            Dispatcher.Invoke(() =>
            {
                OutputTextBlock.Text = $"🌅 {report.Headline}\n\n{report.Summary}\n\nAction items:\n" +
                                       string.Join("\n", report.KeyActionItems.Select(a => $"• {a}"));
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => OutputTextBlock.Text = $"Briefing error: {ex.Message}");
        }
    }

    private async void OnScanProjectsClick(object sender, RoutedEventArgs e)
    {
        if (_services == null) return;

        OutputTextBlock.Text = "Scanning approved workspaces for Git repos, TODOs, and changes...";
        try
        {
            await _services.ProjectDiscovery.StartScanAsync();
            await _services.TaskDiscovery.DiscoverTasksAsync(_services.ProjectDiscovery.DiscoveredProjects);
            Dispatcher.Invoke(() =>
            {
                OutputTextBlock.Text = $"Scan complete!\nFound {_services.ProjectDiscovery.DiscoveredProjects.Count} projects and {_services.TaskDiscovery.DiscoveredTasks.Count} actionable tasks.";
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => OutputTextBlock.Text = $"Scan error: {ex.Message}");
        }
    }
}
