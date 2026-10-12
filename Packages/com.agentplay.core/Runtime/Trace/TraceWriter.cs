using System;
using System.Collections.Generic;
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

        /// <summary>The header; <paramref name="hello"/> (all observes, actions and events) lets the reflector read the trace later.</summary>
        public void WriteHeader(string game, string version, string who, string tag, DateTime startedUtc, HelloMessage hello = null)
        {
            var header = new JObject
            {
                ["type"] = "header",
                ["game"] = game,
                ["version"] = version,
                ["who"] = who,
                ["tag"] = tag,
                ["started"] = startedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            };
            if (hello != null) header["hello"] = JObject.FromObject(hello, ContractJson.Serializer);
            WriteLine(header);
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

        public void WriteReflex(string id, double t, IReadOnlyList<string> handled)
        {
            var events = new JArray();
            if (handled != null)
                foreach (var name in handled) events.Add(name);
            WriteLine(new JObject { ["type"] = "reflex", ["t"] = t, ["id"] = id, ["handled_events"] = events });
        }

        public void WriteReflexEnd(string id, double t, string status)
        {
            WriteLine(new JObject { ["type"] = "reflex_end", ["t"] = t, ["id"] = id, ["status"] = status });
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
