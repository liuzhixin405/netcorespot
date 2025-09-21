using Microsoft.Extensions.DependencyInjection;
using CryptoSpot.Bus.Core;
using CryptoSpot.Bus.Implementations;
using CryptoSpot.Bus.Monitoring;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoSpot.Bus.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加标准的CommandBus实现
        /// </summary>
        public static IServiceCollection AddCommandBus(this IServiceCollection services)
        {
            // 使用单例模式以提高性能，因为CommandBus本身是无状态的（除了缓存）
            services.AddSingleton<ICommandBus, CommandBus>();
            return services;
        }

        /// <summary>
        /// 添加基于TPL数据流的高性能CommandBus实现
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="maxConcurrency">最大并发数，默认为处理器核心数的2倍</param>
        public static IServiceCollection AddDataflowCommandBus(this IServiceCollection services, int? maxConcurrency = null)
        {
            services.AddSingleton<ICommandBus>(provider =>
            {
                var logger = provider.GetService<ILogger<DataflowCommandBus>>();
                var concurrency = maxConcurrency ?? Environment.ProcessorCount * 2;
                return new DataflowCommandBus(provider, logger, concurrency);
            });
            return services;
        }

        /// <summary>
        /// 添加强类型的TPL数据流CommandBus实现（推荐）
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="maxConcurrency">最大并发数，默认为处理器核心数的2倍</param>
        public static IServiceCollection AddTypedDataflowCommandBus(this IServiceCollection services, int? maxConcurrency = null)
        {
            services.AddSingleton<ICommandBus>(provider =>
            {
                var logger = provider.GetService<ILogger<TypedDataflowCommandBus>>();
                var concurrency = maxConcurrency ?? Environment.ProcessorCount * 2;
                return new TypedDataflowCommandBus(provider, logger, concurrency);
            });
            return services;
        }

        /// <summary>
        /// 添加实时监控支持
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="collectionInterval">指标收集间隔，默认1秒</param>
        public static IServiceCollection AddMetricsCollector(this IServiceCollection services, TimeSpan? collectionInterval = null)
        {
            services.AddSingleton<IMetricsCollector>(provider =>
            {
                var commandBus = provider.GetRequiredService<ICommandBus>();
                var logger = provider.GetService<ILogger<MetricsCollector>>();
                return new MetricsCollector(commandBus, logger, collectionInterval);
            });
            return services;
        }

        /// <summary>
        /// 添加支持批处理的高性能CommandBus实现
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="batchSize">批处理大小，默认为10</param>
        /// <param name="batchTimeout">批处理超时时间，默认为100毫秒</param>
        /// <param name="maxConcurrency">最大并发数，默认为处理器核心数</param>
        public static IServiceCollection AddBatchDataflowCommandBus(this IServiceCollection services, 
            int batchSize = 10, TimeSpan? batchTimeout = null, int? maxConcurrency = null)
        {
            services.AddSingleton<ICommandBus>(provider =>
            {
                var logger = provider.GetService<ILogger<BatchDataflowCommandBus>>();
                var concurrency = maxConcurrency ?? Environment.ProcessorCount;
                return new BatchDataflowCommandBus(provider, logger, batchSize, batchTimeout, concurrency);
            });
            return services;
        }

        /// <summary>
        /// 添加支持监控的CommandBus实现
        /// </summary>
        public static IServiceCollection AddMonitoredCommandBus(this IServiceCollection services, 
            CommandBusType busType = CommandBusType.Dataflow, int? maxConcurrency = null)
        {
            switch (busType)
            {
                case CommandBusType.Standard:
                    services.AddSingleton<IMonitoredCommandBus, MonitoredCommandBus>();
                    break;
                case CommandBusType.Dataflow:
                    services.AddSingleton<IMonitoredCommandBus>(provider =>
                    {
                        var logger = provider.GetService<ILogger<DataflowCommandBus>>();
                        var concurrency = maxConcurrency ?? Environment.ProcessorCount * 2;
                        return new MonitoredDataflowCommandBus(provider, logger, concurrency);
                    });
                    break;
                case CommandBusType.BatchDataflow:
                    services.AddSingleton<IMonitoredCommandBus>(provider =>
                    {
                        var logger = provider.GetService<ILogger<BatchDataflowCommandBus>>();
                        var concurrency = maxConcurrency ?? Environment.ProcessorCount;
                        return new MonitoredBatchDataflowCommandBus(provider, logger, 10, TimeSpan.FromMilliseconds(100), concurrency);
                    });
                    break;
            }
            return services;
        }

        public static IServiceCollection AddCommandBehaviorOpenGeneric<TBehavior>(this IServiceCollection services)
            where TBehavior :class
        {
            services.AddScoped(typeof(ICommandPipelineBehavior<,>), typeof(TBehavior));
            return services;
        }

        /// <summary>
        /// 自动注册所有CommandHandler和CommandProcessor
        /// </summary>
        public static IServiceCollection AddCommandHandlers(this IServiceCollection services)
        {
            // 注册所有ICommandHandler实现
            services.Scan(scan => scan
                .FromCallingAssembly()
                .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)))
                .AsImplementedInterfaces()
                .WithScopedLifetime());

            // 注册所有ICommandProcessor实现
            services.Scan(scan => scan
                .FromCallingAssembly()
                .AddClasses(classes => classes.AssignableTo(typeof(ICommandProcessor)))
                .AsImplementedInterfaces()
                .WithScopedLifetime());

            // 注册泛型CommandProcessor<TCommand, TResult>
            services.AddScoped(typeof(CommandProcessor<,>));

            return services;
        }

        // 添加性能监控扩展方法
        public static IServiceCollection AddCommandBusWithMetrics(this IServiceCollection services)
        {
            services.AddSingleton<ICommandBus, CommandBus>();
            // 可以在这里添加性能监控相关的服务
            return services;
        }

        /// <summary>
        /// 一次性注册所有CommandBus实现
        /// </summary>
        public static IServiceCollection AddAllCommandBusImplementations(this IServiceCollection services)
        {
            // 注册标准CommandBus作为默认的ICommandBus实现
            services.AddSingleton<ICommandBus, CommandBus>();
            services.AddSingleton<CommandBus>();
            
            // 注册DataflowCommandBus
            services.AddSingleton<DataflowCommandBus>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<DataflowCommandBus>>();
                return new DataflowCommandBus(provider, logger, Environment.ProcessorCount * 2);
            });
            
            // 注册BatchDataflowCommandBus
            services.AddSingleton<BatchDataflowCommandBus>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<BatchDataflowCommandBus>>();
                return new BatchDataflowCommandBus(provider, logger, 1, TimeSpan.FromMilliseconds(10), Environment.ProcessorCount * 2);
            });
            
            // 注册TypedDataflowCommandBus
            services.AddSingleton<TypedDataflowCommandBus>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<TypedDataflowCommandBus>>();
                return new TypedDataflowCommandBus(provider, logger, Environment.ProcessorCount * 2);
            });
            
            // 注册MonitoredCommandBus
            services.AddSingleton<MonitoredCommandBus>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<MonitoredCommandBus>>();
                return new MonitoredCommandBus(provider, logger);
            });
            
            // 注册增强的CommandBus
            services.AddSingleton<EnhancedCommandBus>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<EnhancedCommandBus>>();
                return new EnhancedCommandBus(provider, provider.GetRequiredService<EnhancedPipelineExecutor>(), logger);
            });
            
            // 注册增强的管道执行器
            services.AddSingleton<EnhancedPipelineExecutor>();
            
            // 注册服务定位器
            services.AddScoped<CommandBusServiceLocator>();
            
            return services;
        }

        /// <summary>
        /// 注册增强的AOP行为
        /// </summary>
        public static IServiceCollection AddEnhancedBehaviors(this IServiceCollection services)
        {
            // 注册参数贯穿行为
            services.AddScoped(typeof(IParameterInterceptionBehavior<,>), typeof(CryptoSpot.Bus.Behaviors.ParameterValidationBehavior<,>));
            
            // 注册方法执行前行为
            services.AddScoped(typeof(IPreExecutionBehavior<,>), typeof(CryptoSpot.Bus.Behaviors.PreExecutionLoggingBehavior<,>));
            
            // 注册方法执行后行为
            services.AddScoped(typeof(IPostExecutionBehavior<,>), typeof(CryptoSpot.Bus.Behaviors.PostExecutionLoggingBehavior<,>));
            
            // 注册返回值贯穿行为
            services.AddScoped(typeof(IReturnValueInterceptionBehavior<,>), typeof(CryptoSpot.Bus.Behaviors.ReturnValueEnhancementBehavior<,>));
            
            // 注册异常处理行为
            services.AddScoped(typeof(IExceptionHandlingBehavior<,>), typeof(CryptoSpot.Bus.Behaviors.ExceptionHandlingBehavior<,>));
            
            return services;
        }
    }
}
