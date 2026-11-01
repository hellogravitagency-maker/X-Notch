using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using XNotch.Models;

namespace XNotch.Services;

/// <summary>
/// Intercepts incoming Windows Toast Notifications via UserNotificationListener
/// and surfaces them as transient Dynamic Island alerts and cards.
/// Gracefully falls back if unpackaged access is denied.
/// </summary>
public sealed class NotificationService : IDisposable
{
    private readonly HashSet<uint> _seenNotificationIds = [];
    private readonly List<NotificationItem> _history = [];
    private readonly object _lock = new();

    private CancellationTokenSource? _pollCts;
    private UserNotificationListener? _listener;
    private bool _isAllowed;
    private bool _disposed;

    public bool IsSupported => _isAllowed;
    public IReadOnlyList<NotificationItem> History
    {
        get
        {
            lock (_lock) return _history.ToList();
        }
    }

    public event Action<NotificationItem>? NotificationArrived;
    public event Action<uint>? NotificationDismissed;

    public async Task InitializeAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            var status = await _listener.RequestAccessAsync();
            _isAllowed = status == UserNotificationListenerAccessStatus.Allowed;

            if (_isAllowed)
            {
                _pollCts = new CancellationTokenSource();
                _ = Task.Run(() => PollLoopAsync(_pollCts.Token));
            }
            SeedWelcomeNotificationIfEmpty();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NotificationService access error: {ex.Message}");
            _isAllowed = false;
        }
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && !_disposed)
        {
            try
            {
                await CheckNewNotificationsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Notification poll error: {ex.Message}");
            }

            try
            {
                await Task.Delay(1200, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckNewNotificationsAsync()
    {
        if (_listener == null || !_isAllowed) return;

        var rawList = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
        var currentBatchIds = new HashSet<uint>();

        foreach (var item in rawList)
        {
            uint id = item.Id;
            currentBatchIds.Add(id);

            bool isNew;
            lock (_lock)
            {
                isNew = _seenNotificationIds.Add(id);
            }

            if (!isNew) continue;

            string appName = string.Empty;
            string aumid = string.Empty;
            try
            {
                appName = item.AppInfo?.DisplayInfo?.DisplayName ?? "";
                aumid = item.AppInfo?.AppUserModelId ?? "";
            }
            catch { }

            string title = string.Empty;
            string body = string.Empty;
            string? logoUri = null;

            try
            {
                var binding = item.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                if (binding != null)
                {
                    var texts = binding.GetTextElements();
                    if (texts.Count > 0) title = texts[0].Text ?? "";
                    if (texts.Count > 1) body = string.Join(" ", texts.Skip(1).Select(t => t.Text));

                    if (binding.Hints != null)
                    {
                        foreach (var kvp in binding.Hints)
                        {
                            if (kvp.Key.Contains("image", StringComparison.OrdinalIgnoreCase) ||
                                kvp.Key.Contains("logo", StringComparison.OrdinalIgnoreCase) ||
                                kvp.Key.Contains("icon", StringComparison.OrdinalIgnoreCase))
                            {
                                logoUri = kvp.Value;
                                break;
                            }
                        }
                    }
                }
            }
            catch { }

            byte[]? logoBytes = null;
            try
            {
                var logoStreamRef = item.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(48, 48));
                if (logoStreamRef != null)
                {
                    using var stream = await logoStreamRef.OpenReadAsync();
                    if (stream != null && stream.Size > 0)
                    {
                        using var reader = new DataReader(stream);
                        await reader.LoadAsync((uint)stream.Size);
                        logoBytes = new byte[stream.Size];
                        reader.ReadBytes(logoBytes);
                    }
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(appName)) appName = "System Notification";
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
                title = appName;

            var notification = new NotificationItem(
                Id: id,
                AppName: appName,
                Title: title,
                Body: body,
                Timestamp: DateTime.UtcNow,
                AppUserModelId: aumid,
                AppLogoBytes: logoBytes,
                AppLogoUri: logoUri
            );

            lock (_lock)
            {
                _history.Insert(0, notification);
                if (_history.Count > 50) _history.RemoveAt(_history.Count - 1);
            }

            NotificationArrived?.Invoke(notification);
        }

        // Clean up dismissed IDs from seen set
        lock (_lock)
        {
            _seenNotificationIds.RemoveWhere(id => !currentBatchIds.Contains(id));
        }
    }

    public void DismissNotification(uint id)
    {
        try
        {
            _listener?.RemoveNotification(id);
            lock (_lock)
            {
                _history.RemoveAll(n => n.Id == id);
            }
            NotificationDismissed?.Invoke(id);
        }
        catch { }
    }

    public void ClearHistory()
    {
        lock (_lock)
        {
            _history.Clear();
        }
    }

    public void SeedWelcomeNotificationIfEmpty()
    {
        lock (_lock)
        {
            if (_history.Count == 0)
            {
                _history.Add(new NotificationItem(
                    Id: 1001,
                    AppName: "X-Notch",
                    Title: "Dynamic Island Active",
                    Body: "Notification alerts and widget cards are active and ready.",
                    Timestamp: DateTime.UtcNow.AddMinutes(-3)
                ));
                _history.Add(new NotificationItem(
                    Id: 1002,
                    AppName: "System",
                    Title: "Battery & Performance Optimized",
                    Body: "High-performance DirectX StreamGeometry rendering enabled.",
                    Timestamp: DateTime.UtcNow.AddMinutes(-18)
                ));
            }
        }
    }

    public void AddNotification(string appName, string title, string body)
    {
        var item = new NotificationItem(
            Id: (uint)Random.Shared.Next(10000, 99999),
            AppName: appName,
            Title: title,
            Body: body,
            Timestamp: DateTime.UtcNow
        );
        lock (_lock)
        {
            _history.Insert(0, item);
            if (_history.Count > 50) _history.RemoveAt(_history.Count - 1);
        }
        NotificationArrived?.Invoke(item);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollCts?.Cancel();
        _pollCts?.Dispose();
    }
}
