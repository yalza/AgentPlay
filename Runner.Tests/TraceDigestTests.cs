using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using AgentPlay.Runner.Learning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class TraceDigestTests
    {
        static string Header(bool withHello = true)
        {
            var o = new JObject { ["type"] = "header", ["game"] = "G", ["version"] = "v1", ["who"] = "human", ["tag"] = "play" };
            if (withHello) o["hello"] = JObject.FromObject(new HelloMessage { Game = "G", Version = "v1", SessionTag = "play" }, ContractJson.Serializer);
            return o.ToString(Formatting.None);
        }

        static string Snap(double t, double x, bool hidden) => new JObject
        {
            ["type"] = "snapshot",
            ["t"] = t,
            ["frame"] = 1,
            ["state"] = new JObject { ["player.pos"] = new JObject { ["x"] = x, ["y"] = 1.0, ["z"] = 0.0 }, ["player.hidden"] = hidden, ["items.left"] = 3 },
        }.ToString(Formatting.None);

        static string Line(JObject o) => o.ToString(Formatting.None);

        [Fact]
        public void MissingHelloIsAnError()
        {
            var d = TraceDigest.Build(new[] { Header(withHello: false) }, 1000);
            Assert.NotNull(d.Error);
            Assert.Null(d.Text);
        }

        [Fact]
        public void EmptyTraceIsAnError() => Assert.NotNull(TraceDigest.Build(new string[0], 1000).Error);

        [Fact]
        public void DiffsSnapshotsAndRoundsNumbers()
        {
            var lines = new[] { Header(), Snap(0, -8.0, false), Snap(0.5, -8.02, false), Snap(1.0, -6.5, true) };
            var d = TraceDigest.Build(lines, 10000);
            Assert.Null(d.Error);
            Assert.Equal("play", d.Tag);
            Assert.Equal("human", d.Who);
            Assert.False(d.AgentControlled);
            Assert.Equal("G", d.Hello.Game);
            var text = d.Text.Split('\n').ToList();
            Assert.StartsWith("Game: G (version v1). Player: human. Session tag: play. Length: 1.0 game seconds.", text[0]);
            Assert.Contains("t=0.00 state player.pos=(-8.0,1.0,0.0) player.hidden=false items.left=3", text);
            Assert.DoesNotContain(text, l => l.StartsWith("t=0.50"));
            Assert.Contains("t=1.00 state player.pos=(-6.5,1.0,0.0) player.hidden=true", text);
        }

        [Fact]
        public void ListsAreRenderedCompactly()
        {
            var snap = new JObject
            {
                ["type"] = "snapshot",
                ["t"] = 0.0,
                ["state"] = new JObject { ["guards"] = new JArray(new JObject { ["distance"] = 5.234, ["sees_player"] = true, ["zone"] = "" }) },
            };
            var d = TraceDigest.Build(new[] { Header(), Line(snap) }, 10000);
            Assert.Contains("guards=[{distance:5.2,sees_player:true,zone:''}]", d.Text);
        }

        [Fact]
        public void EventsPlansAndReflexesAreKept()
        {
            var lines = new List<string>
            {
                Header(),
                Line(new JObject { ["type"] = "event", ["t"] = 2.0, ["name"] = "guard.spotted", ["payload"] = new JObject { ["distance"] = 6.99 }, ["state"] = new JObject { ["player.hidden"] = false } }),
                Line(new JObject { ["type"] = "control", ["t"] = 2.5, ["who"] = "agent" }),
                Line(new JObject
                {
                    ["type"] = "plan",
                    ["t"] = 2.5,
                    ["plan"] = JObject.Parse("{\"id\":\"p1\",\"intent\":\"Hide\",\"steps\":[{\"do\":\"hide\",\"args\":{\"zone\":\"cover_1\"},\"until\":\"\"},{\"do\":\"wait\",\"args\":{\"seconds\":3},\"until\":\"x > 1\"}],\"rules\":[{\"when\":\"event:guard.spotted\",\"do\":\"hide\",\"args\":{\"zone\":\"cover_2\"},\"then\":\"replan\"}],\"timeout_s\":30}"),
                }),
                Line(new JObject { ["type"] = "plan_status", ["t"] = 2.9, ["plan_id"] = "p1", ["status"] = "rule_fired", ["step"] = 0, ["reason"] = "replan" }),
                Line(new JObject { ["type"] = "reflex", ["t"] = 3.0, ["id"] = "R-L2", ["handled_events"] = new JArray("guard.spotted") }),
                Line(new JObject { ["type"] = "reflex_end", ["t"] = 3.4, ["id"] = "R-L2", ["status"] = "done" }),
            };
            var d = TraceDigest.Build(lines, 10000);
            Assert.True(d.AgentControlled);
            Assert.Contains("t=2.00 event guard.spotted {\"distance\":7.0}", d.Text);
            Assert.Contains("t=2.00 state player.hidden=false", d.Text);
            Assert.Contains("t=2.50 agent takes control", d.Text);
            Assert.Contains("t=2.50 plan p1 \"Hide\": hide(zone:'cover_1'); wait(seconds:3) until x > 1 | rules: event:guard.spotted -> hide(zone:'cover_2') then replan", d.Text);
            Assert.Contains("t=2.90 plan p1 rule_fired step=0 replan", d.Text);
            Assert.Contains("t=3.00 reflex R-L2 fired (handled guard.spotted)", d.Text);
            Assert.Contains("t=3.40 reflex R-L2 done", d.Text);
        }

        [Fact]
        public void LongTracesDropStateLinesButKeepEvents()
        {
            var lines = new List<string> { Header() };
            for (int i = 0; i < 400; i++) lines.Add(Snap(i * 0.5, i, i % 2 == 0));
            lines.Add(Line(new JObject { ["type"] = "event", ["t"] = 999.0, ["name"] = "game.won", ["payload"] = new JObject(), ["state"] = new JObject() }));
            var d = TraceDigest.Build(lines, 4000);
            Assert.True(d.Text.Length <= 4000, "length " + d.Text.Length);
            Assert.Contains("event game.won", d.Text);
            Assert.Contains("t=0.00 state", d.Text);
        }

        [Fact]
        public void BrokenLinesAreSkipped()
        {
            var d = TraceDigest.Build(new[] { Header(), "{ not json", Snap(0, 1, false) }, 10000);
            Assert.Null(d.Error);
            Assert.Contains("t=0.00 state", d.Text);
        }
    }
}
