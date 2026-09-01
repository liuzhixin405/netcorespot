using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.DTOs.Risk;

public sealed record RiskEventDto(
    long Id,
    long? UserId,
    RiskEventType EventType,
    RiskSeverity Severity,
    string Message,
    string? Symbol,
    long? OrderId,
    string MetaJson,
    DateTime DetectedAt,
    RiskEventStatus Status,
    DateTime? HandledAt);

public sealed record RiskEventInput(
    long? UserId,
    RiskEventType EventType,
    RiskSeverity Severity,
    string Message,
    string? Symbol = null,
    long? OrderId = null,
    string MetaJson = "{}");

public sealed record RiskDashboardDto(
    int OpenEvents,
    int CriticalEvents,
    int HighEvents,
    IReadOnlyDictionary<string, int> EventsByType,
    IReadOnlyList<RiskEventDto> RecentEvents);
