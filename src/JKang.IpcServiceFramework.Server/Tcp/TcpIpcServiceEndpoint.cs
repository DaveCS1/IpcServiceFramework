using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Net.Security;

namespace JKang.IpcServiceFramework.Tcp
{
    public class TcpIpcServiceEndpoint<TContract> : IpcServiceEndpoint<TContract>
        where TContract : class
    {
        private readonly ILogger<TcpIpcServiceEndpoint<TContract>> _logger;

        public int Port { get; private set; }

        public bool SSL { get; private set; }

        private readonly TcpListener _listener;
        private readonly Func<Stream, Stream> _streamTranslator;
        private readonly X509Certificate _serverCertificate;

        public TcpIpcServiceEndpoint(String name, IServiceProvider serviceProvider, IPAddress ipEndpoint, int port)
            : base(name, serviceProvider)
        {
            _listener = new TcpListener(ipEndpoint, port);
            _logger = serviceProvider.GetService<ILogger<TcpIpcServiceEndpoint<TContract>>>();
            Port = port;
        }

        public TcpIpcServiceEndpoint(String name, IServiceProvider serviceProvider, IPAddress ipEndpoint)
            : this(name, serviceProvider, ipEndpoint, 0)
        {
        }

        public TcpIpcServiceEndpoint(String name, IServiceProvider serviceProvider, IPAddress ipEndpoint, Func<Stream, Stream> streamTranslator)
            : this(name, serviceProvider, ipEndpoint, 0)
        {
            _streamTranslator = streamTranslator;
        }

        public TcpIpcServiceEndpoint(String name, IServiceProvider serviceProvider, IPAddress ipEndpoint, X509Certificate sslCertificate)
            : this(name, serviceProvider, ipEndpoint, 0)
        {
            _serverCertificate = sslCertificate;
            SSL = true;
        }

        public TcpIpcServiceEndpoint(String name, IServiceProvider serviceProvider, IPAddress ipEndpoint, X509Certificate sslCertificate, Func<Stream, Stream> streamTranslator)
            : this(name, serviceProvider, ipEndpoint, sslCertificate)
        {
            _streamTranslator = streamTranslator;
        }

        public TcpIpcServiceEndpoint(String name, IServiceProvider serviceProvider, IPAddress ipEndpoint, int port, Func<Stream, Stream> streamTranslator)
            : this(name, serviceProvider, ipEndpoint, port)
        {
            _streamTranslator = streamTranslator;
        }

        public TcpIpcServiceEndpoint(String name, IServiceProvider serviceProvider, IPAddress ipEndpoint, int port, X509Certificate sslCertificate)
            : this(name, serviceProvider, ipEndpoint, port)
        {
            _serverCertificate = sslCertificate;
            SSL = true;
        }

        public TcpIpcServiceEndpoint(String name, IServiceProvider serviceProvider, IPAddress ipEndpoint, int port, X509Certificate sslCertificate, Func<Stream, Stream> streamTranslator)
            : this(name, serviceProvider, ipEndpoint, port, sslCertificate)
        {
            _streamTranslator = streamTranslator;
        }

        public override Task ListenAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            _listener.Start();

            // If port is dynamically assigned, get the port number after start
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            cancellationToken.Register(() =>
            {
                _listener.Stop();
            });

            return Task.Run(async () =>
            {
                try
                {
                    _logger.LogDebug($"Endpoint '{Name}' listening on port {Port}...");
                    while (true)
                    {
                        TcpClient client = await _listener.AcceptTcpClientAsync();

                        Stream server = client.GetStream();

                        // if there's a stream translator, apply it here
                        if (_streamTranslator != null)
                        {
                            server = _streamTranslator(server);
                        }

                        // if SSL is enabled, wrap the stream in an SslStream in client mode
                        if (SSL)
                        {
                            var ssl = new SslStream(server, false);
                            ssl.AuthenticateAsServer(_serverCertificate);
                            server = ssl;
                        }

                        await ProcessAsync(server, _logger, cancellationToken);
                    }
                }
                catch when (cancellationToken.IsCancellationRequested)
                { }
            });
        }
    }
}