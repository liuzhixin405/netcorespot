namespace CryptoSpot.Application.Abstractions.Services.Ai;

public interface IAiAuditService
{
    Task<long> WriteAsync(AiAuditEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiAuditEntry>> ListAsync(long userId, CancellationToken cancellationToken = default);
}

public sealed record AiAuditEntry(long UserId, long? ConversationId, string Action, string? ModelId, string ToolName, string ToolArgumentsJson, string ResultJson, string Status, long ElapsedMs, long? Id = null);
