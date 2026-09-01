namespace CryptoSpot.Application.Abstractions.Services.Ai;

public interface IAiApprovalService
{
    Task<AiApprovalDto> CreateAsync(long userId, string actionType, string payloadJson, CancellationToken cancellationToken = default);
    Task<AiApprovalDto> DecideAsync(long userId, long approvalId, bool approved, CancellationToken cancellationToken = default);
}

public sealed record AiApprovalDto(long Id, string ActionType, string PayloadJson, string Status, DateTime ExpiresAt);
