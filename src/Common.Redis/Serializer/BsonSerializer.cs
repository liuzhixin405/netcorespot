using Newtonsoft.Json.Bson;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Common.Redis.Extensions.Serializer
{
    /// <summary>
    /// Bson序列化器
    /// </summary>
    public class BsonSerializer : ISerializer
    {
        /// <summary>
        /// 序列化
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public byte[] Serialize(object item)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BsonDataWriter(ms))
            {
                var serializer = new Newtonsoft.Json.JsonSerializer();
                serializer.Serialize(writer, item);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// 异步序列化
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public   Task<byte[]> SerializeAsync(object item)
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
            return Deserialize<object>(serializedObject);
        }

        /// <summary>
        /// 异步反序列化
        /// </summary>
        /// <param name="serializedObject"></param>
        /// <returns></returns>
        public async Task<object> DeserializeAsync(byte[] serializedObject)
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
            using (var ms = new MemoryStream(serializedObject))
            using(var reader = new BsonDataReader(ms))
            {
                var serializer = new Newtonsoft.Json.JsonSerializer();
                return serializer.Deserialize<T>(reader);
            }
        }

        /// <summary>
        /// 异步泛型反序列化
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="serializedObject"></param>
        /// <returns></returns>
        public   Task<T> DeserializeAsync<T>(byte[] serializedObject)
        {
            return Task.Run(() => Deserialize<T>(serializedObject));
        }
    }
}
