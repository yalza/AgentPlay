using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgentPlay.Runner;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class BridgeClientTests
    {
        [Fact]
        public async Task ScanningClosedPortsIsFast()
        {
            var sw = Stopwatch.StartNew();
            var client = await BridgeClient.TryConnectAsync("127.0.0.1", 47870, 47879, CancellationToken.None);
            sw.Stop();
            Assert.Null(client);
            Assert.True(sw.ElapsedMilliseconds < 1500, "scan took " + sw.ElapsedMilliseconds + " ms");
        }

        [Fact]
        public async Task PicksTheLowestListeningPort()
        {
            var a = new TcpListener(IPAddress.Loopback, 47884);
            var b = new TcpListener(IPAddress.Loopback, 47882);
            a.Start();
            b.Start();
            try
            {
                using var client = await BridgeClient.TryConnectAsync("127.0.0.1", 47880, 47885, CancellationToken.None);
                Assert.NotNull(client);
                Assert.Equal(47882, client.Port);
            }
            finally
            {
                a.Stop();
                b.Stop();
            }
        }

        [Fact]
        public async Task OversizedLineEndsTheReadInsteadOfThrowing()
        {
            var listener = new TcpListener(IPAddress.Loopback, 47888);
            listener.Start();
            try
            {
                var serverSide = listener.AcceptTcpClientAsync();
                using var client = await BridgeClient.TryConnectAsync("127.0.0.1", 47888, 47888, CancellationToken.None);
                using var server = await serverSide;
                var junk = Encoding.UTF8.GetBytes(new string('x', 2 * 1024 * 1024));
                _ = server.GetStream().WriteAsync(junk, 0, junk.Length);

                int lines = 0;
                await foreach (var _ in client.ReadLinesAsync(CancellationToken.None)) lines++;
                Assert.Equal(0, lines);
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
