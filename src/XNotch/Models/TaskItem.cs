using System;

namespace XNotch.Models;

public enum TaskStatusState
{
    Observed,
    Suggested,
    AwaitingApproval,
    Confirmed,
    InProgress,
    Completed,
    Dismissed
}

public enum TaskPriority
{
    Low,
    Medium,
    High,
    Critical
}

public sealed record TaskItem(
    string Id,
    string ProjectId,
    string ProjectName,
    string Title,
    string SourceEvidence,
    TaskPriority Priority,
    TaskStatusState Status,
    DateTime DiscoveredAt,
    DateTime? DueDate = null,
    string? SuggestedNextAction = null
);
