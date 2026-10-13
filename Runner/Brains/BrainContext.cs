using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentPlay.Contract;
using AgentPlay.Runner.Learning;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner.Brains
{
    /// <summary>What the runner knows when it must choose the next plan.</summary>
    public sealed class BrainContext
    {
        public HelloMessage Hello;
        /// <summary>Lessons of earlier sessions of this game; null when there is no playbook.</summary>
        public Playbook Playbook;
        /// <summary>Snapshot carried by the paused message that asked for this decision.</summary>
        public JObject State;
        public string PauseReason;
        /// <summary>Game time of that pause.</summary>
        public double PauseT;
        /// <summary>Events since the previous plan was sent, oldest first.</summary>
        public List<EventMessage> RecentEvents = new List<EventMessage>();
        /// <summary>plan_status messages since the previous plan was sent.</summary>
        public List<PlanStatusMessage> PlanStatuses = new List<PlanStatusMessage>();
        public Plan LastPlan;
        public int PlansSent;
        /// <summary>One line per finished plan: "id: intent -> status (reason)", oldest first.</summary>
        public List<string> History = new List<string>();
        /// <summary>Set by a brain that returns null, to explain why it stopped.</summary>
        public string StopReason;
        /// <summary>Extra instruction for a look-ahead request; null for a normal decision.</summary>
        public string PrefetchNote;

        /// <summary>A copy for a call that runs while the session keeps receiving messages.</summary>
        public BrainContext Clone()
        {
            var c = (BrainContext)MemberwiseClone();
            c.RecentEvents = new List<EventMessage>(RecentEvents);
            c.PlanStatuses = new List<PlanStatusMessage>(PlanStatuses);
            c.History = new List<string>(History);
            return c;
        }
    }

    public interface IBrain
    {
        /// <summary>Returns the next plan, or null when the brain has nothing more to do.</summary>
        Task<Plan> NextPlanAsync(BrainContext context, CancellationToken ct);
    }
}
