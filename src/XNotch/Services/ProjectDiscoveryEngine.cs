using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XNotch.Models;

namespace XNotch.Services;

/// <summary>
/// Autonomous engine that scans user-approved directories to discover software projects,
/// extract technology stacks, git branches, uncommitted changes, and readmes.
/// </summary>
public sealed class ProjectDiscoveryEngine : IDisposable
{
    private readonly List<string> _approvedRoots = [];
    private readonly List<ProjectRecord> _discoveredProjects = [];
    private readonly object _lock = new();

    private CancellationTokenSource? _scanCts;
    private bool _isScanning;
    private bool _disposed;

    public IReadOnlyList<ProjectRecord> DiscoveredProjects
    {
        get
        {
            lock (_lock) return _discoveredProjects.ToList();
        }
    }

    public bool IsScanning => _isScanning;

    public event Action<IReadOnlyList<ProjectRecord>>? ProjectsDiscovered;

    public ProjectDiscoveryEngine(IEnumerable<string>? customRoots = null)
    {
        if (customRoots != null)
        {
            _approvedRoots.AddRange(customRoots.Where(Directory.Exists));
        }

        // Add standard development roots if present
        string defaultP = @"P:\Reverse engineering applications";
        if (Directory.Exists(defaultP) && !_approvedRoots.Contains(defaultP, StringComparer.OrdinalIgnoreCase))
            _approvedRoots.Add(defaultP);

        string userRepos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos");
        if (Directory.Exists(userRepos) && !_approvedRoots.Contains(userRepos, StringComparer.OrdinalIgnoreCase))
            _approvedRoots.Add(userRepos);
    }

    public void AddApprovedRoot(string path)
    {
        if (Directory.Exists(path) && !_approvedRoots.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            _approvedRoots.Add(path);
        }
    }

    public void RemoveApprovedRoot(string path)
    {
        _approvedRoots.RemoveAll(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase));
    }

    public async Task StartScanAsync()
    {
        if (_isScanning) return;
        _isScanning = true;

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        await Task.Run(() =>
        {
            var results = new List<ProjectRecord>();

            try
            {
                foreach (var root in _approvedRoots)
                {
                    if (token.IsCancellationRequested) break;
                    ScanDirectoryRecursive(root, depth: 0, maxDepth: 2, results, token);
                }

                lock (_lock)
                {
                    _discoveredProjects.Clear();
                    _discoveredProjects.AddRange(results);
                }

                ProjectsDiscovered?.Invoke(results);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProjectDiscovery scan failed: {ex.Message}");
            }
            finally
            {
                _isScanning = false;
            }
        }, token);
    }

    private static void ScanDirectoryRecursive(string path, int depth, int maxDepth, List<ProjectRecord> results, CancellationToken token)
    {
        if (depth > maxDepth || token.IsCancellationRequested || !Directory.Exists(path))
            return;

        try
        {
            // Check if current directory is a project
            if (IsProjectRoot(path, out string stack))
            {
                string name = Path.GetFileName(path);
                string branch = ResolveGitBranch(path);
                bool hasUncommitted = CheckGitUncommitted(path);
                var lastMod = Directory.GetLastWriteTimeUtc(path);
                string? desc = ReadReadmeSummary(path);

                string normPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
                string id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normPath)))[..12].ToLowerInvariant();

                results.Add(new ProjectRecord(
                    Id: id,
                    Name: name,
                    Path: path,
                    TechStack: stack,
                    GitBranch: string.IsNullOrEmpty(branch) ? "main" : branch,
                    HasUncommittedChanges: hasUncommitted,
                    LastModified: lastMod,
                    Description: desc
                ));

                // Don't recurse deeper into nested submodules of the same project
                return;
            }

            // Recurse into subdirectories
            foreach (var subDir in Directory.GetDirectories(path))
            {
                string dirName = Path.GetFileName(subDir);
                if (dirName.StartsWith('.') || dirName is "node_modules" or "bin" or "obj" or "target" or "dist" or "build")
                    continue;

                ScanDirectoryRecursive(subDir, depth + 1, maxDepth, results, token);
            }
        }
        catch { }
    }

    private static bool IsProjectRoot(string path, out string techStack)
    {
        techStack = "Generic Project";

        bool hasGit = Directory.Exists(Path.Combine(path, ".git"));
        bool hasSln = Directory.GetFiles(path, "*.sln").Length > 0 || Directory.GetFiles(path, "*.csproj").Length > 0;
        bool hasPackageJson = File.Exists(Path.Combine(path, "package.json"));
        bool hasCargo = File.Exists(Path.Combine(path, "Cargo.toml"));
        bool hasPython = File.Exists(Path.Combine(path, "pyproject.toml")) || File.Exists(Path.Combine(path, "requirements.txt"));
        bool hasGo = File.Exists(Path.Combine(path, "go.mod"));

        if (hasSln) techStack = ".NET / C# / WPF";
        else if (hasPackageJson) techStack = "TypeScript / Node.js";
        else if (hasCargo) techStack = "Rust";
        else if (hasPython) techStack = "Python";
        else if (hasGo) techStack = "Go";
        else if (hasGit) techStack = "Git Repository";

        return hasGit || hasSln || hasPackageJson || hasCargo || hasPython || hasGo;
    }

    private static bool CheckGitUncommitted(string path)
    {
        try
        {
            string gitDir = Path.Combine(path, ".git");
            if (!Directory.Exists(gitDir)) return false;

            // Check index or index.lock timestamp vs working tree
            string indexFile = Path.Combine(gitDir, "index");
            if (File.Exists(indexFile))
            {
                var indexTime = File.GetLastWriteTimeUtc(indexFile);
                var dirTime = Directory.GetLastWriteTimeUtc(path);
                return (dirTime - indexTime).TotalMinutes > 5;
            }
        }
        catch { }
        return false;
    }

    private static string? ReadReadmeSummary(string path)
    {
        try
        {
            string[] readmeNames = ["README.md", "readme.md", "README.txt"];
            foreach (var r in readmeNames)
            {
                string p = Path.Combine(path, r);
                if (File.Exists(p))
                {
                    using var reader = new StreamReader(p);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim('#', ' ', '\t');
                        if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith('['))
                        {
                            return line.Length > 120 ? line[..120] + "..." : line;
                        }
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private static string ResolveGitBranch(string directory)
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
                    return content[..Math.Min(7, content.Length)];
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
