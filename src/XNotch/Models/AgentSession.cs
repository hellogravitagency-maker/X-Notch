using System;

namespace XNotch.Models;

public enum AgentPhase
{
    Idle,
    Thinking,
    RunningTool,
    AwaitingApproval,
    Completed,
    Failed
}

public sealed record AgentSession(
    string AgentId,
    string DisplayName,
    int ProcessId,
    string ProjectPath,
    string GitBranch,
    AgentPhase Phase,
    string CurrentAction,
    DateTime StartedAt,
    bool RequiresApproval = false,
    string? PendingApprovalPrompt = null
)
{
    public TimeSpan ElapsedDuration => DateTime.UtcNow - StartedAt;
}

public sealed record AgentDefinition(
    string Id,
    string DisplayName,
    string Glyph,
    string[] ExecutableNames,
    string[] CommandLineMarkers
);
