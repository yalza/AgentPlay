using System;
using System.Globalization;
using System.IO;
using AgentPlay.Contract;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay
{
    /// <summary>Writes one JSON object per line. Not thread-safe; call from the main thread.</summary>
    public sealed class TraceWriter : IDisposable
    {
        readonly TextWriter _out;

        public TraceWriter(TextWriter output)
        {
            _out = output;
        }

        public void WriteHeader(string game, string version, string who, string tag, DateTime startedUtc)
        {
            WriteLine(new JObject
            {
                ["type"] = "header",
                ["game"] = game,
                ["version"] = version,
                ["who"] = who,
                ["tag"] = tag,
                ["started"] = startedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            });
        }

        public void WriteEvent(AgentEventRecord e, JObject state)
        {
            WriteLine(new JObject
            {
                ["type"] = "event",
                ["t"] = e.Time,
                ["frame"] = e.Frame,
                ["name"] = e.Name,
                ["important"] = e.Important,
                ["outcome"] = e.Outcome.ToString().ToLowerInvariant(),
                ["payload"] = e.Payload != null ? (JToken)e.Payload : JValue.CreateNull(),
                ["state"] = state,
            });
        }

        public void WriteSnapshot(double t, long frame, JObject state)
        {
            WriteLine(new JObject
            {
                ["type"] = "snapshot",
                ["t"] = t,
                ["frame"] = frame,
                ["state"] = state,
            });
        }

        /// <summary>Marks who controls the player from here on ("agent" once the runner sends its first plan).</summary>
        public void WriteControl(string who, double t)
        {
            WriteLine(new JObject { ["type"] = "control", ["t"] = t, ["who"] = who });
        }

        public void WritePlan(JObject plan, double t)
        {
            WriteLine(new JObject { ["type"] = "plan", ["t"] = t, ["plan"] = plan });
        }

        public void WritePlanStatus(PlanStatusMessage status, double t)
        {
            WriteLine(new JObject
            {
                ["type"] = "plan_status",
                ["t"] = t,
                ["plan_id"] = status.PlanId,
                ["status"] = status.Status,
                ["step"] = status.Step,
                ["reason"] = status.Reason,
            });
        }

        void WriteLine(JObject o)
        {
            _out.Write(o.ToString(Formatting.None));
            _out.Write('\n');
        }

        public void Dispose()
        {
            _out.Dispose();
        }
    }
}
