using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class ProtocolV4Tests
    {
        [Fact]
        public void AttentionRoundTrips()
        {
            var m = new AttentionMessage { Reason = "event", T = 3, State = new JObject { ["x"] = 1 }, TimeSplit = new TimeSplit { Safe = 1, Plan = 2, Reflex = 0.5 } };
            var back = MessageCodec.Decode(MessageCodec.Encode(MessageTypes.Attention, 1, m)).As<AttentionMessage>();
            Assert.Equal("event", back.Reason);
            Assert.Equal(0.5, back.TimeSplit.Reflex);
            Assert.Equal(1, (int)back.State["x"]);
            Assert.Equal(4, ProtocolVersion.Current);
        }

        [Fact]
        public void PausedCarriesTimeSplit()
        {
            var back = MessageCodec.Decode(MessageCodec.Encode(MessageTypes.Paused, 1,
                new PausedMessage { Reason = "plan", TimeSplit = new TimeSplit { Safe = 4 } })).As<PausedMessage>();
            Assert.Equal(4, back.TimeSplit.Safe);
        }
    }
}
