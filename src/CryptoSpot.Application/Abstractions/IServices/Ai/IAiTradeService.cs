namespace CryptoSpot.Application.Abstractions.Services.Ai;

public interface IAiTradeService
{
    Task<AiTradeResult> PlanAndExecuteAsync(long userId, string instruction, string defaultSymbol, CancellationToken cancellationToken = default);
    Task<AiTradeResult> ExecuteApprovedAsync(long userId, long approvalId, CancellationToken cancellationToken = default);
}

public sealed record AiTradeResult(
    string Summary,
    string Status,
    long? ApprovalId = null,
    long? OrderId = null,
    IReadOnlyList<long>? ApprovalIds = null,
    IReadOnlyList<AiTradeAction>? ExecutedActions = null);

public sealed record AiTradeAction(string ToolName, string Summary, long? OrderId = null);
