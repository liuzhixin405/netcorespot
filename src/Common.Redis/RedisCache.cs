using Common.Redis.Extensions.Configuration;
using Common.Redis.Extensions.Serializer;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Common.Redis.Extensions
{
    /// <summary>
    /// RedisClient
    /// </summary>
    public class RedisCache : IRedisCache
    {
        private readonly IConnectionMultiplexer _connection;
        private readonly RedisConfiguration configuration;
        private readonly ILogger<RedisCache> _logger;

        /// <summary>
        /// 序列化器
        /// </summary>
        public ISerializer Serializer { get; }

        /// <summary>
        /// 获取IConnectionMultiplexer对象
        /// </summary>
        public IConnectionMultiplexer Connection { get { return _connection; } }

        /// <summary>
        /// 获取database
        /// </summary>
        public IDatabase Database => _connection.GetDatabase(configuration.Database);

        /// <summary>
        /// 序列化器
        /// </summary> 
        /// <param name="logger"></param>
        /// <param name="connection"></param>
        /// <param name="config"></param>
        /// <param name="serializer"></param>
        public RedisCache(ILogger<RedisCache> logger, IConnectionMultiplexer connection, RedisConfiguration config, ISerializer serializer)
        {
            configuration = config ?? throw new ArgumentNullException(nameof(config), "The configuration could not be null");
            _logger = logger;
            _connection = connection;
            Serializer = serializer ?? new Serializer.JsonSerializer(null);
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
                var valueBytes = Database.StringGet(key);
                return !valueBytes.HasValue ? default(T) : SerializerHelper.Deserialize<T>(valueBytes, Serializer, customSerializer);
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
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public async Task<T> GetAsync<T>(string key, ISerializer customSerializer = null)
        {
            try
            {
                var valueBytes = await Database.StringGetAsync(key);

                if (!valueBytes.HasValue)
                {
                    return default(T);
                }

                return SerializerHelper.Deserialize<T>(valueBytes, Serializer, customSerializer);
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
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public bool Add<T>(string key, T value, ISerializer customSerializer = null)
        {
            try
            {
                var entryBytes = SerializerHelper.Serialize(value, Serializer, customSerializer);
                bool added = Database.StringSet(key, entryBytes);
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
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public async Task<bool> AddAsync<T>(string key, T value, ISerializer customSerializer = null)
        {
            try
            {
                //var entryBytes = SerializerHelper.Serialize(value, Serializer, customSerializer);
                //  var jsonString = JsonConvert.SerializeObject(value);
                var entryBytes = SerializerHelper.Serialize(value, Serializer, customSerializer);
                bool added = await Database.StringSetAsync(key, entryBytes);
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
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public bool Add<T>(string key, T value, TimeSpan expiresIn, ISerializer customSerializer = null)
        {
            try
            {
                var entryBytes = SerializerHelper.Serialize(value, Serializer, customSerializer);
                bool added = Database.StringSet(key, entryBytes, expiresIn);
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
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public bool Add<T>(string key, T value, DateTimeOffset expiresAt, ISerializer customSerializer = null)
        {
            try
            {
                var expiration = expiresAt.Subtract(DateTimeOffset.Now);
                var entryBytes = SerializerHelper.Serialize(value, Serializer, customSerializer);
                bool added = Database.StringSet(key, entryBytes, expiration);
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
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public async Task<bool> AddAsync<T>(string key, T value, TimeSpan expiresIn, ISerializer customSerializer = null)
        {
            try
            {
                var entryBytes = SerializerHelper.Serialize(value, Serializer, customSerializer);
                bool added = await Database.StringSetAsync(key, entryBytes, expiresIn);
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
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public async Task<bool> AddAsync<T>(string key, T value, DateTimeOffset expiresAt, ISerializer customSerializer = null)
        {
            try
            {
                var expiration = expiresAt.Subtract(DateTimeOffset.Now);
                var entryBytes = SerializerHelper.Serialize(value, Serializer, customSerializer);
                bool added = await Database.StringSetAsync(key, entryBytes, expiration);
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
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public bool Add<T>(string key, T value, Int32 expiresSeconds, ISerializer customSerializer = null)
        {
            try
            {
                var expiration = DateTimeOffset.Now.AddSeconds(expiresSeconds).Subtract(DateTimeOffset.Now);
                var entryBytes = SerializerHelper.Serialize(value, Serializer, customSerializer);
                bool added = Database.StringSet(key, entryBytes, expiration);
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
                return Database.KeyExists(key);
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
                return await Database.KeyExistsAsync(key);
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
                bool deleted = Database.KeyDelete(key);
                if (!deleted)
                {
                    _logger.LogDebug($" rediss删除{key}键值数据出错");
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
                bool deleted = await Database.KeyDeleteAsync(key);
                if (!deleted)
                {
                    _logger.LogDebug($" rediss删除{key}键值数据出错");
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
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns>返回list长度</returns>
        public long ListLeftPush<T>(string key, T value, ISerializer customSerializer = null) where T : class
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                {
                    throw new ArgumentException("key cannot be empty.", nameof(key));
                }
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value), "item cannot be null.");
                }
                var serializedItem = SerializerHelper.Serialize(value, Serializer, customSerializer);

                return Database.ListLeftPush(key, serializedItem);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"redis ListAddToLeft key:{key} Error，Exception：{ex.Message}");
                return 0;
            }

        }
        /// <summary>
        /// ListLeftPushAsync
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns>返回list长度</returns>
        public async Task<long> ListLeftPushAsync<T>(string key, T value, ISerializer customSerializer = null) where T : class
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                {
                    throw new ArgumentException("key cannot be empty.", nameof(key));
                }
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value), "item cannot be null.");
                }
                var serializedItem = SerializerHelper.Serialize(value, Serializer, customSerializer);

                return await Database.ListLeftPushAsync(key, serializedItem);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"redis ListAddToLeft key:{key} Error，Exception：{ex.Message}");
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
                if (string.IsNullOrEmpty(key))
                {
                    throw new ArgumentException("key cannot be empty.", nameof(key));
                }

                if (null == valueList || 0 == valueList.Count)
                {
                    throw new ArgumentNullException(nameof(valueList), "valueList cannot be null.");
                }

                List<RedisValue> list = new List<RedisValue>();
                foreach (var value in valueList)
                {
                    list.Add(SerializerHelper.Serialize(value, Serializer, customSerializer));

                }

                return await Database.ListLeftPushAsync(key, list.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"redis ListAddToLeft key:{key} Error，Exception：{ex.Message}");
                return 0;
            }

        }

        /// <summary>
        /// ListLeftPushAsync
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns>返回list长度</returns>
        public async Task<long> ListLeftPushAsync(string key, string value)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                {
                    throw new ArgumentException("key cannot be empty.", nameof(key));
                }

#if DEBUG
                _logger.LogWarning("日志输出OK");
#endif
                return await Database.ListLeftPushAsync(key, value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"redis ListAddToLeft key:{key} Error，Exception：{ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// ListRightPop
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param> 
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        public T ListRightPop<T>(string key, ISerializer customSerializer = null) where T : class, new()
        {
            try
            {

                if (string.IsNullOrEmpty(key))
                {
                    throw new ArgumentException("key cannot be empty.", nameof(key));
                }

                var item = Database.ListRightPop(key);

                if (item == RedisValue.Null) return null;

                return item == RedisValue.Null ? null : SerializerHelper.Deserialize<T>(item, Serializer, customSerializer);

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis ListLeftPop  key:{key} Error，Exception：{ex.Message}");
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

                if (string.IsNullOrEmpty(key))
                {
                    throw new ArgumentException("key cannot be empty.", nameof(key));
                }

                var item = await Database.ListRightPopAsync(key);

                if (item == RedisValue.Null) return null;

                return item == RedisValue.Null ? null : SerializerHelper.Deserialize<T>(item, Serializer, customSerializer);

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis ListLeftPop  key:{key} Error，Exception：{ex.Message}");
                return null;
            }

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
        public async Task<List<T>> ListBatchRightPopAsync<T>(string key, int popnum = 100, bool isUseLockTake = true, int lockTakeTimeOut = 60, ISerializer customSerializer = null) where T : class, new()
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                {
                    throw new ArgumentException("key cannot be empty.", nameof(key));
                }
                string lockkey = $"LockTake:{Convert.ToBase64String(UTF8Encoding.UTF8.GetBytes(key))}";
                if ((!isUseLockTake) || await Database.LockTakeAsync(lockkey, lockkey, TimeSpan.FromSeconds(lockTakeTimeOut)))
                {
                    try
                    {
                        var r = Database.CreateBatch();
                        RedisKey redisKey = key;
                        var task = r.ListRangeAsync(redisKey, -popnum, -1);
                        var tasktrim = r.ListTrimAsync(redisKey, 0, -popnum - 1);
                        r.Execute();
                        await Task.WhenAll(task, tasktrim);
                        var list = await task;
                        List<T> rlist = new List<T>();
                        if (list != null && list.Length > 0)
                        {
                            for (int i = list.Length - 1; i >= 0; i--)
                            {
                                var item = list[i];
                                rlist.Add(SerializerHelper.Deserialize<T>(item, Serializer, customSerializer));
                            }
                        }
                        return rlist;
                    }
                    finally
                    {
                        if (isUseLockTake)
                        {
                            await Database.LockReleaseAsync(lockkey, lockkey);
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
        /// ListRightPopAsync
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public async Task<string> ListRightPopAsync(string key)
        {
            try
            {

                if (string.IsNullOrEmpty(key))
                {
                    throw new ArgumentException("key cannot be empty.", nameof(key));
                }

                return await Database.ListRightPopAsync(key);

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis ListLeftPop  key:{key} Error，Exception：{ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 批量pop队列数据，最多只pop topNum条记录
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">redis的key值</param>
        /// <param name="topNum">pop 数量</param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
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
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
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
                return Database.ListLength(key);
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
                return await Database.ListLengthAsync(key);
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
        /// <param name="start"></param>
        /// <param name="stop"></param>
        /// <returns></returns>
        public string[] ListRange(string key, long start, long stop)
        {
            try
            {
                List<string> list = new List<string>();
                var listArray = Database.ListRange(key, start, stop);
                foreach (var item in listArray)
                {
                    list.Add(item);
                }
                return list.ToArray();
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
        /// <param name="start"></param>
        /// <param name="stop"></param>
        /// <returns></returns>
        public async Task<string[]> ListRangeAsync(string key, long start, long stop)
        {
            try
            {
                List<string> list = new List<string>();
                var listArray = await Database.ListRangeAsync(key, start, stop);
                foreach (var item in listArray)
                {
                    list.Add(item);
                }
                return list.ToArray();
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
        /// <param name="start"></param>
        /// <param name="stop"></param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法，此参数只对StackExchange.Redis生效,CsRedis只能使用json</param>
        /// <returns></returns>
        public T[] ListRange<T>(string key, long start, long stop, ISerializer customSerializer = null)
        {
            try
            {
                List<T> list = new List<T>();
                var listArray = Database.ListRange(key, start, stop);
                foreach (var item in listArray)
                {
                    list.Add(SerializerHelper.Deserialize<T>(item, Serializer, customSerializer));

                }
                return list.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis ListRange<T>  key:{key} Error，Exception：{ex.Message}");
                return default(T[]);
            }
        }
        /// <summary>
        /// 获取指定范围内的元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start"></param>
        /// <param name="stop"></param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法，此参数只对StackExchange.Redis生效,CsRedis只能使用json</param>
        /// <returns></returns>
        public async Task<T[]> ListRangeAsync<T>(string key, long start, long stop, ISerializer customSerializer = null)
        {
            try
            {
                List<T> list = new List<T>();
                var listArray = await Database.ListRangeAsync(key, start, stop);
                foreach (var item in listArray)
                {
                    list.Add(SerializerHelper.Deserialize<T>(item, Serializer, customSerializer));

                }
                return list.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis ListRangeAsync<T>  key:{key} Error，Exception：{ex.Message}");
                return default(T[]);
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
                NameValueEntry[] streamPairs = new NameValueEntry[fieldValues.Length];

                for (int i = 0; i < fieldValues.Length; i++)
                {
                    streamPairs[i] = new NameValueEntry(fieldValues[i].Item1, fieldValues[i].Item2);
                }
                return Database.StreamAdd(key, streamPairs, id, maxLength: maxLength);
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
            try
            {
                NameValueEntry[] streamPairs = new NameValueEntry[fieldValues.Length];

                for (int i = 0; i < fieldValues.Length; i++)
                {
                    streamPairs[i] = new NameValueEntry(fieldValues[i].Item1, fieldValues[i].Item2);
                }
                return await Database.StreamAddAsync(key, streamPairs, id, maxLength: maxLength);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis XAddAsync  key:{key} Error，Exception：{ex.Message}");
                return string.Empty;
            }
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
                return Database.StreamLength(key);
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
            try
            {
                return await Database.StreamLengthAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis XLenAsync  key:{key} Error，Exception：{ex.Message}");
                return -1;
            }
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

                return Database.StreamTrim(key, maxLen);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis XTrim  key:{key} Error，Exception：{ex.Message}");
                return -1;
            }
        } /// <summary>
          ///  XTRIM将流裁剪为指定数量的项目，如有需要，将驱逐旧的项目（ID较小的项目）。此命令被设想为接受多种修整策略，但目前只实现了一种，即MAXLEN，并且与XADD中的MAXLEN选项完全相同。
          /// </summary>
          /// <param name="key">不含prefix前辍</param>
          /// <param name="maxLen">上限流，当小于0时</param>
          /// <returns></returns>
        public async Task<long> XTrimAsync(string key, int maxLen)
        {
            try
            {
                return await Database.StreamTrimAsync(key, maxLen);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis XTrimAsync  key:{key} Error，Exception：{ex.Message}");
                return -1;
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
        public (string id, string[] items)[] XRange(string key, string start, string end, int count = 1)
        {
            try
            {
                StreamEntry[] resultEntrys = Database.StreamRange(key, start, end, count);
                return StreamEntryToArray(resultEntrys);
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
            try
            {
                StreamEntry[] resultEntrys = await Database.StreamRangeAsync(key, start, end, count);
                return StreamEntryToArray(resultEntrys);

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis XRangeAsync  key:{key} Error，Exception：{ex.Message}");
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
        public (string id, string[] items)[] XRevRange(string key, string end, string start, int count = 1)
        {
            try
            {
                StreamEntry[] resultEntrys = Database.StreamRange(key, start, end, count, Order.Descending);
                return StreamEntryToArray(resultEntrys);
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
            try
            {
                StreamEntry[] resultEntrys = await Database.StreamRangeAsync(key, start, end, count, Order.Descending);
                return StreamEntryToArray(resultEntrys);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis XRevRangeAsync  key:{key} Error，Exception：{ex.Message}");
                return null;
            }
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
                var server = _connection.GetServer(configuration.Hosts[0].Host, configuration.Hosts[0].Port);

                var keysArray = server.Keys(configuration.Database, pattern);
                if (null != keysArray)
                {
                    List<string> keysList = new List<string>();
                    foreach (var item in keysArray)
                    {

                        keysList.Add(item);
                    }
                    return keysList.ToArray();
                }
                else
                {
                    return null;
                }

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
                Database.ListTrim(key, start, stop);
                return true;
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
                await Database.ListTrimAsync(key, start, stop);
                return true;
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

                return Database.Publish(channel, entryBytes);
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
                return await Database.PublishAsync(channel, entryBytes);

            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        /// <summary>
        ///  Subscribe to perform some operation when a message to the preferred/active node is broadcast, without any guarantee of ordered handling.
        /// </summary>
        /// <param name="channel">The channel to subscribe to.</param>
        /// <param name="handler">The handler to invoke when a message is received on channel.</param>
        /// <param name="flags">The command flags to use.</param>
        public void Subscribe(string channel, Action<RedisChannel, RedisValue> handler)
        {
            try
            {
                _connection.GetSubscriber().Subscribe(channel, handler, CommandFlags.None);

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis Subscribe  channel:{channel} Error，Exception：{ex.Message}");

            }
        }
        /// <summary>
        ///  Subscribe to perform some operation when a message to the preferred/active node is broadcast, without any guarantee of ordered handling.
        /// </summary>
        /// <param name="channel">The channel to subscribe to.</param>
        /// <param name="handler">The handler to invoke when a message is received on channel.</param>
        /// <param name="flags">The command flags to use.</param>
        public async Task SubscribeAsync(string channel, Action<RedisChannel, RedisValue> handler)
        {
            try
            {
                await _connection.GetSubscriber().SubscribeAsync(channel, handler, CommandFlags.None);

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis SubscribeAsync  channel:{channel} Error，Exception：{ex.Message}");

            }
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
                return StreamEntryToArray(Database.HashGetAll(key));

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
                return StreamEntryToArray(await Database.HashGetAllAsync(key));

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis HashGetAllAsync  channel:{key} Error，Exception：{ex.Message}");
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
                if (!await Database.KeyExistsAsync(key))
                {
                    return null;
                }
                return await StreamEntryToArrayAsync<T>(await Database.HashGetAllAsync(key), customSerializer);

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
        ///   <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法，此参数只对StackExchange.Redis生效,CsRedis只能使用json</param>
        /// <returns></returns>
        public async Task<T> HGetAsync<T>(string key, string field, ISerializer customSerializer = null)
        {

            try
            {
                if (!await Database.HashExistsAsync(key, field))
                {
                    return default(T);
                }
                var result = await Database.HashGetAsync(key, field);
                return SerializerHelper.Deserialize<T>(result, Serializer, customSerializer);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis  HGetAsync<T> channel:{key},fields:{field} Error，Exception：{ex.Message}");
                return default(T);

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
                return await Database.HashGetAsync(key, field);

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis HGetAsync  channel:{key} Error，Exception：{ex.Message}");
                return null;

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
                if (null == fields || 0 == fields.Length)
                {
                    return null;
                }
                List<RedisValue> fieldsList = new List<RedisValue>();
                for (int i = 0; i < fields.Length; i++)
                {
                    fieldsList.Add(fields[i]);
                }
                var values = await Database.HashGetAsync(key, fieldsList.ToArray());
                List<T> dataList = new List<T>();
                if (null != values && values.Length > 0)
                {
                    foreach (var item in values)
                    {
                        if (item.HasValue)
                        {
                            dataList.Add(SerializerHelper.Deserialize<T>(item, Serializer, customSerializer));
                        }

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
                RedisValue[] hashFields = new RedisValue[fields.Length];
                for (int i = 0; i < fields.Length; i++)
                {
                    hashFields[i] = fields[i];
                }
                return Database.HashDelete(key, hashFields);

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
                RedisValue[] hashFields = new RedisValue[fields.Length];
                for (int i = 0; i < fields.Length; i++)
                {
                    hashFields[i] = fields[i];
                }
                return await Database.HashDeleteAsync(key, hashFields);

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
                Database.HashSet(key, StreamEntryToArray(keyValues).ToArray());
                return true;
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
                await Database.HashSetAsync(key, StreamEntryToArray(keyValues).ToArray());
                return true;

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis HMSetAsync  channel:{key} Error，Exception：{ex.Message}");
                return false;

            }
        }

        /// <summary>
        /// 直接将HashEntry数组批量写入hash key中
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="keyValues">key1 value1 [key2 value2]</param>
        /// <returns></returns>
        public async Task<bool> HMSetAsync(string key, HashEntry[] keyValues)
        {
            try
            {
                await Database.HashSetAsync(key, keyValues);
                return true;
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
            try
            {
                await Database.HashIncrementAsync(key, field, num);
                return true;

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis HincrbyAsync  channel:{key} Error，Exception：{ex.Message}");
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
        public bool Hincrby(string key, string field, long num = 1)
        {
            try
            {
                Database.HashIncrement(key, field, num);
                return true;

            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis Hincrby  channel:{key} Error，Exception：{ex.Message}");
                return false;
            }
        }


        /// <summary>
        ///  Allows creation of a group of operations that will be sent to the server as asingle unit, but which may or may not be processed on the server contiguously.
        /// </summary>
        /// <param name="asyncState">The async object state to be passed into the created StackExchange.Redis.IBatch.</param>
        /// <returns></returns>
        public IBatch CreateBatch(object asyncState = null)
        {
            return Database.CreateBatch(asyncState);
        }
        /// <summary>
        ///   Allows creation of a group of operations that will be sent to the server as a single unit, and processed on the server as a single unit
        /// </summary>
        /// <param name="asyncState">The async object state to be passed into the created StackExchange.Redis.ITransaction.</param>
        /// <returns> The created transaction.</returns>
        public IBatch CreateTransaction(object asyncState = null)
        {
            return Database.CreateTransaction(asyncState);
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
            return Database.Execute(command, args);
        }

        #region 辅助方法
        /// <summary>
        /// 
        /// </summary>
        /// <param name="resultEntrys"></param>
        /// <returns></returns>
        public (string id, string[] items)[] StreamEntryToArray(StreamEntry[] resultEntrys)
        {
            if (null != resultEntrys)
            {
                (string id, string[] items)[] resultArray = new (string id, string[] items)[resultEntrys.Length];
                for (int i = 0; i < resultEntrys.Length; i++)
                {
                    resultArray[i].id = resultEntrys[i].Id;
                    if (null != resultEntrys[i].Values)
                    {
                        resultArray[i].items = new string[resultEntrys[i].Values.Length + 1];
                        resultArray[i].items[0] = resultEntrys[i].Id;
                        for (int j = 0; j < resultEntrys[i].Values.Length; j++)
                        {
                            resultArray[i].items[j + 1] = resultEntrys[i].Values[j].Value;
                        }
                    }

                }
                return resultArray;
            }
            else
            {
                return null;
            }
        }
        /// <summary>
        /// HashEntry[]转换成Dictionary<string, string>
        /// </summary>
        /// <param name="resultEntrys"></param> 
        /// <returns></returns>
        public Dictionary<string, string> StreamEntryToArray(HashEntry[] resultEntrys)
        {
            Dictionary<string, string> resultDc = new Dictionary<string, string>();
            if (null != resultEntrys)
            {

                for (int i = 0; i < resultEntrys.Length; i++)
                {
                    if (!resultDc.ContainsKey(resultEntrys[i].Name))
                    {
                        resultDc.Add(resultEntrys[i].Name, resultEntrys[i].Value);
                    }
                }
                return resultDc;
            }
            else
            {
                return null;
            }
        }
        /// <summary>
        /// HashEntry[]转换成Dictionary<string, string>
        /// </summary>
        /// <param name="resultEntrys"></param> 
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法，此参数只对StackExchange.Redis生效,CsRedis只能使用json</param>
        /// <returns></returns>
        public async Task<Dictionary<string, T>> StreamEntryToArrayAsync<T>(HashEntry[] resultEntrys, ISerializer customSerializer = null)
        {
            Dictionary<string, T> resultDc = new Dictionary<string, T>();
            if (null != resultEntrys)
            {

                for (int i = 0; i < resultEntrys.Length; i++)
                {
                    if (!resultDc.ContainsKey(resultEntrys[i].Name))
                    {
                        resultDc.Add(resultEntrys[i].Name, SerializerHelper.Deserialize<T>(resultEntrys[i].Value, Serializer, customSerializer));

                    }
                }
                return resultDc;
            }
            else
            {
                return null;
            }
        }
        /// <summary>
        /// object[] keyValues 转换成 HashEntry[]
        /// </summary>
        /// <param name="keyValues"></param>
        /// <returns></returns>
        public List<HashEntry> StreamEntryToArray(object[] keyValues)
        {
            List<HashEntry> resultList = new List<HashEntry>();
            // HashEntry[] resultDc = new HashEntry[keyValues.Length];
            if (null != keyValues && keyValues.Length > 0)
            {

                for (int i = 0; i < keyValues.Length; i++)
                {
                    resultList.Add((HashEntry)keyValues[i]);
                    // resultDc[i] = new HashEntry(keyValues[i].ToString(), keyValues[i].ToString()); 
                }
                return resultList;
            }
            else
            {
                return null;
            }
        }

        #endregion


        public Task<bool> LockTakeAsync(string key, string token, TimeSpan expiresIn)
        {
            return Database.LockTakeAsync(key, token, expiresIn);
        }

        public Task<bool> LockReleaseAsync(string key, string token)
        {
            return Database.LockReleaseAsync(key, token);
        }

        public Task<TimeSpan?> TTLAsync(string key, CommandFlags flag = CommandFlags.None)
        {
            return Database.KeyTimeToLiveAsync(key, flag);
        }

        public async Task<bool> KeyExpireAsync(string key, TimeSpan? expiry, CommandFlags flag = CommandFlags.None)
        {
            return await Database.KeyExpireAsync(key, expiry, flag);
        }
        /// <summary>
        /// Increments the number stored at key by increment. If the key does not exist,
        ///     it is set to 0 before performing the operation. An error is returned if the key
        ///     contains a value of the wrong type or contains a string that is not representable
        ///    as integer. This operation is limited to 64 bit signed integers.
        ///     https://redis.io/commands/incrby
        /// </summary>
        public async Task<long> StringIncrementAsync(string key, TimeSpan? expiry = null, long value = 1, CommandFlags flags = CommandFlags.None)
        {
            var r = await Database.StringIncrementAsync(key, value, flags);
            if (expiry != null)
            {
                await Database.KeyExpireAsync(key, expiry);
            }
            return r;
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
          return  await Database.ScriptEvaluateAsync(script, parameters, flags); 
        }

        /// <summary>
        /// SortedSetAdd
        /// </summary>
        /// <param name="key"></param>
        /// <param name="valueDc"></param>
        /// <returns></returns>
        public async Task<bool> SortedSetAddAsync(string key, Dictionary<string,long> valueDc)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                {
                    throw new ArgumentException("key cannot be empty.", nameof(key));
                }

                if (null == valueDc || 0 == valueDc.Count)
                {
                    throw new ArgumentNullException(nameof(valueDc), "valueList cannot be null.");
                }
                List<SortedSetEntry> list = new List<SortedSetEntry>();
                foreach (var value in valueDc)
                {
                    list.Add(new SortedSetEntry(value.Key, value.Value) );

                }
                await Database.SortedSetAddAsync(key, list.ToArray());
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis SortedSetAddAsync  channel:{key} Error，Exception：{ex.Message}");
                return false;

            }
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
            try
            {
                if (string.IsNullOrEmpty(key))
                {
                    throw new ArgumentException("key cannot be empty.", nameof(key));
                }
 
                await Database.SortedSetRemoveRangeByScoreAsync(key, start, stop);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis SortedSetAddAsync  channel:{key} Error，Exception：{ex.Message}");
                return false;

            }
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
        public async Task<List<string>> SortedSetRangeByScoreAsync(string key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity,  bool isAscending=true, long skip = 0, long take = -1)
        {
                List<string> dataList = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(key))
                {
                    throw new ArgumentException("key cannot be empty.", nameof(key));
                }


                var order =isAscending ?   Order.Ascending:   Order.Descending;
              var values= await Database.SortedSetRangeByScoreAsync(key, start  ,   stop, Exclude.None,   order  ,   skip ,   take);
                foreach (var item in values)
                {
                    if (item.HasValue)
                    {
                        dataList.Add(item.ToString());
                    }

                }
                return dataList;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis SortedSetAddAsync  channel:{key} Error，Exception：{ex.Message}");
                return null;

            }
        }

        /// <summary>
        /// 获取SortedSet的总记录数
        /// </summary>
        /// <param name="key"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        public async Task<long> SortedSetLengthAsync(string key,  double min = double.NegativeInfinity, double max = double.PositiveInfinity)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                {
                    throw new ArgumentException("key cannot be empty.", nameof(key));
                }

                return await Database.SortedSetLengthAsync(key, min, max);
                 
            }
            catch (Exception ex)
            {
                _logger.LogWarning($" redis SortedSetLengthAsync key:{key} Error，Exception：{ex.Message}");
                return -1;

            }
        }

    }

}
