using System.Collections.Generic;
using System.Net.Security;
using StackExchange.Redis;

namespace Common.Redis.Extensions.Configuration
{
    public class RedisConfiguration
    {
        private   ConnectionMultiplexer connection;
        private   ConfigurationOptions options;

        public int PoolSize { get; set; } = 20;

        /// <summary>
        /// The key separation prefix used for all cache entries
        /// </summary>
        public string KeyPrefix { get; set; }

        /// <summary>
        /// The password or access key
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// Specify if the connection can use Admin commands like flush database
        /// </summary>
        /// <value>
        ///   <c>true</c> if can use admin commands; otherwise, <c>false</c>.
        /// </value>
        public bool AllowAdmin { get; set; } = false;

        /// <summary>
        /// Specify if the connection is a secure connection or not.
        /// </summary>
        /// <value>
        ///   <c>true</c> if is secure; otherwise, <c>false</c>.
        /// </value>
        public bool Ssl { get; set; } = false;

        /// <summary>
        /// The connection timeout,默认5000
        /// </summary>
        public int ConnectTimeout { get; set; } = 5000;

        
        
        /// <summary>
        /// WriteBuffer
        /// </summary>
        public int WriteBuffer { get; set; } = 4096000;
        
        /// <summary>
        /// If true, Connect will not create a connection while no servers are available
        /// </summary>
        public bool AbortOnConnectFail { get; set; }

        /// <summary>
        /// Database Id
        /// </summary>
        /// <value>
        /// The database id, the default value is 0
        /// </value>
        public int Database { get; set; } = 0;
        /// <summary>
        /// 连接名称，可以使用client list 查看
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 预热的连接数
        /// </summary>
        public int PreHeat { get; set; } = 0;

        /// <summary>
        /// 连接池内元素空闲时间（毫秒），适用连接原创redis-server
        /// </summary>
        public int IdleTimeout { get; set; } = 0;
        /// <summary>
        /// 执行命令出错，尝试执行的次数
        /// </summary>
        public int TryIt { get; set; } = 0; 

 
        /// <summary>
        /// The host of Redis Server
        /// </summary>
        /// <value>
        /// The ip or name
        /// </value>
        public RedisHost[] Hosts { get; set; }

        /// <summary>
        /// The strategy to use when executing server wide commands
        /// </summary>
        public ServerEnumerationStrategy ServerEnumerationStrategy { get; set; }


        /// <summary>
        /// A RemoteCertificateValidationCallback delegate responsible for validating the certificate supplied by the remote party; note
        /// that this cannot be specified in the configuration-string.
        /// </summary>
        public event RemoteCertificateValidationCallback CertificateValidation;

        public ConfigurationOptions ConfigurationOptions
        {
            get
            {
                if (options == null)
                {
                    options = new ConfigurationOptions
                    {
                        Ssl = Ssl,
                        AllowAdmin = AllowAdmin,
                        Password = Password,
                        ConnectTimeout = ConnectTimeout,
                        AbortOnConnectFail = AbortOnConnectFail,
                        ClientName=Name
                    };

                    foreach (var redisHost in Hosts)
                        options.EndPoints.Add((string)redisHost.Host, redisHost.Port);

                    options.CertificateValidation += CertificateValidation;
                }

                return options;
            }
        }

        public ConnectionMultiplexer Connection
        {
            get
            {
                if (connection == null)
                    connection = ConnectionMultiplexer.Connect(options);

                return connection;
            }
        }
        /// <summary>
        /// 转换成csredis所需的连接字符串
        /// </summary>
        public override string ToString()
        {

            string hostPorts = string.Empty;
            if (null != Hosts)
            {

                foreach (var host in Hosts)
                {

                    hostPorts += string.Format("{0}:{1},", host.Host, host.Port);

                }
            }

            return string.Format("{0}{1}defaultDatabase={2},poolsize={3},ssl={4},writeBuffer={5},prefix={6}{7}{8}{9}{10}", hostPorts,
                       !string.IsNullOrEmpty(this.Password) ? string.Format("password={0},", this.Password) : "",
                       this.Database < 0 ? 0 : (this.Database >= 16 ? 0 : this.Database),
                       this.PoolSize <= 0 ? 20 : this.PoolSize,
                       this.Ssl ? "true" : "false",
                       this.WriteBuffer,
                       string.IsNullOrEmpty(this.KeyPrefix) ? "" : this.KeyPrefix,
                        !string.IsNullOrEmpty(this.Name) ? string.Format(",name={0}", this.Name) : "",
                        this.PreHeat>0? string.Format(",preheat={0}", this.PreHeat) : "",
                          this.TryIt > 0 ? string.Format(",tryit={0}", this.TryIt) : "",
                            this.IdleTimeout > 0 ? string.Format(",idleTimeout={0}", this.PreHeat) : ""
                 );
         

        }


    }
}