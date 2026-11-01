using System;

namespace XNotch.Models;

public sealed record ProjectRecord(
    string Id,
    string Name,
    string Path,
    string TechStack,
    string GitBranch,
    bool HasUncommittedChanges,
    DateTime LastModified,
    string? Description = null,
    string? ReadmeSummary = null
);
