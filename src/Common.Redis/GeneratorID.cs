using Common.Redis.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Common.Redis.Extensions
{
    /// <summary>
    /// 分布式id生成器
    /// </summary>
    public interface IGeneratorID
    {
        /// <summary>
        /// 下个id
        /// </summary>
        /// <param name="keyprefix">计算key的前缀</param>
        /// <param name="currtime">当前时间</param>
        /// <param name="startTime">第一次计算id时间</param>
        /// <returns>下个id</returns>
        Task<long> NextIDAsync(string keyprefix, DateTimeOffset currtime, DateTimeOffset? startTime = null);
    }
    public class GeneratorID : IGeneratorID
    {
        private IRedisCache _redisCache;
        private ILogger<GeneratorID> _logger;
        public GeneratorID(IRedisCache _redisCache, ILogger<GeneratorID> _logger)
        {
            this._redisCache = _redisCache;
            this._logger = _logger;
        }
        private static DateTimeOffset DefaultStartTime = new DateTimeOffset(2020, 11, 1, 0, 0, 0, TimeSpan.FromHours(8));
        /// <summary>
        /// 下个id
        /// </summary>
        /// <param name="keyprefix">计算key的前缀</param>
        /// <param name="currtime">当前时间</param>
        /// <param name="startTime">第一次计算id时间</param>
        /// <returns>下个id</returns>
        public async Task<long> NextIDAsync(string keyprefix, DateTimeOffset currtime, DateTimeOffset? startTime = null)
        {
            var diffTotalMinutes = Convert.ToInt64((currtime - (startTime ?? DefaultStartTime)).TotalMinutes);
            var rediskey = $"{keyprefix}:{diffTotalMinutes}";
            var r = await _redisCache.StringIncrementAsync(rediskey, TimeSpan.FromMinutes(2));
            var nexid = long.Parse($"{diffTotalMinutes}{r}");
#if DEBUG
            _logger.LogError($"{nexid},{r}-{currtime.ToUnixTimeSeconds()},{startTime ?? DefaultStartTime},{currtime.ToUnixTimeSeconds()},{rediskey}");
#endif
            return nexid;
        }
    }
}
