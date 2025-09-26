using CryptoSpot.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CryptoSpot.Persistence.Data.Configurations;

public class TradeConfiguration : IEntityTypeConfiguration<Trade>
{
    public void Configure(EntityTypeBuilder<Trade> entity)
    {
        entity.HasKey(e => e.Id);
        entity.Property(e => e.TradeId).IsRequired().HasMaxLength(50);
        entity.Property(e => e.Quantity).HasColumnType("decimal(18,8)");
        entity.Property(e => e.Price).HasColumnType("decimal(18,8)");
        entity.Property(e => e.Fee).HasColumnType("decimal(18,8)");
        entity.Property(e => e.FeeAsset).HasMaxLength(10);
        entity.HasIndex(e => e.TradeId).IsUnique();
        entity.HasIndex(e => e.ExecutedAt);
    }
}
