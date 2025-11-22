namespace CryptoSpot.Application.Common.Interfaces
{
    /// <summary>
    /// 当前用户服务接口
    /// </summary>
    public interface ICurrentUserService
    {
        long UserId { get; }
        string Username { get; }
        bool IsAuthenticated { get; }
    }
}
