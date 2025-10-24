using System.Collections.Generic;
using System.Threading.Tasks;

namespace CryptoSpot.Redis
{
    public interface IRedisService : IRedisCache
    {
        /// <summary>
        /// 批量读取list数据（left push）
        /// </summary>
        /// <param name="cacheKey">redis key</param>
        /// <param name="count">一次</param>
        ///  <param name="serializer">自定义序列化类</param>
        /// <returns></returns>
        Task<List<T>> BatchRPopAsync<T>(string cacheKey, int count = 100, ISerializer serializer = null);
        /// <summary>
        /// 批量lpush
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cacheKey">redis key</param>
        /// <param name="mqList">推送的消息</param>
        /// <returns></returns>
        Task<bool> BatchLPushAsync<T>(string cacheKey, List<T> mqList);
    }
}