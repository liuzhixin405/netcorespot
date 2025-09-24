using System;
using System.Collections.Generic;
using System.Linq;
using Common.Redis.Extensions.Configuration;
using StackExchange.Redis;

namespace Common.Redis.Extensions.ServerIteration
{
	public static class ServerIteratorFactory
	{
		public static IEnumerable<IServer> GetServers(
			ConnectionMultiplexer multiplexer,
			ServerEnumerationStrategy serverEnumerationStrategy)
		{
			switch (serverEnumerationStrategy.Mode)
			{
				case ServerEnumerationStrategy.ModeOptions.All:
					var serversAll = new ServerEnumerable(multiplexer,
						serverEnumerationStrategy.TargetRole,
						serverEnumerationStrategy.UnreachableServerAction);
					return serversAll;

				case ServerEnumerationStrategy.ModeOptions.Single:
					var serversSingle = new ServerEnumerable(multiplexer,
						serverEnumerationStrategy.TargetRole,
						serverEnumerationStrategy.UnreachableServerAction);
					return serversSingle.Take(1);

				default:
					throw new NotImplementedException();
			}
		}
	}
}
