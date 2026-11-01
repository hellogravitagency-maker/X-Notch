using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using XNotch.Helpers;
using XNotch.Models;

namespace XNotch.Services;

/// <summary>
/// Autonomous coding agent monitor that detects installed and running agent sessions
/// (Claude Code, OpenAI Codex, Google Antigravity, OpenCode, Aider), tracks their operational
/// phase, elapsed duration, working directories, and Git branches.
/// </summary>
public sealed class CodingAgentMonitor : IDisposable
{
    public static readonly IReadOnlyList<AgentDefinition> Catalog =
    [
        new("claude", "Claude Code", "", ["claude"], ["@anthropic-ai/claude-code", "claude-code"]),
        new("codex", "OpenAI Codex", "", ["codex"], ["@openai/codex"]),
        new("antigravity", "Antigravity", "", ["agy", "gemini"], ["@google/gemini-cli", "gemini-cli", "antigravity"]),
        new("opencode", "OpenCode", "", ["opencode"], ["@opencode/cli", "opencode-ai"]),
        new("aider", "Aider", "", ["aider"], ["aider-chat", "-m aider"])
    ];

    private readonly List<AgentSession> _activeSessions = [];
    private readonly Dictionary<int, DateTime> _sessionStartTimes = [];
    private readonly object _lock = new();

    private CancellationTokenSource? _scanCts;
    private bool _disposed;

    public IReadOnlyList<AgentSession> ActiveSessions
    {
        get
        {
            lock (_lock) return _activeSessions.ToList();
        }
    }

    public event Action<IReadOnlyList<AgentSession>>? ActiveSessionsChanged;

    public void Start()
    {
        _scanCts = new CancellationTokenSource();
        _ = Task.Run(() => ScanLoopAsync(_scanCts.Token));
    }

    private async Task ScanLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && !_disposed)
        {
            try
            {
                ScanActiveSessions();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Agent scan error: {ex.Message}");
            }

            try
            {
                await Task.Delay(3000, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void ScanActiveSessions()
    {
        var discovered = new List<AgentSession>();
        var runningPids = new HashSet<int>();

        try
        {
            var processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    string procName = proc.ProcessName.ToLowerInvariant();
                    runningPids.Add(proc.Id);

                    foreach (var def in Catalog)
                    {
                        bool isMatch = false;

                        // 1. Direct process name match
                        if (def.ExecutableNames.Any(exe => procName.Equals(exe, StringComparison.OrdinalIgnoreCase)))
                        {
                            isMatch = true;
                        }
                        // 2. Generic runtime match (node, python, bun)
                        else if (procName is "node" or "python" or "python3" or "bun")
                        {
                            // In non-elevated desktop apps, inspect window title as low-overhead marker
                            string windowTitle = proc.MainWindowTitle.ToLowerInvariant();
                            if (def.CommandLineMarkers.Any(marker => windowTitle.Contains(marker.ToLowerInvariant())))
                            {
                                isMatch = true;
                            }
                        }

                        if (isMatch)
                        {
                            DateTime startTime;
                            lock (_lock)
                            {
                                if (!_sessionStartTimes.TryGetValue(proc.Id, out startTime))
                                {
                                    startTime = DateTime.UtcNow;
                                    _sessionStartTimes[proc.Id] = startTime;
                                }
                            }

                            string workingDir = ResolveWorkingDirectory(proc);
                            string gitBranch = ResolveGitBranch(workingDir);

                            discovered.Add(new AgentSession(
                                AgentId: def.Id,
                                DisplayName: def.DisplayName,
                                ProcessId: proc.Id,
                                ProjectPath: workingDir,
                                GitBranch: gitBranch,
                                Phase: AgentPhase.RunningTool,
                                CurrentAction: "Working",
                                StartedAt: startTime
                            ));
                            break;
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        // Clean up terminated session start times
        lock (_lock)
        {
            var terminated = _sessionStartTimes.Keys.Where(pid => !runningPids.Contains(pid)).ToList();
            foreach (var pid in terminated)
            {
                _sessionStartTimes.Remove(pid);
            }

            _activeSessions.Clear();
            _activeSessions.AddRange(discovered);
        }

        ActiveSessionsChanged?.Invoke(discovered);
    }

    public static bool FocusSession(AgentSession session)
    {
        return TerminalFocusHelper.FocusProcessWindow(session.ProcessId);
    }

    private static string ResolveWorkingDirectory(Process proc)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {proc.Id}");
            using var objects = searcher.Get();
            foreach (ManagementObject mo in objects)
            {
                string? cmd = mo["CommandLine"]?.ToString();
                if (!string.IsNullOrWhiteSpace(cmd))
                {
                    var matches = System.Text.RegularExpressions.Regex.Matches(cmd, @"(?:""([a-zA-Z]:\[^""]+)""|([a-zA-Z]:\[^\s""]+))");
                    foreach (System.Text.RegularExpressions.Match m in matches)
                    {
                        string candidate = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                        if (Directory.Exists(candidate))
                            return candidate;
                        string parent = Path.GetDirectoryName(candidate) ?? "";
                        if (Directory.Exists(parent) && !parent.Equals(Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase))
                            return parent;
                    }
                }
            }
        }
        catch { }

        try
        {
            if (proc.MainModule?.FileName is { } file)
            {
                string dir = Path.GetDirectoryName(file) ?? string.Empty;
                if (!dir.Contains("nodejs", StringComparison.OrdinalIgnoreCase) && 
                    !dir.Contains("Python", StringComparison.OrdinalIgnoreCase))
                {
                    return dir;
                }
            }
        }
        catch { }

        return Environment.CurrentDirectory;
    }

    public static string ResolveGitBranch(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return string.Empty;

        try
        {
            var dir = new DirectoryInfo(directory);
            while (dir != null)
            {
                string headFile = Path.Combine(dir.FullName, ".git", "HEAD");
                if (File.Exists(headFile))
                {
                    string content = File.ReadAllText(headFile).Trim();
                    if (content.StartsWith("ref: refs/heads/"))
                    {
                        return content["ref: refs/heads/".Length..];
                    }
                    return content[..Math.Min(7, content.Length)]; // Detached HEAD hash
                }
                dir = dir.Parent;
            }
        }
        catch { }

        return string.Empty;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scanCts?.Cancel();
        _scanCts?.Dispose();
    }
}
