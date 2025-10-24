using CryptoSpot.Redis.Configuration;
using CryptoSpot.Redis.Serializer;
using Microsoft.Extensions.Logging;
using Serilog;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace CryptoSpot.Redis
{

    /// <summary>
    /// 缓存服务类
    /// </summary>
    public class RedisService : RedisCache, IRedisService 
    {


        public RedisService(ILogger<RedisService> logger, IConnectionMultiplexer connection, RedisConfiguration config, ISerializer serializer)
            : base(logger, connection, config, serializer)
        {

        }

        /// <summary>
        /// 批量读取list数据（left push）
        /// </summary>
        /// <param name="cacheKey">redis key</param>
        /// <param name="count">一次</param>
        ///  <param name="serializer">自定义序列化类</param>
        /// <returns></returns>
        public async Task<List<T>> BatchRPopAsync<T>(string cacheKey, int count = 100, ISerializer serializer = null)
        {
            var watch = new Stopwatch();
            watch.Start();
            var tasklist = new List<Task<RedisValue>>();
            var result = new List<T>();
            try
            {
                var kllen = await this.ListLengthAsync(cacheKey);
                if (kllen > 0)
                {
                    var batch = this.Database.CreateBatch();
                    count = kllen < count ? (int)kllen : count;
                    for (var i = 0; i < count; i++)
                    {
                        var item = batch.ListRightPopAsync(cacheKey);
                        tasklist.Add(item);
                    }
                    batch.Execute();
                    // batch.WaitAll(tasklist.ToArray());
                    await Task.WhenAll(tasklist.ToArray());

                    if (tasklist.Count > 0)
                    {
                        foreach (var item in tasklist)
                        {
                            var value = await item;
                            if (RedisValue.Null != value)
                            {
                                var data = serializer == null ? await this.Serializer.DeserializeAsync<T>(value) : await serializer.DeserializeAsync<T>(value);
                                if (data != null)
                                    result.Add(data);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"数据批量获取失败：key-{cacheKey},msg -{ex.Message},trace-{ex.StackTrace}");
            }
            watch.Stop();
            if (result.Count > 0 && watch.ElapsedMilliseconds > 500)
            {
                Log.Warning($"{cacheKey}-读取{result.Count}条数据 总耗时共{watch.ElapsedMilliseconds}毫秒");
            }

            return result;
        }


        /// <summary>
        /// 批量lpush
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cacheKey">redis key</param>
        /// <param name="mqList">推送的消息</param>
        /// <returns></returns>
        public async Task<bool> BatchLPushAsync<T>(string cacheKey, List<T> mqList)
        {
            bool isSuccess = false;
            var watch = new Stopwatch();
            watch.Start();
            int totalCount = 0;

            try
            {
                var batch = this.Database.CreateBatch();

                List<RedisValue> redisValueList = new List<RedisValue>();

                foreach (var value in mqList)
                {
                    totalCount++;
                    var serializedItem = SerializerHelper.Serialize(value, Serializer, this.Serializer);
                    redisValueList.Add(serializedItem);
                }
                var result = batch.ListLeftPushAsync(cacheKey, redisValueList.ToArray());


                batch.Execute();
                await Task.WhenAll(result);
                isSuccess = true;
            }
            catch (Exception ex)
            {
                Log.Error($"BatchLPushAsync失败,耗时[{watch.ElapsedMilliseconds}]毫秒,出错原因-{ex.Message},trace-{ex.StackTrace}");
                isSuccess = false;
            }
            finally
            {
                watch.Stop();
                if (watch.ElapsedMilliseconds > 100)
                    Log.Warning($"BatchLPushAsync  {totalCount}条记录，耗时{watch.ElapsedMilliseconds}毫秒");

            }
            return isSuccess;


        }
    }
}
