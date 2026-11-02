using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using XNotch.Helpers;

namespace XNotch.Plugins;

/// <summary>
/// Service for discovering and downloading plugins from the online plugin library.
/// </summary>
public class PluginLibraryService
{
    private static readonly string[] DefaultLibraryUrls = new[]
    {
        "https://raw.githubusercontent.com/hellogravitagency-maker/X-Notch-Plugins/main/library.json",
        "https://raw.githubusercontent.com/N3uralCreativity/X-Notch-Plugins/main/library.json"
    };
    private const string DefaultLibraryUrl = "https://raw.githubusercontent.com/hellogravitagency-maker/X-Notch-Plugins/main/library.json";
    private readonly HttpClient _httpClient;
    private List<PluginManifest> _availablePlugins = new();

    public IReadOnlyList<PluginManifest> AvailablePlugins => _availablePlugins.AsReadOnly();

    public event Action? LibraryUpdated;

    public string? LastError { get; private set; }

    public PluginLibraryService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", $"X-Notch/{AppInfo.Version}");
    }

    public PluginManifest? GetAvailablePlugin(string pluginId)
    {
        return _availablePlugins.FirstOrDefault(plugin => plugin.Id == pluginId);
    }

    public PluginManifest? ReadInstalledManifest(string pluginId, string targetDirectory)
    {
        var manifestPath = Path.Combine(
            targetDirectory,
            SanitizeFileName(pluginId),
            "manifest.json");

        if (!File.Exists(manifestPath))
            return null;

        try
        {
            var json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<PluginManifest>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Fetch the latest plugin library from GitHub.
    /// </summary>
    public async Task<bool> RefreshLibraryAsync(string? customUrl = null)
    {
        try
        {
            string? json = null;
            if (!string.IsNullOrWhiteSpace(customUrl))
            {
                try { json = await _httpClient.GetStringAsync(customUrl); } catch { }
            }
            else
            {
                foreach (var url in DefaultLibraryUrls)
                {
                    try
                    {
                        json = await _httpClient.GetStringAsync(url);
                        if (!string.IsNullOrWhiteSpace(json)) break;
                    }
                    catch { }
                }
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                // Fall back to bundled offline library catalog
                json = LoadBundledLibraryJson();
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                LastError = "Unable to fetch plugin library from configured endpoints or local catalog.";
                return false;
            }

            var library = JsonSerializer.Deserialize<PluginLibrary>(json);
            if (library?.Plugins != null)
            {
                LastError = null;
                _availablePlugins = library.Plugins;
                LibraryUpdated?.Invoke();
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            System.Diagnostics.Debug.WriteLine($"Failed to refresh plugin library: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Download a plugin from the library to the local plugins directory.
    /// </summary>
    public async Task<string?> DownloadPluginAsync(string pluginId, string targetDirectory, IProgress<double>? progress = null)
    {
        var manifest = _availablePlugins.FirstOrDefault(p => p.Id == pluginId);
        if (manifest == null)
        {
            throw new InvalidOperationException($"Plugin {pluginId} not found in library");
        }

        try
        {
            // Create plugin-specific directory
            var pluginDir = Path.Combine(targetDirectory, SanitizeFileName(pluginId));
            Directory.CreateDirectory(pluginDir);

            var localDll = FindLocalPluginDll(manifest);
            string fileName;
            if (!string.IsNullOrWhiteSpace(localDll))
            {
                fileName = Path.GetFileName(localDll);
            }
            else if (!string.IsNullOrEmpty(manifest.DownloadUrl))
            {
                try
                {
                    fileName = Path.GetFileName(new Uri(manifest.DownloadUrl).LocalPath);
                }
                catch
                {
                    fileName = $"{manifest.Name.Replace(" ", "")}Plugin.dll";
                }
            }
            else
            {
                fileName = $"{manifest.Name.Replace(" ", "")}Plugin.dll";
            }
            if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                fileName = $"{manifest.Name.Replace(" ", "")}Plugin.dll";
            }

            var targetPath = Path.Combine(pluginDir, fileName);
            var tempPath = targetPath + ".tmp";

            // 1. Check for local plugin DLL first (bundled / local build / repo)
            if (!string.IsNullOrWhiteSpace(localDll) && File.Exists(localDll))
            {
                progress?.Report(50);
                File.Copy(localDll, tempPath, true);
                progress?.Report(100);
            }
            // 2. Check for embedded assembly resource
            else if (TryExtractEmbeddedPlugin(manifest, tempPath))
            {
                progress?.Report(100);
            }
            // 3. Fallback to HTTP download
            else
            {
                if (string.IsNullOrEmpty(manifest.DownloadUrl))
                {
                    throw new InvalidOperationException($"Plugin '{manifest.Name}' has no download URL or bundled binary.");
                }

                // Download the plugin DLL to a temp file first
                try
                {
                    using var response = await _httpClient.GetAsync(manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    var downloadedBytes = 0L;

                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                    var buffer = new byte[8192];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        downloadedBytes += bytesRead;

                        if (totalBytes > 0)
                        {
                            progress?.Report((double)downloadedBytes / totalBytes * 100);
                        }
                    }

                    fileStream.Close();
                }
                catch (HttpRequestException ex)
                {
                    throw new InvalidOperationException($"Failed to download plugin from {manifest.DownloadUrl}: {ex.Message}", ex);
                }
            }

            // Verify SHA256 if provided
            if (!string.IsNullOrEmpty(manifest.Sha256))
            {
                var computedHash = await ComputeSha256Async(tempPath);
                if (!computedHash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tempPath);
                    throw new InvalidOperationException("Plugin file hash verification failed. The file may be corrupted or tampered with.");
                }
            }

            // Move temp file to final location, handling locked files
            try
            {
                if (File.Exists(targetPath))
                    File.Delete(targetPath);
                File.Move(tempPath, targetPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // File is locked (plugin already loaded) — write to .pending for next restart
                var pendingPath = targetPath + ".pending";
                if (File.Exists(pendingPath))
                    File.Delete(pendingPath);
                File.Move(tempPath, pendingPath);

                // Save manifest alongside the DLL
                var manifestPath2 = Path.Combine(pluginDir, "manifest.json");
                var manifestJson2 = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(manifestPath2, manifestJson2);

                return pendingPath;
            }

            // Save manifest alongside the DLL
            var manifestPath = Path.Combine(pluginDir, "manifest.json");
            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(manifestPath, manifestJson);

            return targetPath;
        }
        catch (Exception ex)
        {
            var pluginDir = Path.Combine(targetDirectory, SanitizeFileName(pluginId));
            var fileName = $"{SanitizeFileName(manifest.Name)}.dll";
            var tempPath = Path.Combine(pluginDir, fileName) + ".tmp";
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Best effort only.
                }
            }

            System.Diagnostics.Debug.WriteLine($"Failed to download plugin {pluginId}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Check if a newer version of a plugin is available.
    /// </summary>
    public bool IsUpdateAvailable(string pluginId, string currentVersion)
    {
        var manifest = _availablePlugins.FirstOrDefault(p => p.Id == pluginId);
        if (manifest == null) return false;

        try
        {
            var current = new Version(currentVersion);
            var available = new Version(manifest.Version);
            return available > current;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Search plugins by name or description.
    /// </summary>
    public IEnumerable<PluginManifest> SearchPlugins(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _availablePlugins;

        query = query.ToLowerInvariant();
        return _availablePlugins.Where(p =>
            p.Name.ToLowerInvariant().Contains(query) ||
            p.Description.ToLowerInvariant().Contains(query) ||
            p.Author.ToLowerInvariant().Contains(query));
    }

    /// <summary>
    /// Filter plugins by category.
    /// </summary>
    public IEnumerable<PluginManifest> GetPluginsByCategory(PluginCategory category)
    {
        return _availablePlugins.Where(p => p.Category == category);
    }

    private async Task<string> ComputeSha256Async(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await Task.Run(() => sha256.ComputeHash(stream));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    public static string? LoadBundledLibraryJson()
    {
        try
        {
            var assembly = typeof(PluginLibraryService).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("example-library.json", StringComparison.OrdinalIgnoreCase));
            if (resourceName != null)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                }
            }
        }
        catch { }

        try
        {
            var baseDir = AppContext.BaseDirectory;
            var candidates = new List<string>
            {
                Path.Combine(baseDir, "Assets", "example-library.json"),
                Path.Combine(baseDir, "example-library.json")
            };

            var current = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && current != null; i++)
            {
                var repoPath = Path.Combine(current.FullName, "Examples", "Plugins", "example-library.json");
                if (File.Exists(repoPath))
                {
                    candidates.Add(repoPath);
                    break;
                }
                var assetsPath = Path.Combine(current.FullName, "src", "XNotch", "Assets", "example-library.json");
                if (File.Exists(assetsPath))
                {
                    candidates.Add(assetsPath);
                    break;
                }
                current = current.Parent;
            }

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }
            }
        }
        catch { }

        return null;
    }

    public static bool TryExtractEmbeddedPlugin(PluginManifest manifest, string destinationPath)
    {
        try
        {
            var assembly = typeof(PluginLibraryService).Assembly;
            var resourceNames = assembly.GetManifestResourceNames();
            if (resourceNames == null || resourceNames.Length == 0)
                return false;

            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(manifest.DownloadUrl))
            {
                try
                {
                    var uri = new Uri(manifest.DownloadUrl);
                    var fn = Path.GetFileName(uri.LocalPath);
                    if (!string.IsNullOrWhiteSpace(fn)) candidates.Add(fn);
                }
                catch { }
            }

            var cleanName = manifest.Name.Replace(" ", "").Replace("-", "");
            candidates.Add($"{cleanName}Plugin.dll");
            candidates.Add($"{cleanName}.dll");
            candidates.Add($"{manifest.Id}.dll");

            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var match = resourceNames.FirstOrDefault(r => r.EndsWith(candidate, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    using var stream = assembly.GetManifestResourceStream(match);
                    if (stream != null)
                    {
                        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        stream.CopyTo(fileStream);
                        return true;
                    }
                }
            }
        }
        catch { }

        return false;
    }

    public static string? FindLocalPluginDll(PluginManifest manifest)
    {
        var possibleFileNames = new List<string>();
        if (!string.IsNullOrEmpty(manifest.DownloadUrl))
        {
            try
            {
                var uri = new Uri(manifest.DownloadUrl);
                var fn = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrWhiteSpace(fn))
                    possibleFileNames.Add(fn);
            }
            catch { }
        }

        var baseName = SanitizeFileName(manifest.Name).Replace(" ", "").Replace("-", "");
        possibleFileNames.Add($"{baseName}.dll");
        possibleFileNames.Add($"{baseName}Plugin.dll");
        possibleFileNames.Add($"{SanitizeFileName(manifest.Name)}.dll");
        possibleFileNames.Add($"{manifest.Id}.dll");
        if (manifest.Id.StartsWith("com.xnotch."))
        {
            possibleFileNames.Add($"{manifest.Id.Replace("com.xnotch.", "")}.dll");
            possibleFileNames.Add($"{manifest.Id.Replace("com.xnotch.", "")}Plugin.dll");
        }

        var searchDirs = new List<string>();

        // 1. Process directory (e.g. publish\ folder for published single-file app)
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var procDir = Path.GetDirectoryName(processPath);
                if (!string.IsNullOrWhiteSpace(procDir) && Directory.Exists(procDir))
                {
                    searchDirs.Add(procDir);
                    searchDirs.Add(Path.Combine(procDir, "BundledPlugins"));
                    searchDirs.Add(Path.Combine(procDir, "Plugins"));
                }
            }
        }
        catch { }

        // 2. Base directory and repo directories
        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDir) && Directory.Exists(baseDir))
        {
            searchDirs.Add(baseDir);
            searchDirs.Add(Path.Combine(baseDir, "BundledPlugins"));
            searchDirs.Add(Path.Combine(baseDir, "Plugins"));

            var current = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && current != null; i++)
            {
                var examplesPath = Path.Combine(current.FullName, "Examples", "Plugins");
                if (Directory.Exists(examplesPath))
                {
                    searchDirs.Add(examplesPath);
                }
                var bundledPath = Path.Combine(current.FullName, "src", "XNotch", "Assets", "BundledPlugins");
                if (Directory.Exists(bundledPath))
                {
                    searchDirs.Add(bundledPath);
                }
                var publishBundledPath = Path.Combine(current.FullName, "publish", "BundledPlugins");
                if (Directory.Exists(publishBundledPath))
                {
                    searchDirs.Add(publishBundledPath);
                }
                current = current.Parent;
            }
        }

        var appDomainDir = AppDomain.CurrentDomain.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(appDomainDir) && Directory.Exists(appDomainDir) && !searchDirs.Contains(appDomainDir))
        {
            searchDirs.Add(appDomainDir);
        }

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var name in possibleFileNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var directPath = Path.Combine(dir, name);
                if (File.Exists(directPath)) return directPath;

                try
                {
                    var matches = Directory.GetFiles(dir, name, SearchOption.AllDirectories);
                    if (matches.Length > 0)
                    {
                        var releaseMatch = matches.FirstOrDefault(m => m.Contains("Release", StringComparison.OrdinalIgnoreCase));
                        return releaseMatch ?? matches[0];
                    }
                }
                catch { }
            }
        }

        return null;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
    }

    public class PluginLibrary
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        [JsonPropertyName("plugins")]
        public List<PluginManifest> Plugins { get; set; } = new();
    }
}
