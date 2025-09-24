using System;
using System.Collections.Generic;
using System.Text;
using CodeProject.ObjectPool;
using Common.Redis.Extensions.Configuration;
using Common.Redis.Extensions.Serializer;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Common.Redis.Extensions
{
    /// <summary>
    /// RedisDI
    /// </summary>
    public static class DependencyInjectionExtensions
    {
        /// <summary>
        /// 添加Redis服务
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configsStr"></param>
        /// <param name="serializerEnum"></param>
        /// <param name="poolSize"></param>
        /// <returns></returns>
        public static IServiceCollection AddRedis(this IServiceCollection services, string configsStr, SerializerEnum serializerEnum, int poolSize)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));
            //ConfigurationOptions config = ConfigurationOptions.Parse(configsStr);
            RedisConfiguration redisConfiguration = new RedisConfiguration { Hosts = configsStr.Split(',').Select(c => c.Split(':')).Where(c => c.Length == 2).Select(c => new RedisHost { Host = c[0], Port = int.Parse(c[1]) }).ToArray() };

            AddRedis(services, redisConfiguration, serializerEnum, poolSize);
            return services;
        }

        /// <summary>
        /// 添加Redis服务
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configureOptions"></param>
        /// <param name="serializerEnum"></param>
        /// <returns></returns>
        public static IServiceCollection AddRedis(this IServiceCollection services, Func<Configuration.RedisConfiguration> configureOptions, SerializerEnum serializerEnum)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));
            AddRedis(services, configureOptions(), serializerEnum);
            return services;
        }

        /// <summary>
        /// 添加Redis服务
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configureOptions"></param>
        /// <param name="serializerEnum"></param>
        /// <returns></returns>
        public static IServiceCollection AddRedis(this IServiceCollection services, Configuration.RedisConfiguration configureOptions, SerializerEnum serializerEnum)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));
            AddRedis(services, configureOptions, serializerEnum, configureOptions.PoolSize);
            return services;
        }


        /// <summary>
        /// 添加Redis服务
        /// </summary>
        /// <param name="services"></param>
        /// <param name="config"></param>
        ///  <param name="serializerEnum"></param>
        /// <param name="poolSize"></param>
        /// <returns></returns>
        public static IServiceCollection AddRedis(this IServiceCollection services, Configuration.RedisConfiguration config, SerializerEnum serializerEnum, int poolSize)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));
            services.AddSingleton(config);
            switch (serializerEnum)
            {
                case SerializerEnum.PROTOBUF:
                    services.AddSingleton<ISerializer, Serializer.ProtobufSerializer>();

                    break;
                case SerializerEnum.BSON:
                    services.AddSingleton<ISerializer, Serializer.BsonSerializer>();

                    break;
                case SerializerEnum.JSON:
                default:
                    services.AddSingleton<ISerializer, Serializer.JsonSerializer>();
                    break;

            }

            services.AddSingleton<ObjectPool<PooledConnectionMultiplexer>>(srv =>
                new ObjectPool<PooledConnectionMultiplexer>(poolSize, () => new PooledConnectionMultiplexer(config.ConfigurationOptions)));
            services.AddScoped<IConnectionMultiplexer>(srv => srv.GetRequiredService<ObjectPool<PooledConnectionMultiplexer>>().GetObject());
            services.AddScoped<IRedisCache, RedisCache>();
            return services;
        }

        /// <summary>
        /// 添加Redis连接池
        /// </summary>
        /// <param name="serviceCollection"></param>
        /// <param name="config"></param>
        /// <param name="poolSize"></param>
        public static void AddRedisConnectionPool(this IServiceCollection serviceCollection, ConfigurationOptions config, int poolSize)
        {
            serviceCollection.AddSingleton<ObjectPool<PooledConnectionMultiplexer>>(srv =>
                new ObjectPool<PooledConnectionMultiplexer>(poolSize, () => new PooledConnectionMultiplexer(config)));
            serviceCollection.AddScoped<IConnectionMultiplexer>(srv => srv.GetRequiredService<ObjectPool<PooledConnectionMultiplexer>>().GetObject());
        }

        /// <summary>
        /// 添加Redis连接池
        /// </summary>
        /// <param name="serviceCollection"></param>
        /// <param name="configAction"></param>
        /// <param name="poolSize"></param>
        public static void AddRedisConnectionPool(this IServiceCollection serviceCollection, Action<ConfigurationOptions> configAction, int poolSize)
        {
            var config = new ConfigurationOptions();
            configAction(config);
            AddRedisConnectionPool(serviceCollection, config, poolSize);
        }

        /// <summary>
        /// 添加Redis连接池
        /// </summary>
        /// <param name="serviceCollection"></param>
        /// <param name="configsStr"></param>
        /// <param name="poolSize"></param>
        public static void AddRedisConnectionPool(this IServiceCollection serviceCollection, string configsStr, int poolSize)
        {
            AddRedisConnectionPool(serviceCollection, ConfigurationOptions.Parse(configsStr), poolSize);
        }


        /// <summary>
        /// 配置 REDIS 缓存
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configuration"></param>
        public static IServiceCollection AddRedis(this IServiceCollection services, IConfiguration configuration)
        {
            RedisConfiguration redisConfiguration;
            if (string.IsNullOrEmpty(configuration["REDIS_CONNSTR"]))
            {
                redisConfiguration = configuration.GetSection("Redis").Get<RedisConfiguration>();
            }
            else
            {
                redisConfiguration = new RedisConfiguration { Hosts = configuration["REDIS_CONNSTR"].Split(',').Select(c => c.Split(':')).Where(c => c.Length == 2).Select(c => new RedisHost { Host = c[0], Port = int.Parse(c[1]) }).ToArray() };
                if (!string.IsNullOrEmpty(configuration["REDIS_PASSWORD"])) redisConfiguration.Password = configuration["REDIS_PASSWORD"];
            }
            return services.AddRedis(redisConfiguration);
        }

        /// <summary>
        /// 配置 REDIS 缓存
        /// </summary>
        /// <param name="services"></param>
        /// <param name="section"></param>
        public static IServiceCollection AddRedis(this IServiceCollection services, IConfigurationSection section)
        {
            var config = section.Get<RedisConfiguration>();
            return services.AddRedis(config);
        }

        /// <summary>
        /// 配置 REDIS 缓存
        /// </summary>
        /// <param name="services"></param>
        /// <param name="config"></param>
        public static IServiceCollection AddRedis(this IServiceCollection services, RedisConfiguration config)
        {
            services.AddSingleton<IRedisCache>(obj =>
            {
                var serializer = new MsgPackSerializer();
                var connection = new PooledConnectionMultiplexer(config.ConfigurationOptions);
                return new RedisCache(obj.GetRequiredService<ILogger<RedisCache>>(), connection, config, serializer);
            });

            return services;
        }
    }
}
