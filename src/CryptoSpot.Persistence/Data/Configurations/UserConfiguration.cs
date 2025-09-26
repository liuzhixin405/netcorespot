using CryptoSpot.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CryptoSpot.Persistence.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> entity)
    {
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
        entity.Property(e => e.Email).HasMaxLength(100);
        entity.Property(e => e.PasswordHash).HasMaxLength(255);
        entity.Property(e => e.Description).HasMaxLength(200);
        entity.Property(e => e.MaxRiskRatio).HasColumnType("decimal(5,4)");
        entity.Property(e => e.DailyTradingLimit).HasColumnType("decimal(18,8)");
        entity.Property(e => e.DailyTradedAmount).HasColumnType("decimal(18,8)");
        entity.HasIndex(e => e.Username).IsUnique();
        entity.HasIndex(e => e.Email).IsUnique();
    }
}
