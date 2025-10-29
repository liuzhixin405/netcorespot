using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using CodeProject.ObjectPool;
using StackExchange.Redis;
using StackExchange.Redis.Profiling;

namespace CryptoSpot.Redis
{
    /// <summary>
    /// PooledConnectionMultiplexer
    /// </summary>
    // Lightweight wrapper kept for compatibility in case other projects reference this type.
    // It intentionally does NOT implement IConnectionMultiplexer to avoid API/ABI coupling with
    // StackExchange.Redis versions. Use ConnectionMultiplexer.Connect(...) directly via DI.
    public class PooledConnectionMultiplexer
    {
        private readonly ConnectionMultiplexer _inner;

        public PooledConnectionMultiplexer(ConfigurationOptions config)
        {
            _inner = ConnectionMultiplexer.Connect(config);
        }

        public ConnectionMultiplexer Inner => _inner;
    }
}
