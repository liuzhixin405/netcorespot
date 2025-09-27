// filepath: g:\\github\\netcorespot\\src\\CryptoSpot.Domain\\Entities\\MarketMakerOptions.cs
namespace CryptoSpot.Domain.Entities
{
    /// <summary>
    /// 多做市账号配置
    /// </summary>
    public class MarketMakerOptions
    {
        public int[] UserIds { get; set; } = System.Array.Empty<int>();
    }
}
