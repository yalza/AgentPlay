using System;
using System.Collections.Generic;
using AgentPlay.Contract;
using AgentPlay.Runner.Learning;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class PlaybookMergerTests
    {
        static readonly DateTime Now = new DateTime(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);

        static HelloMessage Hello()
        {
            var h = new HelloMessage { Game = "G", Version = "m3" };
            h.Events.Add(new EventInfo { Name = "guard.spotted" });
            h.Actions.Add(new ActionInfo { Name = "hide_nearest" });
            h.Actions.Add(new ActionInfo { Name = "stop", Safe = true });
            return h;
        }

        static ReflectorLesson L(string id, string verdict, string text = "Hide when spotted", ReflexCandidate reflex = null) =>
            new ReflectorLesson { Id = id, Verdict = verdict, Text = text, Evidence = "t=3", Reflex = reflex };

        static ReflexCandidate Hide() => new ReflexCandidate { When = "event:guard.spotted", Do = "hide_nearest", Args = new JObject() };

        static ReflectorOutput Out(params ReflectorLesson[] lessons) => new ReflectorOutput { Summary = "s", Lessons = new List<ReflectorLesson>(lessons) };

        [Fact]
        public void NewLessonGetsAnIdAndOneConfirmation()
        {
            var pb = new Playbook();
            var r = PlaybookMerger.Merge(pb, Out(L("", "new")), "t1.ndjson", Hello(), 3, Now);
            Assert.Equal(1, r.New);
            Assert.Equal("L1", pb.Lessons[0].Id);
            Assert.Equal(1, pb.Lessons[0].Confirmations);
            Assert.Equal(new[] { "t1.ndjson" }, pb.Lessons[0].Sources);
            Assert.Equal("m3", pb.Lessons[0].GameVersion);
            Assert.Equal(2, pb.NextId);
        }

        [Fact]
        public void UnknownConfirmedIdBecomesNewLesson()
        {
            var pb = new Playbook();
            var r = PlaybookMerger.Merge(pb, Out(L("L9", "confirmed")), "t1.ndjson", Hello(), 3, Now);
            Assert.Equal(1, r.New);
            Assert.Equal("L1", pb.Lessons[0].Id);
        }

        [Fact]
        public void UnknownContradictedIdIsIgnoredWithANote()
        {
            var pb = new Playbook();
            var r = PlaybookMerger.Merge(pb, Out(L("L9", "contradicted")), "t1.ndjson", Hello(), 3, Now);
            Assert.Empty(pb.Lessons);
            Assert.Contains(r.Notes, n => n.Contains("L9"));
        }

        [Fact]
        public void ConfirmedIdIsMatchedCaseInsensitively()
        {
            var pb = new Playbook();
            PlaybookMerger.Merge(pb, Out(L("", "new")), "t1.ndjson", Hello(), 3, Now);
            var r = PlaybookMerger.Merge(pb, Out(L("l1", "confirmed", "Hide behind cover when spotted")), "t2.ndjson", Hello(), 3, Now);
            Assert.Equal(1, r.Confirmed);
            Assert.Single(pb.Lessons);
            Assert.Equal(2, pb.Lessons[0].Confirmations);
            Assert.Equal("Hide behind cover when spotted", pb.Lessons[0].Text);
        }

        [Fact]
        public void ThirdConfirmationProposesTheReflexOnce()
        {
            var pb = new Playbook();
            PlaybookMerger.Merge(pb, Out(L("", "new", reflex: Hide())), "t1.ndjson", Hello(), 3, Now);
            Assert.Empty(PlaybookMerger.Merge(pb, Out(L("L1", "confirmed")), "t2.ndjson", Hello(), 3, Now).Proposals);
            var third = PlaybookMerger.Merge(pb, Out(L("L1", "confirmed")), "t3.ndjson", Hello(), 3, Now);
            var proposal = Assert.Single(third.Proposals);
            Assert.Equal("R-L1", proposal.Id);
            Assert.Equal("L1", proposal.LessonId);
            Assert.Equal(ReflexStatuses.Proposed, proposal.Status);
            Assert.Equal("m3", proposal.GameVersion);
            Assert.Equal("event:guard.spotted", proposal.When);
            Assert.True(pb.Lessons[0].ReflexProposed);
            Assert.Empty(PlaybookMerger.Merge(pb, Out(L("L1", "confirmed")), "t4.ndjson", Hello(), 3, Now).Proposals);
        }

        [Fact]
        public void SameTraceCountsOnce()
        {
            var pb = new Playbook();
            PlaybookMerger.Merge(pb, Out(L("", "new")), "t1.ndjson", Hello(), 3, Now);
            PlaybookMerger.Merge(pb, Out(L("L1", "confirmed"), L("L1", "confirmed")), "t2.ndjson", Hello(), 3, Now);
            PlaybookMerger.Merge(pb, Out(L("L1", "confirmed")), "t2.ndjson", Hello(), 3, Now);
            Assert.Equal(2, pb.Lessons[0].Confirmations);
        }

        [Fact]
        public void ContradictionBlocksProposal()
        {
            var pb = new Playbook();
            PlaybookMerger.Merge(pb, Out(L("", "new", reflex: Hide())), "t1.ndjson", Hello(), 3, Now);
            PlaybookMerger.Merge(pb, Out(L("L1", "contradicted")), "t2.ndjson", Hello(), 3, Now);
            PlaybookMerger.Merge(pb, Out(L("L1", "confirmed")), "t3.ndjson", Hello(), 3, Now);
            Assert.Empty(PlaybookMerger.Merge(pb, Out(L("L1", "confirmed")), "t4.ndjson", Hello(), 3, Now).Proposals);
            Assert.Equal(1, pb.Lessons[0].Contradictions);
        }

        [Fact]
        public void InvalidReflexCandidateIsDroppedWithANote()
        {
            var pb = new Playbook();
            var bad = new ReflexCandidate { When = "event:guard.spotted", Do = "stop", Args = new JObject() };
            var r = PlaybookMerger.Merge(pb, Out(L("", "new", reflex: bad)), "t1.ndjson", Hello(), 3, Now);
            Assert.Null(pb.Lessons[0].Reflex);
            Assert.Contains(r.Notes, n => n.StartsWith("L1: reflex dropped"));
        }

        [Fact]
        public void PlaybookIsCappedAtFiftyLessons()
        {
            var pb = new Playbook();
            for (int i = 0; i < 50; i++) pb.Lessons.Add(new Lesson { Id = "L" + (i + 1), Text = "t", Confirmations = 2, Updated = "2026-01-01" });
            pb.NextId = 51;
            pb.Lessons[7].Confirmations = 0;
            PlaybookMerger.Merge(pb, Out(L("", "new")), "t.ndjson", Hello(), 3, Now);
            Assert.Equal(50, pb.Lessons.Count);
            Assert.DoesNotContain(pb.Lessons, l => l.Id == "L8");
            Assert.Contains(pb.Lessons, l => l.Id == "L51");
        }
    }
}
