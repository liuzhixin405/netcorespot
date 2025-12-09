using System.Collections.Concurrent;

namespace CryptoSpot.Infrastructure.MatchEngine.Services
{
    /// <summary>
    /// 内存资产存储
    /// </summary>
    public class InMemoryAssetStore
    {
        private readonly ConcurrentDictionary<(long userId, string currency), AssetBalance> _balances = new();

        public Task<bool> FreezeAssetAsync(long userId, string currency, decimal amount)
        {
            var key = (userId, currency);
            var balance = _balances.GetOrAdd(key, _ => new AssetBalance());

            lock (balance)
            {
                if (balance.Available < amount)
                    return Task.FromResult(false);

                balance.Available -= amount;
                balance.Frozen += amount;
                return Task.FromResult(true);
            }
        }

        public Task UnfreezeAssetAsync(long userId, string currency, decimal amount)
        {
            var key = (userId, currency);
            var balance = _balances.GetOrAdd(key, _ => new AssetBalance());

            lock (balance)
            {
                balance.Frozen -= amount;
                if (balance.Frozen < 0) balance.Frozen = 0;
            }

            return Task.CompletedTask;
        }

        public Task AddAvailableBalanceAsync(long userId, string currency, decimal amount)
        {
            var key = (userId, currency);
            var balance = _balances.GetOrAdd(key, _ => new AssetBalance());

            lock (balance)
            {
                balance.Available += amount;
            }

            return Task.CompletedTask;
        }

        public Task InitializeBalanceAsync(long userId, string currency, decimal availableBalance)
        {
            var key = (userId, currency);
            _balances[key] = new AssetBalance
            {
                Available = availableBalance,
                Frozen = 0
            };

            return Task.CompletedTask;
        }

        /// <summary>
        /// 获取所有余额（用于持久化到数据库）
        /// </summary>
        public IEnumerable<(long UserId, string Currency, decimal Available, decimal Frozen)> GetAllBalances()
        {
            foreach (var kvp in _balances)
            {
                var (userId, currency) = kvp.Key;
                var balance = kvp.Value;

                lock (balance)
                {
                    yield return (userId, currency, balance.Available, balance.Frozen);
                }
            }
        }

        /// <summary>
        /// 获取用户的所有资产
        /// </summary>
        public Dictionary<string, AssetBalance> GetUserAssets(long userId)
        {
            var userAssets = new Dictionary<string, AssetBalance>();

            foreach (var kvp in _balances)
            {
                if (kvp.Key.userId == userId)
                {
                    var balance = kvp.Value;
                    lock (balance)
                    {
                        userAssets[kvp.Key.currency] = new AssetBalance
                        {
                            Available = balance.Available,
                            Frozen = balance.Frozen
                        };
                    }
                }
            }

            return userAssets;
        }

        /// <summary>
        /// 清空所有余额（关闭时使用）
        /// </summary>
        public void Clear()
        {
            _balances.Clear();
        }

        public class AssetBalance
        {
            public decimal Available { get; set; }
            public decimal Frozen { get; set; }
        }
    }
}
