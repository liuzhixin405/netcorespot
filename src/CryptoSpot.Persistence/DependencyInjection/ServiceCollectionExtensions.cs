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
        services.AddDbContextPool<ApplicationDbContext>(options =>
        {
            options.UseMySql(connectionString, ServerVersion.Parse("8.0"), mysqlOptions =>
            {
                mysqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(30), null);
                mysqlOptions.CommandTimeout(60);
            });
            options.EnableThreadSafetyChecks(false);
        }, poolSize: 20);

        // Repositories & UoW
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITradingPairRepository, TradingPairRepository>();
        services.AddScoped<ITradeRepository, TradeRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IKLineDataRepository, KLineDataRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
