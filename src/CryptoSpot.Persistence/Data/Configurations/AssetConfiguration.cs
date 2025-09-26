using CryptoSpot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CryptoSpot.Persistence.Data.Configurations;

public class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> entity)
    {
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Symbol).IsRequired().HasMaxLength(10);
        entity.Property(e => e.Available).HasColumnType("decimal(18,8)");
        entity.Property(e => e.Frozen).HasColumnType("decimal(18,8)");
        entity.Property(e => e.MinReserve).HasColumnType("decimal(18,8)");
        entity.Property(e => e.TargetBalance).HasColumnType("decimal(18,8)");
        entity.HasIndex(e => new { e.UserId, e.Symbol }).IsUnique();
    }
}
