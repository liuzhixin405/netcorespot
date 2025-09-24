using MessagePack;
using Newtonsoft.Json.Bson;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Common.Redis.Extensions.Serializer
{
    /// <summary>
    /// MsgPackSerializer序列化器
    /// </summary>
    public class MsgPackSerializer : ISerializer
    {
        /// <summary>
        /// 序列化
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public byte[] Serialize(object item)
        {
            return MessagePackSerializer.Serialize(item, MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray));
        }

        /// <summary>
        /// 异步序列化
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        [Obsolete("弃用")]
        public Task<byte[]> SerializeAsync(object item)
        {
           
            return Task.Run(() => Serialize(item));
        }

        /// <summary>
        /// 反序列化
        /// </summary>
        /// <param name="serializedObject"></param>
        /// <returns></returns> 
        public object Deserialize(byte[] serializedObject)
        {
            return MessagePackSerializer.Deserialize<object>(serializedObject);
        }

        /// <summary>
        /// 异步反序列化
        /// </summary>
        /// <param name="serializedObject"></param>
        /// <returns></returns>
        [Obsolete("弃用")]
        public Task<object> DeserializeAsync(byte[] serializedObject)
        {
            return Task.Run(() => Deserialize(serializedObject));
        }

        /// <summary>
        /// 泛型反序列化
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="serializedObject"></param>
        /// <returns></returns>
        public T Deserialize<T>(byte[] serializedObject)
        {
            return MessagePackSerializer.Deserialize<T>(serializedObject, MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray));
        }

        /// <summary>
        /// 异步泛型反序列化
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="serializedObject"></param>
        /// <returns></returns>
        [Obsolete("弃用")]
        public Task<T> DeserializeAsync<T>(byte[] serializedObject)
        {
            return Task.Run(() => Deserialize<T>(serializedObject));
        }
    }
}
