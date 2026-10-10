using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using AgentPlay.Contract;

namespace AgentPlay.Editor
{
    public enum BridgeSignal { Connected, Disconnected }

    /// <summary>
    /// Loopback TCP server for one runner at a time. A background thread accepts and reads;
    /// the main thread drains TryReceive/TryGetSignal and calls TrySend.
    /// </summary>
    public sealed class BridgeServer : IDisposable
    {
        public const int FirstPort = 47800;
        public const int LastPort = 47809;
        public const int SendTimeoutMs = 2000;

        readonly ConcurrentQueue<string> _inbound = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<BridgeSignal> _signals = new ConcurrentQueue<BridgeSignal>();
        readonly object _clientLock = new object();

        TcpListener _listener;
        Thread _thread;
        TcpClient _client;
        NetworkStream _stream;
        volatile bool _running;

        public int Port { get; private set; }

        public bool IsClientConnected
        {
            get { lock (_clientLock) return _stream != null; }
        }

        public bool Start(int firstPort = FirstPort, int lastPort = LastPort)
        {
            for (int port = firstPort; port <= lastPort; port++)
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                try
                {
                    listener.Start();
                }
                catch (SocketException)
                {
                    continue;
                }
                _listener = listener;
                Port = port;
                _running = true;
                _thread = new Thread(Serve) { IsBackground = true, Name = "AgentPlay.Bridge" };
                _thread.Start();
                return true;
            }
            return false;
        }

        public bool TryReceive(out string line) => _inbound.TryDequeue(out line);

        public bool TryGetSignal(out BridgeSignal signal) => _signals.TryDequeue(out signal);

        public bool TrySend(string line)
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            lock (_clientLock)
            {
                if (_stream == null) return false;
                try
                {
                    _stream.Write(bytes, 0, bytes.Length);
                    return true;
                }
                catch (Exception)
                {
                    // Timeout or broken pipe: drop the client so the main thread is never blocked again.
                    CloseClientLocked();
                    return false;
                }
            }
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch (Exception) { }
            lock (_clientLock) CloseClientLocked();
            if (_thread != null && _thread != Thread.CurrentThread) _thread.Join(1000);
            _thread = null;
            _listener = null;
        }

        public void Dispose() => Stop();

        void Serve()
        {
            var buffer = new byte[8192];
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    return;   // listener stopped
                }

                client.NoDelay = true;
                client.SendTimeout = SendTimeoutMs;
                NetworkStream stream;
                lock (_clientLock)
                {
                    _client = client;
                    _stream = stream = client.GetStream();
                }
                _signals.Enqueue(BridgeSignal.Connected);

                var framer = new LineFramer();
                try
                {
                    int n;
                    while (_running && (n = stream.Read(buffer, 0, buffer.Length)) > 0)
                        foreach (var line in framer.Push(buffer, 0, n))
                            _inbound.Enqueue(line);
                }
                catch (Exception)
                {
                    // Closed by Stop/TrySend, reset by the runner, or a line over the size limit.
                }

                lock (_clientLock)
                {
                    if (_client == client) CloseClientLocked();
                }
                _signals.Enqueue(BridgeSignal.Disconnected);
            }
        }

        void CloseClientLocked()
        {
            try { _client?.Close(); } catch (Exception) { }
            _client = null;
            _stream = null;
        }
    }
}
