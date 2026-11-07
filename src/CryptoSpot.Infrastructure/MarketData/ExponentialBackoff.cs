namespace CryptoSpot.Infrastructure.MarketData;

/// <summary>
/// 带抖动的指数退避：每次失败后上限翻倍，实际延迟在上限内随机，避免多连接同时重连。
/// </summary>
internal sealed class ExponentialBackoff(TimeSpan min, TimeSpan max)
{
    private int _attempt;

    public TimeSpan Next()
    {
        double ceiling = Math.Min(max.TotalMilliseconds, min.TotalMilliseconds * Math.Pow(2, _attempt));
        _attempt = Math.Min(_attempt + 1, 30);

        // [联网-非官方: https://learn.microsoft.com/en-us/dotnet/api/system.random.shared]
        return TimeSpan.FromMilliseconds(min.TotalMilliseconds +
            (Random.Shared.NextDouble() * (ceiling - min.TotalMilliseconds)));
    }

    public void Reset() => _attempt = 0;
}
