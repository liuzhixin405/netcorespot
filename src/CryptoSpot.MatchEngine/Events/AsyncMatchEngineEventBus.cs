using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CryptoSpot.MatchEngine.Events
{
    /// <summary>
    /// 异步事件总线：Publish 将事件写入 Channel，后台分发 Worker 读取并执行订阅处理。
    /// </summary>
    public class AsyncMatchEngineEventBus : IMatchEngineEventBus
    {
        private readonly Channel<IMatchEngineEvent> _channel;
        private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
        private readonly int _capacity;

        public AsyncMatchEngineEventBus(int capacity = 10000)
        {
            _capacity = capacity;
            var options = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };
            _channel = Channel.CreateBounded<IMatchEngineEvent>(options);
        }

        public ChannelReader<IMatchEngineEvent> Reader => _channel.Reader;

        public void Subscribe<TEvent>(MatchEngineEventHandler<TEvent> handler) where TEvent : IMatchEngineEvent
        {
            var list = _handlers.GetOrAdd(typeof(TEvent), _ => new List<Delegate>());
            lock (list)
            {
                list.Add(handler);
            }
        }

        public async Task PublishAsync<TEvent>(TEvent evt) where TEvent : IMatchEngineEvent
        {
            // 背压：等待写入，Channel 满时阻塞，设置一个最大等待时间
            var writeTask = _channel.Writer.WriteAsync(evt).AsTask();
            var timeout = Task.Delay(TimeSpan.FromSeconds(2));
            var completed = await Task.WhenAny(writeTask, timeout);
            if (completed == timeout)
            {
                // 超时：降级丢弃或可选扩展 DLQ，此处简单尝试再次立即写入（可能仍阻塞）
                // 为保持无外部依赖不引入 ILogger，可在后续注入时增加日志记录
                // 再次尝试不带超时
                await writeTask; // 仍等待，避免丢事件；若需丢弃可改为 return。
            }
        }

        internal async Task DispatchAsync(IMatchEngineEvent evt)
        {
            if (_handlers.TryGetValue(evt.GetType(), out var list))
            {
                List<Delegate> snapshot;
                lock (list) snapshot = new List<Delegate>(list);
                var tasks = new List<Task>(snapshot.Count);
                foreach (var h in snapshot)
                {
                    try
                    {
                        var task = (Task?)h.DynamicInvoke(evt);
                        if (task != null) tasks.Add(task);
                    }
                    catch { /* 记录可在扩展时加入 ILogger */ }
                }
                if (tasks.Count > 0) await Task.WhenAll(tasks);
            }
        }
    }
}
