using System;
using System.IO;
using AgentPlay.Runner;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class RunnerConfigTests
    {
        [Fact]
        public void MissingFileGivesDefaults()
        {
            var c = RunnerConfig.Load(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N") + ".json"));
            Assert.Equal("claude-opus-5-5", c.Model);
            Assert.Equal("high", c.Effort);
            Assert.Equal(16000, c.MaxTokens);
            Assert.Equal(40, c.MaxCalls);
        }

        [Fact]
        public void FileOverridesDefaults()
        {
            var path = Path.Combine(Path.GetTempPath(), "runner-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{\"effort\":\"medium\",\"max_calls\":12}");
            try
            {
                var c = RunnerConfig.Load(path);
                Assert.Equal("medium", c.Effort);
                Assert.Equal(12, c.MaxCalls);
                Assert.Equal("claude-opus-5-5", c.Model);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Theory]
        [InlineData("{\"effort\":\"huge\"}", "effort")]
        [InlineData("{\"max_calls\":0}", "max_calls")]
        [InlineData("{\"max_tokens\":-1}", "max_tokens")]
        [InlineData("{\"model\":\"\"}", "model")]
        public void ValidateRejectsBadValues(string json, string field)
        {
            var c = JObject.Parse(json).ToObject<RunnerConfig>();
            var e = Assert.Throws<ArgumentException>(() => c.Validate());
            Assert.Contains(field, e.Message);
        }

        [Fact]
        public void RunLogWritesCallsAndSummary()
        {
            var sw = new StringWriter();
            using (var log = new RunLog(sw))
            {
                log.WriteCall(new CallRecord { Time = new DateTime(2026, 10, 12, 9, 0, 0), Model = "m", LatencyMs = 900, InputTokens = 10, OutputTokens = 5, CacheReadTokens = 8, StopReason = "end_turn", PlanId = "p1", Valid = true });
                log.WriteSummary("won", 3, 30, 15, 24, 6);
            }
            var lines = sw.ToString().Trim().Split('\n');
            var call = JObject.Parse(lines[0]);
            Assert.Equal("call", (string)call["type"]);
            Assert.Equal("p1", (string)call["plan_id"]);
            Assert.Equal(8, (long)call["cache_read"]);
            var summary = JObject.Parse(lines[1]);
            Assert.Equal("summary", (string)summary["type"]);
            Assert.Equal("won", (string)summary["result"]);
            Assert.Equal(3, (int)summary["calls"]);
        }

        [Fact]
        public void RunLogOpenCreatesTheRunsFolder()
        {
            var root = Path.Combine(Path.GetTempPath(), "agentplay-runs-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var log = RunLog.Open(root, new DateTime(2026, 10, 12, 9, 5, 7)))
                    Assert.Equal(Path.Combine(root, "AgentPlay", "Runs", "20261012-090507.ndjson"), log.Path);
                Assert.True(File.Exists(Path.Combine(root, "AgentPlay", "Runs", "20261012-090507.ndjson")));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
