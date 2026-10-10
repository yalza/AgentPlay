using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgentPlay.Contract;

namespace AgentPlay.Runner
{
    public sealed class BridgeClient : IDisposable
    {
        readonly TcpClient _client;
        readonly NetworkStream _stream;
        readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        public int Port { get; }

        BridgeClient(TcpClient client, int port)
        {
            _client = client;
            _client.NoDelay = true;
            _stream = client.GetStream();
            Port = port;
        }

        /// <summary>Connects to the first port in the range that accepts; null when none does.</summary>
        public static async Task<BridgeClient> TryConnectAsync(string host, int firstPort, int lastPort, CancellationToken ct)
        {
            for (int port = firstPort; port <= lastPort; port++)
            {
                var client = new TcpClient();
                try
                {
                    await client.ConnectAsync(host, port, ct);
                    return new BridgeClient(client, port);
                }
                catch (SocketException)
                {
                    client.Dispose();
                }
            }
            return null;
        }

        public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken ct)
        {
            var framer = new LineFramer();
            var buffer = new byte[8192];
            while (true)
            {
                int n = 0;
                bool broken = false;
                try
                {
                    n = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                }
                catch (IOException)
                {
                    broken = true;
                }
                catch (ObjectDisposedException)
                {
                    broken = true;
                }
                if (broken || n == 0) yield break;
                foreach (var line in framer.Push(buffer, 0, n))
                    yield return line;
            }
        }

        public async Task SendAsync(string line, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            await _writeLock.WaitAsync(ct);
            try
            {
                await _stream.WriteAsync(bytes.AsMemory(0, bytes.Length), ct);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Dispose()
        {
            _client.Dispose();
            _writeLock.Dispose();
        }
    }
}
