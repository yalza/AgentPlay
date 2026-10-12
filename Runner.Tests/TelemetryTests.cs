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
    }
}
