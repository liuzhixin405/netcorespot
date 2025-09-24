using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Common.Redis.Extensions.Serializer
{
    /// <summary>
    /// 序列化辅助类
    /// </summary>
    public class SerializerHelper
    {
        /// <summary>
        /// 反序列化
        /// </summary>
        /// <typeparam name="T">类型</typeparam>
        /// <param name="valueBytes">byte数组</param>
        /// <param name="defaultSerializer">默认的序列化方法</param>
        /// <param name="serializer">自定义的序列化方法</param>
        /// <returns></returns>
        public static T Deserialize<T>(byte[] valueBytes, ISerializer defaultSerializer, ISerializer serializer)
        {
            return (null == serializer ? defaultSerializer.Deserialize<T>(valueBytes) : serializer.Deserialize<T>(valueBytes));
        }
        ///// <summary>
        //   /// 反序列化
        //   /// </summary>
        //   /// <typeparam name="T">类型</typeparam>
        //   /// <param name="valueBytes">byte数组</param>
        //   /// <param name="defaultSerializer">默认的序列化方法</param>
        //   /// <param name="serializer">自定义的序列化方法</param>
        //   /// <returns></returns>
        //public static async Task<T> Deserialize<T>(byte[] valueBytes, ISerializer defaultSerializer, ISerializer serializer)
        //{
        //    return (null == serializer ? await defaultSerializer.Deserialize<T>(valueBytes) : await serializer.Deserialize<T>(valueBytes));
        //}

        /// <summary>
        /// 序列化
        /// </summary>
        /// <param name="item">要序列化的对象</param> 
        /// <param name="defaultSerializer">默认的序列化方法</param>
        /// <param name="serializer">自定义的序列化方法</param>
        /// <returns></returns>
        public static byte[] Serialize(object item, ISerializer defaultSerializer, ISerializer serializer)
        {
            return (null == serializer ? defaultSerializer.Serialize(item) : serializer.Serialize(item));
        }
        ///// <summary>
        ///// 序列化
        ///// </summary>
        ///// <param name="item">要序列化的对象</param> 
        ///// <param name="defaultSerializer">默认的序列化方法</param>
        ///// <param name="serializer">自定义的序列化方法</param>
        ///// <returns></returns>
        //public static async Task<byte[]> Serialize(object item, ISerializer defaultSerializer, ISerializer serializer)
        //{
        //    return (null == serializer ?await  defaultSerializer.Serialize(item) : await serializer.Serialize(item));
        //}   
    }
}
