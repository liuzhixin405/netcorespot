using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using CryptoSpot.Bus.Core;

namespace CryptoSpot.Bus.Implementations
{
    public class CommandBus : ICommandBus
    {
        private readonly IServiceProvider _provider;
        private readonly ConcurrentDictionary<Type, Func<object>> _handlerCache = new();
        private readonly ConcurrentDictionary<Type, Func<object[]>> _behaviorsCache = new();
        private readonly ConcurrentDictionary<Type, Func<object, object, CancellationToken, Task<object>>> _pipelineCache = new();

        public CommandBus(IServiceProvider serviceProvider)
        {
            _provider = serviceProvider;
        }

        // 添加清理缓存的方法，用于测试或动态重新加载
        public void ClearCache()
        {
            _handlerCache.Clear();
            _behaviorsCache.Clear();
            _pipelineCache.Clear();
        }

        public async Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken ct = default) where TCommand : ICommand<TResult>
        {
            var commandType = typeof(TCommand);
            
            // 获取缓存的Handler
            var handler = GetCachedHandler<TCommand, TResult>(commandType);
            
            // 获取缓存的Pipeline
            var pipeline = GetCachedPipeline<TCommand, TResult>(commandType);
            
            // 执行Pipeline
            var result = await pipeline(handler, command, ct);
            return (TResult)result;
        }

        private ICommandHandler<TCommand, TResult> GetCachedHandler<TCommand, TResult>(Type commandType) where TCommand : ICommand<TResult>
        {
            var handlerFactory = (Func<object>)_handlerCache.GetOrAdd(commandType, _ =>
            {
                return new Func<object>(() =>
                {
                    using var scope = _provider.CreateScope();
                    var handler = scope.ServiceProvider.GetService(typeof(ICommandHandler<TCommand, TResult>));
                    if (handler == null)
                        throw new InvalidOperationException($"No handler registered for {commandType.Name}");
                    return handler;
                });
            });
            return (ICommandHandler<TCommand, TResult>)handlerFactory();
        }

        private ICommandPipelineBehavior<TCommand, TResult>[] GetCachedBehaviors<TCommand, TResult>(Type commandType) where TCommand : ICommand<TResult>
        {
            var behaviorsFactory = (Func<object[]>)_behaviorsCache.GetOrAdd(commandType, _ =>
            {
                return new Func<object[]>(() =>
                {
                    using var scope = _provider.CreateScope();
                    var behaviors = scope.ServiceProvider.GetServices<ICommandPipelineBehavior<TCommand, TResult>>().ToArray();
                    return behaviors.Cast<object>().ToArray();
                });
            });
            return behaviorsFactory().Cast<ICommandPipelineBehavior<TCommand, TResult>>().ToArray();
        }

        private Func<object, object, CancellationToken, Task<object>> GetCachedPipeline<TCommand, TResult>(Type commandType) where TCommand : ICommand<TResult>
        {
            return _pipelineCache.GetOrAdd(commandType, _ =>
            {
                var behaviors = GetCachedBehaviors<TCommand, TResult>(commandType);
                
                // 预构建Pipeline，避免每次调用时重新构建
                return async (handler, command, ct) =>
                {
                    if (handler == null || command == null)
                        throw new ArgumentNullException("Handler or command cannot be null");
                        
                    var typedHandler = (ICommandHandler<TCommand, TResult>)handler;
                    var typedCommand = (TCommand)command;

                    // 如果没有behaviors，直接调用handler
                    if (behaviors.Length == 0)
                    {
                        var result = await typedHandler.HandleAsync(typedCommand, ct);
                        return (object)result!;
                    }

                    // 使用递归方式构建pipeline，减少委托创建
                    var pipelineResult = await ExecutePipeline(typedHandler, typedCommand, behaviors, 0, ct);
                    return (object)pipelineResult!;
                };
            });
        }

        private async Task<TResult> ExecutePipeline<TCommand, TResult>(
            ICommandHandler<TCommand, TResult> handler, 
            TCommand command, 
            ICommandPipelineBehavior<TCommand, TResult>[] behaviors, 
            int behaviorIndex, 
            CancellationToken ct) where TCommand : ICommand<TResult>
        {
            if (behaviorIndex >= behaviors.Length)
            {
                return await handler.HandleAsync(command, ct);
            }

            var behavior = behaviors[behaviorIndex];
            return await behavior.Handle(command, cmd => ExecutePipeline(handler, command, behaviors, behaviorIndex + 1, ct), ct);
        }
    }
}
