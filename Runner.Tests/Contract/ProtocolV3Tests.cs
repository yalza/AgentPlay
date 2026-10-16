using AgentPlay.Contract;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class ProtocolV3Tests
    {
        [Fact]
        public void HelloCarriesSessionTagAndReflexes()
        {
            var hello = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G", SessionTag = "play" };
            hello.Reflexes.Add(new ReflexInfo { Id = "R-L1", When = "event:x", Do = "hide" });
            string line = MessageCodec.Encode(MessageTypes.Hello, 1, hello);
            Assert.Contains("\"session_tag\":\"play\"", line);
            var back = MessageCodec.Decode(line).As<HelloMessage>();
            Assert.Equal(ProtocolVersion.Current, back.Protocol);
            Assert.Equal("R-L1", back.Reflexes[0].Id);
        }

        [Fact]
        public void HelloDefaultsToDebugTag() => Assert.Equal("debug", new HelloMessage().SessionTag);

        [Fact]
        public void ReflexMessagesRoundTrip()
        {
            var msg = new ReflexMessage { Id = "R-L1", T = 3.5, Frame = 40 };
            msg.HandledEvents.Add("guard.spotted");
            var back = MessageCodec.Decode(MessageCodec.Encode(MessageTypes.Reflex, 2, msg)).As<ReflexMessage>();
            Assert.Equal(new[] { "guard.spotted" }, back.HandledEvents);
            var end = MessageCodec.Decode(MessageCodec.Encode(MessageTypes.ReflexEnd, 3, new ReflexEndMessage { Id = "R-L1", T = 4, Status = "done" })).As<ReflexEndMessage>();
            Assert.Equal("done", end.Status);
        }
    }
}
