﻿using Consul;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rabbit.Cloud.Discovery.Abstractions;
using Rabbit.Cloud.Extensions.Consul.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Rabbit.Cloud.Extensions.Consul.Discovery
{
    public class ConsulDiscoveryClient : ConsulService, IDiscoveryClient
    {
        public class InstanceEntry
        {
            public InstanceEntry()
            {
                Instances = new List<IServiceInstance>();
            }

            public ICollection<IServiceInstance> Instances { get; }
            public ulong Index { get; set; }
        }

        private readonly ConcurrentDictionary<string, ICollection<IServiceInstance>> _instances = new ConcurrentDictionary<string, ICollection<IServiceInstance>>(StringComparer.OrdinalIgnoreCase);

        #region Constructor

        public ConsulDiscoveryClient(IConsulClient consulClient, ILogger<ConsulDiscoveryClient> logger) : base(consulClient)
        {
            Task.Factory.StartNew(async () =>
            {
                var healthEndpoint = consulClient.Health;

                ulong index = 0;
                //watcher
                while (!Disposed)
                {
                    var result = await healthEndpoint.State(HealthStatus.Passing, new QueryOptions { WaitIndex = index });
                    var response = result.Response;

                    //timeout ignore
                    if (index == result.LastIndex)
                        continue;

                    // expired to delete
                    var expiredServices = response.GroupBy(i => i.ServiceName).Select(i => i.Key).ToArray();
                    if (logger.IsEnabled(LogLevel.Debug))
                        logger.LogDebug($"ready delete expired service info ,{string.Join(",", expiredServices)}");
                    foreach (var serviceName in expiredServices)
                    {
                        _instances.TryRemove(serviceName, out var _);
                    }

                    index = result.LastIndex;
                }
            });
        }

        public ConsulDiscoveryClient(IOptionsMonitor<RabbitConsulOptions> consulOptionsMonitor, ILogger<ConsulDiscoveryClient> logger)
            : this(consulOptionsMonitor.CurrentValue.CreateClient(), logger)
        {
        }

        #endregion Constructor

        #region Implementation of IDiscoveryClient

        public string Description => "Rabbit Cloud Consul Client";
        public IReadOnlyCollection<string> Services { get; private set; }

        public IReadOnlyCollection<IServiceInstance> GetInstances(string serviceId)
        {
            if (_instances.TryGetValue(serviceId, out var instances))
                return instances.ToArray();

            Task.Run(async () =>
            {
                instances = new List<IServiceInstance>();

                var result = await ConsulClient.Health.Service(serviceId, null, true);
                foreach (var instance in result.Response.Where(i => i.Checks.All(c => c.Status.Status == HealthStatus.Passing.Status)).Select(i => ConsulUtil.Create(i.Service)).Where(i => i != null))
                {
                    instances.Add(instance);
                }
            }).Wait();

            return instances.ToArray();
        }

        #endregion Implementation of IDiscoveryClient
    }
}