using System;
using System.Globalization;
using System.IO;
using System.Text;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner
{
    /// <summary>One NDJSON line per model call, plus a summary line, next to the game's traces.</summary>
    public sealed class RunLog : IDisposable
    {
        readonly TextWriter _out;

        public string Path { get; private set; }

        public RunLog(TextWriter output)
        {
            _out = output;
        }

        public static RunLog Open(string projectRoot, DateTime now)
        {
            string dir = System.IO.Path.Combine(projectRoot, "AgentPlay", "Runs");
            Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".ndjson");
            var writer = new StreamWriter(path, true, new UTF8Encoding(false)) { AutoFlush = true };
            return new RunLog(writer) { Path = path };
        }

        public void WriteCall(CallRecord r)
        {
            Write(new JObject
            {
                ["type"] = "call",
                ["time"] = r.Time.ToString("o", CultureInfo.InvariantCulture),
                ["model"] = r.Model,
                ["latency_ms"] = r.LatencyMs,
                ["input_tokens"] = r.InputTokens,
                ["output_tokens"] = r.OutputTokens,
                ["cache_read"] = r.CacheReadTokens,
                ["cache_write"] = r.CacheWriteTokens,
                ["stop_reason"] = r.StopReason,
                ["plan_id"] = r.PlanId,
                ["valid"] = r.Valid,
                ["error"] = r.Error,
            });
        }

        public void WriteSummary(string result, int calls, long inputTokens, long outputTokens, long cacheRead, long cacheWrite)
        {
            Write(new JObject
            {
                ["type"] = "summary",
                ["result"] = result,
                ["calls"] = calls,
                ["input_tokens"] = inputTokens,
                ["output_tokens"] = outputTokens,
                ["cache_read"] = cacheRead,
                ["cache_write"] = cacheWrite,
            });
        }

        void Write(JObject o)
        {
            _out.Write(o.ToString(Formatting.None));
            _out.Write('\n');
        }

        public void Dispose() => _out.Dispose();
    }
}
