using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using XNotch.Models;

namespace XNotch.Services;

/// <summary>
/// Maintains local persistent project memory and provides 1-click "Resume Work"
/// actions to restore developer workspaces in editors or terminal sessions.
/// </summary>
public sealed class ProjectMemoryService : IDisposable
{
    private static readonly string StorageFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XNotch", "project_memory.json");

    private readonly ConcurrentDictionary<string, ProjectRecord> _lastKnownProjects = new(StringComparer.OrdinalIgnoreCase);

    public ProjectMemoryService()
    {
        LoadFromDisk();
    }

    private void LoadFromDisk()
    {
        try
        {
            if (File.Exists(StorageFile))
            {
                string json = File.ReadAllText(StorageFile);
                var list = JsonSerializer.Deserialize<ProjectRecord[]>(json);
                if (list != null)
                {
                    foreach (var p in list)
                    {
                        _lastKnownProjects[p.Path] = p;
                    }
                }
            }
        }
        catch { }
    }

    public async Task RecordProjectAccessAsync(ProjectRecord project)
    {
        _lastKnownProjects[project.Path] = project;
        try
        {
            string dir = Path.GetDirectoryName(StorageFile)!;
            Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(_lastKnownProjects.Values);
            await File.WriteAllTextAsync(StorageFile, json);
        }
        catch { }
    }

    /// <summary>
    /// Launches the developer workspace for the given project using safe ArgumentList execution.
    /// Probes for VS Code, Antigravity IDE, Visual Studio, or Windows Terminal.
    /// </summary>
    public static bool ResumeWork(ProjectRecord project)
    {
        if (!Directory.Exists(project.Path)) return false;

        // Try VS Code first
        if (TryLaunch("code", ["."], project.Path)) return true;

        // Try Antigravity IDE
        if (TryLaunch("antigravity", ["."], project.Path)) return true;

        // Try Windows Terminal
        if (TryLaunch("wt.exe", ["-d", project.Path], project.Path)) return true;

        // Fallback: Open File Explorer
        return TryLaunch("explorer.exe", [project.Path], project.Path);
    }

    private static bool TryLaunch(string fileName, string[] args, string workingDir)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDir,
                UseShellExecute = true
            };
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            var proc = Process.Start(psi);
            return proc != null;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
    }
}
