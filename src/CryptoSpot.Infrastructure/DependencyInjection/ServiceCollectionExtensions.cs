using Microsoft.Extensions.DependencyInjection;
using CryptoSpot.Application.Common.Interfaces;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Infrastructure.Identity;
using CryptoSpot.Persistence.Repositories;

namespace CryptoSpot.Infrastructure.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Register infrastructure-specific services (expose IKLineCache mapping to RedisCacheService, etc.)
        /// </summary>
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
        {
            // RedisCacheService is registered in Program as a singleton. Expose the IKLineCache interface
            // so application handlers can depend on the interface instead of concrete implementation.
            services.AddSingleton<CryptoSpot.Application.Abstractions.Services.IKLineCache>(sp => sp.GetRequiredService<CryptoSpot.Infrastructure.Services.RedisCacheService>());

            // UnitOfWork 和仓储
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IOrderRepository, OrderRepository>();
            services.AddScoped<IAssetRepository, AssetRepository>();
            services.AddScoped<ITradeRepository, TradeRepository>();
            services.AddScoped<ITradingPairRepository, TradingPairRepository>();

            // Identity 服务
            services.AddScoped<ICurrentUserService, CurrentUserService>();
            services.AddScoped<ITokenService, JwtTokenService>();
            services.AddScoped<IPasswordHasher, PasswordHasher>();

            // Note: HttpContextAccessor 需要在 API 项目的 Program.cs 中注册
            // services.AddHttpContextAccessor();

            return services;
        }
    }
}
