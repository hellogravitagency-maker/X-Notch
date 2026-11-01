using System;

namespace XNotch.Plugins;

/// <summary>
/// Attribute to mark a class as an X-Notch plugin.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class XNotchPluginAttribute : Attribute
{
    public string Id { get; }
    public string Name { get; }
    public string Version { get; }
    public string Author { get; }

    public XNotchPluginAttribute(string id, string name, string version, string author)
    {
        Id = id;
        Name = name;
        Version = version;
        Author = author;
    }
}

/// <summary>
/// Attribute to specify plugin dependencies.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class PluginDependencyAttribute : Attribute
{
    public string PluginId { get; }
    public string MinVersion { get; }

    public PluginDependencyAttribute(string pluginId, string minVersion = "0.0.1")
    {
        PluginId = pluginId;
        MinVersion = minVersion;
    }
}

/// <summary>
/// Attribute to specify permissions required by the plugin.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class PluginPermissionAttribute : Attribute
{
    public string Permission { get; }
    public string Reason { get; }

    public PluginPermissionAttribute(string permission, string reason = "")
    {
        Permission = permission;
        Reason = reason;
    }
}
