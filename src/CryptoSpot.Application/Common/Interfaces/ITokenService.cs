namespace CryptoSpot.Application.Common.Interfaces
{
    /// <summary>
    /// JWT Token 服务接口
    /// </summary>
    public interface ITokenService
    {
        string GenerateToken(long userId, string username);
        (bool isValid, long userId, string username) ValidateToken(string token);
    }
}
