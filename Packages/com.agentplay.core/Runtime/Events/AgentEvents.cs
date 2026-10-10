using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AgentPlay
{
    public readonly struct AgentEventRecord
    {
        public readonly string Name;
        public readonly JObject Payload;
        public readonly bool Important;
        public readonly AgentOutcome Outcome;
        public readonly double Time;
        public readonly long Frame;

        public AgentEventRecord(string name, JObject payload, bool important, AgentOutcome outcome, double time, long frame)
        {
            Name = name;
            Payload = payload;
            Important = important;
            Outcome = outcome;
            Time = time;
            Frame = frame;
        }
    }

    /// <summary>Game-facing API. Costs nothing when AgentPlay is not listening.</summary>
    public static class AgentEvents
    {
        public static event Action<AgentEventRecord> Emitted;

        public static Func<double> TimeSource = () => UnityEngine.Time.timeAsDouble;
        public static Func<long> FrameSource = () => UnityEngine.Time.frameCount;

        /// <param name="important">Pauses turn-based play and must reach the runner when no rule handles it.</param>
        /// <param name="outcome">Marks the event as good or bad for scoring reflexes.</param>
        public static void Emit(string name, object payload = null, bool important = false, AgentOutcome outcome = AgentOutcome.Neutral)
        {
            var handlers = Emitted;
            if (handlers == null) return;
            if (string.IsNullOrEmpty(name))
            {
                Debug.LogWarning("[AgentPlay] Ignored an event with an empty event name");
                return;
            }

            var record = new AgentEventRecord(name, UnityJson.ToPayload(payload), important, outcome, TimeSource(), FrameSource());
            foreach (Action<AgentEventRecord> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(record);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }
}
