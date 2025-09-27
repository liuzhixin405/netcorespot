// filepath: g:\\github\\netcorespot\\src\\CryptoSpot.Application\\Abstractions\\Users\\IMarketMakerRegistry.cs
namespace CryptoSpot.Application.Abstractions.Users
{
    public interface IMarketMakerRegistry
    {
        bool IsMaker(int userId);
    }
}
