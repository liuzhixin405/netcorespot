namespace Common.Redis.Extensions.Configuration
{
	/// <summary>
	/// redis host
	/// </summary>
	public class RedisHost
	{
		/// <summary>
		/// 主机名
		/// </summary>
		public string Host { get; set; } = "localhost";
		/// <summary>
		/// 端口
		/// </summary>
		public int Port { get; set; } = 6379;
	}
}