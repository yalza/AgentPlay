using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using AgentPlay.Runner.Brains;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class DecisionCoordinatorTests
    {
        static BrainContext Ctx(string reason, double t = 1) => new BrainContext { PauseReason = reason, PauseT = t };

        static Plan P(string id) => new Plan { Id = id, Steps = new List<PlanStep> { new PlanStep { Do = "wait" } }, TimeoutSeconds = 10 };

        static string Kinds(IEnumerable<CoordinatorCommand> cmds) => string.Join(",", cmds.Select(c => c.Kind == CoordinatorCommand.Start ? "start:" + c.Call : c.Kind));

        [Fact]
        public void FirstDecisionStartsACall()
        {
            var c = new DecisionCoordinator(prefetchEnabled: true);
            var cmds = c.OnDecisionNeeded(Ctx("mode"), null, false);
            Assert.Equal("start:Decision", Kinds(cmds));
            Assert.True(c.DecisionInFlight);
        }

        [Fact]
        public void DecisionWhileBusyIsCoalescedAndStartedAfter()
        {
            var c = new DecisionCoordinator(true);
            c.OnDecisionNeeded(Ctx("mode", 1), null, false);
            Assert.Empty(c.OnDecisionNeeded(Ctx("event", 2), null, false));
            Assert.Empty(c.OnDecisionNeeded(Ctx("event", 3), null, false));
            var cmds = c.OnCallCompleted(CallKind.Decision, P("p1"));
            Assert.Equal("send,start:Decision", Kinds(cmds));
            Assert.Equal("p1", cmds[0].Plan.Id);
            Assert.Equal(3, cmds[1].Context.PauseT);
            Assert.Equal("send", Kinds(c.OnCallCompleted(CallKind.Decision, P("p2"))));
            Assert.False(c.DecisionInFlight);
        }

        [Fact]
        public void PrefetchUsedWhenPlanDoneWithoutSurprise()
        {
            var c = new DecisionCoordinator(true);
            Assert.Equal("start:Prefetch", Kinds(c.OnPrefetchOpportunity(Ctx("prefetch"), "p1")));
            Assert.Empty(c.OnCallCompleted(CallKind.Prefetch, P("next")));
            var cmds = c.OnDecisionNeeded(Ctx("plan"), "p1", true);
            Assert.Equal("send", Kinds(cmds));
            Assert.Equal("next", cmds[0].Plan.Id);
            Assert.True(cmds[0].FromPrefetch);
            Assert.Equal(1, c.PrefetchUsed);
            Assert.Equal(1, c.PrefetchRequested);
        }

        [Fact]
        public void DecisionWaitsForPrefetchInFlight()
        {
            var c = new DecisionCoordinator(true);
            c.OnPrefetchOpportunity(Ctx("prefetch"), "p1");
            Assert.Empty(c.OnDecisionNeeded(Ctx("plan"), "p1", true));
            var cmds = c.OnCallCompleted(CallKind.Prefetch, P("next"));
            Assert.Equal("send", Kinds(cmds));
            Assert.True(cmds[0].FromPrefetch);
        }

        [Fact]
        public void InvalidatedPrefetchIsDropped()
        {
            var c = new DecisionCoordinator(true);
            c.OnPrefetchOpportunity(Ctx("prefetch"), "p1");
            Assert.Equal("cancel_prefetch", Kinds(c.OnInvalidated()));
            Assert.Empty(c.OnCallCompleted(CallKind.Prefetch, P("stale")));
            Assert.Equal(1, c.PrefetchDiscarded);
            Assert.Equal("start:Decision", Kinds(c.OnDecisionNeeded(Ctx("plan"), "p1", true)));
        }

        [Fact]
        public void InvalidatedWhileADecisionWaitsStartsTheDecision()
        {
            var c = new DecisionCoordinator(true);
            c.OnPrefetchOpportunity(Ctx("prefetch"), "p1");
            c.OnDecisionNeeded(Ctx("plan", 5), "p1", true);
            var cmds = c.OnInvalidated();
            Assert.Equal("cancel_prefetch,start:Decision", Kinds(cmds));
            Assert.Equal(5, cmds[1].Context.PauseT);
        }

        [Fact]
        public void PrefetchForOtherPlanIsDiscarded()
        {
            var c = new DecisionCoordinator(true);
            c.OnPrefetchOpportunity(Ctx("prefetch"), "p1");
            c.OnCallCompleted(CallKind.Prefetch, P("next"));
            Assert.Equal("start:Decision", Kinds(c.OnDecisionNeeded(Ctx("plan"), "p9", true)));
            Assert.Equal(1, c.PrefetchDiscarded);
        }

        [Fact]
        public void PlanFailedDoesNotUsePrefetch()
        {
            var c = new DecisionCoordinator(true);
            c.OnPrefetchOpportunity(Ctx("prefetch"), "p1");
            Assert.Equal("cancel_prefetch,start:Decision", Kinds(c.OnDecisionNeeded(Ctx("plan"), "p1", false)));
        }

        [Fact]
        public void EventDecisionDoesNotUsePrefetch()
        {
            var c = new DecisionCoordinator(true);
            c.OnPrefetchOpportunity(Ctx("prefetch"), "p1");
            c.OnCallCompleted(CallKind.Prefetch, P("next"));
            Assert.Equal("start:Decision", Kinds(c.OnDecisionNeeded(Ctx("event"), "p1", true)));
        }

        [Fact]
        public void NullPrefetchFallsBackToADecision()
        {
            var c = new DecisionCoordinator(true);
            c.OnPrefetchOpportunity(Ctx("prefetch"), "p1");
            c.OnDecisionNeeded(Ctx("plan", 4), "p1", true);
            var cmds = c.OnCallCompleted(CallKind.Prefetch, null);
            Assert.Equal("start:Decision", Kinds(cmds));
            Assert.Equal(4, cmds[0].Context.PauseT);
        }

        [Fact]
        public void NoPrefetchWhenDisabledOrBusy()
        {
            Assert.Empty(new DecisionCoordinator(false).OnPrefetchOpportunity(Ctx("prefetch"), "p1"));
            var c = new DecisionCoordinator(true);
            c.OnDecisionNeeded(Ctx("mode"), null, false);
            Assert.Empty(c.OnPrefetchOpportunity(Ctx("prefetch"), "p1"));
            c.OnCallCompleted(CallKind.Decision, P("p1"));
            Assert.Equal("start:Prefetch", Kinds(c.OnPrefetchOpportunity(Ctx("prefetch"), "p1")));
            Assert.Empty(c.OnPrefetchOpportunity(Ctx("prefetch"), "p1"));
        }

        [Fact]
        public void NullPlanFinishesTheRun()
        {
            var c = new DecisionCoordinator(true);
            c.OnDecisionNeeded(Ctx("mode"), null, false);
            var cmds = c.OnCallCompleted(CallKind.Decision, null);
            Assert.Equal("send", Kinds(cmds));
            Assert.Null(cmds[0].Plan);
        }
    }
}
