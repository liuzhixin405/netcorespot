using CryptoSpot.Application.DTOs.Trading;

namespace CryptoSpot.MatchEngine.Core
{
    public interface IOrderPayloadDecoder
    {
        bool TryDecode(string payload, out CreateOrderRequestDto? dto, out int userId, out string? error);
    }
}
