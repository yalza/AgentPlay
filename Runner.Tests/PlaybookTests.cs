using System;
using System.Collections.Generic;
using System.IO;
using AgentPlay.Runner.Learning;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class PlaybookTests
    {
        static string Root()
        {
            var r = Path.Combine(Path.GetTempPath(), "agentplay-pb-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(r);
            return r;
        }

        [Fact]
        public void MissingPlaybookIsEmpty() => Assert.Empty(PlaybookStore.Load(Root()).Lessons);

        [Fact]
        public void SaveWritesJsonAndMarkdown()
        {
            var root = Root();
            var pb = new Playbook();
            pb.Lessons.Add(new Lesson { Id = "L1", Text = "Hide when spotted", Confirmations = 3, Sources = { "a.ndjson" }, GameVersion = "m3" });
            PlaybookStore.Save(root, pb);
            Assert.Equal("L1", PlaybookStore.Load(root).Lessons[0].Id);
            var md = File.ReadAllText(Path.Combine(root, "AgentPlay", "playbook.md"));
            Assert.Contains("## L1", md);
            Assert.Contains("Hide when spotted", md);
            Assert.Contains("confirmed 3", md);
        }

        [Fact]
        public void CorruptPlaybookThrows()
        {
            var root = Root();
            Directory.CreateDirectory(Path.Combine(root, "AgentPlay"));
            File.WriteAllText(Path.Combine(root, "AgentPlay", "playbook.json"), "{ nope");
            Assert.Throws<InvalidDataException>(() => PlaybookStore.Load(root));
        }

        [Fact]
        public void ForPromptOrdersByScoreAndCaps()
        {
            var pb = new Playbook();
            for (int i = 0; i < 25; i++) pb.Lessons.Add(new Lesson { Id = "L" + i, Text = "t", Confirmations = i, Contradictions = i % 2 });
            var top = PlaybookStore.ForPrompt(pb);
            Assert.Equal(20, top.Count);
            Assert.Equal("L24", top[0].Id);
        }

        [Fact]
        public void LedgerRoundTrips()
        {
            var root = Root();
            ReflectedLedger.Save(root, new HashSet<string> { "b.ndjson", "a.ndjson" });
            var back = ReflectedLedger.Load(root);
            Assert.Contains("a.ndjson", back);
            Assert.Contains("B.NDJSON", back);
        }
    }
}
