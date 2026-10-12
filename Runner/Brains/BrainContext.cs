using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner.Brains
{
    /// <summary>What the runner knows when it must choose the next plan.</summary>
    public sealed class BrainContext
    {
        public HelloMessage Hello;
        /// <summary>Snapshot carried by the paused message that asked for this decision.</summary>
        public JObject State;
        public string PauseReason;
        /// <summary>Game time of that pause.</summary>
        public double PauseT;
        /// <summary>Events since the previous plan was sent, oldest first.</summary>
        public readonly List<EventMessage> RecentEvents = new List<EventMessage>();
        /// <summary>plan_status messages since the previous plan was sent.</summary>
        public readonly List<PlanStatusMessage> PlanStatuses = new List<PlanStatusMessage>();
        public Plan LastPlan;
        public int PlansSent;
    }

    public interface IBrain
    {
        /// <summary>Returns the next plan, or null when the brain has nothing more to do.</summary>
        Task<Plan> NextPlanAsync(BrainContext context, CancellationToken ct);
    }
}
