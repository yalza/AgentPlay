using AgentPlay.Contract;
using AgentPlay.Runner.Brains;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class ModelRouterTests
    {
        static RunnerConfig Config() => new RunnerConfig { Model = "claude-opus-5-5", Effort = "high" };

        static BrainContext Ctx(string reason, double t, string failedReason = null)
        {
            var c = new BrainContext { PauseReason = reason, PauseT = t };
            if (failedReason != null) c.PlanStatuses.Add(new PlanStatusMessage { Status = PlanStatuses.PlanFailed, Reason = failedReason });
            return c;
        }

        [Fact]
        public void EventGoesToTheFastModel()
        {
            var r = new ModelRouter(Config()).Route(Ctx("event", 5));
            Assert.Equal("fast", r.Role);
            Assert.Equal("claude-haiku-5-5", r.Model);
            Assert.Equal("low", r.Effort);
            Assert.Equal(3, r.MaxSteps);
        }

        [Theory]
        [InlineData("mode", null, "strategy")]
        [InlineData("plan", null, "strategy")]
        [InlineData("plan", "replan:0", "fast")]
        [InlineData("plan", "timeout", "strategy")]
        [InlineData("plan", "action_failed:goto", "strategy")]
        [InlineData("budget", null, "strategy")]
        public void RoutingTable(string reason, string failed, string role)
        {
            var r = new ModelRouter(Config()).Route(Ctx(reason, 1, failed));
            Assert.Equal(role, r.Role);
            if (role == "strategy")
            {
                Assert.Equal("claude-opus-5-5", r.Model);
                Assert.Equal("high", r.Effort);
                Assert.Equal(0, r.MaxSteps);
            }
        }

        [Fact]
        public void ThirdQuickReactionWithinTheWindowEscalates()
        {
            var router = new ModelRouter(Config());
            Assert.Equal("fast", router.Route(Ctx("event", 10)).Role);
            Assert.Equal("fast", router.Route(Ctx("event", 20)).Role);
            Assert.Equal("strategy", router.Route(Ctx("event", 30)).Role);
            Assert.Equal("fast", router.Route(Ctx("event", 31)).Role);   // the counter starts over after escalating
        }

        [Fact]
        public void QuickReactionsFarApartStayFast()
        {
            var router = new ModelRouter(Config());
            Assert.Equal("fast", router.Route(Ctx("event", 10)).Role);
            Assert.Equal("fast", router.Route(Ctx("event", 45)).Role);
            Assert.Equal("fast", router.Route(Ctx("event", 80)).Role);
        }

        [Fact]
        public void StrategyDecisionResetsTheCount()
        {
            var router = new ModelRouter(Config());
            router.Route(Ctx("event", 10));
            router.Route(Ctx("event", 12));
            router.Route(Ctx("plan", 13));
            Assert.Equal("fast", router.Route(Ctx("event", 14)).Role);
        }

        [Fact]
        public void DisabledRouterAlwaysUsesStrategy()
        {
            var config = Config();
            config.Router.Enabled = false;
            Assert.Equal("strategy", new ModelRouter(config).Route(Ctx("event", 1)).Role);
        }

        [Fact]
        public void PrefetchUsesTheStrategyModel()
        {
            var r = new ModelRouter(Config()).Prefetch();
            Assert.Equal("prefetch", r.Role);
            Assert.Equal("claude-opus-5-5", r.Model);
        }
    }
}
