using Microsoft.EntityFrameworkCore;
using CryptoSpot.Core.Entities;

namespace CryptoSpot.Infrastructure.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<TradingPair> TradingPairs { get; set; }
        public DbSet<KLineData> KLineData { get; set; }
        public DbSet<Asset> Assets { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<Trade> Trades { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 配置并发检测


            // Configure User entity
            modelBuilder.Entity<User>(entity =>
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
            });

            // Configure TradingPair entity
            modelBuilder.Entity<TradingPair>(entity =>
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
            });

            // Configure KLineData entity
            modelBuilder.Entity<KLineData>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.TimeFrame).IsRequired().HasMaxLength(10);
                entity.Property(e => e.Open).HasColumnType("decimal(18,8)");
                entity.Property(e => e.High).HasColumnType("decimal(18,8)");
                entity.Property(e => e.Low).HasColumnType("decimal(18,8)");
                entity.Property(e => e.Close).HasColumnType("decimal(18,8)");
                entity.Property(e => e.Volume).HasColumnType("decimal(18,8)");
                entity.HasIndex(e => new { e.TradingPairId, e.TimeFrame, e.OpenTime });
            });

            // Configure Asset entity
            modelBuilder.Entity<Asset>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Symbol).IsRequired().HasMaxLength(10);
                entity.Property(e => e.Available).HasColumnType("decimal(18,8)");
                entity.Property(e => e.Frozen).HasColumnType("decimal(18,8)");
                entity.Property(e => e.MinReserve).HasColumnType("decimal(18,8)");
                entity.Property(e => e.TargetBalance).HasColumnType("decimal(18,8)");
                entity.HasIndex(e => new { e.UserId, e.Symbol }).IsUnique();
            });

            // Configure Order entity
            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.OrderId).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Quantity).HasColumnType("decimal(18,8)");
                entity.Property(e => e.Price).HasColumnType("decimal(18,8)");
                entity.Property(e => e.FilledQuantity).HasColumnType("decimal(18,8)");
                entity.Property(e => e.AveragePrice).HasColumnType("decimal(18,8)");
                entity.HasIndex(e => e.OrderId).IsUnique();
                entity.HasIndex(e => new { e.UserId, e.Status });
            });

            // Configure Trade entity
            modelBuilder.Entity<Trade>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.TradeId).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Quantity).HasColumnType("decimal(18,8)");
                entity.Property(e => e.Price).HasColumnType("decimal(18,8)");
                entity.Property(e => e.Fee).HasColumnType("decimal(18,8)");
                entity.Property(e => e.FeeAsset).HasMaxLength(10);
                entity.HasIndex(e => e.TradeId).IsUnique();
                entity.HasIndex(e => e.ExecutedAt);
            });


            // Configure relationships
            modelBuilder.Entity<KLineData>()
                .HasOne(k => k.TradingPair)
                .WithMany(tp => tp.KLineData)
                .HasForeignKey(k => k.TradingPairId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Asset>()
                .HasOne(a => a.User)
                .WithMany(u => u.Assets)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Order>()
                .HasOne(o => o.User)
                .WithMany(u => u.Orders)
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Order>()
                .HasOne(o => o.TradingPair)
                .WithMany(tp => tp.Orders)
                .HasForeignKey(o => o.TradingPairId)
                .OnDelete(DeleteBehavior.Restrict);


            modelBuilder.Entity<Trade>()
                .HasOne(t => t.BuyOrder)
                .WithMany(o => o.Trades)
                .HasForeignKey(t => t.BuyOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Trade>()
                .HasOne(t => t.SellOrder)
                .WithMany()
                .HasForeignKey(t => t.SellOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Trade>()
                .HasOne(t => t.TradingPair)
                .WithMany(tp => tp.Trades)
                .HasForeignKey(t => t.TradingPairId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
