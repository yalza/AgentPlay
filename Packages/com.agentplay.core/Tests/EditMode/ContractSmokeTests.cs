using AgentPlay.Contract;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class ContractSmokeTests
    {
        [Test]
        public void ContractCompilesAndRoundTripsInUnity()
        {
            var line = MessageCodec.Encode(MessageTypes.Resume, 5, new ResumeMessage { MaxGameSeconds = 1.25 });
            var env = MessageCodec.Decode(line);
            Assert.AreEqual(MessageTypes.Resume, env.Type);
            Assert.AreEqual(1.25, env.As<ResumeMessage>().MaxGameSeconds, 1e-9);
        }
    }
}
