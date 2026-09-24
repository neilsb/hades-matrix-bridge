using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using HadesMatrixBridge.HadesClient;

namespace HadesMatrixBridge.Tests
{
    public class TelnetProxyTests : IAsyncLifetime
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        private readonly BlockingCollection<byte[]> _sentToHades = new();
        private TelnetProxy _proxy = null!;
        private readonly List<TcpClient> _clients = new();

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            foreach (var client in _clients)
            {
                client.Dispose();
            }

            await _proxy.StopAsync();
        }

        private void StartProxy(bool readOnly)
        {
            _proxy = new TelnetProxy(IPAddress.Loopback, 0, readOnly, data => _sentToHades.Add(data));
            _proxy.Start();
        }

        private async Task<NetworkStream> ConnectAsync(int expectedClients)
        {
            var client = new TcpClient();
            _clients.Add(client);
            await client.ConnectAsync(IPAddress.Loopback, _proxy.Port);
            await WaitUntil(() => _proxy.ClientCount == expectedClients);
            return client.GetStream();
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("Condition not met");
                }

                await Task.Delay(10);
            }
        }

        private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count)
        {
            var buffer = new byte[count];
            using var cts = new CancellationTokenSource(Timeout);
            await stream.ReadExactlyAsync(buffer, cts.Token);
            return buffer;
        }

        [Fact]
        public async Task Sends_Data_From_Hades_To_All_Clients_Exactly_As_Received()
        {
            StartProxy(readOnly: true);
            var first = await ConnectAsync(1);
            var second = await ConnectAsync(2);

            // Includes telnet negotiation (IAC WILL ECHO), ANSI colour codes, CR/LF and invalid UTF-8
            byte[] data = [0xFF, 0xFB, 0x01, 0x1B, (byte)'[', (byte)'1', (byte)'m', (byte)'H', (byte)'i', 0x0D, 0x0A, 0xEF, 0xBF, 0xBD, 0x05, 0x80];
            _proxy.Broadcast(data);
            _proxy.Broadcast("more"u8);

            byte[] expected = [.. data, .. "more"u8.ToArray()];
            Assert.Equal(expected, await ReadExactlyAsync(first, expected.Length));
            Assert.Equal(expected, await ReadExactlyAsync(second, expected.Length));
        }

        [Fact]
        public async Task Read_Only_Ignores_Client_Input()
        {
            StartProxy(readOnly: true);
            var client = await ConnectAsync(1);

            await client.WriteAsync("say hello\r\n"u8.ToArray());

            // Check the proxy is still working after the input, then that nothing was forwarded
            _proxy.Broadcast("ok"u8);
            Assert.Equal("ok"u8.ToArray(), await ReadExactlyAsync(client, 2));
            Assert.False(_sentToHades.TryTake(out _, TimeSpan.FromMilliseconds(200)));
        }

        [Fact]
        public async Task Read_Write_Passes_Client_Input_To_Hades_Unaltered()
        {
            StartProxy(readOnly: false);
            var client = await ConnectAsync(1);

            byte[] input = [(byte)'.', (byte)'s', (byte)'a', (byte)'y', (byte)' ', (byte)'h', (byte)'i', 0x0D, 0x0A, 0xFF, 0xFD, 0x03];
            await client.WriteAsync(input);

            var received = new List<byte>();
            while (received.Count < input.Length && _sentToHades.TryTake(out var chunk, Timeout))
            {
                received.AddRange(chunk);
            }

            Assert.Equal(input, received);
        }

        [Fact]
        public async Task Disconnecting_A_Client_Does_Not_Affect_Others()
        {
            StartProxy(readOnly: true);
            await ConnectAsync(1);
            var remaining = await ConnectAsync(2);

            _clients[0].Close();
            await WaitUntil(() => _proxy.ClientCount == 1);

            _proxy.Broadcast("still here"u8);
            Assert.Equal("still here"u8.ToArray(), await ReadExactlyAsync(remaining, 10));
        }

        [Fact]
        public async Task Stop_Disconnects_Clients_And_Frees_The_Port()
        {
            StartProxy(readOnly: true);
            var client = await ConnectAsync(1);
            var port = _proxy.Port;

            await _proxy.StopAsync();

            // Client sees the connection closed
            using var cts = new CancellationTokenSource(Timeout);
            Assert.Equal(0, await client.ReadAsync(new byte[1], cts.Token));

            // Port can be reused, e.g. when a puppet is restarted after being edited
            _proxy = new TelnetProxy(IPAddress.Loopback, port, true, _ => { });
            _proxy.Start();
            Assert.Equal(port, _proxy.Port);
        }
    }
}
