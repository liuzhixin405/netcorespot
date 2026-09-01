using CryptoSpot.Application.Abstractions.Services.Ai;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Domain.Extensions;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Infrastructure.Ai;

public sealed class AiApprovalService : IAiApprovalService
{
    private readonly ApplicationDbContext _dbContext;

    public AiApprovalService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AiApprovalDto> CreateAsync(long userId, string actionType, string payloadJson, CancellationToken cancellationToken = default)
    {
        var approval = new AiApproval
        {
            UserId = userId,
            ActionType = actionType,
            PayloadJson = payloadJson,
            ExpiresAt = DateTimeExtensions.GetCurrentUnixTimeMilliseconds() + (long)TimeSpan.FromMinutes(5).TotalMilliseconds
        };
        _dbContext.AiApprovals.Add(approval);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(approval);
    }

    public async Task<AiApprovalDto> DecideAsync(long userId, long approvalId, bool approved, CancellationToken cancellationToken = default)
    {
        var approval = await _dbContext.AiApprovals.SingleOrDefaultAsync(value =>
            value.Id == approvalId && value.UserId == userId && !value.IsDeleted, cancellationToken)
            ?? throw new InvalidOperationException("AI approval was not found.");
        if (approval.Status != "Pending")
            throw new InvalidOperationException("AI approval is no longer pending.");
        if (approval.ExpiresAt < DateTimeExtensions.GetCurrentUnixTimeMilliseconds())
        {
            approval.Status = "Expired";
        }
        else
        {
            approval.Status = approved ? "Approved" : "Rejected";
            approval.DecidedAt = DateTimeExtensions.GetCurrentUnixTimeMilliseconds();
        }
        approval.Touch();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(approval);
    }

    private static AiApprovalDto ToDto(AiApproval approval) =>
        new(approval.Id, approval.ActionType, approval.PayloadJson, approval.Status,
            DateTimeOffset.FromUnixTimeMilliseconds(approval.ExpiresAt).UtcDateTime);
}
