using System;
using System.IO;
using AgentPlay.Contract;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class TelemetryTests
    {
        [Fact]
        public void CountsReflexHandledEventsCallsAndPauses()
        {
            var t = new TelemetryRecorder();
            t.OnHello(new HelloMessage { Game = "G", Version = "v", SessionTag = "play" });
            foreach (var ms in new long[] { 1000, 3000, 2000, 10000 })
                t.OnCall(new CallRecord { LatencyMs = ms, InputTokens = 2, OutputTokens = 100, CacheReadTokens = 50 });
            t.OnEvent(new EventMessage { Name = "guard.spotted", Important = true, T = 10 });
            t.OnEvent(new EventMessage { Name = "item.picked", T = 20 });
            t.OnEvent(new EventMessage { Name = "guard.spotted", Important = true, T = 30 });
            var rx = new ReflexMessage { Id = "R-L1", T = 30 };
            rx.HandledEvents.Add("guard.spotted");
            t.OnReflex(rx);
            t.OnReflexEnd(new ReflexEndMessage { Id = "R-L1", T = 31, Status = "failed:timeout" });
            t.OnPaused(new PausedMessage { Reason = "event", T = 10 });
            t.OnPaused(new PausedMessage { Reason = "plan", T = 60 });
            t.OnPlanSent();
            t.OnPlanSent();
            t.OnPlanStatus(new PlanStatusMessage { Status = PlanStatuses.PlanFailed });

            var r = JObject.FromObject(t.Build("won"));
            Assert.Equal(4, (int)r["model_calls"]);
            Assert.Equal(4.0, (double)r["calls_per_game_minute"], 3);
            Assert.Equal(2500, (double)r["wait_ms"]["median"]);
            Assert.Equal(10000, (double)r["wait_ms"]["p90"]);
            Assert.Equal(2, (int)r["important_events"]);
            Assert.Equal(1, (int)r["important_events_handled_by_reflex"]);
            Assert.Equal(1, (int)r["reflex_fires"]["R-L1"]);
            Assert.Equal(1, (int)r["reflex_failures"]);
            Assert.Equal(1, (int)r["pauses_by_reason"]["event"]);
            Assert.Equal(2, (int)r["plans_sent"]);
            Assert.Equal(1, (int)r["plans_failed"]);
            Assert.Equal(400, (long)r["tokens"]["output"]);
            Assert.Equal("play", (string)r["session_tag"]);
            Assert.Equal(60, (double)r["game_seconds"]);
        }

        [Fact]
        public void EmptySessionHasZeroRates()
        {
            var r = JObject.FromObject(new TelemetryRecorder().Build("unfinished"));
            Assert.Equal(0, (double)r["calls_per_game_minute"]);
            Assert.Equal(0, (double)r["wait_ms"]["median"]);
        }

        [Fact]
        public void WriteCreatesUniqueFiles()
        {
            var root = Path.Combine(Path.GetTempPath(), "agentplay-tel-" + Guid.NewGuid().ToString("N"));
            var now = new DateTime(2026, 10, 13, 9, 0, 0);
            var a = TelemetryRecorder.Write(root, now, new TelemetryRecorder().Build("lost"));
            var b = TelemetryRecorder.Write(root, now, new TelemetryRecorder().Build("lost"));
            Assert.EndsWith("20261013-090000.json", a);
            Assert.EndsWith("20261013-090000-2.json", b);
            Assert.Equal("lost", (string)JObject.Parse(File.ReadAllText(a))["result"]);
        }

        [Fact]
        public void SessionFeedsTheRecorder()
        {
            var t = new TelemetryRecorder();
            var s = new RunnerSession(RunnerOptions.Parse(new string[0]), _ => { }, t);
            s.Handle(MessageCodec.Decode(MessageCodec.Encode(MessageTypes.Event, 1, new EventMessage { Name = "x", Important = true, T = 5 })));
            s.Handle(MessageCodec.Decode(MessageCodec.Encode(MessageTypes.Paused, 2, new PausedMessage { Reason = "event", T = 5 })));
            var r = JObject.FromObject(t.Build("stopped"));
            Assert.Equal(1, (int)r["important_events"]);
            Assert.Equal(1, (int)r["pauses_by_reason"]["event"]);
        }

        [Fact]
        public void CountsByModelRoleAndPrefetch()
        {
            var t = new TelemetryRecorder { Mode = "realtime" };
            t.OnCall(new CallRecord { Model = "claude-haiku-5-5", Role = "fast", LatencyMs = 3000 });
            t.OnCall(new CallRecord { Model = "claude-haiku-5-5", Role = "fast", LatencyMs = 5000 });
            t.OnCall(new CallRecord { Model = "claude-opus-5-5", Role = "strategy", LatencyMs = 12000 });
            t.OnCall(new CallRecord { Model = "claude-opus-5-5", Role = "prefetch", LatencyMs = 9000 });
            t.SetPrefetchCounts(2, 1, 1);
            var r = JObject.FromObject(t.Build("won"));
            Assert.Equal("realtime", (string)r["mode"]);
            Assert.Equal(2, (int)r["calls_by_model"]["claude-haiku-5-5"]["role"]["fast"]);
            Assert.Equal(4000, (double)r["calls_by_model"]["claude-haiku-5-5"]["wait_ms"]["median"]);
            Assert.Equal(1, (int)r["calls_by_model"]["claude-opus-5-5"]["role"]["prefetch"]);
            Assert.Equal(2, (int)r["prefetch"]["requested"]);
            Assert.Equal(1, (int)r["prefetch"]["used"]);
            Assert.Equal(1, (int)r["prefetch"]["discarded"]);
        }

        [Fact]
        public void TimeSplitFromTheLastAttention()
        {
            var t = new TelemetryRecorder();
            t.OnAttention(new AttentionMessage { Reason = "plan", T = 5, TimeSplit = new TimeSplit { Safe = 1, Plan = 3, Reflex = 0 } });
            t.OnPaused(new PausedMessage { Reason = "plan", T = 10, TimeSplit = new TimeSplit { Safe = 2, Plan = 6, Reflex = 2 } });
            var r = JObject.FromObject(t.Build("won"));
            Assert.Equal(6, (double)r["time_split"]["plan"]);
            Assert.Equal(0.6, (double)r["time_split"]["plan_ratio"], 3);
            Assert.Equal(0.2, (double)r["time_split"]["reflex_ratio"], 3);
        }

        [Fact]
        public void DecisionWaitIsMeasured()
        {
            var now = new DateTime(2026, 1, 1, 0, 0, 0);
            var t = new TelemetryRecorder(() => now);
            t.OnDecisionStarted();
            now = now.AddMilliseconds(4000);
            t.OnPlanSent(false);
            t.OnDecisionStarted();
            now = now.AddMilliseconds(200);
            t.OnPlanSent(true);
            var r = JObject.FromObject(t.Build("won"));
            Assert.Equal(2100, (double)r["decision_wait_ms"]["median"]);
            Assert.Equal(200, (double)r["prefetch"]["used_wait_ms"][0]);
        }
    }
}
