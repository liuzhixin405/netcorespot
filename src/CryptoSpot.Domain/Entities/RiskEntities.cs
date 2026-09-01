using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoSpot.Domain.Entities;

public enum RiskEventType
{
    AbnormalOrder,
    PriceDeviation,
    WashTrading,
    OrderFlood,
    InventoryConcentration,
    TradingSuspension
}

public enum RiskSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum RiskEventStatus
{
    New,
    Acknowledged,
    Resolved,
    Ignored
}

[Table("RiskEvents")]
public sealed class RiskEvent : BaseEntity
{
    public long? UserId { get; set; }

    public RiskEventType EventType { get; set; }

    public RiskSeverity Severity { get; set; }

    [Required]
    [MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Symbol { get; set; }

    public long? OrderId { get; set; }

    [Required]
    public string MetaJson { get; set; } = "{}";

    public long DetectedAt { get; set; }

    public RiskEventStatus Status { get; set; } = RiskEventStatus.New;

    public long? HandledAt { get; set; }

    public long? HandledByUserId { get; set; }
}

[Table("BacktestRuns")]
public sealed class BacktestRun : BaseEntity
{
    public long UserId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Symbol { get; set; } = string.Empty;

    [Required]
    [MaxLength(10)]
    public string Interval { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Strategy { get; set; } = string.Empty;

    [Required]
    public string ConfigJson { get; set; } = "{}";

    [Required]
    public string ResultJson { get; set; } = "{}";

    public long CompletedAt { get; set; }
}
