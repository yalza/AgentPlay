using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class MessageCodecTests
    {
        [Fact]
        public void EncodeProducesSingleLineWithTypeAndSeq()
        {
            var line = MessageCodec.Encode("hello", 7, new JObject { ["text"] = "a\nb" });
            Assert.DoesNotContain("\n", line);
            var o = JObject.Parse(line);
            Assert.Equal("hello", (string)o["type"]);
            Assert.Equal(7L, (long)o["seq"]);
            Assert.Equal("a\nb", (string)o["text"]);
        }

        [Fact]
        public void EncodeDoesNotMutateBody()
        {
            var body = new JObject { ["x"] = 1 };
            MessageCodec.Encode("t", 1, body);
            Assert.Null(body["type"]);
        }

        [Fact]
        public void EncodeAcceptsNullBody()
        {
            var env = MessageCodec.Decode(MessageCodec.Encode("get_state", 2, null));
            Assert.Equal("get_state", env.Type);
        }

        [Fact]
        public void DecodeKeepsIsoDatesAsStrings()
        {
            var env = MessageCodec.Decode("{\"type\":\"x\",\"seq\":1,\"at\":\"2026-10-11T01:02:03Z\"}");
            Assert.Equal(JTokenType.String, env.Body["at"].Type);
            Assert.Equal("2026-10-11T01:02:03Z", (string)env.Body["at"]);
        }

        [Fact]
        public void DecodeRoundTrips()
        {
            var env = MessageCodec.Decode(MessageCodec.Encode("event", 3, new JObject { ["name"] = "x" }));
            Assert.Equal("event", env.Type);
            Assert.Equal(3L, env.Seq);
            Assert.Equal("x", (string)env.Body["name"]);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[1,2]")]
        [InlineData("{\"seq\":1}")]
        [InlineData("{\"type\":5,\"seq\":1}")]
        [InlineData("{\"type\":\"x\"}")]
        [InlineData("{\"type\":\"x\",\"seq\":\"1\"}")]
        [InlineData("{\"type\":\"x\",\"seq\":1} {}")]
        [InlineData("{\"type\":\"x\",\"seq\":99999999999999999999}")]
        public void DecodeRejectsMalformed(string line)
        {
            Assert.Throws<ProtocolException>(() => MessageCodec.Decode(line));
        }
    }
}
