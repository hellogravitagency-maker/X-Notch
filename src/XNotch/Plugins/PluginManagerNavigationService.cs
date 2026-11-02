using System;
using System.Windows;

namespace XNotch.Plugins;

/// <summary>
/// Allows plugins to route the user back into the plugin manager for setup.
/// </summary>
public sealed class PluginManagerNavigationService
{
    private readonly Action<string?> _openPluginManager;

    public PluginManagerNavigationService(Action<string?> openPluginManager)
    {
        _openPluginManager = openPluginManager;
    }

    public void OpenPluginManager(string? pluginId = null)
    {
        if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.CheckAccess())
        {
            _openPluginManager(pluginId);
            return;
        }

        _ = Application.Current.Dispatcher.BeginInvoke(new Action(() => _openPluginManager(pluginId)));
    }
}
