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

        public const int AttemptTimeoutMs = 300;

        /// <summary>
        /// Tries every port at once and keeps the lowest one that accepted; null when none does.
        /// Sequential connects are too slow on Windows, where a refused loopback connect takes about 2 s.
        /// </summary>
        public static async Task<BridgeClient> TryConnectAsync(string host, int firstPort, int lastPort, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(AttemptTimeoutMs);

            var clients = new List<TcpClient>();
            var attempts = new List<Task>();
            for (int port = firstPort; port <= lastPort; port++)
            {
                var client = new TcpClient();
                clients.Add(client);
                attempts.Add(client.ConnectAsync(host, port, timeout.Token).AsTask());
            }
            try
            {
                await Task.WhenAll(attempts);
            }
            catch (Exception)
            {
                // Refused or timed-out ports; each task's status is inspected below.
            }
            ct.ThrowIfCancellationRequested();

            BridgeClient chosen = null;
            for (int i = 0; i < clients.Count; i++)
            {
                if (chosen == null && attempts[i].Status == TaskStatus.RanToCompletion && clients[i].Connected)
                    chosen = new BridgeClient(clients[i], firstPort + i);
                else
                    clients[i].Dispose();
            }
            return chosen;
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
                IReadOnlyList<string> lines;
                try
                {
                    lines = framer.Push(buffer, 0, n);
                }
                catch (LineTooLongException)
                {
                    yield break;   // treat as a broken connection; the caller reconnects
                }
                foreach (var line in lines)
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
