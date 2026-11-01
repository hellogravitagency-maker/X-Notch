using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XNotch.Models;
using XNotch.Services.AI;

namespace XNotch.Services;

public sealed record DailyBriefingReport(
    string Headline,
    string Summary,
    IReadOnlyList<string> KeyActionItems,
    DateTime GeneratedAt,
    bool IsAiGenerated
);

/// <summary>
/// Autonomous morning executive summary &amp; daily recap service.
/// Synthesizes recent Git commits, uncommitted work, high-priority tasks, and hardware status
/// using local AI (Ollama/OpenAI) or crisp native algorithmic generation.
/// </summary>
public sealed class DailyBriefingService : IDisposable
{
    private readonly OllamaProvider _ollama;
    private readonly OpenAICompatibleProvider _openAi;

    public DailyBriefingReport? LatestReport { get; private set; }
    public event Action<DailyBriefingReport>? BriefingReady;

    public DailyBriefingService(OllamaProvider? ollama = null, OpenAICompatibleProvider? openAi = null)
    {
        _ollama = ollama ?? new OllamaProvider();
        _openAi = openAi ?? new OpenAICompatibleProvider();
    }

    public async Task<DailyBriefingReport> GenerateBriefingAsync(
        IReadOnlyList<ProjectRecord> projects,
        IReadOnlyList<TaskItem> tasks,
        HardwareSnapshot? snapshot = null,
        CancellationToken ct = default)
    {
        var pendingTasks = tasks.Where(t => t.Status != TaskStatusState.Completed).ToList();
        var urgentTasks = pendingTasks.Where(t => t.Priority == TaskPriority.Critical || t.Priority == TaskPriority.High).ToList();
        var uncommittedProjects = projects.Where(p => p.HasUncommittedChanges).ToList();

        // 1. Try Local AI if available
        if (await _ollama.CheckAvailabilityAsync(ct))
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("You are X-Notch's executive developer assistant. Generate a concise 2-sentence morning status and 3 bullet action items.");
                sb.AppendLine($"Active Projects: {projects.Count}. Uncommitted Repos: {uncommittedProjects.Count} ({string.Join(", ", uncommittedProjects.Take(3).Select(p => p.Name))}).");
                sb.AppendLine($"Pending Tasks: {pendingTasks.Count}. Urgent Deadlines: {urgentTasks.Count} ({string.Join(", ", urgentTasks.Take(3).Select(t => t.Title))}).");
                if (snapshot != null)
                {
                    sb.AppendLine($"System: CPU {snapshot.CpuUsagePercent:F0}%, RAM {snapshot.RamUsedGb:F1}/{snapshot.RamTotalGb:F0}GB.");
                }

                string prompt = sb.ToString();
                string response = await _ollama.GenerateCompletionAsync(prompt, ct);

                if (!string.IsNullOrWhiteSpace(response) && !response.StartsWith("Ollama unavailable"))
                {
                    var lines = response.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var bullets = lines.Where(l => l.StartsWith("- ") || l.StartsWith("* ") || (l.Length > 2 && char.IsDigit(l[0]) && l[1] == '.')).ToList();
                    string summary = lines.FirstOrDefault(l => !l.StartsWith("-") && !l.StartsWith("*") && !char.IsDigit(l[0])) ?? "Workspace scanned and ready.";

                    var aiReport = new DailyBriefingReport(
                        Headline: GetGreetingHeadline(),
                        Summary: summary,
                        KeyActionItems: bullets.Count > 0 ? bullets : [$"Address uncommitted changes in {uncommittedProjects.Count} projects", $"Review {urgentTasks.Count} priority tasks"],
                        GeneratedAt: DateTime.UtcNow,
                        IsAiGenerated: true
                    );

                    LatestReport = aiReport;
                    BriefingReady?.Invoke(aiReport);
                    return aiReport;
                }
            }
            catch { }
        }

        // 2. High-Performance Algorithmic Fallback (Deterministic, zero latency)
        var actions = new List<string>();
        foreach (var ut in urgentTasks.Take(3))
        {
            actions.Add($"Urgent: {ut.Title} ({ut.ProjectName})");
        }
        foreach (var up in uncommittedProjects.Take(2))
        {
            actions.Add($"Uncommitted: {up.Name} on branch {up.GitBranch}");
        }
        if (actions.Count == 0)
        {
            actions.Add("All repositories clean and tasks up to date");
            actions.Add("Workspace ready for new instructions");
        }

        string summaryText;
        if (uncommittedProjects.Count > 0 && urgentTasks.Count > 0)
        {
            summaryText = $"{urgentTasks.Count} high-priority tasks pending and {uncommittedProjects.Count} repositories have uncommitted changes.";
        }
        else if (uncommittedProjects.Count > 0)
        {
            summaryText = $"All tasks on schedule, but {uncommittedProjects.Count} projects have uncommitted edits.";
        }
        else if (urgentTasks.Count > 0)
        {
            summaryText = $"Working tree clean. {urgentTasks.Count} priority tasks awaiting completion.";
        }
        else
        {
            summaryText = "Everything is clean and organized across all scanned workspace repositories.";
        }

        var report = new DailyBriefingReport(
            Headline: GetGreetingHeadline(),
            Summary: summaryText,
            KeyActionItems: actions,
            GeneratedAt: DateTime.UtcNow,
            IsAiGenerated: false
        );

        LatestReport = report;
        BriefingReady?.Invoke(report);
        return report;
    }

    private static string GetGreetingHeadline()
    {
        int hour = DateTime.Now.Hour;
        return hour switch
        {
            >= 5 and < 12 => "Good morning, Developer",
            >= 12 and < 17 => "Good afternoon, Developer",
            >= 17 and < 22 => "Good evening, Developer",
            _ => "Working late, Developer"
        };
    }

    public void Dispose()
    {
    }
}
