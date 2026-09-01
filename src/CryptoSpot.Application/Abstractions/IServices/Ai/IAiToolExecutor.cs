namespace CryptoSpot.Application.Abstractions.Services.Ai;

public interface IAiToolExecutor
{
    IReadOnlyList<AiToolDefinition> Definitions { get; }
    Task<string> ExecuteAsync(long userId, AiToolCall toolCall, CancellationToken cancellationToken = default);
}
