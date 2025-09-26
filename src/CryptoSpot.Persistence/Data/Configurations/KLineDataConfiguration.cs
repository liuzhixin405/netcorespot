using CryptoSpot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CryptoSpot.Persistence.Data.Configurations;

public class KLineDataConfiguration : IEntityTypeConfiguration<KLineData>
{
    public void Configure(EntityTypeBuilder<KLineData> entity)
    {
        entity.HasKey(e => e.Id);
        entity.Property(e => e.TimeFrame).IsRequired().HasMaxLength(10);
        entity.Property(e => e.Open).HasColumnType("decimal(18,8)");
        entity.Property(e => e.High).HasColumnType("decimal(18,8)");
        entity.Property(e => e.Low).HasColumnType("decimal(18,8)");
        entity.Property(e => e.Close).HasColumnType("decimal(18,8)");
        entity.Property(e => e.Volume).HasColumnType("decimal(18,8)");
        entity.HasIndex(e => new { e.TradingPairId, e.TimeFrame, e.OpenTime });
    }
}
