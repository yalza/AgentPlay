using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using AgentPlay.Editor;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class BridgeServerTests
    {
        const int First = 47850, Last = 47859;
        BridgeServer _server;
        TcpClient _client;
        TcpListener _blocker;

        [TearDown]
        public void TearDown()
        {
            _client?.Close();
            _server?.Dispose();
            _blocker?.Stop();
        }

        static bool WaitFor(System.Func<bool> condition, int timeoutMs = 3000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                Thread.Sleep(10);
            }
            return condition();
        }

        void StartAndConnect()
        {
            _server = new BridgeServer();
            Assert.IsTrue(_server.Start(First, Last));
            _client = new TcpClient();
            _client.Connect(IPAddress.Loopback, _server.Port);
            BridgeSignal s = default;
            Assert.IsTrue(WaitFor(() => _server.TryGetSignal(out s)), "no Connected signal");
            Assert.AreEqual(BridgeSignal.Connected, s);
        }

        [Test]
        public void ExchangesLinesBothWays()
        {
            StartAndConnect();
            var bytes = Encoding.UTF8.GetBytes("{\"a\":1}\n{\"b\":");
            _client.GetStream().Write(bytes, 0, bytes.Length);
            string line = null;
            Assert.IsTrue(WaitFor(() => _server.TryReceive(out line)));
            Assert.AreEqual("{\"a\":1}", line);
            Assert.IsFalse(_server.TryReceive(out _), "partial line must wait for its newline");

            Assert.IsTrue(_server.TrySend("{\"c\":2}"));
            var reader = new StreamReader(_client.GetStream(), Encoding.UTF8);
            Assert.AreEqual("{\"c\":2}", reader.ReadLine());
        }

        [Test]
        public void ReportsDisconnectAndRefusesSendAfterward()
        {
            StartAndConnect();
            _client.Close();
            BridgeSignal s = default;
            Assert.IsTrue(WaitFor(() => _server.TryGetSignal(out s)));
            Assert.AreEqual(BridgeSignal.Disconnected, s);
            Assert.IsFalse(_server.IsClientConnected);
            Assert.IsFalse(_server.TrySend("x"));
        }

        [Test]
        public void SkipsPortThatIsAlreadyTaken()
        {
            _blocker = new TcpListener(IPAddress.Loopback, First);
            _blocker.Start();
            _server = new BridgeServer();
            Assert.IsTrue(_server.Start(First, Last));
            Assert.AreEqual(First + 1, _server.Port);
        }

        [Test]
        public void StopReleasesThePort()
        {
            StartAndConnect();
            int port = _server.Port;
            _server.Stop();
            var again = new TcpListener(IPAddress.Loopback, port);
            Assert.DoesNotThrow(() => again.Start());
            again.Stop();
        }

        [Test]
        public void ClientThatNeverReadsCannotFreezeTheSender()
        {
            StartAndConnect();
            _client.ReceiveBufferSize = 4096;
            string chunk = new string('x', 64 * 1024);
            var sw = Stopwatch.StartNew();
            bool failed = false;
            for (int i = 0; i < 4000 && !failed; i++)
                failed = !_server.TrySend(chunk);
            sw.Stop();
            Assert.IsTrue(failed, "sending to a stalled client must eventually fail");
            Assert.Less(sw.ElapsedMilliseconds, 10000);
            Assert.IsFalse(_server.IsClientConnected);
        }
    }
}
