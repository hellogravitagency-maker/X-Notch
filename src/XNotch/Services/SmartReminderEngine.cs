using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XNotch.Models;

namespace XNotch.Services;

public sealed record ReminderItem(
    string Id,
    string Title,
    string Reason,
    DateTime ScheduledTime,
    bool IsOverdue,
    string? AssociatedProjectPath = null,
    bool IsDismissed = false
);

/// <summary>
/// Smart reminder engine managing upcoming deadlines, inactive project nudges,
/// overdue tasks, and milestone check-ins while respecting quiet hours.
/// </summary>
public sealed class SmartReminderEngine : IDisposable
{
    private readonly List<ReminderItem> _reminders = [];
    private readonly object _lock = new();

    private CancellationTokenSource? _timerCts;
    private bool _disposed;

    public TimeSpan QuietHoursStart { get; set; } = new(22, 0, 0); // 10 PM
    public TimeSpan QuietHoursEnd { get; set; } = new(8, 0, 0);    // 8 AM

    public IReadOnlyList<ReminderItem> ActiveReminders
    {
        get
        {
            lock (_lock) return _reminders.Where(r => !r.IsDismissed).ToList();
        }
    }

    public event Action<ReminderItem>? ReminderTriggered;

    public void Start()
    {
        _timerCts = new CancellationTokenSource();
        _ = Task.Run(() => EvaluationLoopAsync(_timerCts.Token));
    }

    public void AddReminder(ReminderItem item)
    {
        lock (_lock)
        {
            _reminders.Add(item);
        }
    }

    public void DismissReminder(string id)
    {
        lock (_lock)
        {
            var idx = _reminders.FindIndex(r => r.Id == id);
            if (idx >= 0)
            {
                _reminders[idx] = _reminders[idx] with { IsDismissed = true };
            }
        }
    }

    public void EvaluateTasks(IEnumerable<TaskItem> tasks)
    {
        var now = DateTime.UtcNow;
        foreach (var task in tasks)
        {
            if (task.DueDate.HasValue && task.DueDate.Value <= now.AddHours(2) && task.Status != TaskStatusState.Completed)
            {
                string id = "task_due_" + task.Id;
                lock (_lock)
                {
                    if (!_reminders.Any(r => r.Id == id))
                    {
                        _reminders.Add(new ReminderItem(
                            Id: id,
                            Title: $"Deadline Approaching: {task.Title}",
                            Reason: $"Task in {task.ProjectName} is due soon.",
                            ScheduledTime: task.DueDate.Value,
                            IsOverdue: task.DueDate.Value < now
                        ));
                    }
                }
            }
        }
    }

    public void EvaluateInactiveProjects(IEnumerable<ProjectRecord> projects)
    {
        var now = DateTime.UtcNow;
        foreach (var p in projects)
        {
            if (p.HasUncommittedChanges && (now - p.LastModified).TotalDays > 3)
            {
                string id = "inactive_proj_" + p.Id;
                lock (_lock)
                {
                    if (!_reminders.Any(r => r.Id == id))
                    {
                        _reminders.Add(new ReminderItem(
                            Id: id,
                            Title: $"Uncommitted Work in {p.Name}",
                            Reason: $"Project has modified files not touched in {(int)(now - p.LastModified).TotalDays} days.",
                            ScheduledTime: now,
                            IsOverdue: false,
                            AssociatedProjectPath: p.Path
                        ));
                    }
                }
            }
        }
    }

    private async Task EvaluationLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && !_disposed)
        {
            try
            {
                CheckPendingReminders();
            }
            catch { }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void CheckPendingReminders()
    {
        if (IsQuietHours()) return;

        var now = DateTime.UtcNow;
        List<ReminderItem> due = [];

        lock (_lock)
        {
            foreach (var r in _reminders)
            {
                if (!r.IsDismissed && r.ScheduledTime <= now)
                {
                    due.Add(r);
                }
            }
        }

        foreach (var r in due)
        {
            ReminderTriggered?.Invoke(r);
        }
    }

    private bool IsQuietHours()
    {
        var time = DateTime.Now.TimeOfDay;
        if (QuietHoursStart <= QuietHoursEnd)
        {
            return time >= QuietHoursStart && time <= QuietHoursEnd;
        }
        // Spans midnight
        return time >= QuietHoursStart || time <= QuietHoursEnd;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timerCts?.Cancel();
        _timerCts?.Dispose();
    }
}
