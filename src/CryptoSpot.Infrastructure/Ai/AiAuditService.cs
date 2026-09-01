using CryptoSpot.Application.Abstractions.Services.Ai;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Infrastructure.Ai;

public sealed class AiAuditService : IAiAuditService
{
    private readonly ApplicationDbContext _dbContext;

    public AiAuditService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<long> WriteAsync(AiAuditEntry entry, CancellationToken cancellationToken = default)
    {
        var entity = new AiAuditLog
        {
            UserId = entry.UserId,
            ConversationId = entry.ConversationId,
            Action = entry.Action,
            ModelId = entry.ModelId,
            ToolName = entry.ToolName,
            ToolArgumentsJson = entry.ToolArgumentsJson,
            ResultJson = entry.ResultJson,
            Status = entry.Status,
            ElapsedMs = entry.ElapsedMs
        };
        _dbContext.AiAuditLogs.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<IReadOnlyList<AiAuditEntry>> ListAsync(long userId, CancellationToken cancellationToken = default) =>
        await _dbContext.AiAuditLogs.AsNoTracking().Where(value => value.UserId == userId && !value.IsDeleted)
            .OrderByDescending(value => value.CreatedAt).Take(200)
            .Select(value => new AiAuditEntry(value.UserId, value.ConversationId, value.Action, value.ModelId,
                value.ToolName, value.ToolArgumentsJson ?? "{}", value.ResultJson, value.Status, value.ElapsedMs, value.Id))
            .ToListAsync(cancellationToken);
}
