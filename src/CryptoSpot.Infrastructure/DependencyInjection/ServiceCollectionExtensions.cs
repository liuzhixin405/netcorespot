using Microsoft.Extensions.DependencyInjection;

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

            return services;
        }
    }
}
