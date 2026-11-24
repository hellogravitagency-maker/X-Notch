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
    public AgentHubService Agents { get; } = new();
    public ClipboardService Clipboard { get; } = new();
    public SystemControlsService Controls { get; } = new();
    public ForegroundAppService Foreground { get; } = new();
    public FpsService Fps { get; } = new();
    public WidgetsService Widgets { get; } = new();
    public CaptureService Capture { get; } = new();
    public ChatService Chat { get; } = new();
    public ObsService Obs { get; } = new();
    private Views.ChatOverlayWindow? _chatOverlay;
    private Views.LiveWallpaperWindow? _wallpaper;
    private string? _wallpaperFile;
    private readonly Views.WindowSwitcher _switcher = new();
    private string _streamConfig = "";
    private bool _wasStreaming, _wasRecording;
    public DeckPairing? DeckPairing { get; private set; }
    public DeckServer? Deck { get; private set; }

    /// <summary>Deck state, pairing window or paired phones changed (any thread).</summary>
    public event Action? DeckChanged;
    private AppSettings _settings = new();

    // Developer Intelligence & Autonomous Productivity
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
        await Controls.InitializeAsync();

        AudioCapture.Start();
        Volume.Initialize();
        Brightness.Initialize();
        Battery.Initialize();
        Calendar.Initialize();
        Webcam.SetTargetFps(settings.WebcamFps);
        Fullscreen.Start();
        Foreground.Start();
        Privacy.Initialize();
        Timer.Initialize();
        LockKeys.Start();

        // Coding agents: island alert when one needs you or finishes
        Agents.Attention += s => Notifications.AddNotification(
            s.KindLabel,
            s.State switch
            {
                AgentState.Waiting => Loc.T("{0} needs you", s.KindLabel),
                AgentState.Error => Loc.T("{0} stopped", s.KindLabel),
                _ => Loc.T("{0} finished", s.KindLabel),
            },
            string.IsNullOrEmpty(s.Name) ? s.Activity : $"{s.Name} · {s.Activity}");
        SetAgentsEnabled(settings.AgentsEnabled);
        ApplyClipboardSettings(settings);
        _settings = settings;
        Widgets.Start(settings);

        // Captures saved by Snipping Tool (Win+Shift+S / Win+Shift+R / PrtSc) land on the shelf.
        Capture.CaptureSaved += path =>
        {
            Shelf.AddFiles([path]);
            bool video = path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);
            Notifications.AddNotification("X-Notch", video ? Loc.T("Recording saved") : Loc.T("Screenshot on the shelf"), System.IO.Path.GetFileName(path));
        };
        ApplyCaptureSettings(settings);

        DeckPairing = new DeckPairing(settings.DeckDevices, () => _settings.Save());
        DeckPairing.Changed += () => DeckChanged?.Invoke();
        DeckPairing.Paired += d => Notifications.AddNotification("X-Notch", Loc.T("Phone paired"), Loc.T("{0} can now control this PC. Remove it in the Deck card.", d.Name));
        Deck = new DeckServer(DeckPairing, BuildDeckHost());
        Deck.StateChanged += () => DeckChanged?.Invoke();
        _ = SetDeckEnabledAsync(settings.DeckEnabled);

        var ui = System.Windows.Application.Current?.Dispatcher;
        Chat.MessageReceived += m => ui?.BeginInvoke(() => _chatOverlay?.Add(m));
        Obs.StateChanged += () =>
        {
            if (Obs.Streaming != _wasStreaming)
                Notifications.AddNotification("OBS", Obs.Streaming ? Loc.T("You're live") : Loc.T("Stream ended"), Obs.CurrentScene ?? "");
            if (Obs.Recording != _wasRecording)
                Notifications.AddNotification("OBS", Obs.Recording ? Loc.T("Recording") : Loc.T("Recording stopped"), Obs.CurrentScene ?? "");
            _wasStreaming = Obs.Streaming;
            _wasRecording = Obs.Recording;
        };
        ApplyStreamSettings(settings);

        // Live wallpaper re-checks visibility whenever focus or fullscreen state changes.
        Foreground.Changed += () => _wallpaper?.Evaluate();
        Fullscreen.FullscreenChanged += _ => ui?.BeginInvoke(() => _wallpaper?.Evaluate());
        ApplyDesktopSettings(settings);

        // Pomodoro: a finished focus block starts a break; a finished break says so.
        Timer.Completed += () =>
        {
            if (Timer.Label == "Focus" && _settings.BreakMinutes > 0)
            {
                Notifications.AddNotification("X-Notch", Loc.T("Focus done, take a break"), Loc.T("{0} min break started", _settings.BreakMinutes));
                Timer.Start(TimeSpan.FromMinutes(_settings.BreakMinutes), "Break");
            }
            else if (Timer.Label == "Break")
                Notifications.AddNotification("X-Notch", Loc.T("Break over"), Loc.T("Ready for the next focus block"));
        };

        // Start Intelligence Engines
        Reminders.Start();

        // Wire Smart Reminders to Notification Service so alerts pop up on the Dynamic Island
        Reminders.ReminderTriggered += reminder =>
        {
            Notifications.AddNotification(Loc.T("X-Notch Reminder"), reminder.Title, reminder.Reason);
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

    /// <summary>Idempotent; must run on the UI thread (the listener is a message-only window).</summary>
    public void ApplyClipboardSettings(AppSettings settings)
    {
        Clipboard.KeepHours = Math.Max(1, settings.ClipboardKeepHours);
        Clipboard.MaxItems = Math.Max(5, settings.ClipboardHistorySize);
        if (settings.ClipboardEnabled) Clipboard.Start(); else Clipboard.Stop();
    }

    /// <summary>Live wallpaper + Alt-Tab switcher. Idempotent; UI thread.</summary>
    public void ApplyDesktopSettings(AppSettings s)
    {
        bool wantWallpaper = s.LiveWallpaper && System.IO.File.Exists(s.LiveWallpaperFile);
        if (_wallpaper != null && (!wantWallpaper || _wallpaperFile != s.LiveWallpaperFile))
        {
            _wallpaper.Close();
            _wallpaper = null;
        }
        if (wantWallpaper && _wallpaper == null)
        {
            _wallpaperFile = s.LiveWallpaperFile;
            _wallpaper = new Views.LiveWallpaperWindow(s.LiveWallpaperFile!, s.LiveWallpaperBrightness)
            {
                IsFullscreen = () => Fullscreen.IsFullscreen,
            };
            _wallpaper.Show();
        }
        _wallpaper?.SetBrightness(s.LiveWallpaperBrightness);

        if (s.AltTabEnabled) _switcher.Start(); else _switcher.Stop();
    }

    /// <summary>Idempotent: reconnects chat/OBS only when their settings actually change. UI thread.</summary>
    public void ApplyStreamSettings(AppSettings s)
    {
        string config = string.Join("|", s.ChatTwitch, s.ChatYouTubeKeyProtected, s.ChatYouTubeVideo, s.ObsEnabled, s.ObsPort, s.ObsPasswordProtected);
        if (config != _streamConfig)
        {
            _streamConfig = config;
            Chat.Configure(s.ChatTwitch, AppSettings.Unprotect(s.ChatYouTubeKeyProtected), s.ChatYouTubeVideo);
            Obs.Configure(s.ObsEnabled, s.ObsPort, AppSettings.Unprotect(s.ObsPasswordProtected));
        }

        bool overlay = s.ChatOverlay && (!string.IsNullOrWhiteSpace(s.ChatTwitch) || !string.IsNullOrWhiteSpace(s.ChatYouTubeVideo));
        if (overlay && _chatOverlay == null) { _chatOverlay = new Views.ChatOverlayWindow(); _chatOverlay.Show(); }
        else if (!overlay && _chatOverlay != null) { _chatOverlay.Close(); _chatOverlay = null; }
    }

    private bool _deckOn;

    /// <summary>Idempotent; starts or stops the LAN server only when the setting changes.</summary>
    public async Task SetDeckEnabledAsync(bool enabled)
    {
        if (Deck == null || _deckOn == enabled) return;
        _deckOn = enabled;
        if (enabled) await Deck.StartAsync(); else { DeckPairing?.Close(); await Deck.StopAsync(); }
    }

    private int _artVersion;

    private DeckHost BuildDeckHost()
    {
        var ui = System.Windows.Application.Current?.Dispatcher;
        Media.MediaInfo.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MediaInfo.AlbumArt)) _artVersion++; };
        return new DeckHost
        {
            Status = () =>
            {
                var m = Media.MediaInfo;
                return new
                {
                    title = m.HasMedia ? m.Title : "", artist = m.HasMedia ? m.Artist : "", playing = m.IsPlaying,
                    position = m.Position.TotalSeconds, duration = m.Duration.TotalSeconds,
                    volume = Volume.Volume, muted = Volume.IsMuted, artVersion = _artVersion,
                };
            },
            ArtPng = () => ui?.Invoke(() =>
            {
                if (Media.MediaInfo.AlbumArt is not { } art) return null;
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(art));
                using var ms = new System.IO.MemoryStream();
                enc.Save(ms);
                return ms.ToArray();
            }),
            Media = async (cmd, value) =>
            {
                if (ui == null) return;
                await await ui.InvokeAsync(() => cmd switch
                {
                    "play" => Media.PlayPauseAsync(),
                    "next" => Media.NextAsync(),
                    "prev" => Media.PreviousAsync(),
                    _ => Media.SeekToAsync(Math.Clamp(value, 0, 1)),
                });
            },
            Volume = (value, mute) =>
            {
                if (value is double v) Volume.SetVolume((float)v);
                if (mute) Volume.ToggleMute();
            },
            Apps = () => _settings.DockPins.Where(Views.AppIdentity.Exists).Select(Views.AppIdentity.DisplayName).ToList(),
            LaunchApp = i =>
            {
                var pins = _settings.DockPins.Where(Views.AppIdentity.Exists).ToList();
                if (i < pins.Count) Views.AppIdentity.Launch(pins[i]);
            },
            RunAction = id =>
            {
                switch (id)
                {
                    case "lock": LockWorkStation(); break;
                    case "mic": Controls.MicMuted = !Controls.MicMuted; break;
                    case "mute": Volume.ToggleMute(); break;
                    case "snip": CaptureService.Snip(); break;
                    case "desktop": Views.Native.SendWinChord(0x44); break; // Win+D
                }
            },
        };
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool LockWorkStation();

    /// <summary>Idempotent; UI thread.</summary>
    public void ApplyCaptureSettings(AppSettings settings)
    {
        if (settings.CapturesToShelf) Capture.Start(); else Capture.Stop();
    }

    public void ApplyWidgetSettings(AppSettings settings)
    {
        _settings = settings;
        Widgets.UpdateSettings(settings);
    }

    private bool? _agentsEnabled;

    /// <summary>
    /// Idempotent: every island calls this from ApplySettings; work happens only on change.
    /// On: start the local endpoint and (re)install hooks. Off: remove hooks and stop.
    /// </summary>
    public void SetAgentsEnabled(bool enabled)
    {
        if (_agentsEnabled == enabled) return;
        bool firstCall = _agentsEnabled == null;
        _agentsEnabled = enabled;
        if (firstCall && !enabled) return; // never touch config files unless the user turned it on

        if (enabled) Agents.Start(); else Agents.Stop();
        _ = Task.Run(() =>
        {
            string status;
            try { status = AgentHookInstaller.Apply(enabled); }
            catch (Exception ex) { status = "hook setup failed: " + ex.Message; }
            if (!firstCall) Notifications.AddNotification("X-Notch", enabled ? Loc.T("Agents on") : Loc.T("Agents off"), status);
        });
    }

    public void Dispose()
    {
        Agents.Dispose();
        Clipboard.Dispose();
        Controls.Dispose();
        Foreground.Dispose();
        Fps.Dispose();
        Widgets.Dispose();
        Capture.Dispose();
        Chat.Dispose();
        _switcher.Dispose();
        _wallpaper?.Close();
        Obs.Dispose();
        try { Deck?.StopAsync().Wait(TimeSpan.FromSeconds(2)); } catch { }
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

        ProjectDiscovery.Dispose();
        TaskDiscovery.Dispose();
        ProjectMemory.Dispose();
        Reminders.Dispose();
        Briefing.Dispose();
        Database.Dispose();
    }
}
