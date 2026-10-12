using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AgentPlay.Contract;
using AgentPlay.Runner;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class RunnerBrainTests
    {
        readonly List<string> _printed = new List<string>();

        RunnerSession Session() => new RunnerSession(RunnerOptions.Parse(new[] { "--brain", "script", "win.json" }), _printed.Add);

        static Envelope Env<T>(string type, T body) => MessageCodec.Decode(MessageCodec.Encode(type, 1, body));

        static HelloMessage Hello(int protocol = ProtocolVersion.Current)
        {
            var h = new HelloMessage { Protocol = protocol, Game = "G", Version = "1" };
            h.Observes.Add(new ObserveInfo
            {
                Path = "guards",
                ValueType = ValueKinds.List,
                Fields = new List<ObserveInfo> { new ObserveInfo { Path = "distance", ValueType = ValueKinds.Float } },
            });
            h.Actions.Add(new ActionInfo { Name = "goto", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } } });
            h.Actions.Add(new ActionInfo { Name = "stop", Safe = true });
            h.Events.Add(new EventInfo { Name = ReservedEvents.GameWon, Outcome = "good" });
            return h;
        }

        static Plan GotoPlan(string id) => JObject.Parse(
            "{\"id\":\"" + id + "\",\"intent\":\"go\",\"steps\":[{\"do\":\"goto\",\"args\":{\"zone\":\"exit\"}}]}").ToObject<Plan>(ContractJson.Serializer);

        [Fact]
        public void BrainOptionForcesTurnBased()
        {
            var o = RunnerOptions.Parse(new[] { "--brain", "script", "win.json" });
            Assert.Equal("script", o.Brain);
            Assert.Equal("win.json", o.ScriptPath);
            Assert.Equal(Modes.TurnBased, o.Mode);
        }

        [Theory]
        [InlineData("--brain script", "--brain script needs a value")]
        [InlineData("--brain opus", "M2b")]
        [InlineData("--brain manual", "M2b")]
        [InlineData("--brain fancy", "Unknown brain")]
        [InlineData("--brain script f.json --mode realtime", "turn_based")]
        [InlineData("--brain script f.json --auto-resume", "--auto-resume")]
        public void InvalidBrainOptions(string commandLine, string expected)
        {
            var e = Assert.Throws<ArgumentException>(() => RunnerOptions.Parse(commandLine.Split(' ')));
            Assert.Contains(expected, e.Message);
        }

        [Fact]
        public void HelloPrintsListFieldsAndSetsTurnBased()
        {
            var s = Session();
            var outgoing = s.Handle(Env(MessageTypes.Hello, Hello()));
            Assert.Contains("  observe guards : list [distance:float]", _printed);
            Assert.Equal(MessageTypes.SetMode, Assert.Single(outgoing).Type);
        }

        [Fact]
        public void ProtocolMismatchFinishes()
        {
            var s = Session();
            s.Handle(Env(MessageTypes.Hello, Hello(protocol: 1)));
            Assert.True(s.Finished);
            Assert.Equal(RunnerSession.ExitProtocol, s.ExitCode);
        }

        [Fact]
        public void PausedRequestsADecisionWithTheState()
        {
            var s = Session();
            s.Handle(Env(MessageTypes.Hello, Hello()));
            var outgoing = s.Handle(Env(MessageTypes.Paused, new PausedMessage { Reason = "mode", T = 1, State = new JObject { ["x"] = 3 } }));
            Assert.Empty(outgoing);
            Assert.True(s.DecisionPending);
            var ctx = s.TakeDecisionContext();
            Assert.False(s.DecisionPending);
            Assert.Equal(3, (int)ctx.State["x"]);
            Assert.Equal("mode", ctx.PauseReason);
            Assert.Equal("G", ctx.Hello.Game);
        }

        [Fact]
        public void OnPlanSendsPlanThenResume()
        {
            var s = Session();
            s.Handle(Env(MessageTypes.Hello, Hello()));
            var outgoing = s.OnPlan(GotoPlan("p1"));
            Assert.Equal(new[] { MessageTypes.Plan, MessageTypes.Resume }, outgoing.Select(o => o.Type));
            Assert.Equal("p1", ((PlanMessage)outgoing[0].Body).Plan.Id);
            Assert.Equal(0.0, ((ResumeMessage)outgoing[1].Body).MaxGameSeconds);
            Assert.Contains("sent plan p1: go", _printed);
        }

        [Fact]
        public void InvalidPlanFinishesWithBrainStopped()
        {
            var s = Session();
            s.Handle(Env(MessageTypes.Hello, Hello()));
            var bad = JObject.Parse("{\"id\":\"b\",\"steps\":[{\"do\":\"fly\"}]}").ToObject<Plan>(ContractJson.Serializer);
            Assert.Empty(s.OnPlan(bad));
            Assert.True(s.Finished);
            Assert.Equal(RunnerSession.ExitBrainStopped, s.ExitCode);
            Assert.Contains(_printed, l => l.StartsWith("brain produced an invalid plan b:"));
        }

        [Fact]
        public void NoMorePlansFinishes()
        {
            var s = Session();
            s.Handle(Env(MessageTypes.Hello, Hello()));
            Assert.Empty(s.OnPlan(null));
            Assert.Equal(RunnerSession.ExitBrainStopped, s.ExitCode);
            Assert.Contains("brain has no more plans", _printed);
        }

        [Theory]
        [InlineData("game.won", "won", RunnerSession.ExitWon)]
        [InlineData("game.lost", "lost", RunnerSession.ExitLost)]
        public void GameEndThenPausedFinishes(string eventName, string result, int exitCode)
        {
            var s = Session();
            s.Handle(Env(MessageTypes.Hello, Hello()));
            s.Handle(Env(MessageTypes.Event, new EventMessage { Name = eventName, T = 5 }));
            s.Handle(Env(MessageTypes.Paused, new PausedMessage { Reason = "event", T = 5 }));
            Assert.True(s.Finished);
            Assert.False(s.DecisionPending);
            Assert.Equal(result, s.GameResult);
            Assert.Equal(exitCode, s.ExitCode);
            Assert.Contains("game over: " + result, _printed);
        }

        [Fact]
        public void RecentEventsAreCappedAndClearedAfterAPlan()
        {
            var s = Session();
            s.Handle(Env(MessageTypes.Hello, Hello()));
            for (int i = 0; i < RunnerSession.MaxRecentEvents + 5; i++)
                s.Handle(Env(MessageTypes.Event, new EventMessage { Name = "e" + i, T = i }));
            s.Handle(Env(MessageTypes.PlanStatus, new PlanStatusMessage { PlanId = "p0", Status = PlanStatuses.PlanDone, Step = 1 }));
            var ctx = s.TakeDecisionContext();
            Assert.Equal(RunnerSession.MaxRecentEvents, ctx.RecentEvents.Count);
            Assert.Equal("e5", ctx.RecentEvents[0].Name);
            Assert.Single(ctx.PlanStatuses);
            Assert.Contains("plan_status p0 plan_done step=1 reason=", _printed);
            s.OnPlan(GotoPlan("p1"));
            Assert.Empty(ctx.RecentEvents);
            Assert.Empty(ctx.PlanStatuses);
            Assert.Equal(1, ctx.PlansSent);
            Assert.Equal("p1", ctx.LastPlan.Id);
        }

        [Fact]
        public void ScriptBrainReturnsPlansInOrderThenNull()
        {
            var path = Path.Combine(Path.GetTempPath(), "agentplay-script-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{\"plans\":[{\"id\":\"a\",\"steps\":[{\"do\":\"stop\"}]},{\"id\":\"b\",\"steps\":[{\"do\":\"stop\"}]}]}");
            try
            {
                var brain = ScriptBrain.Load(path);
                Assert.Equal(2, brain.Remaining);
                Assert.Equal("a", brain.NextPlanAsync(new BrainContext(), CancellationToken.None).Result.Id);
                Assert.Equal("b", brain.NextPlanAsync(new BrainContext(), CancellationToken.None).Result.Id);
                Assert.Null(brain.NextPlanAsync(new BrainContext(), CancellationToken.None).Result);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ScriptBrainRejectsFilesWithoutPlans()
        {
            var path = Path.Combine(Path.GetTempPath(), "agentplay-script-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{\"steps\":[]}");
            try
            {
                Assert.Throws<InvalidDataException>(() => ScriptBrain.Load(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
