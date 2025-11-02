using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CryptoSpot.MatchEngine.Events
{
    /// <summary>
    /// 简单同步调度实现；后续可替换 Channel 背压。为避免阻塞撮合线程，发布时逐个 await。
    /// </summary>
    public class InMemoryMatchEngineEventBus : IMatchEngineEventBus
    {
        private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

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
            if (_handlers.TryGetValue(typeof(TEvent), out var list))
            {
                List<Delegate> snapshot;
                lock (list) snapshot = new List<Delegate>(list);
                foreach (var h in snapshot)
                {
                    try
                    {
                        var task = ((MatchEngineEventHandler<TEvent>)h)(evt);
                        if (task is not null) await task; // 顺序等待，保证事件顺序
                    }
                    catch (Exception)
                    {
                        // TODO: 记录日志（此处暂保持最小依赖，稍后可注入 ILogger）
                    }
                }
            }
        }
    }
}
