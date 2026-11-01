using System.Threading;
using System.Threading.Tasks;

namespace XNotch.Services.AI;

public interface IAIProvider
{
    string ProviderName { get; }
    bool IsAvailable { get; }
    Task<string> GenerateCompletionAsync(string prompt, CancellationToken ct = default);
}
