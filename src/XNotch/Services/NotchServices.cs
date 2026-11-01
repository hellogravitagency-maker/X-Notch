using System;
using System.Threading.Tasks;
using XNotch.Helpers;
using XNotch.Models;
using XNotch.Persistence;
using XNotch.Services.AI;

namespace XNotch.Services;

/// <summary>
/// Central container for system-level services, developer intelligence,
/// media controls, audio DSP, SQLite database, and background autonomous productivity engines.
/// Exactly one instance exists per app run, shared across all displays.
/// </summary>
public sealed class NotchServices : IDisposable
{
    // Persistence
    public XNotchDatabase Database { get; } = new();

    // Media & Audio DSP
    public LyricsService Lyrics { get; } = new();
    public MediaService Media { get; }
    public AudioCaptureService AudioCapture { get; } = new(bandCount: 12);

    // Hardware & System Telemetry
    public HardwareMonitorService Hardware { get; } = new();
    public NotificationService Notifications { get; } = new();
    public VolumeService Volume { get; } = new();
    public BrightnessService Brightness { get; } = new();
    public BatteryService Battery { get; } = new();
    public CalendarService Calendar { get; } = new();
    public ShelfService Shelf { get; } = new();
    public FullscreenService Fullscreen { get; } = new();
    public WebcamService Webcam { get; } = new();
    public PrivacyIndicatorService Privacy { get; } = new();
    public CountdownTimerService Timer { get; } = new();
    public LockKeyHook LockKeys { get; } = new();
    public UsbDeviceService Usb { get; } = new();
    public ArmouryCrateService ArmouryCrate { get; } = new();

    // Developer Intelligence & Autonomous Productivity
    public CodingAgentMonitor AgentMonitor { get; } = new();
    public ApprovalService Approvals { get; } = new();
    public ProjectDiscoveryEngine ProjectDiscovery { get; } = new();
    public TaskDiscoveryEngine TaskDiscovery { get; } = new();
    public ProjectMemoryService ProjectMemory { get; } = new();
    public SmartReminderEngine Reminders { get; } = new();
    public DailyBriefingService Briefing { get; }

    // Optional AI Layer
    public OllamaProvider Ollama { get; } = new();
    public OpenAICompatibleProvider OpenAI { get; } = new();

    private Task? _initTask;

    public NotchServices()
    {
        Media = new MediaService(Lyrics);
        Briefing = new DailyBriefingService(Ollama, OpenAI);
        Hardware.SetArmouryCrateService(ArmouryCrate);
    }

    /// <summary>
    /// One-time global initialization; safe to await from every window.
    /// </summary>
    public Task EnsureInitializedAsync(AppSettings settings)
    {
        return _initTask ??= InitializeAsync(settings);
    }

    private async Task InitializeAsync(AppSettings settings)
    {
        await Media.InitializeAsync();
        await Notifications.InitializeAsync();

        AudioCapture.Start();
        Volume.Initialize();
        Brightness.Initialize();
        Battery.Initialize();
        Calendar.Initialize();
        Webcam.SetTargetFps(settings.WebcamFps);
        Fullscreen.Start();
        Privacy.Initialize();
        Timer.Initialize();
        LockKeys.Start();

        // Start Intelligence Engines
        AgentMonitor.Start();
        Approvals.Start();
        Reminders.Start();

        // Wire Smart Reminders to Notification Service so alerts pop up on the Dynamic Island
        Reminders.ReminderTriggered += reminder =>
        {
            Notifications.AddNotification("X-Notch Reminder", reminder.Title, reminder.Reason);
        };

        // Add user-approved workspace roots from settings
        if (settings.WorkspaceRoots != null)
        {
            foreach (var root in settings.WorkspaceRoots)
            {
                ProjectDiscovery.AddApprovedRoot(root);
            }
        }

        // Persist discovered projects and tasks to SQLite
        ProjectDiscovery.ProjectsDiscovered += projects =>
        {
            _ = Task.Run(async () =>
            {
                foreach (var p in projects)
                {
                    await Database.UpsertProjectAsync(p);
                }
            });
        };

        TaskDiscovery.TasksDiscovered += tasks =>
        {
            _ = Task.Run(async () =>
            {
                foreach (var t in tasks)
                {
                    await Database.UpsertTaskAsync(t);
                }
            });
        };

        // Run initial project scan on startup and generate briefing
        _ = ProjectDiscovery.StartScanAsync().ContinueWith(async t =>
        {
            if (ProjectDiscovery.DiscoveredProjects.Count > 0)
            {
                await TaskDiscovery.DiscoverTasksAsync(ProjectDiscovery.DiscoveredProjects);
                Reminders.EvaluateTasks(TaskDiscovery.DiscoveredTasks);
                Reminders.EvaluateInactiveProjects(ProjectDiscovery.DiscoveredProjects);
                await Briefing.GenerateBriefingAsync(ProjectDiscovery.DiscoveredProjects, TaskDiscovery.DiscoveredTasks, Hardware.CurrentSnapshot);
            }
        });

        _ = Ollama.CheckAvailabilityAsync();
    }

    public void Dispose()
    {
        AudioCapture.Dispose();
        Media.Dispose();
        Lyrics.Dispose();
        Hardware.Dispose();
        Notifications.Dispose();
        Volume.Dispose();
        Brightness.Dispose();
        Battery.Dispose();
        Calendar.Dispose();
        Fullscreen.Dispose();
        Webcam.Dispose();
        Privacy.Dispose();
        Timer.Dispose();
        LockKeys.Dispose();
        ArmouryCrate.Dispose();

        AgentMonitor.Dispose();
        Approvals.Dispose();
        ProjectDiscovery.Dispose();
        TaskDiscovery.Dispose();
        ProjectMemory.Dispose();
        Reminders.Dispose();
        Briefing.Dispose();
        Database.Dispose();
    }
}
