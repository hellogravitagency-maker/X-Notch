using System;
using System.IO;
using System.Threading.Tasks;
using XNotch.Models;
using XNotch.Services;
using XNotch.Services.AI;
using Xunit;

namespace XNotch.Tests;

public class IntelligenceTests
{
    [Fact]
    public void CodingAgentMonitor_Catalog_ContainsExpectedAgents()
    {
        var catalog = CodingAgentMonitor.Catalog;
        Assert.Contains(catalog, a => a.Id == "claude");
        Assert.Contains(catalog, a => a.Id == "codex");
        Assert.Contains(catalog, a => a.Id == "antigravity");
        Assert.Contains(catalog, a => a.Id == "opencode");
    }

    [Fact]
    public void ContextSanitizer_RedactsOpenAiAndGitHubTokens()
    {
        string sample = "Connecting with sk-1234567890abcdef1234567890 and ghp_abcdefghijklmnopqrstuvwx";
        string sanitized = ContextSanitizer.Sanitize(sample);

        Assert.DoesNotContain("sk-1234567890abcdef1234567890", sanitized);
        Assert.DoesNotContain("ghp_abcdefghijklmnopqrstuvwx", sanitized);
        Assert.Contains("[REDACTED_SECRET]", sanitized);
    }

    [Fact]
    public async Task TaskDiscovery_ExtractsMarkdownChecklists()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "xnotch_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string planFile = Path.Combine(tempDir, "PLAN.md");
            File.WriteAllText(planFile, @"
# Test Plan
- [ ] Implement audio visualizer
- [x] Create project models
- [ ] Add urgent security patch
");

            using var engine = new TaskDiscoveryEngine();
            var proj = new ProjectRecord(
                Id: "test_proj",
                Name: "TestProj",
                Path: tempDir,
                TechStack: ".NET",
                GitBranch: "main",
                HasUncommittedChanges: false,
                LastModified: DateTime.UtcNow
            );

            await engine.DiscoverTasksAsync([proj]);

            var tasks = engine.DiscoveredTasks;
            Assert.Equal(2, tasks.Count);
            Assert.Contains(tasks, t => t.Title == "Implement audio visualizer" && t.Priority == TaskPriority.Medium);
            Assert.Contains(tasks, t => t.Title == "Add urgent security patch" && t.Priority == TaskPriority.High);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void SmartReminderEngine_DetectsDueTasks()
    {
        using var engine = new SmartReminderEngine();
        var dueTask = new TaskItem(
            Id: "t1",
            ProjectId: "p1",
            ProjectName: "X-Notch",
            Title: "Finalize Phase 2 Release",
            SourceEvidence: "PLAN.md: Line 1",
            Priority: TaskPriority.High,
            Status: TaskStatusState.Confirmed,
            DiscoveredAt: DateTime.UtcNow,
            DueDate: DateTime.UtcNow.AddMinutes(30)
        );

        engine.EvaluateTasks([dueTask]);
        var reminders = engine.ActiveReminders;

        Assert.Single(reminders);
        Assert.Contains("Finalize Phase 2 Release", reminders[0].Title);
    }
}
