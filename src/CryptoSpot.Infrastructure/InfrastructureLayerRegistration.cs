using CryptoSpot.Application.Common.Interfaces;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Infrastructure.Identity;
using CryptoSpot.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoSpot.Infrastructure
{
    /// <summary>
    /// Infrastructure 层依赖注入
    /// </summary>
    public static class InfrastructureLayerRegistration
    {
        public static IServiceCollection AddInfrastructureLayer(this IServiceCollection services)
        {
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
