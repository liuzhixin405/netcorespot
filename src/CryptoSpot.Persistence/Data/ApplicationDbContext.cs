using Microsoft.EntityFrameworkCore;
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Persistence.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<TradingPair> TradingPairs => Set<TradingPair>();
    public DbSet<KLineData> KLineData => Set<KLineData>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Trade> Trades => Set<Trade>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
