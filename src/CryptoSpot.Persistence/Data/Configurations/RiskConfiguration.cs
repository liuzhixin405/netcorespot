using CryptoSpot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CryptoSpot.Persistence.Data.Configurations;

public sealed class RiskEventConfiguration : IEntityTypeConfiguration<RiskEvent>
{
    public void Configure(EntityTypeBuilder<RiskEvent> entity)
    {
        entity.HasKey(value => value.Id);
        entity.Property(value => value.EventType).HasConversion<string>().HasMaxLength(50);
        entity.Property(value => value.Severity).HasConversion<string>().HasMaxLength(20);
        entity.Property(value => value.Status).HasConversion<string>().HasMaxLength(20);
        entity.Property(value => value.Message).IsRequired().HasMaxLength(500);
        entity.Property(value => value.MetaJson).IsRequired().HasColumnType("longtext");
        entity.HasIndex(value => new { value.UserId, value.Status, value.CreatedAt });
        entity.HasIndex(value => new { value.Status, value.Severity, value.CreatedAt });
        entity.HasIndex(value => new { value.EventType, value.Symbol, value.OrderId, value.CreatedAt });
    }
}

public sealed class BacktestRunConfiguration : IEntityTypeConfiguration<BacktestRun>
{
    public void Configure(EntityTypeBuilder<BacktestRun> entity)
    {
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Symbol).IsRequired().HasMaxLength(50);
        entity.Property(value => value.Interval).IsRequired().HasMaxLength(10);
        entity.Property(value => value.Strategy).IsRequired().HasMaxLength(50);
        entity.Property(value => value.ConfigJson).IsRequired().HasColumnType("longtext");
        entity.Property(value => value.ResultJson).IsRequired().HasColumnType("longtext");
        entity.HasIndex(value => new { value.UserId, value.CreatedAt });
    }
}
