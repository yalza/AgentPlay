using System.Collections.Generic;
using AgentPlay.Contract;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class ReflexTests
    {
        static HelloMessage Hello()
        {
            var h = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G", Version = "v1" };
            h.Observes.Add(new ObserveInfo { Path = "player.hidden", ValueType = ValueKinds.Bool });
            h.Events.Add(new EventInfo { Name = "guard.spotted", Important = true, Outcome = "bad" });
            h.Actions.Add(new ActionInfo { Name = "hide_nearest" });
            h.Actions.Add(new ActionInfo { Name = "goto", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } } });
            h.Actions.Add(new ActionInfo { Name = "stop", Safe = true });
            return h;
        }

        static Reflex Good() => new Reflex { Id = "R-L1", When = "event:guard.spotted", Do = "hide_nearest", Args = new JObject(), Status = ReflexStatuses.Proposed };

        [Fact]
        public void ValidReflexHasNoErrors() => Assert.Empty(ReflexValidator.Validate(Good(), Hello()));

        [Theory]
        [InlineData("", "event:guard.spotted", "hide_nearest", 1, "id: required")]
        [InlineData("R", "", "hide_nearest", 1, "when: required")]
        [InlineData("R", "nope > 1", "hide_nearest", 1, "when: Unknown observe 'nope'")]
        [InlineData("R", "event:guard.spotted", "fly", 1, "do: unknown action 'fly'")]
        [InlineData("R", "event:guard.spotted", "stop", 1, "do: the safe action cannot be a reflex")]
        [InlineData("R", "event:guard.spotted", "hide_nearest", 61, "cooldown_s: must be between 0 and 60")]
        [InlineData("R", "event:guard.spotted", "goto", 1, "args.zone: required")]
        public void InvalidReflexesAreReported(string id, string when, string action, double cooldown, string expected)
        {
            var r = new Reflex { Id = id, When = when, Do = action, Args = new JObject(), CooldownSeconds = cooldown };
            var errors = ReflexValidator.Validate(r, Hello());
            Assert.True(errors.Exists(e => e.StartsWith(expected)), "expected '" + expected + "' in: " + string.Join(" | ", errors));
        }

        [Fact]
        public void PlanValidatorMessagesKeepTheirPrefix()
        {
            var plan = new Plan { Id = "p", Steps = new List<PlanStep> { new PlanStep { Do = "fly" } }, TimeoutSeconds = 10 };
            Assert.Contains("steps[0].do: unknown action 'fly'", PlanValidator.Validate(plan, Hello()));
        }

        [Fact]
        public void FileRoundTripsWithSnakeCaseNames()
        {
            var file = new ReflexFile();
            file.Reflexes.Add(Good());
            string json = JsonConvert.SerializeObject(file, ContractJson.Settings);
            Assert.Contains("\"cooldown_s\":1", json);
            Assert.Contains("\"eval_window_s\":10", json);
            var back = JsonConvert.DeserializeObject<ReflexFile>(json, ContractJson.Settings);
            Assert.Equal("R-L1", back.Reflexes[0].Id);
            Assert.Equal(0.3, back.Policy.MaxFailRate);
        }

        [Fact]
        public void ApproveRejectDisableReenable()
        {
            var r = Good();
            Assert.True(ReflexLifecycle.Approve(r, "v1"));
            Assert.Equal(ReflexStatuses.Active, r.Status);
            Assert.Equal("v1", r.GameVersion);
            Assert.False(ReflexLifecycle.Approve(r, "v1"));
            Assert.True(ReflexLifecycle.Disable(r));
            Assert.Equal(ReflexStatuses.Suspended, r.Status);
            Assert.Equal("disabled by user", r.Note);
            r.Stats.Fired = 7;
            r.Stats.Bad = 4;
            Assert.True(ReflexLifecycle.Reenable(r, "v2"));
            Assert.Equal(ReflexStatuses.Active, r.Status);
            Assert.Equal(0, r.Stats.Fired);
            Assert.Equal("v2", r.GameVersion);
            Assert.True(ReflexLifecycle.Reject(r));
            Assert.Equal(ReflexStatuses.Rejected, r.Status);
            Assert.False(ReflexLifecycle.Reject(r));
        }

        [Fact]
        public void VersionChangeMovesActiveToNeedsReview()
        {
            var file = new ReflexFile();
            var a = Good();
            ReflexLifecycle.Approve(a, "v1");
            var p = Good();
            p.Id = "R-L2";
            file.Reflexes.Add(a);
            file.Reflexes.Add(p);
            Assert.Empty(ReflexLifecycle.MarkVersionChanges(file, "v1"));
            Assert.Equal(new[] { "R-L1" }, ReflexLifecycle.MarkVersionChanges(file, "v2"));
            Assert.Equal(ReflexStatuses.NeedsReview, a.Status);
            Assert.Equal("game version changed from v1 to v2", a.Note);
            Assert.Equal(ReflexStatuses.Proposed, p.Status);
            Assert.True(ReflexLifecycle.Approve(a, "v2"));
        }

        [Fact]
        public void SuspendsWhenFailRateExceedsPolicyAfterMinFires()
        {
            var r = Good();
            ReflexLifecycle.Approve(r, "v1");
            var policy = new ReflexPolicy();
            bool[] outcomes = { false, true, false, true };
            foreach (var bad in outcomes)
            {
                ReflexLifecycle.RecordFired(r);
                Assert.False(ReflexLifecycle.RecordOutcome(r, bad, policy));
            }
            ReflexLifecycle.RecordFired(r);
            Assert.True(ReflexLifecycle.RecordOutcome(r, false, policy));
            Assert.Equal(ReflexStatuses.Suspended, r.Status);
            Assert.Equal("fail rate 0.40 after 5 outcomes", r.Note);
            Assert.Equal(5, r.Stats.Fired);
            Assert.Equal(3, r.Stats.Good);
        }

        [Fact]
        public void MonitorResolvesWindows()
        {
            var m = new ReflexMonitor(new ReflexPolicy { EvalWindowSeconds = 10 });
            m.OnFired("A", 1);
            m.OnFired("B", 5);
            Assert.Empty(m.Tick(8));
            m.OnBadEvent();
            var bad = m.Tick(8.1);
            Assert.Equal(2, bad.Count);
            Assert.All(bad, o => Assert.True(o.Bad));
            m.OnFired("A", 20);
            Assert.Empty(m.Tick(29.9));
            var good = m.Tick(30);
            Assert.Single(good);
            Assert.False(good[0].Bad);
            m.OnFired("C", 40);
            m.OnFailed("C");
            var failed = m.Tick(40.1);
            Assert.Single(failed);
            Assert.True(failed[0].Bad);
            Assert.Empty(m.Tick(60));
        }

        [Fact]
        public void AtomicWriteReplacesExistingFile()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agentplay-atomic-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "a.json");
            AtomicFile.WriteAllText(path, "one");
            AtomicFile.WriteAllText(path, "two");
            Assert.Equal("two", System.IO.File.ReadAllText(path));
            Assert.Single(System.IO.Directory.GetFiles(dir));
        }
    }
}
