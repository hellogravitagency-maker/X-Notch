using System;

namespace XNotch.Models;

public sealed record ApprovalRequest(
    string PromptId,
    string AgentId,
    string ToolName,
    string Command,
    string WorkingDirectory,
    int TimeoutSeconds,
    DateTime ReceivedAt
);

public enum ApprovalDecision
{
    Allow,
    Deny,
    Cancel
}
