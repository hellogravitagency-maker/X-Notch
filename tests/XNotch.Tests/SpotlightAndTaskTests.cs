using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XNotch.Models;
using XNotch.Services;
using XNotch.Views;
using Xunit;

namespace XNotch.Tests;

public class SpotlightAndTaskTests
{
    [Fact]
    public void SpotlightItem_FilteringMatchesCorrectly()
    {
        var items = new List<SpotlightItem>
        {
            new() { Title = "Scan Projects", Subtitle = "Workspace roots", Category = "Action" },
            new() { Title = "X-Notch", Subtitle = "C# • main • P:\\X-Notch", Category = "Project" },
            new() { Title = "Implement Lyrics", Subtitle = "TODO in MediaService", Category = "Task" }
        };

        var query = "notch";
        var matches = items.Where(i => i.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                       i.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Single(matches);
        Assert.Equal("X-Notch", matches[0].Title);
        Assert.Equal("Project", matches[0].Category);
    }

    [Fact]
    public void TaskItem_StatusTransitions_AreConsistent()
    {
        var task = new TaskItem(
            Id: "task-001",
            ProjectId: "proj-1",
            ProjectName: "X-Notch",
            Title: "Add Liquid Glass Vibrancy",
            SourceEvidence: "TODO at line 42",
            Priority: TaskPriority.High,
            Status: TaskStatusState.Observed,
            DiscoveredAt: DateTime.UtcNow
        );

        Assert.Equal(TaskStatusState.Observed, task.Status);

        var confirmedTask = task with { Status = TaskStatusState.Confirmed };
        Assert.Equal(TaskStatusState.Confirmed, confirmedTask.Status);

        var completedTask = confirmedTask with { Status = TaskStatusState.Completed };
        Assert.Equal(TaskStatusState.Completed, completedTask.Status);
    }
}
