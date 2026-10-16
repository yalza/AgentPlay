using System.Collections.Generic;
using AgentPlay.Contract;
using AgentPlay.Runner.Brains;

namespace AgentPlay.Runner
{
    public enum CallKind { Decision, Prefetch }

    public sealed class CoordinatorCommand
    {
        public const string Start = "start";
        public const string Send = "send";
        public const string CancelPrefetch = "cancel_prefetch";

        public string Kind;
        /// <summary>For start: which call.</summary>
        public CallKind Call;
        /// <summary>For start: the context the brain answers.</summary>
        public BrainContext Context;
        /// <summary>For send: the plan, or null when the brain stopped.</summary>
        public Plan Plan;
        public bool FromPrefetch;
    }

    /// <summary>
    /// Decides when to ask the brain (spec M4a sections 3 and 5): one decision call at a time, decisions that arrive
    /// meanwhile collapse into one follow-up call, and an optional look-ahead call whose plan is used only if the plan
    /// it follows finished without surprises. Pure: the caller runs the calls and sends the plans.
    /// </summary>
    public sealed class DecisionCoordinator
    {
        readonly bool _prefetchEnabled;
        BrainContext _pending;
        bool _prefetchInFlight;
        string _prefetchFor;
        Plan _prefetched;
        /// <summary>A plan-done decision waiting for the look-ahead call to come back.</summary>
        BrainContext _waiting;

        public bool DecisionInFlight { get; private set; }
        public bool PrefetchInFlight => _prefetchInFlight;
        public int PrefetchRequested { get; private set; }
        public int PrefetchUsed { get; private set; }
        public int PrefetchDiscarded { get; private set; }

        bool HasPrefetch => _prefetchInFlight || _prefetched != null;

        public DecisionCoordinator(bool prefetchEnabled)
        {
            _prefetchEnabled = prefetchEnabled;
        }

        public List<CoordinatorCommand> OnDecisionNeeded(BrainContext ctx, string endedPlanId, bool endedDone)
        {
            var cmds = new List<CoordinatorCommand>();
            if (HasPrefetch && ctx.PauseReason == "plan" && endedDone && endedPlanId == _prefetchFor)
            {
                if (_prefetched != null)
                {
                    cmds.Add(SendPrefetched());
                    return cmds;
                }
                _waiting = ctx;
                return cmds;
            }
            if (HasPrefetch) Discard(cmds);
            StartOrQueue(ctx, cmds);
            return cmds;
        }

        public List<CoordinatorCommand> OnPrefetchOpportunity(BrainContext ctx, string planId)
        {
            var cmds = new List<CoordinatorCommand>();
            if (!_prefetchEnabled || DecisionInFlight || HasPrefetch) return cmds;
            _prefetchInFlight = true;
            _prefetchFor = planId;
            PrefetchRequested++;
            cmds.Add(new CoordinatorCommand { Kind = CoordinatorCommand.Start, Call = CallKind.Prefetch, Context = ctx });
            return cmds;
        }

        public List<CoordinatorCommand> OnInvalidated()
        {
            var cmds = new List<CoordinatorCommand>();
            if (!HasPrefetch) return cmds;
            Discard(cmds);
            if (_waiting != null)
            {
                var ctx = _waiting;
                _waiting = null;
                StartOrQueue(ctx, cmds);
            }
            return cmds;
        }

        public List<CoordinatorCommand> OnCallCompleted(CallKind kind, Plan plan)
        {
            var cmds = new List<CoordinatorCommand>();
            if (kind == CallKind.Decision)
            {
                DecisionInFlight = false;
                cmds.Add(new CoordinatorCommand { Kind = CoordinatorCommand.Send, Plan = plan });
                if (_pending != null)
                {
                    var next = _pending;
                    _pending = null;
                    StartOrQueue(next, cmds);
                }
                return cmds;
            }

            if (!_prefetchInFlight) return cmds;   // discarded while it ran
            _prefetchInFlight = false;
            if (plan == null)
            {
                _prefetchFor = null;
                if (_waiting != null)
                {
                    var ctx = _waiting;
                    _waiting = null;
                    StartOrQueue(ctx, cmds);
                }
                return cmds;
            }
            _prefetched = plan;
            if (_waiting != null)
            {
                _waiting = null;
                cmds.Add(SendPrefetched());
            }
            return cmds;
        }

        CoordinatorCommand SendPrefetched()
        {
            var plan = _prefetched;
            _prefetched = null;
            _prefetchFor = null;
            PrefetchUsed++;
            return new CoordinatorCommand { Kind = CoordinatorCommand.Send, Plan = plan, FromPrefetch = true };
        }

        void Discard(List<CoordinatorCommand> cmds)
        {
            if (_prefetchInFlight) cmds.Add(new CoordinatorCommand { Kind = CoordinatorCommand.CancelPrefetch });
            _prefetchInFlight = false;
            _prefetched = null;
            _prefetchFor = null;
            PrefetchDiscarded++;
        }

        void StartOrQueue(BrainContext ctx, List<CoordinatorCommand> cmds)
        {
            if (DecisionInFlight)
            {
                _pending = ctx;   // the latest decision wins
                return;
            }
            DecisionInFlight = true;
            cmds.Add(new CoordinatorCommand { Kind = CoordinatorCommand.Start, Call = CallKind.Decision, Context = ctx });
        }
    }
}
