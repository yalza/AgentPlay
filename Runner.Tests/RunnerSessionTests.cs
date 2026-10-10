using System.Collections.Generic;
using AgentPlay.Contract;
using AgentPlay.Runner;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class RunnerSessionTests
    {
        readonly List<string> _printed = new List<string>();

        RunnerSession Session(params string[] args) => new RunnerSession(RunnerOptions.Parse(args), _printed.Add);

        static Envelope Env<T>(string type, T body) => MessageCodec.Decode(MessageCodec.Encode(type, 1, body));

        static HelloMessage Hello()
        {
            var h = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "AgentPlay Testbed", Version = "m1" };
            h.Observes.Add(new ObserveInfo { Path = "walker.x", ValueType = "float" });
            h.Actions.Add(new ActionInfo { Name = "wait", Safe = true });
            h.Actions.Add(new ActionInfo { Name = "goto", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = "string" } } });
            return h;
        }

        [Fact]
        public void HelloIsPrintedAndTriggersSetModeWhenRequested()
        {
            var s = Session("--mode", "turn_based");
            var outgoing = s.Handle(Env(MessageTypes.Hello, Hello()));

            Assert.True(s.HasHello);
            Assert.Contains("hello: game=AgentPlay Testbed version=m1 observes=1 actions=2", _printed);
            Assert.Contains("  observe walker.x : float", _printed);
            Assert.Contains("  action wait() [safe]", _printed);
            Assert.Contains("  action goto(zone:string)", _printed);
            var msg = Assert.Single(outgoing);
            Assert.Equal(MessageTypes.SetMode, msg.Type);
            Assert.Equal("turn_based", ((SetModeMessage)msg.Body).Mode);
        }

        [Fact]
        public void HelloWithoutModeSendsNothing()
        {
            Assert.Empty(Session().Handle(Env(MessageTypes.Hello, Hello())));
        }

        [Fact]
        public void PausedTriggersDelayedResumeWhenAutoResumeIsOn()
        {
            var s = Session("--auto-resume", "--resume-seconds", "2", "--think-ms", "3000");
            var outgoing = s.Handle(Env(MessageTypes.Paused, new PausedMessage { Reason = "budget", T = 12.346 }));

            Assert.Contains("paused t=12.35 reason=budget", _printed);
            var msg = Assert.Single(outgoing);
            Assert.Equal(MessageTypes.Resume, msg.Type);
            Assert.Equal(2.0, ((ResumeMessage)msg.Body).MaxGameSeconds);
            Assert.Equal(3000, msg.DelayMs);
        }

        [Fact]
        public void PausedWithoutAutoResumeOnlyPrints()
        {
            Assert.Empty(Session().Handle(Env(MessageTypes.Paused, new PausedMessage { Reason = "event", T = 1 })));
        }

        [Fact]
        public void EventStateAndErrorArePrinted()
        {
            var s = Session();
            s.Handle(Env(MessageTypes.Event, new EventMessage { Name = "walker.turned", T = 2.5, Frame = 150, Important = true, Payload = new JObject { ["laps"] = 1 } }));
            s.Handle(Env(MessageTypes.State, new StateMessage { T = 3, State = new JObject { ["walker.x"] = 1.5 } }));
            s.Handle(Env(MessageTypes.Error, new ErrorMessage { Message = "Unknown mode 'fast'" }));

            Assert.Contains("event t=2.50 frame=150 walker.turned (important) payload={\"laps\":1}", _printed);
            Assert.Contains("state t=3.00 {\"walker.x\":1.5}", _printed);
            Assert.Contains("unity error: Unknown mode 'fast'", _printed);
        }

        [Fact]
        public void MistypedFieldsArePrintedInsteadOfThrown()
        {
            var env = MessageCodec.Decode("{\"type\":\"event\",\"seq\":1,\"t\":\"abc\"}");
            Assert.Empty(Session().Handle(env));
            Assert.Contains(_printed, line => line.StartsWith("bad 'event' message from Unity:"));
        }

        [Fact]
        public void ResetForgetsHello()
        {
            var s = Session();
            s.Handle(Env(MessageTypes.Hello, Hello()));
            s.Reset();
            Assert.False(s.HasHello);
            Assert.Null(s.Hello);
        }
    }
}
