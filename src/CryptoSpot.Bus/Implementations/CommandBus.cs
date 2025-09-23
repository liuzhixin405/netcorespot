using System;
using System.Collections.Concurrent; // 仅缓存已编译的委托，不缓存实例
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using CryptoSpot.Bus.Core;
using CryptoSpot.Bus.Monitoring;

namespace CryptoSpot.Bus.Implementations
{
    public class CommandBus : ICommandBus
    {
        private readonly IServiceProvider _provider;
        private readonly ConcurrentDictionary<Type, Func<IServiceProvider, object, CancellationToken, Task<object>>> _pipelineCache = new();
        private long _processed;
        private long _failed;
        private long _totalProcTicks;
        private long _totalQueueTicks; // 近似排队等待(构建scope前时间差)

        public CommandBus(IServiceProvider serviceProvider) => _provider = serviceProvider;

        public async Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken ct = default) where TCommand : ICommand<TResult>
        {
            var enqueueTime = DateTime.UtcNow;
            var pipeline = _pipelineCache.GetOrAdd(typeof(TCommand), _ => BuildPipelineDelegate<TCommand, TResult>());
            using var scope = _provider.CreateScope();
            try
            {
                var start = DateTime.UtcNow;
                Interlocked.Add(ref _totalQueueTicks, (start - enqueueTime).Ticks);
                var sw = Stopwatch.StartNew();
                var result = await pipeline(scope.ServiceProvider, command!, ct);
                sw.Stop();
                Interlocked.Increment(ref _processed);
                Interlocked.Add(ref _totalProcTicks, sw.ElapsedTicks);
                return (TResult)result;
            }
            catch
            {
                Interlocked.Increment(ref _failed);
                throw;
            }
        }

        private Func<IServiceProvider, object, CancellationToken, Task<object>> BuildPipelineDelegate<TCommand, TResult>() where TCommand : ICommand<TResult>
        {
            return async (sp, cmdObj, ct) =>
            {
                var handler = sp.GetRequiredService<ICommandHandler<TCommand, TResult>>();
                var behaviors = sp.GetServices<ICommandPipelineBehavior<TCommand, TResult>>().ToArray();
                var command = (TCommand)cmdObj;
                Func<Task<TResult>> pipeline = () => handler.HandleAsync(command, ct);
                foreach (var behavior in behaviors.Reverse())
                {
                    var next = pipeline;
                    var b = behavior;
                    pipeline = () => b.Handle(command, _ => next(), ct);
                }
                return (object)(await pipeline())!;
            };
        }

        public DataflowMetrics GetMetrics()
        {
            var p = Interlocked.Read(ref _processed);
            var f = Interlocked.Read(ref _failed);
            var tp = Interlocked.Read(ref _totalProcTicks);
            var tq = Interlocked.Read(ref _totalQueueTicks);
            var procSeconds = tp > 0 ? TimeSpan.FromTicks(tp).TotalSeconds : 0;
            return new DataflowMetrics
            {
                ProcessedCommands = p,
                FailedCommands = f,
                TotalProcessingTime = TimeSpan.FromTicks(tp),
                AverageProcessingTime = p > 0 ? TimeSpan.FromTicks(tp / Math.Max(1, p)) : TimeSpan.Zero,
                AverageQueueWaitTime = p > 0 ? TimeSpan.FromTicks(tq / Math.Max(1, p)) : TimeSpan.Zero,
                TotalQueueWaitTime = TimeSpan.FromTicks(tq),
                MaxConcurrency = 1,
                AvailableConcurrency = 1,
                InputQueueSize = 0, // 无内置队列
                ThroughputPerSecond = procSeconds > 0 ? p / procSeconds : 0,
                FailureRate = p + f > 0 ? (double)f / (p + f) * 100 : 0
            };
        }
    }
}
