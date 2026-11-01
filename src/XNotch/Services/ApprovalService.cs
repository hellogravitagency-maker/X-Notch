using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XNotch.Models;

namespace XNotch.Services;

/// <summary>
/// High-security named pipe server (CurrentUserOnly) listening for interactive coding agent
/// approval requests, routing them to the Dynamic Island UI, and returning user decisions.
/// Supports both XNotch-Bridge and XNotch-AgentHooks endpoints for cross-compatibility.
/// </summary>
public sealed class ApprovalService : IDisposable
{
    public const string DefaultPipeName = "XNotch-AgentHooks";
    public const string BridgePipeName = "XNotch-Bridge";

    private readonly string _primaryPipeName;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ApprovalDecision>> _pendingRequests = new();
    private readonly CancellationTokenSource _stopCts = new();
    private bool _disposed;

    public ApprovalService(string pipeName = DefaultPipeName)
    {
        _primaryPipeName = pipeName;
    }

    public event Action<ApprovalRequest>? ApprovalPromptArrived;
    public event Action<string>? ApprovalPromptSettled;

    public void Start()
    {
        _ = Task.Run(() => ServerLoopAsync(_primaryPipeName, _stopCts.Token));
        if (!string.Equals(_primaryPipeName, BridgePipeName, StringComparison.OrdinalIgnoreCase))
        {
            _ = Task.Run(() => ServerLoopAsync(BridgePipeName, _stopCts.Token));
        }
    }

    private async Task ServerLoopAsync(string pipeName, CancellationToken token)
    {
        while (!token.IsCancellationRequested && !_disposed)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreateSecurePipe(pipeName);
                await pipe.WaitForConnectionAsync(token);

                // Handle client on background task so server can accept concurrent connections
                var connectedPipe = pipe;
                pipe = null;
                _ = Task.Run(() => HandleClientAsync(connectedPipe, token), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ApprovalService ({pipeName}) error: {ex.Message}");
                await Task.Delay(500, CancellationToken.None);
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private static NamedPipeServerStream CreateSecurePipe(string pipeName)
    {
        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User!;
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            64 * 1024,
            64 * 1024,
            security);
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        using (pipe)
        using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true))
        using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
        {
            try
            {
                string? line = await reader.ReadLineAsync(token);
                if (string.IsNullOrWhiteSpace(line) || line.Length > 64 * 1024)
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new { ok = false, error = "invalid_or_oversized_message" }));
                    return;
                }

                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                string type = root.TryGetProperty("type", out var tProp) ? tProp.GetString() ?? "" : "";

                if (type == "approval_prompt")
                {
                    string promptId = root.TryGetProperty("promptId", out var pProp) ? pProp.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                    string agentId = root.TryGetProperty("agentId", out var aProp) ? aProp.GetString() ?? "agent" : "agent";
                    string tool = root.TryGetProperty("tool", out var toolProp) ? toolProp.GetString() ?? "command" : "command";
                    string cmd = root.TryGetProperty("command", out var cmdProp) ? cmdProp.GetString() ?? "" : "";
                    string dir = root.TryGetProperty("workingDir", out var dirProp) ? dirProp.GetString() ?? "" : "";
                    int timeout = root.TryGetProperty("timeoutSeconds", out var toProp) ? toProp.GetInt32() : 60;

                    var request = new ApprovalRequest(
                        PromptId: promptId,
                        AgentId: agentId,
                        ToolName: tool,
                        Command: cmd,
                        WorkingDirectory: dir,
                        TimeoutSeconds: timeout,
                        ReceivedAt: DateTime.UtcNow
                    );

                    var tcs = new TaskCompletionSource<ApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _pendingRequests[promptId] = tcs;

                    // Emit to UI
                    ApprovalPromptArrived?.Invoke(request);

                    // Wait for user answer or timeout
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                    try
                    {
                        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, linkedCts.Token));
                        ApprovalDecision decision = ApprovalDecision.Cancel;

                        if (completedTask == tcs.Task)
                        {
                            decision = await tcs.Task;
                        }

                        string decisionStr = decision switch
                        {
                            ApprovalDecision.Allow => "allow",
                            ApprovalDecision.Deny => "deny",
                            _ => "timeout"
                        };

                        await writer.WriteLineAsync(JsonSerializer.Serialize(new
                        {
                            ok = true,
                            type = "approval_reply",
                            promptId,
                            decision = decisionStr
                        }));
                    }
                    catch (OperationCanceledException)
                    {
                        await writer.WriteLineAsync(JsonSerializer.Serialize(new
                        {
                            ok = false,
                            type = "approval_reply",
                            promptId,
                            decision = "timeout"
                        }));
                    }
                    finally
                    {
                        _pendingRequests.TryRemove(promptId, out _);
                        ApprovalPromptSettled?.Invoke(promptId);
                    }
                }
                else
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new { ok = false, error = "unknown_operation" }));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Client handler error: {ex.Message}");
            }
        }
    }

    public bool SubmitDecision(string promptId, ApprovalDecision decision)
    {
        if (_pendingRequests.TryGetValue(promptId, out var tcs))
        {
            return tcs.TrySetResult(decision);
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopCts.Cancel();
        _stopCts.Dispose();

        foreach (var tcs in _pendingRequests.Values)
        {
            tcs.TrySetResult(ApprovalDecision.Cancel);
        }
        _pendingRequests.Clear();
    }
}
