using System;
using System.IO;
using System.Threading.Tasks;
using XNotch.Models;
using XNotch.Persistence;
using Xunit;

namespace XNotch.Tests;

public class DatabaseTests
{
    [Fact]
    public async Task XNotchDatabase_UpsertAndRetrieveProject_WorksCorrectly()
    {
        using var db = new XNotchDatabase();
        var proj = new ProjectRecord(
            Id: "test_db_proj",
            Name: "TestDbProject",
            Path: @"P:\TestDbProject_" + Guid.NewGuid().ToString("N"),
            TechStack: ".NET 8 / C#",
            GitBranch: "feature/database",
            HasUncommittedChanges: true,
            LastModified: DateTime.UtcNow,
            Description: "Unit test project description"
        );

        await db.UpsertProjectAsync(proj);
        var projects = await db.GetAllProjectsAsync();

        Assert.Contains(projects, p => p.Id == proj.Id && p.Name == proj.Name);
    }

    [Fact]
    public async Task XNotchDatabase_UpsertTask_WorksCorrectly()
    {
        using var db = new XNotchDatabase();
        var task = new TaskItem(
            Id: "test_db_task_" + Guid.NewGuid().ToString("N")[..8],
            ProjectId: "test_db_proj",
            ProjectName: "TestDbProject",
            Title: "Verify database persistence",
            SourceEvidence: "DatabaseTests.cs",
            Priority: TaskPriority.High,
            Status: TaskStatusState.Confirmed,
            DiscoveredAt: DateTime.UtcNow,
            SuggestedNextAction: "Run xUnit"
        );

        await db.UpsertTaskAsync(task);
    }
}
