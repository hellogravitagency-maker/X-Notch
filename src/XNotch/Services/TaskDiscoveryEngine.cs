using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XNotch.Models;

namespace XNotch.Services;

/// <summary>
/// Autonomous engine that discovers potential tasks from roadmaps, plans,
/// markdown checklists, and TODO comments within discovered projects.
/// </summary>
public sealed class TaskDiscoveryEngine : IDisposable
{
    private static readonly Regex TodoCommentRegex = new(
        @"(?://|#|/\*|<!--)\s*(?:TODO|FIXME|HACK|NOTE)\s*[:\-]\s*(?<task>.+?)(?:\*/|-->|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MarkdownTaskRegex = new(
        @"^[-*]\s*\[\s\]\s*(?<task>.+)$",
        RegexOptions.Compiled);

    private readonly List<TaskItem> _discoveredTasks = [];
    private readonly object _lock = new();

    private CancellationTokenSource? _scanCts;
    private bool _disposed;

    public IReadOnlyList<TaskItem> DiscoveredTasks
    {
        get
        {
            lock (_lock) return _discoveredTasks.ToList();
        }
    }

    public event Action<IReadOnlyList<TaskItem>>? TasksDiscovered;

    public async Task DiscoverTasksAsync(IEnumerable<ProjectRecord> projects)
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        await Task.Run(() =>
        {
            var tasks = new List<TaskItem>();

            foreach (var proj in projects)
            {
                if (token.IsCancellationRequested) break;
                if (!Directory.Exists(proj.Path)) continue;

                // 1. Scan planning & markdown files (PLAN.md, ROADMAP.md, TODO.md)
                ScanProjectMarkdownFiles(proj, tasks, token);

                // 2. Scan top-level source files for TODO/FIXME comments
                ScanSourceCodeFiles(proj, tasks, token);
            }

            lock (_lock)
            {
                _discoveredTasks.Clear();
                _discoveredTasks.AddRange(tasks);
            }

            TasksDiscovered?.Invoke(tasks);
        }, token);
    }

    private static void ScanProjectMarkdownFiles(ProjectRecord proj, List<TaskItem> tasks, CancellationToken token)
    {
        string[] targetFiles = ["PLAN.md", "ROADMAP.md", "TODO.md", "README.md", "TASKS.md"];
        foreach (var fileName in targetFiles)
        {
            if (token.IsCancellationRequested) break;
            string filePath = Path.Combine(proj.Path, fileName);
            if (!File.Exists(filePath)) continue;

            try
            {
                int lineNum = 0;
                foreach (var line in File.ReadLines(filePath))
                {
                    lineNum++;
                    var match = MarkdownTaskRegex.Match(line.Trim());
                    if (match.Success)
                    {
                        string rawText = match.Groups["task"].Value.Trim();
                        if (rawText.Length < 4) continue;

                        string id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                            System.Text.Encoding.UTF8.GetBytes(proj.Id + fileName + rawText)))[..12].ToLowerInvariant();

                        tasks.Add(new TaskItem(
                            Id: id,
                            ProjectId: proj.Id,
                            ProjectName: proj.Name,
                            Title: rawText,
                            SourceEvidence: $"{fileName}: Line {lineNum}",
                            Priority: rawText.Contains("urgent", StringComparison.OrdinalIgnoreCase) ? TaskPriority.High : TaskPriority.Medium,
                            Status: TaskStatusState.Observed,
                            DiscoveredAt: DateTime.UtcNow,
                            SuggestedNextAction: "Review in task inbox"
                        ));
                    }
                }
            }
            catch { }
        }
    }

    private static void ScanSourceCodeFiles(ProjectRecord proj, List<TaskItem> tasks, CancellationToken token)
    {
        string[] extensions = ["*.cs", "*.ts", "*.js", "*.py", "*.rs", "*.cpp"];
        int scannedCount = 0;

        try
        {
            foreach (var ext in extensions)
            {
                if (token.IsCancellationRequested || scannedCount > 40) break;

                var files = Directory.GetFiles(proj.Path, ext, SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    if (token.IsCancellationRequested || scannedCount++ > 40) break;
                    string fileName = Path.GetFileName(file);

                    int lineNum = 0;
                    foreach (var line in File.ReadLines(file))
                    {
                        lineNum++;
                        var match = TodoCommentRegex.Match(line);
                        if (match.Success)
                        {
                            string rawText = match.Groups["task"].Value.Trim();
                            if (rawText.Length < 4) continue;

                            string id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                                System.Text.Encoding.UTF8.GetBytes(proj.Id + fileName + lineNum + rawText)))[..12].ToLowerInvariant();

                            tasks.Add(new TaskItem(
                                Id: id,
                                ProjectId: proj.Id,
                                ProjectName: proj.Name,
                                Title: rawText,
                                SourceEvidence: $"{fileName}: Line {lineNum}",
                                Priority: TaskPriority.Medium,
                                Status: TaskStatusState.Observed,
                                DiscoveredAt: DateTime.UtcNow,
                                SuggestedNextAction: "Confirm task in backlog"
                            ));
                        }
                    }
                }
            }
        }
        catch { }
    }

    public void UpdateTaskStatus(string taskId, TaskStatusState newState)
    {
        lock (_lock)
        {
            var idx = _discoveredTasks.FindIndex(t => t.Id == taskId);
            if (idx >= 0)
            {
                var existing = _discoveredTasks[idx];
                _discoveredTasks[idx] = existing with { Status = newState };
                TasksDiscovered?.Invoke(_discoveredTasks.ToList());
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scanCts?.Cancel();
        _scanCts?.Dispose();
    }
}
