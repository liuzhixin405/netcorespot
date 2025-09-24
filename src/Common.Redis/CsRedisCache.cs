using CSRedis;
using Common.Redis.Extensions.Serializer;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Common.Redis.Extensions
{
    /// <summary>
    /// 基于CsRedis的实现
    /// </summary>
    public class CsRedisCache : IRedisCache
    { 
        private readonly ILogger<CsRedisCache> _logger;
        /// <summary>
        /// 序列化器
        /// </summary>
        public ISerializer Serializer { get; }

        /// <summary>
        /// 获取IConnectionMultiplexer对象,对于csredis则是null
        /// </summary>
        public IConnectionMultiplexer Connection { get; }

        /// <summary>
        /// 序列化器
        /// </summary> 
        /// <param name="logger"></param>
        /// <param name="customSerializer">MsgPackSerializer/JsonSerializer/ProtobufSerializer/BsonSerializer</param>
       public CsRedisCache(ILogger<CsRedisCache> logger, ISerializer customSerializer)
        {

            Serializer = customSerializer ?? new Serializer.BsonSerializer();
            _logger = logger;
        }

        /// <summary>
        /// 获取
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public T Get<T>(string key, ISerializer customSerializer = null)
        {
            try
            {
                byte[] valueBytes = RedisHelper.Get<byte[]>(key);

                return null == valueBytes || 0 == valueBytes.Length ? default(T) : SerializerHelper.Deserialize<T>(valueBytes, Serializer, customSerializer);

            }
            catch (Exception ex)
            {
                _logger.LogWarning($"从redis读取{key}键值的数据出错，原因：{ex.Message}");
                return default(T);
            }
        }
        /// <summary>
        /// 获取
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public async Task<T> GetAsync<T>(string key, ISerializer customSerializer = null)
        {
            try
            {
                byte[] valueBytes = await RedisHelper.GetAsync<byte[]>(key);
                return null == valueBytes || 0 == valueBytes.Length ? default(T) :   SerializerHelper.Deserialize<T>(valueBytes, Serializer, customSerializer);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"从redis读取{key}键值的数据出错，原因：{ex.Message}");
                return default(T);
            }
        }

        /// <summary>
        /// 添加
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public bool Add<T>(string key, T value, ISerializer customSerializer = null)
        {
            try
            {
                var entryBytes = SerializerHelper.Serialize(value, Serializer, customSerializer);
                bool added = RedisHelper.Set(key, entryBytes);
                if (!added)
                {
                    _logger.LogWarning($" redis添加{key}键值数据出错");
                }

                return added;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis添加{key}键值数据出错，原因：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 添加
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public async Task<bool> AddAsync<T>(string key, T value, ISerializer customSerializer = null)
        {
            try
            {
                var entryBytes = SerializerHelper.Serialize(value, Serializer, customSerializer);
                bool added = await RedisHelper.SetAsync(key, entryBytes);
                if (!added)
                {
                    _logger.LogWarning($" redis添加{key}键值数据出错");
                }

                return added;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis添加{key}键值数据出错，原因：{ex.Message}");
                return false;
            }

        }

        /// <summary>
        /// 添加
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiresIn"></param>
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public bool Add<T>(string key, T value, TimeSpan expiresIn, ISerializer customSerializer = null)
        {
            try
            {
                var entryBytes = SerializerHelper.Serialize(value, Serializer, customSerializer);
                bool added = RedisHelper.Set(key, entryBytes, Convert.ToInt32(expiresIn.TotalSeconds));
                if (!added)
                {
                    _logger.LogWarning($" redis添加{key}键值数据出错");
                }

                return added;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis添加{key}键值数据出错，原因：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 添加
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiresAt"></param>
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public bool Add<T>(string key, T value, DateTimeOffset expiresAt, ISerializer customSerializer = null)
        {
            try
            {
                int expireTimes = Convert.ToInt32(expiresAt.Subtract(DateTimeOffset.Now).TotalSeconds);
                var entryBytes = SerializerHelper.Serialize(value, Serializer, customSerializer);
                bool added = RedisHelper.Set(key, entryBytes, expireTimes);
                if (!added)
                {
                    _logger.LogWarning($" redis添加{key}键值数据出错");
                }

                return added;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis添加{key}键值数据出错，原因：{ex.Message}");
                return false;
            }

        }
        /// <summary>
        /// 添加缓存数据
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        /// <param name="expiresSeconds">缓存超时时间，单位：秒</param>
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public bool Add<T>(string key, T value, Int32 expiresSeconds, ISerializer customSerializer = null)
        {
            try
            {
                var entryBytes = SerializerHelper.Serialize(value, Serializer, customSerializer);
                bool added = RedisHelper.Set(key, entryBytes, expiresSeconds);
                if (!added)
                {
                    _logger.LogWarning($" redis添加{key}键值数据出错");
                }

                return added;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis添加{key}键值数据出错，原因：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 添加
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiresIn"></param>
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public async Task<bool> AddAsync<T>(string key, T value, TimeSpan expiresIn, ISerializer customSerializer = null)
        {
            try
            {
                var entryBytes = SerializerHelper.Serialize(value, Serializer, customSerializer);

                bool added = await RedisHelper.SetAsync(key, entryBytes, Convert.ToInt32(expiresIn.TotalSeconds));
                if (!added)
                {
                    _logger.LogWarning($" redis添加{key}键值数据出错");
                }

                return added;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis添加{key}键值数据出错，原因：{ex.Message}");
                return false;
            }

        }

        /// <summary>
        /// 添加
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiresAt"></param>
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public async Task<bool> AddAsync<T>(string key, T value, DateTimeOffset expiresAt, ISerializer customSerializer = null)
        {
            try
            {
                int expireTimes = Convert.ToInt32(expiresAt.Subtract(DateTimeOffset.Now).TotalSeconds);
                var entryBytes = SerializerHelper.Serialize(value, Serializer, customSerializer);

                bool added = await RedisHelper.SetAsync(key, entryBytes, expireTimes);
                if (!added)
                {
                    _logger.LogWarning($" redis添加{key}键值数据出错");
                }

                return added;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis添加{key}键值数据出错，原因：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 是否存在
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool Exists(string key)
        {
            try
            {
                return RedisHelper.Exists(key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis获取{key}键值数据出错，原因：{ex.Message}");
                return false;
            }

        }

        /// <summary>
        /// 是否存在
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public async Task<bool> ExistsAsync(string key)
        {
            try
            {
                return await RedisHelper.ExistsAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis获取{key}键值数据出错，原因：{ex.Message}");
                return false;
            }

        }

        /// <summary>
        /// 删除
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool Remove(string key)
        {
            try
            {
                bool deleted = RedisHelper.Del(key) > 0;
                if (!deleted)
                {
                    _logger.LogDebug($" redis删除{key}键值数据出错,键值不存在");
                }

                return deleted;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis删除{key}键值数据出错，原因：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 删除
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public async Task<bool> RemoveAsync(string key)
        {
            try
            {
                bool deleted = await RedisHelper.DelAsync(key) > 0;
                if (!deleted)
                {
                    _logger.LogDebug($" redis删除{key}键值数据出错,键值不存在");
                }

                return deleted;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis删除{key}键值数据出错，原因：{ex.Message}");
                return false;
            }
        }
        /// <summary>
        /// ListLeftPush
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public long ListLeftPush<T>(string key, T value, ISerializer customSerializer = null) where T : class
        {
            try
            {
                var entryBytes = SerializerHelper.Serialize(value, Serializer, customSerializer);
                return RedisHelper.LPush<byte[]>(key, entryBytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"redis ListLeftPush key:{key} Error，Exception：{ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// ListLeftPushAsync
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public async Task<long> ListLeftPushAsync<T>(string key, T value, ISerializer customSerializer = null) where T : class
        {
            try
            {
                var entryBytes = SerializerHelper.Serialize(value, Serializer, customSerializer);

                return await RedisHelper.LPushAsync<byte[]>(key, entryBytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"redis ListLeftPushAsync key:{key} Error，Exception：{ex.Message}");
                return 0;
            }

        }

        /// <summary>
        /// ListLeftPushAsync
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="valueList"></param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public async Task<long> ListLeftPushAsync<T>(string key, List<T> valueList, ISerializer customSerializer = null) where T : class
        {
            try
            {
                List<byte[]> list = new List<byte[]>();
                foreach (var value in valueList)
                {
                    list.Add(SerializerHelper.Serialize(value, Serializer, customSerializer));
                }

                return await RedisHelper.LPushAsync<byte[]>(key, list.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"redis ListLeftPushAsync key:{key} Error，Exception：{ex.Message}");
                return 0;
            }

        }
        /// <summary>
        /// ListLeftPushAsync
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public async Task<long> ListLeftPushAsync(string key, string value)
        {
            return await RedisHelper.LPushAsync<string>(key, value);
        }


        /// <summary>
        /// ListRightPop
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public T ListRightPop<T>(string key, ISerializer customSerializer = null) where T : class, new()
        {
            try
            {
                byte[] valueBytes = RedisHelper.RPop<byte[]>(key);

                return null == valueBytes || 0 == valueBytes.Length ? default(T) : SerializerHelper.Deserialize<T>(valueBytes, Serializer, customSerializer);

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis ListRightPop  key:{key} Error，Exception：{ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// ListRightPopAsync
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public async Task<T> ListRightPopAsync<T>(string key, ISerializer customSerializer = null) where T : class, new()
        {
            try
            {
                byte[] valueBytes = await RedisHelper.RPopAsync<byte[]>(key);
                return null == valueBytes || 0 == valueBytes.Length ? default(T) :  SerializerHelper.Deserialize<T>(valueBytes, Serializer, customSerializer);


            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis ListRightPop  key:{key} Error，Exception：{ex.Message}");
                return null;
            }


        }

        /// <summary>
        /// ListRightPopAsync
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public async Task<string> ListRightPopAsync(string key)
        {
            return await RedisHelper.RPopAsync<string>(key);
        }
        /// <summary>
        /// 高性能从列表右边弹出数据并清除弹出数据，类似ListRightPopAsync，会占用一个分布锁（LockTake，占用的key值为LockTake:{key}）
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="popnum">每次批量取回的数据量</param>
        /// <param name="isUseLockTake">使用分布锁防止并发，默认值：true</param>
        /// <param name="lockTakeTimeOut">分布锁超时时间</param>
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public async Task<List<T>> ListBatchRightPopAsync<T>(string key, int popnum = 100, bool isUseLockTake = true, int lockTakeTimeOut = 60, ISerializer customSerializer = null) where T : class, new()
        {

            try
            {
                if (string.IsNullOrEmpty(key))
                {
                    throw new ArgumentException("key cannot be empty.", nameof(key));
                }
                string lockkey = $"LockTake:{Convert.ToBase64String(UTF8Encoding.UTF8.GetBytes(key))}";
                if ((!isUseLockTake)||(RedisHelper.Lock(lockkey, lockTakeTimeOut) !=null))
                {
                    try
                    {
                        RedisKey redisKey = key;
                        var task = RedisHelper.LRangeAsync<T>(redisKey, -popnum, -1);
                        var tasktrim = RedisHelper.LTrimAsync(redisKey, 0, -popnum - 1);
                        await Task.WhenAll(task, tasktrim);
                        var list = await task;
                        List<T> rlist = new List<T>();
                        if (list != null && list.Length > 0)
                        {
                            for (int i = list.Length - 1; i >= 0; i--)
                            {
                                var item = list[i];
                                rlist.Add(item);
                            }
                        }
                        return rlist;
                    }
                    finally
                    {
                        if (isUseLockTake)
                        {
                            await RedisHelper.DelAsync(lockkey);
                        }
                    }
                }
                else
                {
                    return new List<T>(0);
                }

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis ListBatchRightPopAsync  key:{key} Error，Exception：{ex.Message}");
                return null;
            }

        }
        /// <summary>
        /// 批量pop队列数据，最多只pop topNum条记录
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">redis的key值</param>
        /// <param name="topNum">pop 数量</param>
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public List<T> ListRightPop<T>(string key, int topNum, ISerializer customSerializer = null) where T : class, new()
        {
            List<T> list = new List<T>();
            while (list.Count < topNum)
            {
                T item = ListRightPop<T>(key, customSerializer);
                if (null == item)
                {
                    break;
                }
                list.Add(item);
            }
            return list;
        }
        /// <summary>
        /// 批量pop队列数据，最多只pop topNum条记录
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">redis的key值</param>
        /// <param name="topNum">pop 数量</param>
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public async Task<List<T>> ListRightPopAsync<T>(string key, int topNum, ISerializer customSerializer = null) where T : class, new()
        {
            List<T> list = new List<T>();
            while (list.Count < topNum)
            {
                T item = await ListRightPopAsync<T>(key, customSerializer);
                if (null == item)
                {
                    break;
                }
                list.Add(item);
            }
            return list;
        }

        /// <summary>
        /// list队列长度
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public long ListLength(string key)
        {
            try
            {
                return RedisHelper.LLen(key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis ListLength  key:{key} Error，Exception：{ex.Message}");
                return 0;
            }
        }
        /// <summary>
        /// list队列长度
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public async Task<long> ListLengthAsync(string key)
        {
            try
            {
                return await RedisHelper.LLenAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis ListLengthAsync  key:{key} Error，Exception：{ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 获取指定范围内的元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start">0 表示第一个</param>
        /// <param name="stop">-1 表示最后</param>
        /// <returns></returns>
        public string[] ListRange(string key, long start, long stop)
        {
            try
            {
                return RedisHelper.LRange(key, start, stop);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis ListRange  key:{key} Error，Exception：{ex.Message}");
                return new string[0];
            }
        }
        /// <summary>
        /// 获取指定范围内的元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start">0 表示第一个</param>
        /// <param name="stop">-1 表示最后</param>
        /// <returns></returns>
        public async Task<string[]> ListRangeAsync(string key, long start, long stop)
        {
            try
            {
                return await RedisHelper.LRangeAsync(key, start, stop);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis ListRangeAsync  key:{key} Error，Exception：{ex.Message}");
                return new string[0];
            }
        }
        /// <summary>
        /// 获取指定范围内的元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start">0 表示第一个</param>
        /// <param name="stop">-1 表示最后</param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法，此参数只对StackExchange.Redis生效,CsRedis只能使用json</param>
        /// <returns></returns>
        public T[] ListRange<T>(string key, long start, long stop, ISerializer customSerializer = null)
        {
            try
            {
                return RedisHelper.LRange<T>(key, start, stop);

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis ListRange<T>  key:{key} Error，Exception：{ex.Message}");
                return default(T[]);// T[0];
            }
        }
        /// <summary>
        /// 获取指定范围内的元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start">0 表示第一个</param>
        /// <param name="stop">-1 表示最后</param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法，此参数只对StackExchange.Redis生效,CsRedis只能使用json</param>
        /// <returns></returns>
        public async Task<T[]> ListRangeAsync<T>(string key, long start, long stop, ISerializer customSerializer = null)
        {
            try
            {
                return await RedisHelper.LRangeAsync<T>(key, start, stop);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis LRangeAsync<T>  key:{key} Error，Exception：{ex.Message}");
                return default(T[]);// T[0];
            }
        }



        #region Stream相关的功能
        /// <summary>
        /// 将指定的流条目追加到指定key的流中。 如果key不存在，作为运行这个命令的副作用，将使用流的条目自动创建key。
        /// </summary>
        /// <param name="key"> 不含prefix前辍</param>
        /// <param name="id">消息id，如果指定的id参数是字符*（星号ASCII字符），XADD命令会自动为您生成一个唯一的ID。 但是，也可以指定一个良好格式的ID，以便新的条目以指定的ID准确存储</param>
        /// <param name="maxLength"> The maximum length of the stream.</param>
        /// <param name="fieldValues">键值对数组</param>
        /// <returns></returns>
        public string XAdd(string key, string id = "*", int? maxLength = null, params (string, string)[] fieldValues)
        {
            try
            {
                if(null== maxLength)
                {
                    maxLength = 0;
                }
                return RedisHelper.Instance.XAdd(key,Convert.ToInt64( maxLength), id = "*", fieldValues);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis XAdd  key:{key} Error，Exception：{ex.Message}");
                return string.Empty;
            }
        }
        /// <summary>
        /// 将指定的流条目追加到指定key的流中。 如果key不存在，作为运行这个命令的副作用，将使用流的条目自动创建key。
        /// </summary>
        /// <param name="key"> 不含prefix前辍</param>
        /// <param name="id">消息id，如果指定的id参数是字符*（星号ASCII字符），XADD命令会自动为您生成一个唯一的ID。 但是，也可以指定一个良好格式的ID，以便新的条目以指定的ID准确存储</param>
        ///  <param name="maxLength"> The maximum length of the stream.</param>
        /// <param name="fieldValues">键值对数组</param> 
        /// <returns></returns>
        public async Task<string> XAddAsync(string key, string id = "*", int? maxLength = null, params (string, string)[] fieldValues)
        {

            return XAdd(key, id, maxLength, fieldValues);//伪异步

        }
        /// <summary>
        /// Xlen
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public long XLen(string key)
        {
            try
            {
                return RedisHelper.Instance.XLen(key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis XLen  key:{key} Error，Exception：{ex.Message}");
                return -1;
            }
        }
        /// <summary>
        /// Xlen
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public async Task<long> XLenAsync(string key)
        {
            return XLen(key);//伪异步
        }
        /// <summary>
        ///  XTRIM将流裁剪为指定数量的项目，如有需要，将驱逐旧的项目（ID较小的项目）。此命令被设想为接受多种修整策略，但目前只实现了一种，即MAXLEN，并且与XADD中的MAXLEN选项完全相同。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="maxLen">上限流，当小于0时</param>
        /// <returns></returns>
        public long XTrim(string key, int maxLen)
        {
            try
            {
                return RedisHelper.Instance.XTrim(key, maxLen);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis XTrim  key:{key} Error，Exception：{ex.Message}");
                return -1;
            }
        }
        /// <summary>
        ///  XTRIM将流裁剪为指定数量的项目，如有需要，将驱逐旧的项目（ID较小的项目）。此命令被设想为接受多种修整策略，但目前只实现了一种，即MAXLEN，并且与XADD中的MAXLEN选项完全相同。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="maxLen">上限流，当小于0时</param>
        /// <returns></returns>
        public async Task<long> XTrimAsync(string key, int maxLen)
        {
            return XTrim(key, maxLen);//伪异步
        }

        /// <summary>
        /// 返回流中满足给定ID范围的条目。范围由最小和最大ID指定。所有ID在指定的两个ID之间或与其中一个ID相等（闭合区间）的条目将会被返回。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始id，特殊：- 和 +</param>
        /// <param name="end">结束id，特殊：- 和 +</param>
        /// <param name="count">数量</param>
        /// <returns></returns> 
        public (string id, string[] items)[] XRange(string key, string start, string end, int count = 1)
        {
            try
            {
                return RedisHelper.Instance.XRange(key, start, end, count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis XRange  key:{key} Error，Exception：{ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 返回流中满足给定ID范围的条目。范围由最小和最大ID指定。所有ID在指定的两个ID之间或与其中一个ID相等（闭合区间）的条目将会被返回。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始id，特殊：- 和 +</param>
        /// <param name="end">结束id，特殊：- 和 +</param>
        /// <param name="count">数量</param>
        /// <returns></returns> 
        public async Task<(string id, string[] items)[]> XRangeAsync(string key, string start, string end, int count = 1)
        {
            return XRange(key, start, end, count);

        }
        /// <summary>
        /// 与XRANGE完全相同，但显著的区别是以相反的顺序返回条目，并以相反的顺序获取开始-结束参数：在XREVRANGE中，你需要先指定结束ID，再指定开始ID，该命令就会从结束ID侧开始生成两个ID之间（或完全相同）的所有元素。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="end">结束id，特殊：- 和 +</param>
        /// <param name="start">开始id，特殊：- 和 +</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        public (string id, string[] items)[] XRevRange(string key, string end, string start, int count = 1)
        {
            try
            {
                return RedisHelper.Instance.XRevRange(key, end, start, count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis XRevRange  key:{key} Error，Exception：{ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// 与XRANGE完全相同，但显著的区别是以相反的顺序返回条目，并以相反的顺序获取开始-结束参数：在XREVRANGE中，你需要先指定结束ID，再指定开始ID，该命令就会从结束ID侧开始生成两个ID之间（或完全相同）的所有元素。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="end">结束id，特殊：- 和 +</param>
        /// <param name="start">开始id，特殊：- 和 +</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        public async Task<(string id, string[] items)[]> XRevRangeAsync(string key, string end, string start, int count = 1)
        {
            return XRevRange(key, end, start, count);
        }

        /// <summary>
        /// 查找所有分区节点中符合给定模式(pattern)的 key
        /// </summary>
        /// <param name="pattern"> pattern:如：runoob*</param>
        /// <returns></returns>
        public string[] Keys(string pattern)
        {
            try
            {
                return RedisHelper.Instance.Keys(pattern);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis Keys  key:{pattern} Error，Exception：{ex.Message}");
                return null;
            }
        }
        /// <summary>
        ///  对一个列表进行修剪，让列表只保留指定区间内的元素，不在指定区间之内的元素都将被删除
        /// </summary>
        /// <param name="key"> 不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元</param>
        /// <returns></returns>
        public bool LTrim(string key, int start, int stop)
        {
            try
            {
                return RedisHelper.Instance.LTrim(key, start, stop);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis LTrim  key:{key} Error，Exception：{ex.Message}");
                return false;
            }
        }
        /// <summary>
        ///  对一个列表进行修剪，让列表只保留指定区间内的元素，不在指定区间之内的元素都将被删除
        /// </summary>
        /// <param name="key"> 不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元</param>
        /// <returns></returns>
        public async Task<bool> LTrimAsync(string key, int start, int stop)
        {
            try
            {
                return await RedisHelper.Instance.LTrimAsync(key, start, stop);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis LTrimAsync  key:{key} Error，Exception：{ex.Message}");
                return false;
            }
        }


        #endregion

        #region 消息队列相关

        /// <summary>
        ///  用于将信息发送到指定分区节点的频道，最终消息发布格式：1|message
        /// </summary>
        /// <param name="channel"> 频道名</param>
        /// <param name="message">消息文本</param>  
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public long Publish<T>(string channel, T message, ISerializer customSerializer = null)
        {
            try
            {
                var entryBytes = SerializerHelper.Serialize(message, Serializer, customSerializer);
                string value = System.Text.Encoding.UTF8.GetString(entryBytes);
                return RedisHelper.Instance.Publish(channel, value);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        /// <summary>
        ///  用于将信息发送到指定分区节点的频道，最终消息发布格式：1|message
        /// </summary>
        /// <param name="channel"> 频道名</param>
        /// <param name="message">消息文本</param> 
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public async Task<long> PublishAsync<T>(string channel, T message, ISerializer customSerializer = null)
        {
            try
            {
                var entryBytes = SerializerHelper.Serialize(message, Serializer, customSerializer);
                string value = System.Text.Encoding.UTF8.GetString(entryBytes);
                return await RedisHelper.Instance.PublishAsync(channel, value);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        /// <summary>
        ///  订阅，根据分区规则返回SubscribeObject，Subscribe(("chan1", msg => Console.WriteLine(msg.Body)),
        /// </summary>
        /// <param name="channel"> </param>
        /// <param name="handler"> 频道和接收器</param> 
        /// <returns>返回可停止订阅的对象</returns>
        public void Subscribe(string channel, Action<RedisChannel, RedisValue> handler)
        {
            try
            {
                RedisHelper.Instance.SubscribeList(new string[] { channel }, (x1, x2) => { handler(x1, x2); });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis Subscribe  channel:{channel} Error，Exception：{ex.Message}");

            }
        }
        /// <summary>
        ///  订阅，根据分区规则返回SubscribeObject，Subscribe(("chan1", msg => Console.WriteLine(msg.Body)),
        /// </summary>
        /// <param name="channel"> </param>
        /// <param name="handler"> 频道和接收器</param> 
        /// <returns>返回可停止订阅的对象</returns>
        public async Task SubscribeAsync(string channel, Action<RedisChannel, RedisValue> handler)
        {
            Subscribe(channel, handler);
        }
        #endregion


        /// <summary>
        /// 获取在哈希表中指定 key 的所有字段和值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public Dictionary<string, string> HGetAll(string key)
        {

            try
            {
                return RedisHelper.Instance.HGetAll(key);

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis HGetAll  channel:{key} Error，Exception：{ex.Message}");
                return null;

            }
        }
        /// <summary>
        /// 获取在哈希表中指定 key 的所有字段和值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        public async Task<Dictionary<string, string>> HGetAllAsync(string key)
        {

            try
            {
                return await RedisHelper.Instance.HGetAllAsync(key);

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis HGetAll  channel:{key} Error，Exception：{ex.Message}");
                return null;

            }
        }
        /// <summary>
        /// 获取在哈希表中指定 key 的所有字段和值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法，此参数只对StackExchange.Redis生效,CsRedis只能使用json</param>
        /// <returns></returns>
        public async Task<Dictionary<string, T>> HGetAllAsync<T>(string key, ISerializer customSerializer = null)
        {

            try
            {
                return await RedisHelper.Instance.HGetAllAsync<T>(key);

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis  HGetAllAsync<T>  channel:{key} Error，Exception：{ex.Message}");
                return null;

            }
        }
        /// <summary>
        /// 获取存储在哈希表中指定字段的值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        /// <returns></returns>
        public async Task<string> HGetAsync(string key, string field)
        {
            try
            {
                return await RedisHelper.Instance.HGetAsync(key, field);

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis HGetAsync  channel:{key} Error，Exception：{ex.Message}");
                return null;

            }
        }
        /// <summary>
        /// 获取存储在哈希表中指定字段的值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        ///   <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法，此参数只对StackExchange.Redis生效,CsRedis只能使用json</param>
        /// <returns></returns>
        public async Task<T> HGetAsync<T>(string key, string field, ISerializer customSerializer = null)
        {
            try
            {
                return await RedisHelper.Instance.HGetAsync<T>(key, field); //暂时不对customSerializer进行处理，HGet相关方法建议使用RedisCache的

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis HGetAsync  channel:{key} Error，Exception：{ex.Message}");
                return default(T);

            }
        }
        /// <summary>
        /// HGet多个field的值HGet key field1,field2,...
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="fields">字段数组</param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法，此参数只对StackExchange.Redis生效,CsRedis只能使用json</param>
        /// <returns></returns>
        public async Task<List<T>> HGetAsync<T>(string key, string[] fields, ISerializer customSerializer = null)
        {
            try
            {
                List<T> dataList = new List<T>();
                foreach (var field in fields) //简单实现，csredis 不支持批量读取多个，也可使用pipeline操作，性能更好
                {
                  var value=  await RedisHelper.Instance.HGetAsync<T>(key, field); //暂时不对customSerializer进行处理，HGet相关方法建议使用RedisCache的
               if(null!= value)
                    {
                        dataList.Add(value);
                    }
                }
                return dataList;

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis HGetAsync  channel:{key} Error，Exception：{ex.Message}");
                return default(List<T>);

            }
        }


        /// 删除一个或多个哈希表字段
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="fields">字段</param>
        /// <returns></returns>

        public long HDel(string key, params string[] fields)
        {
            try
            {
                return RedisHelper.Instance.HDel(key, fields);

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis HDel  channel:{key} Error，Exception：{ex.Message}");
                return 0;

            }
        }

        /// 删除一个或多个哈希表字段
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="fields">字段</param>
        /// <returns></returns>

        public async Task<long> HDelAsync(string key, params string[] fields)
        {
            try
            {
                return await RedisHelper.Instance.HDelAsync(key, fields);

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis HDelAsync  channel:{key} Error，Exception：{ex.Message}");
                return 0;

            }
        }


        /// <summary>
        /// 同时将多个 field-value (域-值)对设置到哈希表 key 中
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="keyValues">key1 value1 [key2 value2]</param>
        /// <returns></returns>
        public bool HMSet(string key, params object[] keyValues)
        {
            try
            {
                return RedisHelper.Instance.HMSet(key, keyValues);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis HMSet  channel:{key} Error，Exception：{ex.Message}");
                return false;

            }
        }
        /// <summary>
        /// 同时将多个 field-value (域-值)对设置到哈希表 key 中
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="keyValues">key1 value1 [key2 value2]</param>
        /// <returns></returns>
        public async Task<bool> HMSetAsync(string key, params object[] keyValues)
        {
            try
            {
                return await RedisHelper.Instance.HMSetAsync(key, keyValues);

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis HMSetAsync  channel:{key} Error，Exception：{ex.Message}");
                return false;

            }
        }

        /// <summary>
        /// 给hash得给定域进行加法操作
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">域</param>
        /// <param name="num">需要加得值</param>
        /// <returns></returns>
        public async Task<bool> HincrbyAsync(string key, string field, long num = 1)
        {
            return false;
        }


        /// <summary>
        /// 给hash得给定域进行加法操作
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">域</param>
        /// <param name="num">需要加得值</param>
        /// <returns></returns>
        public bool Hincrby(string key, string field, long num = 1)
        {
            return false;
        }


        /// <summary>
        ///  Allows creation of a group of operations that will be sent to the server as asingle unit, but which may or may not be processed on the server contiguously.
        /// </summary>
        /// <param name="asyncState">The async object state to be passed into the created StackExchange.Redis.IBatch.</param>
        /// <returns></returns>
        public IBatch CreateBatch(object asyncState = null)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        ///   Allows creation of a group of operations that will be sent to the server as a single unit, and processed on the server as a single unit
        /// </summary>
        /// <param name="asyncState">The async object state to be passed into the created StackExchange.Redis.ITransaction.</param>
        /// <returns> The created transaction.</returns>
        public IBatch CreateTransaction(object asyncState = null)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Execute an arbitrary command against the server; this is primarily intended for
        /// executing modules, but may also be used to provide access to new features that
        ///  lack a direct API.
        /// </summary>
        /// <param name="command">The command to run.</param>
        /// <param name="args">The arguments to pass for the command.</param>
        /// <returns> A dynamic representation of the command's result</returns>

        public RedisResult Execute(string command, params object[] args)
        {
            throw new NotImplementedException();
        }

        public Task<bool> LockTakeAsync(string key, string token, TimeSpan expiresIn)
        {
            throw new NotImplementedException();
        }

        public Task<bool> LockReleaseAsync(string key, string token)
        {
            throw new NotImplementedException();
        }

        public Task<TimeSpan?> TTLAsync(string key, CommandFlags flag = CommandFlags.None)
        {
            throw new NotImplementedException();
        }

        public Task<bool> KeyExpireAsync(string key, TimeSpan? expiry, CommandFlags flag = CommandFlags.None)
        {
            throw new NotImplementedException();
        }

        public Task<long> StringIncrementAsync(string key, TimeSpan? expiry = null, long value = 1, CommandFlags flags = CommandFlags.None)
        {
            throw new NotImplementedException();
        }

        public Task<bool> HMSetAsync(string key, HashEntry[] keyValues)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Execute a lua script against the server, using previously prepared script.
        /// Named parameters, if any, are provided by the `parameters` object.
        /// </summary>
        /// <param name="script">The script to execute.</param>
        /// <param name="parameters">The parameters to pass to the script.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>A dynamic representation of the script's result</returns>
        public async Task<RedisResult> ScriptEvaluateAsync(LuaScript script, object parameters = null, CommandFlags flags = CommandFlags.None)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// SortedSetAdd
        /// </summary>
        /// <param name="key"></param>
        /// <param name="valueDc"></param>
        /// <returns></returns>
        public async Task<bool> SortedSetAddAsync(string key, Dictionary<string, long> valueDc) 
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// SortedSetRemoveRangeByScore
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start"></param>
        /// <param name="stop"></param>
        /// <returns></returns>
        public async Task<bool> SortedSetRemoveRangeByScore(string key, double start, double stop)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// SortedSetRangeByScoreAsync
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start"></param>
        /// <param name="stop"></param> 
        /// <param name="isAscending"></param>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public async Task<List<string>> SortedSetRangeByScoreAsync(string key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, bool isAscending = true, long skip = 0, long take = -1)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// 获取SortedSet的总记录数
        /// </summary>
        /// <param name="key"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        public async Task<long> SortedSetLengthAsync(string key, double min = double.NegativeInfinity, double max = double.PositiveInfinity)
        {
            throw new NotImplementedException();
        }
    }
}
