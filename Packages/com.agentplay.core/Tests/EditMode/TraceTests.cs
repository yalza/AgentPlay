using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class TraceTests
    {
        static List<JObject> Lines(StringWriter sw)
        {
            var settings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };
            var result = new List<JObject>();
            foreach (var line in sw.ToString().Split('\n'))
                if (line.Length > 0) result.Add(JsonConvert.DeserializeObject<JObject>(line, settings));
            return result;
        }

        [Test]
        public void WritesHeaderEventAndSnapshotAsOneJsonPerLine()
        {
            var sw = new StringWriter();
            using (var w = new TraceWriter(sw))
            {
                w.WriteHeader("G", "v1", "human", "play", new DateTime(2026, 10, 11, 1, 2, 3, DateTimeKind.Utc));
                w.WriteEvent(new AgentEventRecord("tile.pried", new JObject { ["n"] = 1 }, true, AgentOutcome.Good, 3.5, 210),
                    new JObject { ["noise.value"] = 40.0 });
                w.WriteSnapshot(4.0, 240, new JObject { ["noise.value"] = 41.0 });
            }
            var lines = Lines(sw);
            Assert.AreEqual(3, lines.Count);

            Assert.AreEqual("header", (string)lines[0]["type"]);
            Assert.AreEqual("human", (string)lines[0]["who"]);
            Assert.AreEqual("play", (string)lines[0]["tag"]);
            StringAssert.StartsWith("2026-10-11T01:02:03", (string)lines[0]["started"]);

            Assert.AreEqual("event", (string)lines[1]["type"]);
            Assert.AreEqual("tile.pried", (string)lines[1]["name"]);
            Assert.AreEqual("good", (string)lines[1]["outcome"]);
            Assert.AreEqual(true, (bool)lines[1]["important"]);
            Assert.AreEqual(210, (long)lines[1]["frame"]);
            Assert.AreEqual(1, (int)lines[1]["payload"]["n"]);
            Assert.AreEqual(40.0, (double)lines[1]["state"]["noise.value"]);

            Assert.AreEqual("snapshot", (string)lines[2]["type"]);
            Assert.AreEqual(4.0, (double)lines[2]["t"]);
        }

        [Test]
        public void EventWithoutPayloadWritesNull()
        {
            var sw = new StringWriter();
            using (var w = new TraceWriter(sw))
                w.WriteEvent(new AgentEventRecord("x", null, false, AgentOutcome.Neutral, 0, 0), new JObject());
            Assert.AreEqual(JTokenType.Null, Lines(sw)[0]["payload"].Type);
        }

        [Test]
        public void PathUsesTimestampWhoAndTagAndAvoidsCollisions()
        {
            var now = new DateTime(2026, 10, 11, 9, 5, 7);
            string root = Path.Combine(Path.GetTempPath(), "Game");
            string expected = Path.Combine(root, "AgentPlay", "Traces", "20261011-090507-human-debug.ndjson");
            Assert.AreEqual(expected, TracePaths.Build(root, now, "human", "debug", _ => false));

            var taken = new HashSet<string> { expected };
            string second = TracePaths.Build(root, now, "human", "debug", taken.Contains);
            Assert.AreEqual(Path.Combine(root, "AgentPlay", "Traces", "20261011-090507-human-debug-2.ndjson"), second);
        }

        [Test]
        public void OpenCreatesDirectoryAndWritesImmediately()
        {
            string dir = Path.Combine(Path.GetTempPath(), "agentplay-trace-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "a", "t.ndjson");
            try
            {
                var w = TracePaths.Open(path);
                w.WriteSnapshot(1, 1, new JObject());
                string text;
                using (var read = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                    text = read.ReadToEnd();
                StringAssert.Contains("\"snapshot\"", text, "AutoFlush must write without Dispose");
                w.Dispose();
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void IntervalTimerFiresFirstTickThenEveryInterval()
        {
            var t = new IntervalTimer(2.0);
            Assert.IsTrue(t.Tick(0.5));
            Assert.IsFalse(t.Tick(1.0));
            Assert.IsFalse(t.Tick(2.4));
            Assert.IsTrue(t.Tick(2.5));
            Assert.IsFalse(t.Tick(3.0));
            Assert.IsTrue(t.Tick(10.0));
            Assert.IsFalse(t.Tick(11.9));
        }
    }
}
