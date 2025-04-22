using HadesMatrixBridge.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HadesMatrixBridge.HadesClient
{
    internal class TelnetRelay
    {
        private StreamReader _reader;
        private StreamWriter _writer;
        private readonly TelnetConfig _telnetConfig;
        private readonly HadesConfig _hadesConfig;
        private readonly ILogger _logger;
        private TcpListener _listener;
        private readonly IList<TcpClient> _clients = new List<TcpClient>();

        public event EventHandler<String> Message;

        public TelnetRelay(IOptions<TelnetConfig> telnetConfig = null, IOptions<HadesConfig> hadesConfig = null, ILogger<TelnetRelay> logger = null)
        {
            _telnetConfig = telnetConfig?.Value ?? new TelnetConfig();
            _hadesConfig = hadesConfig?.Value ?? new HadesConfig();
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TelnetRelay>.Instance;
        }


        public async Task Start()
        {
            int port = _telnetConfig.Port; // Use configured port
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _logger.LogInformation("Telnet server started on port {Port}", port);

            while (true)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync();
                _logger.LogInformation("Telnet client connected");
                _clients.Add(client);
                _ = HandleClient(client); // Handle each client in a separate task
            }
        }

        public async Task Write(string data)
        {
            if (_writer == null)
                return;

            await _writer.WriteAsync(data);
        }

        async Task HandleClient(TcpClient client)
        {
            using NetworkStream stream = client.GetStream();
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            using StreamReader reader = new StreamReader(stream, Encoding.ASCII);
            using StreamWriter writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

            _reader = reader;
            _writer = writer;

            var hadesUsername = _hadesConfig.DefaultUsername; // Use configured username
            await writer.WriteLineAsync("+---");
            await writer.WriteLineAsync($"| You are connected to Hades as {hadesUsername}");
            await writer.WriteLineAsync("| Any data sent via this connection will be forwarded to Hades as this user");
            await writer.WriteLineAsync("+----------------------------------------------------------------------------");

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                this.Message?.Invoke(this, line);
            }

            _logger.LogInformation("Telnet client disconnected");
            client.Close();
        }

        internal void Stop()
        {
            _logger.LogInformation("Stopping Telnet server");
            
            // Disconnect each client
            foreach (var client in _clients)
            {
                client.Close();
            }
            
            _listener.Stop();
        }
    }
}
