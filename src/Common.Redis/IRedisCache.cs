using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Common.Redis.Extensions
{

    /// <summary>
    /// Redis的client
    /// </summary>
    public interface IRedisCache
    {
        /// <summary>
        /// 序列化器 <see cref="ISerializer"/>
        /// </summary>
        ISerializer Serializer { get; }
        /// <summary>
        /// 获取IConnectionMultiplexer对象,对于csredis则是null
        /// </summary>
        IConnectionMultiplexer Connection { get; }
        /// <summary>
        /// 获取缓存数据
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="key">键</param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        T Get<T>(string key, ISerializer customSerializer = null);

        /// <summary>
        /// 获取缓存数据
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="key">键</param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        Task<T> GetAsync<T>(string key, ISerializer customSerializer = null);

        /// <summary>
        /// 添加缓存数据
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        bool Add<T>(string key, T value, ISerializer customSerializer = null);

        /// <summary>
        /// 添加缓存数据
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        Task<bool> AddAsync<T>(string key, T value, ISerializer customSerializer = null);

        /// <summary>
        /// 添加缓存数据
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        /// <param name="expiresIn"></param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        bool Add<T>(string key, T value, TimeSpan expiresIn, ISerializer customSerializer = null);

        /// <summary>
        /// 添加缓存数据
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        /// <param name="expiresSeconds">缓存超时时间，单位：秒</param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        bool Add<T>(string key, T value, Int32 expiresSeconds, ISerializer customSerializer = null);

        /// <summary>
        /// 添加缓存数据
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        /// <param name="expiresAt">过期</param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        bool Add<T>(string key, T value, DateTimeOffset expiresAt, ISerializer customSerializer = null);

        /// <summary>
        /// 添加缓存数据
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        /// <param name="expiresIn">过期</param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        Task<bool> AddAsync<T>(string key, T value, TimeSpan expiresIn, ISerializer customSerializer = null);

        /// <summary>
        /// 添加缓存数据
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        /// <param name="expiresAt">过期</param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        Task<bool> AddAsync<T>(string key, T value, DateTimeOffset expiresAt, ISerializer customSerializer = null);


        /// <summary>
        /// 是否存在键
        /// </summary>
        /// <param name="key">键</param>
        /// <returns></returns>
        bool Exists(string key);

        /// <summary>
        /// 是否存在键
        /// </summary>
        /// <param name="key">键</param>
        /// <returns></returns>
        Task<bool> ExistsAsync(string key);

        /// <summary>
        /// 删除键
        /// </summary>
        /// <param name="key">键</param>
        /// <returns></returns>
        bool Remove(string key);

        /// <summary>
        /// 删除键
        /// </summary>
        /// <param name="key">键</param>
        /// <returns></returns>
        Task<bool> RemoveAsync(string key);

        /// <summary>
        /// 队列右Push
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns>返回list长度</returns>
        long ListLeftPush<T>(string key, T value, ISerializer customSerializer = null) where T : class;
        /// <summary>
        /// 队列右Push
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns>返回list长度</returns>
        Task<long> ListLeftPushAsync<T>(string key, T value, ISerializer customSerializer = null) where T : class;

        /// <summary>
        /// ListLeftPushAsync
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="valueList"></param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        Task<long> ListLeftPushAsync<T>(string key, List<T> valueList, ISerializer customSerializer = null) where T : class;
        /// <summary>
        /// 队列右Push
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns>返回list长度</returns>
        Task<long> ListLeftPushAsync(string key, string value);

        /// <summary>
        /// 队列左pop
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>

        T ListRightPop<T>(string key, ISerializer customSerializer = null) where T : class, new();
        /// <summary>
        /// 队列左pop
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>

        Task<T> ListRightPopAsync<T>(string key, ISerializer customSerializer = null) where T : class, new();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        Task<string> ListRightPopAsync(string key);

        /// <summary>
        /// 高性能从列表右边弹出数据并清除弹出数据，类似ListRightPopAsync，会占用一个分布锁（LockTake，占用的key值为LockTake:{base64(key)}）
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="popnum">每次批量取回的数据量</param>
        /// <param name="isUseLockTake">使用分布锁防止并发，默认值：true</param>
        /// <param name="lockTakeTimeOut">分布锁超时时间秒数</param>
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        Task<List<T>> ListBatchRightPopAsync<T>(string key, int popnum = 100, bool isUseLockTake = true,int lockTakeTimeOut=60, ISerializer customSerializer = null) where T : class, new();
        /// <summary>
        /// 批量pop队列数据，最多只pop topNum条记录
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="topNum"></param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        List<T> ListRightPop<T>(string key, int topNum, ISerializer customSerializer = null) where T : class, new();
        /// <summary>
        /// 批量pop队列数据，最多只pop topNum条记录
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="topNum"></param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        Task<List<T>> ListRightPopAsync<T>(string key, int topNum, ISerializer customSerializer = null) where T : class, new();
        /// <summary>
        /// 队列长度
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>

        long ListLength(string key);
        /// <summary>
        /// 队列长度
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>

        Task<long> ListLengthAsync(string key);
        /// <summary>
        /// 获取指定范围内的元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start">0 表示第一个</param>
        /// <param name="stop">-1 表示最后</param>
        /// <returns></returns>
        string[] ListRange(string key, long start, long stop);


        /// <summary>
        /// 获取指定范围内的元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start">0 表示第一个</param>
        /// <param name="stop">-1 表示最后</param>
        /// <returns></returns>
        Task<string[]> ListRangeAsync(string key, long start, long stop);
        /// <summary>
        /// 获取指定范围内的元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start">0 表示第一个</param>
        /// <param name="stop">-1 表示最后</param>
        ///   <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法，此参数只对StackExchange.Redis生效,CsRedis只能使用json</param>
        /// <returns></returns>
        T[] ListRange<T>(string key, long start, long stop, ISerializer customSerializer = null);

        /// <summary>
        /// 获取指定范围内的元素
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start">0 表示第一个</param>
        /// <param name="stop">-1 表示最后</param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法，此参数只对StackExchange.Redis生效,CsRedis只能使用json</param>
        /// <returns></returns>
        Task<T[]> ListRangeAsync<T>(string key, long start, long stop, ISerializer customSerializer = null);


        #region Stream相关的功能
        /// <summary>
        /// 将指定的流条目追加到指定key的流中。 如果key不存在，作为运行这个命令的副作用，将使用流的条目自动创建key。
        /// </summary>
        /// <param name="key"> 不含prefix前辍</param>
        /// <param name="id">消息id，如果指定的id参数是字符*（星号ASCII字符），XADD命令会自动为您生成一个唯一的ID。 但是，也可以指定一个良好格式的ID，以便新的条目以指定的ID准确存储</param>
        /// <param name="maxLength"> The maximum length of the stream.</param>
        /// <param name="fieldValues">键值对数组</param>
        /// <returns></returns>
        string XAdd(string key, string id = "*", int? maxLength = null, params (string, string)[] fieldValues);
        /// <summary>
        /// 将指定的流条目追加到指定key的流中。 如果key不存在，作为运行这个命令的副作用，将使用流的条目自动创建key。
        /// </summary>
        /// <param name="key"> 不含prefix前辍</param>
        /// <param name="id">消息id，如果指定的id参数是字符*（星号ASCII字符），XADD命令会自动为您生成一个唯一的ID。 但是，也可以指定一个良好格式的ID，以便新的条目以指定的ID准确存储</param>
        ///  <param name="maxLength"> The maximum length of the stream.</param>
        /// <param name="fieldValues">键值对数组</param> 
        /// <returns></returns>
         Task<string> XAddAsync(string key, string id = "*", int? maxLength = null, params (string, string)[] fieldValues);

        /// <summary>
        /// Xlen
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        long XLen(string key);
        /// <summary>
        /// Xlen
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        Task<long> XLenAsync(string key);
        /// <summary>
        ///  XTRIM将流裁剪为指定数量的项目，如有需要，将驱逐旧的项目（ID较小的项目）。此命令被设想为接受多种修整策略，但目前只实现了一种，即MAXLEN，并且与XADD中的MAXLEN选项完全相同。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="maxLen">上限流，当小于0时</param>
        /// <returns></returns>
        long XTrim(string key, int maxLen);

        /// <summary>
        ///  XTRIM将流裁剪为指定数量的项目，如有需要，将驱逐旧的项目（ID较小的项目）。此命令被设想为接受多种修整策略，但目前只实现了一种，即MAXLEN，并且与XADD中的MAXLEN选项完全相同。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="maxLen">上限流，当小于0时</param>
        /// <returns></returns>
        Task<long> XTrimAsync(string key, int maxLen);



        /// <summary>
        /// 返回流中满足给定ID范围的条目。范围由最小和最大ID指定。所有ID在指定的两个ID之间或与其中一个ID相等（闭合区间）的条目将会被返回。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始id，特殊：- 和 +</param>
        /// <param name="end">结束id，特殊：- 和 +</param>
        /// <param name="count">数量</param>
        /// <returns></returns> 
        (string id, string[] items)[] XRange(string key, string start, string end, int count = 1);

        /// <summary>
        /// 返回流中满足给定ID范围的条目。范围由最小和最大ID指定。所有ID在指定的两个ID之间或与其中一个ID相等（闭合区间）的条目将会被返回。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="start">开始id，特殊：- 和 +</param>
        /// <param name="end">结束id，特殊：- 和 +</param>
        /// <param name="count">数量</param>
        /// <returns></returns> 
        Task<(string id, string[] items)[]> XRangeAsync(string key, string start, string end, int count = 1);

        /// <summary>
        /// 与XRANGE完全相同，但显著的区别是以相反的顺序返回条目，并以相反的顺序获取开始-结束参数：在XREVRANGE中，你需要先指定结束ID，再指定开始ID，该命令就会从结束ID侧开始生成两个ID之间（或完全相同）的所有元素。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="end">结束id，特殊：- 和 +</param>
        /// <param name="start">开始id，特殊：- 和 +</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        (string id, string[] items)[] XRevRange(string key, string end, string start, int count = 1);
        /// <summary>
        /// 与XRANGE完全相同，但显著的区别是以相反的顺序返回条目，并以相反的顺序获取开始-结束参数：在XREVRANGE中，你需要先指定结束ID，再指定开始ID，该命令就会从结束ID侧开始生成两个ID之间（或完全相同）的所有元素。
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="end">结束id，特殊：- 和 +</param>
        /// <param name="start">开始id，特殊：- 和 +</param>
        /// <param name="count">数量</param>
        /// <returns></returns>
        Task<(string id, string[] items)[]> XRevRangeAsync(string key, string end, string start, int count = 1);

        /// <summary>
        /// 查找所有分区节点中符合给定模式(pattern)的 key
        /// </summary>
        /// <param name="pattern"> pattern:如：runoob*</param>
        /// <returns></returns>
        string[] Keys(string pattern);


        /// <summary>
        ///  对一个列表进行修剪，让列表只保留指定区间内的元素，不在指定区间之内的元素都将被删除
        /// </summary>
        /// <param name="key"> 不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元</param>
        /// <returns></returns>
        bool LTrim(string key, int start, int stop);

        /// <summary>
        ///  对一个列表进行修剪，让列表只保留指定区间内的元素，不在指定区间之内的元素都将被删除
        /// </summary>
        /// <param name="key"> 不含prefix前辍</param>
        /// <param name="start">开始位置，0表示第一个元素，-1表示最后一个元素</param>
        /// <param name="stop">结束位置，0表示第一个元素，-1表示最后一个元</param>
        /// <returns></returns>
        Task<bool> LTrimAsync(string key, int start, int stop);

        /// <summary>
        ///  用于将信息发送到指定分区节点的频道，最终消息发布格式：1|message
        /// </summary>
        /// <param name="channel"> 频道名</param>
        /// <param name="message">消息文本</param>   
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        long Publish<T>(string channel, T message, ISerializer customSerializer = null);
        /// <summary>
        ///  用于将信息发送到指定分区节点的频道，最终消息发布格式：1|message
        /// </summary>
        /// <param name="channel"> 频道名</param>
        /// <param name="message">消息文本</param> 
        /// <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法</param>
        /// <returns></returns>
        Task<long> PublishAsync<T>(string channel, T message, ISerializer customSerializer = null);

        /// <summary>
        ///  订阅，根据分区规则返回SubscribeObject，Subscribe(("chan1", msg => Console.WriteLine(msg.Body)),
        /// </summary>
        /// <param name="channel"> </param>
        /// <param name="handler"> 频道和接收器</param> 
        /// <returns>返回可停止订阅的对象</returns>
        void Subscribe(string channel, Action<RedisChannel, RedisValue> handler);

        /// <summary>
        ///  Subscribe to perform some operation when a message to the preferred/active node is broadcast, without any guarantee of ordered handling.
        /// </summary>
        /// <param name="channel">The channel to subscribe to.</param>
        /// <param name="handler">The handler to invoke when a message is received on channel.</param>
        /// <param name="flags">The command flags to use.</param>
        Task SubscribeAsync(string channel, Action<RedisChannel, RedisValue> handler);
        #endregion


        /// <summary>
        /// 获取在哈希表中指定 key 的所有字段和值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        Dictionary<string, string> HGetAll(string key);
        /// <summary>
        /// 获取在哈希表中指定 key 的所有字段和值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <returns></returns>
        Task<Dictionary<string, string>> HGetAllAsync(string key);

        /// <summary>
        /// 获取在哈希表中指定 key 的所有字段和值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法，此参数只对StackExchange.Redis生效,CsRedis只能使用json</param>
        /// <returns></returns>
        Task<Dictionary<string, T>> HGetAllAsync<T>(string key, ISerializer customSerializer = null);
        /// <summary>
        /// 获取存储在哈希表中指定字段的值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        /// <returns></returns>
        Task<string> HGetAsync(string key, string field);

        /// <summary>
        /// 获取存储在哈希表中指定字段的值
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">字段</param>
        ///   <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法，此参数只对StackExchange.Redis生效,CsRedis只能使用json</param>
        /// <returns></returns>
        Task<T> HGetAsync<T>(string key, string field, ISerializer customSerializer = null);
        /// <summary>
        /// HGet多个field的值HGet key field1,field2,...
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="fields">字段数组</param>
        ///  <param name="customSerializer">自定义序列化实例，为null则使用全局设置的序列化方法，此参数只对StackExchange.Redis生效,CsRedis只能使用json</param>
        /// <returns></returns>
       Task<List<T>> HGetAsync<T>(string key, string[] fields, ISerializer customSerializer = null);
        /// 删除一个或多个哈希表字段
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="fields">字段</param>
        /// <returns></returns>

        long HDel(string key, params string[] fields);

        /// 删除一个或多个哈希表字段
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="fields">字段</param>
        /// <returns></returns>

        Task<long> HDelAsync(string key, params string[] fields);


        /// <summary>
        /// 同时将多个 field-value (域-值)对设置到哈希表 key 中
        /// </summary>
        /// <param name="key">不含prefix前辍</param>  
        /// <returns></returns>
        bool HMSet(string key, params object[] keyValues);
        /// <summary>
        /// 同时将多个 field-value (域-值)对设置到哈希表 key 中
        /// </summary>
        /// <param name="key">不含prefix前辍</param> 
        /// <param name="keyValues">key1 value1 [key2 value2]</param>
        /// <returns></returns>
        Task<bool> HMSetAsync(string key, params object[] keyValues);

        /// <summary>
        /// 直接将HashEntry数组批量写入hash key中
        /// </summary>
        /// <param name="key">不含prefix前辍</param> 
        /// <param name="keyValues">key1 value1 [key2 value2]</param>
        /// <returns></returns>
        Task<bool> HMSetAsync(string key, HashEntry[] keyValues);

        /// <summary>
        /// 给hash得给定域进行加法操作
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">域</param>
        /// <param name="num">需要加得值</param>
        /// <returns></returns>
        Task<bool> HincrbyAsync(string key, string field, long num = 1);


        /// <summary>
        /// 给hash得给定域进行加法操作
        /// </summary>
        /// <param name="key">不含prefix前辍</param>
        /// <param name="field">域</param>
        /// <param name="num">需要加得值</param>
        /// <returns></returns>
        bool Hincrby(string key, string field, long num = 1);

        /// <summary>
        ///  Allows creation of a group of operations that will be sent to the server as asingle unit, but which may or may not be processed on the server contiguously.
        /// </summary>
        /// <param name="asyncState">The async object state to be passed into the created StackExchange.Redis.IBatch.</param>
        /// <returns></returns>
        IBatch CreateBatch(object asyncState = null);
        /// <summary>
        ///   Allows creation of a group of operations that will be sent to the server as a single unit, and processed on the server as a single unit
        /// </summary>
        /// <param name="asyncState">The async object state to be passed into the created StackExchange.Redis.ITransaction.</param>
        /// <returns> The created transaction.</returns>
        IBatch CreateTransaction(object asyncState = null);
        /// <summary>
        /// Execute an arbitrary command against the server; this is primarily intended for
        /// executing modules, but may also be used to provide access to new features that
        ///  lack a direct API.
        /// </summary>
        /// <param name="command">The command to run.</param>
        /// <param name="args">The arguments to pass for the command.</param>
        /// <returns> A dynamic representation of the command's result</returns>

        RedisResult Execute(string command, params object[] args);


        /// <summary>
        /// 设置redis锁
        /// </summary>
        /// <param name="key"></param>
        /// <param name="token"></param>
        /// <param name="expiresIn">锁默认过期时间</param>
        /// <returns></returns>
        Task<bool> LockTakeAsync(string key, string token, TimeSpan expiresIn);
        /// <summary>
        /// 释放redis锁
        /// </summary>
        /// <param name="key"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<bool> LockReleaseAsync(string key, string token);

        /// <summary>
        /// 获取指定Key的过期时间
        /// </summary>
        /// <param name="key"></param>
        /// <param name="flag"></param>
        /// <returns></returns>
        Task<TimeSpan?> TTLAsync(string key, CommandFlags flag);
        Task<bool> KeyExpireAsync(string key, TimeSpan? expiry, CommandFlags flag = CommandFlags.None);
        /// <summary>
        /// Increments the number stored at key by increment. If the key does not exist,
        ///     it is set to 0 before performing the operation. An error is returned if the key
        ///     contains a value of the wrong type or contains a string that is not representable
        ///    as integer. This operation is limited to 64 bit signed integers.
        ///     https://redis.io/commands/incrby
        /// </summary>
        Task<long> StringIncrementAsync(string key, TimeSpan? expiry = null, long value = 1, CommandFlags flags = CommandFlags.None);
        /// <summary>
        /// Execute a lua script against the server, using previously prepared script.
        /// Named parameters, if any, are provided by the `parameters` object.
        /// </summary>
        /// <param name="script">The script to execute.</param>
        /// <param name="parameters">The parameters to pass to the script.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>A dynamic representation of the script's result</returns>
            Task<RedisResult> ScriptEvaluateAsync(LuaScript script, object parameters = null, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// SortedSetAdd
        /// </summary>
        /// <param name="key"></param>
        /// <param name="valueDc"></param>
        /// <returns></returns>
        Task<bool> SortedSetAddAsync(string key, Dictionary<string, long> valueDc);

        /// <summary>
        /// SortedSetRemoveRangeByScore
        /// </summary>
        /// <param name="key"></param>
        /// <param name="start"></param>
        /// <param name="stop"></param>
        /// <returns></returns>
        Task<bool> SortedSetRemoveRangeByScore(string key, double start, double stop);

       
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
       Task<List<string>> SortedSetRangeByScoreAsync(string key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, bool isAscending = true, long skip = 0, long take = -1);

        /// <summary>
        /// 获取SortedSet的总记录数
        /// </summary>
        /// <param name="key"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
       Task<long> SortedSetLengthAsync(string key, double min = double.NegativeInfinity, double max = double.PositiveInfinity);
    }
}
