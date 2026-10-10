using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class MessagesTests
    {
        [Fact]
        public void HelloRoundTrips()
        {
            var hello = new HelloMessage
            {
                Protocol = ProtocolVersion.Current,
                Game = "G",
                Version = "v1",
            };
            hello.Observes.Add(new ObserveInfo { Path = "noise.value", ValueType = ValueKinds.Float, Description = "d" });
            hello.Actions.Add(new ActionInfo
            {
                Name = "goto",
                Safe = false,
                Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } },
            });
            hello.Modes.Add(Modes.TurnBased);

            var back = MessageCodec.Decode(MessageCodec.Encode(MessageTypes.Hello, 1, hello)).As<HelloMessage>();

            Assert.Equal(1, back.Protocol);
            Assert.Equal("G", back.Game);
            Assert.Equal("noise.value", back.Observes[0].Path);
            Assert.Equal("float", back.Observes[0].ValueType);
            Assert.Equal("zone", back.Actions[0].Params[0].Name);
            Assert.Equal(new[] { "turn_based" }, back.Modes);
        }

        [Fact]
        public void ResumeUsesSnakeCaseOnTheWire()
        {
            var line = MessageCodec.Encode(MessageTypes.Resume, 4, new ResumeMessage { MaxGameSeconds = 2.5 });
            Assert.Equal(2.5, (double)JObject.Parse(line)["max_game_seconds"]);
        }

        [Fact]
        public void EventWithNullPayloadRoundTrips()
        {
            var ev = new EventMessage { Name = "x", T = 1.5, Frame = 9, Important = true, Outcome = "bad", Payload = null, State = new JObject { ["a"] = 1 } };
            var back = MessageCodec.Decode(MessageCodec.Encode(MessageTypes.Event, 2, ev)).As<EventMessage>();
            Assert.Null(back.Payload);
            Assert.Equal(1, (int)back.State["a"]);
            Assert.True(back.Important);
            Assert.Equal(9L, back.Frame);
        }

        [Fact]
        public void ModesKnowsOnlyTwoModes()
        {
            Assert.True(Modes.IsKnown("turn_based"));
            Assert.True(Modes.IsKnown("realtime"));
            Assert.False(Modes.IsKnown("fast"));
            Assert.False(Modes.IsKnown(null));
        }

        [Fact]
        public void SequenceCounterStartsAtOneAndIsUniqueAcrossThreads()
        {
            var c = new SequenceCounter();
            Assert.Equal(1L, c.Next());
            var seen = new ConcurrentDictionary<long, bool>();
            Parallel.For(0, 1000, _ => Assert.True(seen.TryAdd(c.Next(), true)));
            Assert.Equal(1000, seen.Count);
        }
    }
}
