using System;
using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using Newtonsoft.Json;

namespace AgentPlay.Runner.Brains
{
    /// <summary>Which model answers one call, how hard it thinks, and how long its plan may be (0 = no limit).</summary>
    public sealed class RouteDecision
    {
        public const string Fast = "fast";
        public const string Strategy = "strategy";
        public const string Prefetch = "prefetch";

        public string Role;
        public string Model;
        public string Effort;
        public int MaxSteps;
    }

    /// <summary>The router block of runner.json (spec M4a section 4).</summary>
    public sealed class RouterConfig
    {
        [JsonProperty("enabled")] public bool Enabled = true;
        [JsonProperty("fast_model")] public string FastModel = "claude-haiku-5-5";
        [JsonProperty("fast_effort")] public string FastEffort = "low";
        [JsonProperty("fast_max_steps")] public int FastMaxSteps = 3;
        /// <summary>Quick reactions in a row (within the window) after which the strategy model takes over.</summary>
        [JsonProperty("escalate_after")] public int EscalateAfter = 2;
        [JsonProperty("escalate_window_s")] public double EscalateWindowSeconds = 30;
    }

    /// <summary>
    /// Surprises while a plan runs (an important event, a rule asking to replan) get a quick, short plan from the fast
    /// model; everything else, and repeated quick reactions, go to the strategy model.
    /// </summary>
    public sealed class ModelRouter
    {
        readonly RunnerConfig _config;
        readonly List<double> _recentFast = new List<double>();

        public ModelRouter(RunnerConfig config)
        {
            _config = config;
        }

        public RouteDecision Route(BrainContext ctx)
        {
            var router = _config.Router ?? new RouterConfig();
            if (!router.Enabled) return StrategyRoute();
            bool replanRule = ctx.PlanStatuses.Any(s => s.Status == PlanStatuses.PlanFailed && (s.Reason ?? "").StartsWith("replan:", StringComparison.Ordinal));
            // An event is a surprise for a running plan; with no plan yet, or after it ended, the strategy model answers.
            bool planRunning = ctx.LastPlan != null && !ctx.PlanStatuses.Any(s => s.PlanId == ctx.LastPlan.Id
                && (s.Status == PlanStatuses.PlanDone || s.Status == PlanStatuses.PlanFailed));
            bool quick = (ctx.PauseReason == "event" && planRunning) || (ctx.PauseReason == "plan" && replanRule);
            if (!quick)
            {
                _recentFast.Clear();
                return StrategyRoute();
            }
            _recentFast.RemoveAll(t => ctx.PauseT - t > router.EscalateWindowSeconds);
            if (_recentFast.Count >= router.EscalateAfter)
            {
                _recentFast.Clear();
                return StrategyRoute();
            }
            _recentFast.Add(ctx.PauseT);
            return new RouteDecision { Role = RouteDecision.Fast, Model = router.FastModel, Effort = router.FastEffort, MaxSteps = router.FastMaxSteps };
        }

        public RouteDecision Prefetch() => new RouteDecision { Role = RouteDecision.Prefetch, Model = _config.Model, Effort = _config.Effort };

        RouteDecision StrategyRoute() => new RouteDecision { Role = RouteDecision.Strategy, Model = _config.Model, Effort = _config.Effort };
    }
}
