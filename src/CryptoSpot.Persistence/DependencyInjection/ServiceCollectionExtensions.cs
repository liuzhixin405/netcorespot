using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using CryptoSpot.Persistence.Data;
using CryptoSpot.Application.Abstractions.Repositories; // updated
using CryptoSpot.Persistence.Repositories;

namespace CryptoSpot.Persistence.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        
        // ✅ 只注册 DbContextFactory（不再使用 DbContextPool）
        services.AddDbContextFactory<ApplicationDbContext>(options =>
        {
            options.UseMySql(connectionString, ServerVersion.Parse("8.0"), mysqlOptions =>
            {
                mysqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(30), null);
                mysqlOptions.CommandTimeout(60);
            });
            options.EnableThreadSafetyChecks(false); // Factory 模式下每次创建新实例，禁用线程检查提升性能
        }, lifetime: ServiceLifetime.Singleton); // Factory 本身是 Singleton

        // ✅ Repository 改为 Transient（每次创建新实例，使用 Factory 创建 DbContext）
        services.AddTransient<IUserRepository, UserRepository>();
        services.AddTransient<ITradingPairRepository, TradingPairRepository>();
        services.AddTransient<ITradeRepository, TradeRepository>();
        services.AddTransient<IOrderRepository, OrderRepository>();
        services.AddTransient<IAssetRepository, AssetRepository>();
        services.AddTransient<IKLineDataRepository, KLineDataRepository>();
        
        // ✅ UnitOfWork 保持 Scoped（请求范围内共享，管理事务）
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
