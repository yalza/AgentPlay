using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using AgentPlay.Runner;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class AdapterCheckTests
    {
        static HelloMessage GoodHello()
        {
            var h = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G", Version = "1", Brief = "# Brief\nWin by X." };
            h.Observes.Add(new ObserveInfo { Path = "player.pos", ValueType = "vector3" });
            h.Observes.Add(new ObserveInfo { Path = "mission.id", ValueType = "string" });
            h.Actions.Add(new ActionInfo { Name = "idle", Safe = true });
            h.Actions.Add(new ActionInfo { Name = "goto", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "target", ValueType = "string" } } });
            h.Events.Add(new EventInfo { Name = "game.won", Important = true, Outcome = "good" });
            return h;
        }

        static JObject Snap(object pos, object mission) => new JObject
        {
            ["player.pos"] = pos == null ? JValue.CreateNull() : JToken.FromObject(pos),
            ["mission.id"] = mission == null ? JValue.CreateNull() : JToken.FromObject(mission),
        };

        static List<JObject> TwoGoodSnaps() => new List<JObject> { Snap(new { x = 0, y = 0, z = 0 }, "m1"), Snap(new { x = 1, y = 0, z = 0 }, "m1") };

        static CheckItem Item(List<CheckItem> items, string name) => items.Single(i => i.Name == name);

        [Fact]
        public void GoodAdapterPassesEveryCheck()
        {
            var items = AdapterCheck.Run(GoodHello(), TwoGoodSnaps(), new string[0]);
            Assert.All(items, i => Assert.True(i.Passed, i.Name + ": " + i.Detail));
            Assert.Contains(items, i => i.Name == "observe mission.id has a value");
        }

        [Fact]
        public void ObserveNullInEverySnapshotFails()
        {
            var snaps = new List<JObject> { Snap(new { x = 0 }, null), Snap(new { x = 1 }, null) };
            Assert.False(Item(AdapterCheck.Run(GoodHello(), snaps, new string[0]), "observe mission.id has a value").Passed);
        }

        [Fact]
        public void ObserveNullOnlyInFirstSnapshotPasses()
        {
            var snaps = new List<JObject> { Snap(new { x = 0 }, null), Snap(new { x = 1 }, "m1") };
            Assert.True(Item(AdapterCheck.Run(GoodHello(), snaps, new string[0]), "observe mission.id has a value").Passed);
        }

        [Fact]
        public void ObserveMissingFromSnapshotsFails()
        {
            var snaps = new List<JObject> { new JObject { ["player.pos"] = 1 }, new JObject { ["player.pos"] = 2 } };
            Assert.False(Item(AdapterCheck.Run(GoodHello(), snaps, new string[0]), "observe mission.id has a value").Passed);
        }

        [Fact]
        public void NoSafeActionFails()
        {
            var h = GoodHello();
            h.Actions[0].Safe = false;
            Assert.False(Item(AdapterCheck.Run(h, TwoGoodSnaps(), new string[0]), "exactly one safe action").Passed);
        }

        [Fact]
        public void TwoSafeActionsFail()
        {
            var h = GoodHello();
            h.Actions[1].Safe = true;
            Assert.False(Item(AdapterCheck.Run(h, TwoGoodSnaps(), new string[0]), "exactly one safe action").Passed);
        }

        [Fact]
        public void MissingGameWonFails()
        {
            var h = GoodHello();
            h.Events.Clear();
            Assert.False(Item(AdapterCheck.Run(h, TwoGoodSnaps(), new string[0]), "declares game.won").Passed);
        }

        [Fact]
        public void EmptyBriefFails()
        {
            var h = GoodHello();
            h.Brief = "  \n";
            Assert.False(Item(AdapterCheck.Run(h, TwoGoodSnaps(), new string[0]), "brief is not empty").Passed);
        }

        [Fact]
        public void NoObservesOrNoActionsFail()
        {
            var h = GoodHello();
            h.Observes.Clear();
            h.Actions.Clear();
            var items = AdapterCheck.Run(h, TwoGoodSnaps(), new string[0]);
            Assert.False(Item(items, "has observes").Passed);
            Assert.False(Item(items, "has actions").Passed);
        }

        [Fact]
        public void UnsupportedParameterTypeFails()
        {
            var h = GoodHello();
            h.Actions[1].Params[0].ValueType = "vector3";
            Assert.False(Item(AdapterCheck.Run(h, TwoGoodSnaps(), new string[0]), "action parameter types are supported").Passed);
        }

        [Fact]
        public void UnityErrorFails()
        {
            var item = Item(AdapterCheck.Run(GoodHello(), TwoGoodSnaps(), new[] { "boom" }), "no error from Unity");
            Assert.False(item.Passed);
            Assert.Contains("boom", item.Detail);
        }

        [Fact]
        public void FewerThanTwoSnapshotsFails()
        {
            var items = AdapterCheck.Run(GoodHello(), new List<JObject> { Snap(new { x = 0 }, "m1") }, new string[0]);
            Assert.False(Item(items, "two state snapshots received").Passed);
        }
    }
}
