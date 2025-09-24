using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Common.Redis.Extensions.Serializer
{
    /// <summary>
    /// Json序列化器
    /// </summary>
    public class JsonSerializer : ISerializer
    {
        private static readonly Encoding encoding = Encoding.UTF8;
        private readonly JsonSerializerSettings settings;
        /// <summary>
		/// Initializes a new instance of the <see cref="JsonSerializer"/> class.
		/// </summary>
		/// <param name="settings">The settings.</param>
		public JsonSerializer(JsonSerializerSettings settings)
        {
            this.settings = settings ?? new JsonSerializerSettings();
        }

        /// <summary>
        /// 序列化
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public byte[] Serialize(object item)
        {
            var type = item?.GetType();
            var jsonString = JsonConvert.SerializeObject(item, type, settings);
            return encoding.GetBytes(jsonString);
        }

        /// <summary>
        /// 异步序列化
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
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
            return Deserialize<object>(serializedObject);
        }

        /// <summary>
        /// 异步反序列化
        /// </summary>
        /// <param name="serializedObject"></param>
        /// <returns></returns>
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
            var jsonString = encoding.GetString(serializedObject);
            return JsonConvert.DeserializeObject<T>(jsonString, settings);
        }

        /// <summary>
        /// 异步泛型反序列化
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="serializedObject"></param>
        /// <returns></returns>
        public Task<T> DeserializeAsync<T>(byte[] serializedObject)
        {
            return Task.Run(() => Deserialize<T>(serializedObject));
        }
    }
}
