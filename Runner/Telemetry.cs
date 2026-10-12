using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AgentPlay.Contract;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json;

namespace AgentPlay.Runner
{
    public sealed class TelemetryReport
    {
        [JsonProperty("game")] public string Game;
        [JsonProperty("version")] public string Version;
        [JsonProperty("session_tag")] public string SessionTag;
        [JsonProperty("result")] public string Result;
        [JsonProperty("game_seconds")] public double GameSeconds;
        [JsonProperty("model_calls")] public int ModelCalls;
        [JsonProperty("calls_per_game_minute")] public double CallsPerGameMinute;
        [JsonProperty("wait_ms")] public Dictionary<string, double> WaitMs = new Dictionary<string, double>();
        [JsonProperty("tokens")] public Dictionary<string, long> Tokens = new Dictionary<string, long>();
        [JsonProperty("plans_sent")] public int PlansSent;
        [JsonProperty("plans_failed")] public int PlansFailed;
        [JsonProperty("important_events")] public int ImportantEvents;
        [JsonProperty("important_events_handled_by_reflex")] public int HandledByReflex;
        [JsonProperty("pauses_by_reason")] public Dictionary<string, int> PausesByReason = new Dictionary<string, int>();
        [JsonProperty("reflex_fires")] public Dictionary<string, int> ReflexFires = new Dictionary<string, int>();
        [JsonProperty("reflex_failures")] public int ReflexFailures;
    }

    /// <summary>Counts what happened in one runner run (spec M3 section 8). The runner loop is sequential, so no locking.</summary>
    public sealed class TelemetryRecorder
    {
        readonly TelemetryReport _report = new TelemetryReport();
        readonly List<long> _latencies = new List<long>();
        long _input, _output, _cacheRead, _cacheWrite;
        double _lastT;

        public void OnHello(HelloMessage hello)
        {
            _report.Game = hello.Game;
            _report.Version = hello.Version;
            _report.SessionTag = hello.SessionTag;
        }

        public void OnEvent(EventMessage e)
        {
            Seen(e.T);
            if (e.Important) _report.ImportantEvents++;
        }

        public void OnPaused(PausedMessage p)
        {
            Seen(p.T);
            string reason = p.Reason ?? "unknown";
            _report.PausesByReason[reason] = _report.PausesByReason.TryGetValue(reason, out var n) ? n + 1 : 1;
        }

        public void OnReflex(ReflexMessage m)
        {
            Seen(m.T);
            _report.ReflexFires[m.Id] = _report.ReflexFires.TryGetValue(m.Id, out var n) ? n + 1 : 1;
            _report.HandledByReflex += m.HandledEvents?.Count ?? 0;
        }

        public void OnReflexEnd(ReflexEndMessage m)
        {
            Seen(m.T);
            if (m.Status != null && m.Status.StartsWith("failed", StringComparison.Ordinal)) _report.ReflexFailures++;
        }

        public void OnPlanSent() => _report.PlansSent++;

        public void OnPlanStatus(PlanStatusMessage s)
        {
            if (s.Status == PlanStatuses.PlanFailed) _report.PlansFailed++;
        }

        public void OnCall(CallRecord c)
        {
            _latencies.Add(c.LatencyMs);
            _input += c.InputTokens;
            _output += c.OutputTokens;
            _cacheRead += c.CacheReadTokens;
            _cacheWrite += c.CacheWriteTokens;
        }

        public TelemetryReport Build(string result)
        {
            _report.Result = result;
            _report.GameSeconds = _lastT;
            _report.ModelCalls = _latencies.Count;
            _report.CallsPerGameMinute = _lastT > 0 ? _latencies.Count / (_lastT / 60.0) : 0;
            var sorted = _latencies.OrderBy(x => x).ToList();
            _report.WaitMs["median"] = Median(sorted);
            _report.WaitMs["p90"] = NearestRank(sorted, 0.9);
            _report.Tokens["input"] = _input;
            _report.Tokens["output"] = _output;
            _report.Tokens["cache_read"] = _cacheRead;
            _report.Tokens["cache_write"] = _cacheWrite;
            return _report;
        }

        /// <summary>Writes AgentPlay/Telemetry/yyyyMMdd-HHmmss[-n].json and returns its path.</summary>
        public static string Write(string projectRoot, DateTime localNow, TelemetryReport report)
        {
            string dir = Path.Combine(projectRoot, "AgentPlay", "Telemetry");
            Directory.CreateDirectory(dir);
            string stem = localNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string path = Path.Combine(dir, stem + ".json");
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(dir, stem + "-" + n + ".json");
            AtomicFile.WriteAllText(path, JsonConvert.SerializeObject(report, Formatting.Indented));
            return path;
        }

        void Seen(double t)
        {
            if (t > _lastT) _lastT = t;
        }

        static double Median(List<long> sorted)
        {
            if (sorted.Count == 0) return 0;
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }

        static double NearestRank(List<long> sorted, double p)
        {
            if (sorted.Count == 0) return 0;
            int rank = (int)Math.Ceiling(p * sorted.Count) - 1;
            return sorted[Math.Max(0, Math.Min(sorted.Count - 1, rank))];
        }
    }
}
