using System.Collections.Concurrent;

namespace CryptoSpot.MatchEngine;

/// <summary>
/// 内存资产存储
/// </summary>
public class InMemoryAssetStore
{
    private readonly ConcurrentDictionary<(long userId, string currency), AssetBalance> _balances = new();

    public async Task<bool> FreezeAssetAsync(long userId, string currency, decimal amount)
    {
        var key = (userId, currency);
        var balance = _balances.GetOrAdd(key, _ => new AssetBalance());

        lock (balance)
        {
            if (balance.Available < amount)
                return false;

            balance.Available -= amount;
            balance.Frozen += amount;
            return true;
        }
    }

    public async Task UnfreezeAssetAsync(long userId, string currency, decimal amount)
    {
        var key = (userId, currency);
        var balance = _balances.GetOrAdd(key, _ => new AssetBalance());

        lock (balance)
        {
            balance.Frozen -= amount;
            if (balance.Frozen < 0) balance.Frozen = 0;
        }
        
        await Task.CompletedTask;
    }

    public async Task AddAvailableBalanceAsync(long userId, string currency, decimal amount)
    {
        var key = (userId, currency);
        var balance = _balances.GetOrAdd(key, _ => new AssetBalance());

        lock (balance)
        {
            balance.Available += amount;
        }
        
        await Task.CompletedTask;
    }

    public async Task InitializeBalanceAsync(long userId, string currency, decimal availableBalance)
    {
        var key = (userId, currency);
        _balances[key] = new AssetBalance
        {
            Available = availableBalance,
            Frozen = 0
        };
        
        await Task.CompletedTask;
    }

    private class AssetBalance
    {
        public decimal Available { get; set; }
        public decimal Frozen { get; set; }
    }
}
