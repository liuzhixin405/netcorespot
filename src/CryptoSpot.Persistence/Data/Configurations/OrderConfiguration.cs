using CryptoSpot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CryptoSpot.Persistence.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> entity)
    {
        entity.HasKey(e => e.Id);
        entity.Property(e => e.OrderId).IsRequired().HasMaxLength(50);
        entity.Property(e => e.Quantity).HasColumnType("decimal(18,8)");
        entity.Property(e => e.Price).HasColumnType("decimal(18,8)");
        entity.Property(e => e.FilledQuantity).HasColumnType("decimal(18,8)");
        entity.Property(e => e.AveragePrice).HasColumnType("decimal(18,8)");
        entity.HasIndex(e => e.OrderId).IsUnique();
        entity.HasIndex(e => new { e.UserId, e.Status });
    }
}
