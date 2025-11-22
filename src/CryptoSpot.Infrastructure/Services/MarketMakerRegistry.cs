// filepath: g:\\github\\netcorespot\\src\\CryptoSpot.Infrastructure\\Services\\MarketMakerRegistry.cs
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Domain.Entities;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace CryptoSpot.Infrastructure.Services
{
    public class MarketMakerRegistry : IMarketMakerRegistry
    {
        private readonly IOptionsMonitor<MarketMakerOptions> _options;
        private readonly ConcurrentDictionary<long, byte> _cache = new();

        public MarketMakerRegistry(IOptionsMonitor<MarketMakerOptions> options)
        {
            _options = options;
            RebuildCache(options.CurrentValue);
            _options.OnChange(RebuildCache);
        }

        private void RebuildCache(MarketMakerOptions opts)
        {
            _cache.Clear();
            foreach (var id in opts.UserIds ?? System.Array.Empty<long>())
            {
                _cache[id] = 1;
            }
        }

        public bool IsMaker(long userId) => _cache.ContainsKey(userId);
    }
}
