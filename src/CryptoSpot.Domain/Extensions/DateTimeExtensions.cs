using System;

namespace CryptoSpot.Core.Extensions
{
    /// <summary>
    /// DateTime扩展方法
    /// </summary>
    public static class DateTimeExtensions
    {
        /// <summary>
        /// 将DateTime转换为Unix时间戳（毫秒）
        /// </summary>
        /// <param name="dateTime">要转换的DateTime</param>
        /// <returns>Unix时间戳（毫秒）</returns>
        public static long ToUnixTimeMilliseconds(this DateTime dateTime)
        {
            return ((DateTimeOffset)dateTime).ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// 将DateTime转换为Unix时间戳（秒）
        /// </summary>
        /// <param name="dateTime">要转换的DateTime</param>
        /// <returns>Unix时间戳（秒）</returns>
        public static long ToUnixTimeSeconds(this DateTime dateTime)
        {
            return ((DateTimeOffset)dateTime).ToUnixTimeSeconds();
        }

        /// <summary>
        /// 将Unix时间戳（毫秒）转换为DateTime
        /// </summary>
        /// <param name="unixTimeMilliseconds">Unix时间戳（毫秒）</param>
        /// <returns>DateTime</returns>
        public static DateTime FromUnixTimeMilliseconds(long unixTimeMilliseconds)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixTimeMilliseconds).DateTime;
        }

        /// <summary>
        /// 将Unix时间戳（秒）转换为DateTime
        /// </summary>
        /// <param name="unixTimeSeconds">Unix时间戳（秒）</param>
        /// <returns>DateTime</returns>
        public static DateTime FromUnixTimeSeconds(long unixTimeSeconds)
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixTimeSeconds).DateTime;
        }

        /// <summary>
        /// 获取当前时间的Unix时间戳（毫秒）
        /// </summary>
        /// <returns>Unix时间戳（毫秒）</returns>
        public static long GetCurrentUnixTimeMilliseconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// 获取当前时间的Unix时间戳（秒）
        /// </summary>
        /// <returns>Unix时间戳（秒）</returns>
        public static long GetCurrentUnixTimeSeconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}
