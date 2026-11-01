using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using XNotch.Helpers;
using XNotch.Models;
using XNotch.Services;
using Xunit;

namespace XNotch.Tests;

public class DailyBriefingAndPhaseTests
{
    [Fact]
    public async Task DailyBriefingService_AlgorithmicGeneration_ProducesDetailedReport()
    {
        using var service = new DailyBriefingService();

        var projects = new List<ProjectRecord>
        {
            new("p1", "X-Notch", @"C:\repos\x-notch", "C#", "main", true, DateTime.UtcNow, "feat: blur cache"),
            new("p2", "AgentCore", @"C:\repos\agent-core", "TypeScript", "dev", false, DateTime.UtcNow, "fix: lint")
        };

        var tasks = new List<TaskItem>
        {
            new("t1", "p1", "X-Notch", "Fix multi-monitor buffer thrashing", "TODO line 50", TaskPriority.Critical, TaskStatusState.Observed, DateTime.UtcNow),
            new("t2", "p1", "X-Notch", "Add workspace settings UI", "Feature request", TaskPriority.Medium, TaskStatusState.Observed, DateTime.UtcNow)
        };

        var snapshot = new HardwareSnapshot(
            CpuUsagePercent: 25.5,
            CpuClockGhz: 3.5,
            RamUsagePercent: 37.5,
            RamUsedGb: 12.0,
            RamTotalGb: 32.0,
            GpuUsagePercent: 15.0,
            GpuDedicatedVramUsedGb: 2.0,
            GpuDedicatedVramTotalGb: 8.0,
            GpuTemperatureCelsius: 55.0,
            GpuName: "NVIDIA RTX 4080",
            DiskDriveLetter: "C:",
            DiskUsagePercent: 50.0,
            DiskFreeGb: 500.0,
            DiskTotalGb: 1000.0,
            NetDownloadKbps: 100.0,
            NetUploadKbps: 50.0,
            Timestamp: DateTime.UtcNow
        );

        var report = await service.GenerateBriefingAsync(projects, tasks, snapshot, CancellationToken.None);

        Assert.NotNull(report);
        Assert.False(report.IsAiGenerated);
        Assert.Contains("high-priority", report.Summary);
        Assert.Contains("uncommitted", report.Summary);
        Assert.NotEmpty(report.KeyActionItems);
        Assert.Contains(report.KeyActionItems, item => item.Contains("Fix multi-monitor buffer thrashing"));
        Assert.Contains(report.KeyActionItems, item => item.Contains("X-Notch"));
        Assert.NotNull(service.LatestReport);
        Assert.Equal(report, service.LatestReport);
    }

    [Fact]
    public async Task DailyBriefingService_CleanWorkspace_ReportsAllClean()
    {
        using var service = new DailyBriefingService();

        var projects = new List<ProjectRecord>
        {
            new("p1", "X-Notch", @"C:\repos\x-notch", "C#", "main", false, DateTime.UtcNow, "release v2.0")
        };

        var tasks = new List<TaskItem>();

        var report = await service.GenerateBriefingAsync(projects, tasks);

        Assert.NotNull(report);
        Assert.False(report.IsAiGenerated);
        Assert.Contains("clean", report.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(report.KeyActionItems, item => item.Contains("clean"));
    }

    [Fact]
    public async Task ApprovalService_EndToEnd_DispatchesDecisionToClient()
    {
        string testPipe = $"XNotch-Test-{Guid.NewGuid():N}";
        using var service = new ApprovalService(testPipe);
        service.Start();

        var promptReceivedTcs = new TaskCompletionSource<ApprovalRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var promptSettledTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        service.ApprovalPromptArrived += req => promptReceivedTcs.TrySetResult(req);
        service.ApprovalPromptSettled += id => promptSettledTcs.TrySetResult(id);

        // Connect client
        using var client = new NamedPipeClientStream(".", testPipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000);
        Assert.True(client.IsConnected);

        using var reader = new StreamReader(client, Encoding.UTF8, false, 4096, true);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

        string promptId = $"prompt-{Guid.NewGuid():N}";
        var payload = new
        {
            type = "approval_prompt",
            promptId = promptId,
            agentId = "Claude-Code",
            tool = "TerminalExecute",
            command = "git push origin main",
            workingDir = @"C:\repos\X-Notch",
            timeoutSeconds = 5
        };

        await writer.WriteLineAsync(JsonSerializer.Serialize(payload));

        var receivedReq = await promptReceivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(promptId, receivedReq.PromptId);
        Assert.Equal("Claude-Code", receivedReq.AgentId);
        Assert.Equal("git push origin main", receivedReq.Command);

        // Server user clicks "Allow"
        bool submitted = service.SubmitDecision(promptId, ApprovalDecision.Allow);
        Assert.True(submitted);

        string? responseLine = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.NotNull(responseLine);

        using var doc = JsonDocument.Parse(responseLine);
        string decision = doc.RootElement.GetProperty("decision").GetString()!;
        Assert.Equal("allow", decision);

        string settledId = await promptSettledTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(promptId, settledId);
    }

    [Fact]
    public void AppSettings_WorkspaceRoots_MaintainsOrderAndTrimsPaths()
    {
        var settings = new AppSettings();
        Assert.NotNull(settings.WorkspaceRoots);

        settings.WorkspaceRoots.Clear();
        settings.WorkspaceRoots.Add(@"C:\Projects\Alpha");
        settings.WorkspaceRoots.Add(@"D:\Workspace\Beta");

        Assert.Equal(2, settings.WorkspaceRoots.Count);
        Assert.Equal(@"C:\Projects\Alpha", settings.WorkspaceRoots[0]);
        Assert.Equal(@"D:\Workspace\Beta", settings.WorkspaceRoots[1]);
    }

    [Fact]
    public void BackdropBlurHelper_ContextCache_AllocatesPerBitmap()
    {
        var bmp1 = new WriteableBitmap(100, 100, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
        var bmp2 = new WriteableBitmap(200, 200, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);

        var ctx1 = BackdropBlurHelper.Contexts.GetOrCreateValue(bmp1);
        var ctx2 = BackdropBlurHelper.Contexts.GetOrCreateValue(bmp2);

        Assert.NotNull(ctx1);
        Assert.NotNull(ctx2);
        Assert.NotSame(ctx1, ctx2);

        // Retrieving again should return exact same instance from cache
        var ctx1Again = BackdropBlurHelper.Contexts.GetOrCreateValue(bmp1);
        Assert.Same(ctx1, ctx1Again);
    }
}
