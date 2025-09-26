using CryptoSpot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CryptoSpot.Persistence.Data.Configurations;

public class TradingPairConfiguration : IEntityTypeConfiguration<TradingPair>
{
    public void Configure(EntityTypeBuilder<TradingPair> entity)
    {
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Symbol).IsRequired().HasMaxLength(20);
        entity.Property(e => e.BaseAsset).IsRequired().HasMaxLength(10);
        entity.Property(e => e.QuoteAsset).IsRequired().HasMaxLength(10);
        entity.Property(e => e.Price).HasColumnType("decimal(18,8)");
        entity.Property(e => e.Change24h).HasColumnType("decimal(18,8)");
        entity.Property(e => e.Volume24h).HasColumnType("decimal(18,8)");
        entity.Property(e => e.High24h).HasColumnType("decimal(18,8)");
        entity.Property(e => e.Low24h).HasColumnType("decimal(18,8)");
        entity.HasIndex(e => e.Symbol).IsUnique();
    }
}
