using System.Collections.Concurrent;

namespace CryptoSpot.Infrastructure.MatchEngine.Services
{
    public class InMemoryAssetStore
    {
        private readonly ConcurrentDictionary<(long userId, string currency), AssetBalance> _balances = new();
        private readonly ConcurrentDictionary<long, ConcurrentDictionary<string, AssetBalance>> _userIndex = new();

        public Task<bool> FreezeAssetAsync(long userId, string currency, decimal amount)
        {
            var key = (userId, currency);
            var balance = _balances.GetOrAdd(key, _ =>
            {
                var b = new AssetBalance();
                _userIndex.GetOrAdd(userId, _ => new())[currency] = b;
                return b;
            });

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
            var balance = _balances.GetOrAdd(key, _ =>
            {
                var b = new AssetBalance();
                _userIndex.GetOrAdd(userId, _ => new())[currency] = b;
                return b;
            });

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
            var balance = _balances.GetOrAdd(key, _ =>
            {
                var b = new AssetBalance();
                _userIndex.GetOrAdd(userId, _ => new())[currency] = b;
                return b;
            });

            lock (balance)
            {
                balance.Available += amount;
            }

            return Task.CompletedTask;
        }

        public Task InitializeBalanceAsync(long userId, string currency, decimal availableBalance)
        {
            var key = (userId, currency);
            var balance = new AssetBalance { Available = availableBalance, Frozen = 0 };
            _balances[key] = balance;

            var userAssets = _userIndex.GetOrAdd(userId, _ => new());
            userAssets[currency] = balance;

            return Task.CompletedTask;
        }

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

        public Dictionary<string, AssetBalance> GetUserAssets(long userId)
        {
            var userAssets = new Dictionary<string, AssetBalance>();

            if (_userIndex.TryGetValue(userId, out var currencies))
            {
                foreach (var (currency, balance) in currencies)
                {
                    lock (balance)
                    {
                        userAssets[currency] = new AssetBalance
                        {
                            Available = balance.Available,
                            Frozen = balance.Frozen
                        };
                    }
                }
            }

            return userAssets;
        }

        public void Clear()
        {
            _balances.Clear();
            _userIndex.Clear();
        }

        public class AssetBalance
        {
            public decimal Available { get; set; }
            public decimal Frozen { get; set; }
        }
    }
}
