using CryptoSpot.Application.Abstractions.Services.Risk;
using CryptoSpot.Application.DTOs.Risk;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Infrastructure.Risk;

public sealed class RiskEventService : IRiskEventService
{
    private readonly ApplicationDbContext _dbContext;

    public RiskEventService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<RiskEventDto?> RecordIfNewAsync(
        RiskEventInput input,
        TimeSpan deduplicationWindow,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var cutoff = now - (long)deduplicationWindow.TotalMilliseconds;
        var exists = await _dbContext.RiskEvents.AnyAsync(value =>
            value.EventType == input.EventType &&
            value.UserId == input.UserId &&
            value.Symbol == input.Symbol &&
            value.OrderId == input.OrderId &&
            value.CreatedAt >= cutoff &&
            (value.Status == RiskEventStatus.New || value.Status == RiskEventStatus.Acknowledged),
            cancellationToken);
        if (exists)
            return null;

        var entity = new RiskEvent
        {
            UserId = input.UserId,
            EventType = input.EventType,
            Severity = input.Severity,
            Message = input.Message,
            Symbol = input.Symbol,
            OrderId = input.OrderId,
            MetaJson = input.MetaJson,
            DetectedAt = now
        };
        _dbContext.RiskEvents.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task<IReadOnlyList<RiskEventDto>> ListAsync(
        long userId,
        RiskEventStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.RiskEvents.AsNoTracking()
            .Where(value => !value.IsDeleted && (value.UserId == userId || value.UserId == null));
        if (status.HasValue)
            query = query.Where(value => value.Status == status.Value);

        var entities = await query.OrderByDescending(value => value.DetectedAt).Take(200).ToListAsync(cancellationToken);
        return entities.Select(ToDto).ToList();
    }

    public async Task<RiskDashboardDto> GetDashboardAsync(long userId, CancellationToken cancellationToken = default)
    {
        var events = await _dbContext.RiskEvents.AsNoTracking()
            .Where(value => !value.IsDeleted && (value.UserId == userId || value.UserId == null))
            .OrderByDescending(value => value.DetectedAt).Take(200).ToListAsync(cancellationToken);
        var open = events.Where(value => value.Status is RiskEventStatus.New or RiskEventStatus.Acknowledged).ToList();
        return new RiskDashboardDto(
            open.Count,
            open.Count(value => value.Severity == RiskSeverity.Critical),
            open.Count(value => value.Severity == RiskSeverity.High),
            open.GroupBy(value => value.EventType.ToString()).ToDictionary(group => group.Key, group => group.Count()),
            events.Take(20).Select(ToDto).ToList());
    }

    public async Task<RiskEventDto> HandleAsync(
        long userId,
        long eventId,
        RiskEventStatus status,
        CancellationToken cancellationToken = default)
    {
        if (status is not (RiskEventStatus.Acknowledged or RiskEventStatus.Resolved or RiskEventStatus.Ignored))
            throw new ArgumentException("风险事件只能标记为已确认、已解决或已忽略。", nameof(status));

        var entity = await _dbContext.RiskEvents.SingleOrDefaultAsync(value =>
            value.Id == eventId && value.UserId == userId && !value.IsDeleted, cancellationToken)
            ?? throw new InvalidOperationException("风险事件不存在或无权处置。");
        entity.Status = status;
        entity.HandledAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        entity.HandledByUserId = userId;
        entity.Touch();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    private static RiskEventDto ToDto(RiskEvent value) => new(
        value.Id,
        value.UserId,
        value.EventType,
        value.Severity,
        value.Message,
        value.Symbol,
        value.OrderId,
        value.MetaJson,
        DateTimeOffset.FromUnixTimeMilliseconds(value.DetectedAt).UtcDateTime,
        value.Status,
        value.HandledAt.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(value.HandledAt.Value).UtcDateTime : null);
}
