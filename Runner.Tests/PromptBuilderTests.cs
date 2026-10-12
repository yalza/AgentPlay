using System.Collections.Generic;
using AgentPlay.Contract;
using AgentPlay.Runner;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class PromptBuilderTests
    {
        static HelloMessage Hello()
        {
            var h = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "Demo", Version = "3", Brief = "Steal the gem and leave." };
            h.Observes.Add(new ObserveInfo { Path = "player.zone", ValueType = ValueKinds.String, Description = "Zone the player stands in" });
            h.Observes.Add(new ObserveInfo
            {
                Path = "guards",
                ValueType = ValueKinds.List,
                Description = "Guards",
                Fields = new List<ObserveInfo> { new ObserveInfo { Path = "distance", ValueType = ValueKinds.Float, Description = "Metres to the player" } },
            });
            h.Actions.Add(new ActionInfo { Name = "goto", Description = "Walk to a zone", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } } });
            h.Actions.Add(new ActionInfo { Name = "stop", Description = "Stand still", Safe = true });
            h.Events.Add(new EventInfo { Name = "guard.spotted", Description = "A guard sees you", Important = true, Outcome = "bad" });
            return h;
        }

        [Fact]
        public void SystemDescribesTheGameAndTheDsl()
        {
            var s = PromptBuilder.BuildSystem(Hello());
            Assert.Contains("Steal the gem and leave.", s);
            Assert.Contains("Demo", s);
            Assert.Contains("- player.zone (string): Zone the player stands in", s);
            Assert.Contains("- guards (list): Guards", s);
            Assert.Contains("  - distance (float): Metres to the player", s);
            Assert.Contains("- goto(zone: string): Walk to a zone", s);
            Assert.Contains("- stop(): Stand still [runs whenever there is no plan]", s);
            Assert.Contains("- guard.spotted: A guard sees you [important] [bad]", s);
            Assert.Contains("event:", s);
            Assert.Contains("min(", s);
        }

        [Fact]
        public void SystemIsIdenticalForTheSameGame()
        {
            Assert.Equal(PromptBuilder.BuildSystem(Hello()), PromptBuilder.BuildSystem(Hello()));
        }

        [Fact]
        public void TurnCarriesStateEventsLastPlanAndHistory()
        {
            var ctx = new BrainContext
            {
                Hello = Hello(),
                State = new JObject { ["player.zone"] = "start" },
                PauseReason = "plan",
                LastPlan = new Plan { Id = "p2", Intent = "grab the gem" },
            };
            ctx.RecentEvents.Add(new EventMessage { Name = "guard.spotted", T = 4.5, Payload = new JObject { ["guard"] = "g1" } });
            ctx.PlanStatuses.Add(new PlanStatusMessage { PlanId = "p2", Status = PlanStatuses.PlanFailed, Step = 1, Reason = "replan:0" });
            for (int i = 0; i < 7; i++) ctx.History.Add("h" + i + ": old -> plan_done");

            var t = PromptBuilder.BuildTurn(ctx);
            Assert.Contains("Paused because: plan", t);
            Assert.Contains("\"player.zone\": \"start\"", t);
            Assert.Contains("t=4.50 guard.spotted {\"guard\":\"g1\"}", t);
            Assert.Contains("p2 (grab the gem)", t);
            Assert.Contains("plan_failed at step 1: replan:0", t);
            Assert.DoesNotContain("h1: old", t);
            Assert.Contains("h2: old -> plan_done", t);
            Assert.Contains("h6: old -> plan_done", t);
        }

        [Fact]
        public void FirstTurnSaysThereIsNoPreviousPlan()
        {
            var t = PromptBuilder.BuildTurn(new BrainContext { Hello = Hello(), State = new JObject(), PauseReason = "mode" });
            Assert.Contains("No plan yet", t);
            Assert.Contains("(none)", t);
        }

        [Fact]
        public void SessionRecordsHistoryWhenAPlanEnds()
        {
            var printed = new List<string>();
            var s = new RunnerSession(RunnerOptions.Parse(new[] { "--brain", "script", "x.json" }), printed.Add);
            s.Handle(MessageCodec.Decode(MessageCodec.Encode(MessageTypes.Hello, 1, Hello())));
            var plan = JObject.Parse("{\"id\":\"p1\",\"intent\":\"go\",\"steps\":[{\"do\":\"goto\",\"args\":{\"zone\":\"a\"}}]}").ToObject<Plan>(ContractJson.Serializer);
            s.OnPlan(plan);
            s.Handle(MessageCodec.Decode(MessageCodec.Encode(MessageTypes.PlanStatus, 2,
                new PlanStatusMessage { PlanId = "p1", Status = PlanStatuses.PlanFailed, Step = 0, Reason = "timeout" })));
            Assert.Equal(new[] { "p1: go -> plan_failed (timeout)" }, s.TakeDecisionContext().History);
        }
    }
}
