﻿using Microsoft.Extensions.Logging;
using NetMQ;
using NetMQ.Sockets;
using RabbitCloud.Abstractions.Logging;
using System;
using System.Collections.Concurrent;
using System.Net;

namespace RabbitCloud.Rpc.NetMQ.Internal
{
    public interface IRouterSocketFactory : IDisposable
    {
        RouterSocket OpenSocket<T>(string protocol, IPEndPoint ipEndPoint, Action<T> handler) where T : NetMQSocket;
    }

    public class RouterSocketFactory : IRouterSocketFactory
    {
        private readonly NetMqPollerHolder _netMqPollerHolder;
        private readonly ILogger<RouterSocketFactory> _logger;

        #region Field

        private readonly ConcurrentDictionary<string, Lazy<RouterSocket>> _routerSockets = new ConcurrentDictionary<string, Lazy<RouterSocket>>(StringComparer.OrdinalIgnoreCase);

        #endregion Field

        #region Constructor

        public RouterSocketFactory(NetMqPollerHolder netMqPollerHolder, ILogger<RouterSocketFactory> logger = null)
        {
            _netMqPollerHolder = netMqPollerHolder;
            _logger = logger ?? NullLogger<RouterSocketFactory>.Instance;
        }

        #endregion Constructor

        #region Implementation of IResponseSocketFactory

        public RouterSocket OpenSocket<T>(string protocol, IPEndPoint ipEndPoint, Action<T> handler) where T : NetMQSocket
        {
            var address = $"{protocol}://{ipEndPoint.Address}:{ipEndPoint.Port}";

            return _routerSockets
                .GetOrAdd(address, k => new Lazy<RouterSocket>(() =>
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug($"create ResponseSocket bind to '{address}'");

                    var routerSocket = new RouterSocket();
                    routerSocket.Bind(address);
                    routerSocket.ReceiveReady += (sender, args) => handler(args.Socket as T);
                    _netMqPollerHolder.GetPoller().Add(routerSocket);
                    return routerSocket;
                }))
                .Value;
        }

        #endregion Implementation of IResponseSocketFactory

        #region IDisposable

        public void Dispose()
        {
            foreach (var value in _routerSockets.Values)
            {
                try
                {
                    value.Value.Dispose();
                }
                catch (Exception exception)
                {
                    _logger.LogError(0, exception, $"Dispose '{value.Value.Options.LastEndpoint}' throw exception.");
                }
            }
            _routerSockets.Clear();
        }

        #endregion IDisposable
    }

    public static class RouterSocketFactoryExtensions
    {
        public static RouterSocket OpenSocket<T>(this IRouterSocketFactory factory, IPEndPoint ipEndPoint, Action<T> handler) where T : NetMQSocket
        {
            return factory.OpenSocket("tcp", ipEndPoint, handler);
        }

        public static RouterSocket OpenSocket<T>(this IRouterSocketFactory factory, string ip, int port, Action<T> handler) where T : NetMQSocket
        {
            return factory.OpenSocket("tcp", new IPEndPoint(IPAddress.Parse(ip), port), handler);
        }
    }
}