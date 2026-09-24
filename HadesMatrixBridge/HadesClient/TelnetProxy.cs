using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;

namespace HadesMatrixBridge.HadesClient
{
    /// <summary>
    /// Shares a Hades connection with any number of telnet clients.  Data received from Hades is sent to every
    /// connected client exactly as received.  Unless read-only, data received from clients is passed to Hades
    /// unaltered.  No authentication is performed.
    /// </summary>
    internal sealed class TelnetProxy
    {
        // Chunks of data queued for a client that isn't keeping up.  The client is disconnected once this is exceeded
        private const int MaxQueuedChunks = 1000;

        private readonly IPAddress _bindAddress;
        private readonly int _port;
        private readonly Action<byte[]> _sendToHades;
        private readonly ILogger _logger;

        private readonly ConcurrentDictionary<int, ProxyClient> _clients = new();
        private readonly CancellationTokenSource _stopping = new();
        private TcpListener? _listener;
        private Task? _acceptTask;
        private int _nextClientId;

        /// <param name="bindAddress">Address to listen on</param>
        /// <param name="port">Port to listen on (0 to pick a free port)</param>
        /// <param name="readOnly">If true, data received from clients is ignored</param>
        /// <param name="sendToHades">Called with data received from clients, when not read-only</param>
        public TelnetProxy(IPAddress bindAddress, int port, bool readOnly, Action<byte[]> sendToHades,
            ILogger? logger = null)
        {
            _bindAddress = bindAddress;
            _port = port;
            ReadOnly = readOnly;
            _sendToHades = sendToHades;
            _logger = logger ?? NullLogger.Instance;
        }

        public bool ReadOnly { get; }

        /// <summary>
        /// Port being listened on
        /// </summary>
        public int Port => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? _port;

        public int ClientCount => _clients.Count;

        /// <summary>
        /// Start listening for clients
        /// </summary>
        /// <exception cref="SocketException">The port couldn't be listened on</exception>
        public void Start()
        {
            _listener = new TcpListener(_bindAddress, _port);
            _listener.Start();

            _logger.LogInformation("Telnet proxy listening on {Address}:{Port} ({Mode})", _bindAddress, Port,
                ReadOnly ? "read-only" : "read/write");

            _acceptTask = AcceptClientsAsync(_stopping.Token);
        }

        /// <summary>
        /// Send data received from Hades to all connected clients
        /// </summary>
        public void Broadcast(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty || _clients.IsEmpty)
            {
                return;
            }

            var chunk = data.ToArray();
            foreach (var client in _clients.Values)
            {
                if (!client.Outgoing.Writer.TryWrite(chunk))
                {
                    _logger.LogWarning("Telnet proxy client {Remote} isn't keeping up, disconnecting", client.Remote);
                    client.Disconnect();
                }
            }
        }

        /// <summary>
        /// Stop listening and disconnect all clients
        /// </summary>
        public async Task StopAsync()
        {
            _logger.LogInformation("Stopping telnet proxy on port {Port}", Port);

            _stopping.Cancel();
            _listener?.Stop();

            foreach (var client in _clients.Values)
            {
                client.Disconnect();
            }

            if (_acceptTask is not null)
            {
                await _acceptTask;
            }
        }

        private async Task AcceptClientsAsync(CancellationToken stopping)
        {
            while (!stopping.IsCancellationRequested)
            {
                TcpClient tcpClient;
                try
                {
                    tcpClient = await _listener!.AcceptTcpClientAsync(stopping);
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException ||
                                           stopping.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    _logger.LogWarning("Telnet proxy failed to accept client: {Error}", ex.Message);
                    continue;
                }

                var id = Interlocked.Increment(ref _nextClientId);
                var client = new ProxyClient(tcpClient);
                _clients[id] = client;

                _logger.LogInformation("Telnet proxy client connected from {Remote} ({Mode})", client.Remote,
                    ReadOnly ? "read-only" : "read/write");

                _ = RunClientAsync(id, client);
            }
        }

        private async Task RunClientAsync(int id, ProxyClient client)
        {
            try
            {
                var writeLoop = WriteToClientAsync(client);
                var readLoop = ReadFromClientAsync(client);

                // When either side finishes (client disconnected, or dropped for not keeping up), close both
                await Task.WhenAny(writeLoop, readLoop);
                client.Disconnect();
                await Task.WhenAll(writeLoop, readLoop);
            }
            finally
            {
                _clients.TryRemove(id, out _);
                _logger.LogInformation("Telnet proxy client {Remote} disconnected", client.Remote);
            }
        }

        private static async Task WriteToClientAsync(ProxyClient client)
        {
            try
            {
                var stream = client.TcpClient.GetStream();
                await foreach (var chunk in client.Outgoing.Reader.ReadAllAsync())
                {
                    await stream.WriteAsync(chunk);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Client has gone
            }
        }

        private async Task ReadFromClientAsync(ProxyClient client)
        {
            var buffer = new byte[4096];
            try
            {
                var stream = client.TcpClient.GetStream();
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
                {
                    if (ReadOnly)
                    {
                        continue;
                    }

                    _sendToHades(buffer.AsSpan(0, bytesRead).ToArray());
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Client has gone
            }
        }

        private sealed class ProxyClient
        {
            private int _disconnected;

            public ProxyClient(TcpClient tcpClient)
            {
                TcpClient = tcpClient;
                Remote = tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";
                Outgoing = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(MaxQueuedChunks)
                {
                    SingleReader = true,
                    FullMode = BoundedChannelFullMode.Wait     // TryWrite fails when full
                });
            }

            public TcpClient TcpClient { get; }
            public string Remote { get; }
            public Channel<byte[]> Outgoing { get; }

            public void Disconnect()
            {
                if (Interlocked.Exchange(ref _disconnected, 1) == 0)
                {
                    Outgoing.Writer.TryComplete();
                    TcpClient.Close();
                }
            }
        }
    }
}
