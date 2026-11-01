using System;

namespace XNotch.Models;

public sealed record NotificationItem(
    uint Id,
    string AppName,
    string Title,
    string Body,
    DateTime Timestamp,
    string? AppUserModelId = null,
    byte[]? AppLogoBytes = null,
    string? AppLogoUri = null
);
