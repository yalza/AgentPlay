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
        [JsonProperty("mode")] public string Mode;
        [JsonProperty("calls_by_model")] public Dictionary<string, ModelCalls> CallsByModel = new Dictionary<string, ModelCalls>();
        [JsonProperty("prefetch")] public PrefetchReport Prefetch = new PrefetchReport();
        [JsonProperty("time_split")] public Dictionary<string, double> TimeSplit = new Dictionary<string, double>();
        [JsonProperty("decision_wait_ms")] public Dictionary<string, double> DecisionWaitMs = new Dictionary<string, double>();
    }

    public sealed class ModelCalls
    {
        [JsonProperty("role")] public Dictionary<string, int> Role = new Dictionary<string, int>();
        [JsonProperty("wait_ms")] public Dictionary<string, double> WaitMs = new Dictionary<string, double>();
    }

    public sealed class PrefetchReport
    {
        [JsonProperty("requested")] public int Requested;
        [JsonProperty("used")] public int Used;
        [JsonProperty("discarded")] public int Discarded;
        /// <summary>Wall time from the plan-done signal to sending the look-ahead plan.</summary>
        [JsonProperty("used_wait_ms")] public List<double> UsedWaitMs = new List<double>();
    }

    /// <summary>Counts what happened in one runner run (spec M3 section 8). The runner loop is sequential, so no locking.</summary>
    public sealed class TelemetryRecorder
    {
        readonly TelemetryReport _report = new TelemetryReport();
        readonly List<long> _latencies = new List<long>();
        readonly Dictionary<string, List<long>> _latenciesByModel = new Dictionary<string, List<long>>();
        readonly List<double> _decisionWaits = new List<double>();
        readonly Func<DateTime> _now;
        DateTime? _decisionStarted;
        TimeSplit _lastSplit;

        /// <summary>"turn_based" or "realtime".</summary>
        public string Mode { get; set; }

        public TelemetryRecorder(Func<DateTime> now = null)
        {
            _now = now ?? (() => DateTime.UtcNow);
        }
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
            if (p.TimeSplit != null) _lastSplit = p.TimeSplit;
            string reason = p.Reason ?? "unknown";
            _report.PausesByReason[reason] = _report.PausesByReason.TryGetValue(reason, out var n) ? n + 1 : 1;
        }

        public void OnAttention(AttentionMessage a)
        {
            Seen(a.T);
            if (a.TimeSplit != null) _lastSplit = a.TimeSplit;
            string reason = a.Reason ?? "unknown";
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

        /// <summary>The game asked for a decision (paused or attention).</summary>
        public void OnDecisionStarted() => _decisionStarted = _now();

        /// <summary>A plan left the runner; the wait since the decision started is what the character spent waiting.</summary>
        public void OnPlanSent(bool fromPrefetch)
        {
            if (_decisionStarted == null) return;
            double ms = (_now() - _decisionStarted.Value).TotalMilliseconds;
            _decisionStarted = null;
            _decisionWaits.Add(ms);
            if (fromPrefetch) _report.Prefetch.UsedWaitMs.Add(ms);
        }

        public void SetPrefetchCounts(int requested, int used, int discarded)
        {
            _report.Prefetch.Requested = requested;
            _report.Prefetch.Used = used;
            _report.Prefetch.Discarded = discarded;
        }

        public void OnPlanStatus(PlanStatusMessage s)
        {
            if (s.Status == PlanStatuses.PlanFailed) _report.PlansFailed++;
        }

        public void OnCall(CallRecord c)
        {
            _latencies.Add(c.LatencyMs);
            string model = c.Model ?? "unknown";
            if (!_report.CallsByModel.TryGetValue(model, out var calls)) _report.CallsByModel[model] = calls = new ModelCalls();
            string role = c.Role ?? "strategy";
            calls.Role[role] = calls.Role.TryGetValue(role, out var n) ? n + 1 : 1;
            if (!_latenciesByModel.TryGetValue(model, out var list)) _latenciesByModel[model] = list = new List<long>();
            list.Add(c.LatencyMs);
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
            _report.Mode = Mode;
            foreach (var pair in _latenciesByModel)
            {
                var byModel = pair.Value.OrderBy(x => x).ToList();
                _report.CallsByModel[pair.Key].WaitMs["median"] = Median(byModel);
                _report.CallsByModel[pair.Key].WaitMs["p90"] = NearestRank(byModel, 0.9);
            }
            var waits = _decisionWaits.OrderBy(x => x).Select(x => (long)Math.Round(x)).ToList();
            _report.DecisionWaitMs["median"] = Median(waits);
            _report.DecisionWaitMs["p90"] = NearestRank(waits, 0.9);
            if (_lastSplit != null)
            {
                double total = _lastSplit.Safe + _lastSplit.Plan + _lastSplit.Reflex;
                _report.TimeSplit["safe"] = _lastSplit.Safe;
                _report.TimeSplit["plan"] = _lastSplit.Plan;
                _report.TimeSplit["reflex"] = _lastSplit.Reflex;
                _report.TimeSplit["safe_ratio"] = total > 0 ? _lastSplit.Safe / total : 0;
                _report.TimeSplit["plan_ratio"] = total > 0 ? _lastSplit.Plan / total : 0;
                _report.TimeSplit["reflex_ratio"] = total > 0 ? _lastSplit.Reflex / total : 0;
            }
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
