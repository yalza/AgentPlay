using System.Collections.Generic;
using System.Globalization;

namespace AgentPlay.Contract
{
    /// <summary>Status transitions of reflexes (spec M3 section 3.4). Each returns false when the transition does not apply.</summary>
    public static class ReflexLifecycle
    {
        public static bool Approve(Reflex r, string version)
        {
            if (r.Status != ReflexStatuses.Proposed && r.Status != ReflexStatuses.NeedsReview) return false;
            r.Status = ReflexStatuses.Active;
            r.GameVersion = version;
            r.Note = "";
            return true;
        }

        public static bool Reject(Reflex r)
        {
            if (r.Status == ReflexStatuses.Rejected) return false;
            r.Status = ReflexStatuses.Rejected;
            r.Note = "rejected by user";
            return true;
        }

        public static bool Disable(Reflex r)
        {
            if (r.Status != ReflexStatuses.Active) return false;
            r.Status = ReflexStatuses.Suspended;
            r.Note = "disabled by user";
            return true;
        }

        public static bool Reenable(Reflex r, string version)
        {
            if (r.Status != ReflexStatuses.Suspended) return false;
            r.Status = ReflexStatuses.Active;
            r.GameVersion = version;
            r.Stats = new ReflexStats();
            r.Note = "";
            return true;
        }

        /// <summary>Active reflexes approved for another game version need review; returns their ids.</summary>
        public static List<string> MarkVersionChanges(ReflexFile file, string version)
        {
            var changed = new List<string>();
            foreach (var r in file.Reflexes)
            {
                if (r.Status != ReflexStatuses.Active || r.GameVersion == version) continue;
                r.Note = "game version changed from " + r.GameVersion + " to " + version;
                r.Status = ReflexStatuses.NeedsReview;
                changed.Add(r.Id);
            }
            return changed;
        }

        public static void RecordFired(Reflex r) => r.Stats.Fired++;

        /// <summary>Counts one resolved firing; suspends the reflex when its fail rate is above the policy.</summary>
        public static bool RecordOutcome(Reflex r, bool bad, ReflexPolicy policy)
        {
            if (bad) r.Stats.Bad++;
            else r.Stats.Good++;
            int resolved = r.Stats.Good + r.Stats.Bad;
            if (r.Status != ReflexStatuses.Active || resolved < policy.MinFires) return false;
            double rate = (double)r.Stats.Bad / resolved;
            if (rate <= policy.MaxFailRate) return false;
            r.Status = ReflexStatuses.Suspended;
            r.Note = "fail rate " + rate.ToString("0.00", CultureInfo.InvariantCulture) + " after " + resolved + " outcomes";
            return true;
        }
    }
}
