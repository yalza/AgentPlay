using System.Collections.Generic;
using System.Linq;
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

        static Plan TwoStepPlan(string id) => new Plan
        {
            Id = id,
            Steps = new List<PlanStep> { new PlanStep { Do = "wait" }, new PlanStep { Do = "wait" } },
            TimeoutSeconds = 10,
        };

        [Fact]
        public void AttentionOpensADecisionWithoutResume()
        {
            var s = Session("--brain", "script", "x.json", "--mode", "realtime");
            Assert.True(s.Realtime);
            s.Handle(Env(MessageTypes.Hello, Hello()));
            s.Handle(Env(MessageTypes.Attention, new AttentionMessage { Reason = "mode", T = 1, State = new JObject() }));
            Assert.True(s.DecisionPending);
            Assert.Contains("attention t=1.00 reason=mode", _printed);
            var ctx = s.TakeDecisionContext();
            Assert.Equal("mode", ctx.PauseReason);
            var outgoing = s.OnPlan(TwoStepPlan("p1"));
            Assert.Equal(new[] { MessageTypes.Plan }, outgoing.Select(o => o.Type));
        }

        [Fact]
        public void QueuedDecisionCatchesUpWithThePlanSentMeanwhile()
        {
            var s = Session("--brain", "script", "x.json", "--mode", "realtime");
            s.Handle(Env(MessageTypes.Hello, Hello()));
            s.Handle(Env(MessageTypes.Attention, new AttentionMessage { Reason = "mode", T = 1, State = new JObject() }));
            var first = s.TakeDecisionContext().Clone();
            // An important event while the first call runs: its decision is copied now and starts later.
            s.Handle(Env(MessageTypes.Attention, new AttentionMessage { Reason = "event", T = 2, State = new JObject() }));
            var queued = s.TakeDecisionContext().Clone();
            var router = new AgentPlay.Runner.Brains.ModelRouter(new RunnerConfig { Model = "claude-opus-5-5", Effort = "high" });
            s.PrepareCall(first, router);
            s.OnPlan(TwoStepPlan("p1"));

            s.PrepareCall(queued, router);
            Assert.Equal(1, queued.PlansSent);           // its plan becomes p2, not a second p1
            Assert.Equal("p1", queued.LastPlan.Id);
            Assert.NotSame(s.PeekContext().History, queued.History);
            Assert.Equal("strategy", queued.Route.Role);  // no plan was running when the event came
        }

        [Fact]
        public void PrepareCallWithoutARouterLeavesTheRouteEmpty()
        {
            var s = Session("--brain", "script", "x.json", "--mode", "realtime");
            s.Handle(Env(MessageTypes.Hello, Hello()));
            s.Handle(Env(MessageTypes.Attention, new AttentionMessage { Reason = "mode", T = 1, State = new JObject() }));
            var ctx = s.TakeDecisionContext().Clone();
            s.PrepareCall(ctx, null);
            Assert.Null(ctx.Route);
        }

        [Fact]
        public void AttentionAfterGameOverFinishes()
        {
            var s = Session("--brain", "script", "x.json", "--mode", "realtime");
            s.Handle(Env(MessageTypes.Hello, Hello()));
            s.Handle(Env(MessageTypes.Event, new EventMessage { Name = "game.won", Important = true }));
            s.Handle(Env(MessageTypes.Attention, new AttentionMessage { Reason = "event", T = 5 }));
            Assert.True(s.Finished);
            Assert.False(s.DecisionPending);
            Assert.Equal(0, s.ExitCode);
        }

        [Fact]
        public void LastStepOpensAPrefetchOpportunityAndSurprisesInvalidate()
        {
            var s = Session("--brain", "script", "x.json", "--mode", "realtime");
            s.Handle(Env(MessageTypes.Hello, Hello()));
            s.Handle(Env(MessageTypes.Attention, new AttentionMessage { Reason = "mode", T = 1 }));
            s.TakeDecisionContext();
            s.OnPlan(TwoStepPlan("p1"));
            Assert.Equal("p1", s.TakePrefetchOpportunity());   // look ahead as soon as a plan starts
            Assert.Null(s.TakePrefetchOpportunity());
            s.Handle(Env(MessageTypes.PlanStatus, new PlanStatusMessage { PlanId = "p1", Status = PlanStatuses.StepDone, Step = 0 }));
            Assert.Equal("p1", s.TakePrefetchOpportunity());
            Assert.Null(s.TakePrefetchOpportunity());
            Assert.Equal("p1", s.PeekContext().LastPlan.Id);
            Assert.False(s.TakeInvalidated());
            s.Handle(Env(MessageTypes.Attention, new AttentionMessage { Reason = "event", T = 2 }));
            Assert.True(s.TakeInvalidated());
            Assert.False(s.TakeInvalidated());
        }

        [Fact]
        public void EndedPlanIsTracked()
        {
            var s = Session("--brain", "script", "x.json", "--mode", "realtime");
            s.Handle(Env(MessageTypes.Hello, Hello()));
            s.Handle(Env(MessageTypes.PlanStatus, new PlanStatusMessage { PlanId = "p1", Status = PlanStatuses.PlanDone, Step = 2 }));
            Assert.Equal("p1", s.LastEndedPlanId);
            Assert.True(s.LastEndedDone);
            s.Handle(Env(MessageTypes.PlanStatus, new PlanStatusMessage { PlanId = "p2", Status = PlanStatuses.PlanFailed, Step = 0, Reason = "timeout" }));
            Assert.Equal("p2", s.LastEndedPlanId);
            Assert.False(s.LastEndedDone);
            Assert.True(s.TakeInvalidated(), "a failed plan invalidates any prefetch");
        }

        [Fact]
        public void LatestStateFollowsEveryMessageWithState()
        {
            var s = Session("--brain", "script", "x.json", "--mode", "realtime");
            s.Handle(Env(MessageTypes.Hello, Hello()));
            s.Handle(Env(MessageTypes.Attention, new AttentionMessage { Reason = "mode", T = 1, State = new JObject { ["x"] = 1 } }));
            s.Handle(Env(MessageTypes.Event, new EventMessage { Name = "e", T = 2, State = new JObject { ["x"] = 2 } }));
            Assert.Equal(2, (int)s.LatestState["x"]);
            Assert.Equal(2, s.LatestT);
            s.Handle(Env(MessageTypes.State, new StateMessage { T = 3, State = new JObject { ["x"] = 3 } }));
            Assert.Equal(3, (int)s.LatestState["x"]);
        }

        [Fact]
        public void TurnBasedStillResumes()
        {
            var s = Session("--brain", "script", "x.json");
            Assert.False(s.Realtime);
            s.Handle(Env(MessageTypes.Hello, Hello()));
            s.Handle(Env(MessageTypes.Paused, new PausedMessage { Reason = "mode", T = 1 }));
            s.TakeDecisionContext();
            Assert.Equal(new[] { MessageTypes.Plan, MessageTypes.Resume }, s.OnPlan(TwoStepPlan("p1")).Select(o => o.Type));
            Assert.Null(s.TakePrefetchOpportunity());   // turn-based never looks ahead
        }

        [Fact]
        public void ReflexMessagesArePrinted()
        {
            var s = Session();
            var rx = new ReflexMessage { Id = "R-L1", T = 2, Frame = 9 };
            rx.HandledEvents.Add("guard.spotted");
            s.Handle(Env(MessageTypes.Reflex, rx));
            s.Handle(Env(MessageTypes.ReflexEnd, new ReflexEndMessage { Id = "R-L1", T = 3, Status = "done" }));
            Assert.Contains("reflex R-L1 t=2.00 handled guard.spotted", _printed);
            Assert.Contains("reflex_end R-L1 t=3.00 done", _printed);
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
