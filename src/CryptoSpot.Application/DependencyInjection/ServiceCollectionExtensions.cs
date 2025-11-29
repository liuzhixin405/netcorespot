using Microsoft.Extensions.DependencyInjection;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Mapping;
using FluentValidation;
using System.Reflection;

namespace CryptoSpot.Application.DependencyInjection
{
    /// <summary>
    /// 服务集合扩展 - 配置依赖注入
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加Clean Architecture服务（简化版 - 无Command/Bus模式）
        /// </summary>
        public static IServiceCollection AddCleanArchitecture(this IServiceCollection services)
        {
            // 注册DTO映射服务
            services.AddSingleton<IDtoMappingService, DtoMappingService>();

            // ✅ 注册 FluentValidation 验证器（自动扫描当前程序集）
            services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

            return services;
        }
    }
}
