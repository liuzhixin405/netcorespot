using Microsoft.Extensions.DependencyInjection;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Application.CommandHandlers.Trading;
using CryptoSpot.Application.DomainCommands.Trading;
using CryptoSpot.Application.Mapping;
using CryptoSpot.Bus.Core;
using CryptoSpot.Bus.Extensions;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Common.Behaviors;
using CryptoSpot.Application.Common.Models;
using CryptoSpot.Application.Features.Auth.Register;
using CryptoSpot.Application.Features.Auth.Login;
using CryptoSpot.Application.Features.Auth.GetCurrentUser;

namespace CryptoSpot.Application.DependencyInjection
{
    /// <summary>
    /// 服务集合扩展 - 配置依赖注入
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加Clean Architecture服务
        /// </summary>
        public static IServiceCollection AddCleanArchitecture(this IServiceCollection services)
        {
            // 使用Bus项目的高性能Dataflow实现
            services.AddDataflowCommandBus();

            // CommandHandlers are auto-registered by reflection in Infrastructure's AddCommandHandlers.
            // 注册DTO映射服务
            services.AddSingleton<IDtoMappingService, DtoMappingService>();

            // 注册认证Handler
            RegisterAuthHandlers(services);

            // 注册管道行为（使用项目自己的 ICommandPipelineBehavior）
            services.AddScoped(typeof(ICommandPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            services.AddScoped(typeof(ICommandPipelineBehavior<,>), typeof(TransactionBehavior<,>));

            return services;
        }

        private static void RegisterAuthHandlers(IServiceCollection services)
        {
            services.AddScoped<ICommandHandler<RegisterCommand, Result<RegisterResponse>>, RegisterCommandHandler>();
            services.AddScoped<ICommandHandler<LoginCommand, Result<LoginResponse>>, LoginCommandHandler>();
            services.AddScoped<ICommandHandler<GetCurrentUserQuery, Result<CurrentUserResponse>>, GetCurrentUserQueryHandler>();
        }

        /// <summary>
        /// 添加高频数据处理服务
        /// </summary>
        public static IServiceCollection AddHighFrequencyDataProcessing(this IServiceCollection services)
        {
            // 对于高频数据场景，可以使用BatchDataflowCommandBus
            services.AddBatchDataflowCommandBus();

            return services;
        }
    }
}
