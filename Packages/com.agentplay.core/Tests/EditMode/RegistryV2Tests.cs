using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using Newtonsoft.Json;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class RegistryV2Tests
    {
        [Test]
        public void ListObserveCollectsFieldsAndHidesElementTypeFromTopLevel()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeHeist), typeof(FakeGuardView) });
            CollectionAssert.IsEmpty(m.Errors);
            CollectionAssert.AreEqual(new[] { "guards" }, m.Observes.Select(o => o.Path).ToArray());
            var guards = m.Observes[0];
            Assert.AreEqual(ValueKinds.List, guards.ValueKind);
            CollectionAssert.AreEqual(new[] { "distance", "sees_player" }, guards.Fields.Select(f => f.Path).ToArray());
            Assert.AreEqual(typeof(FakeGuardView), guards.ElementType);
        }

        [Test]
        public void ActionContextParameterIsNotAPlanParameter()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeHeist) });
            var go = m.Actions.Single(a => a.Name == "goto");
            Assert.IsTrue(go.TakesContext);
            CollectionAssert.AreEqual(new[] { "zone" }, go.Params.Select(p => p.Name).ToArray());
            Assert.AreEqual("stop", m.SafeAction);
        }

        [Test]
        public void EventInfosAreCollectedSorted()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeHeist) });
            CollectionAssert.AreEqual(new[] { "game.won", "guard.spotted" }, m.Events.Select(e => e.Name).ToArray());
            var spotted = m.Events[1];
            Assert.IsTrue(spotted.Important);
            Assert.AreEqual("bad", spotted.Outcome);
            Assert.AreEqual("Guard sees the player", spotted.Description);
            Assert.AreEqual("good", m.Events[0].Outcome);
        }

        [Test]
        public void InvalidV2DeclarationsAreReported()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeBadV2) });
            string all = string.Join("\n", m.Errors);
            StringAssert.Contains("'nothing': element type Int32 has no [AgentObserve] members", all);
            StringAssert.Contains("'notlist' must be an IEnumerable<T>", all);
            StringAssert.Contains("AgentActionContext must be the first parameter", all);
            StringAssert.Contains("Safe action 'safe.args' must take no parameters", all);
            Assert.IsNull(m.SafeAction);
        }

        [Test]
        public void DuplicateEventIsReported()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeDupEvents) });
            StringAssert.Contains("Duplicate event 'dup'", string.Join("\n", m.Errors));
            Assert.AreEqual(1, m.Events.Count);
        }

        [Test]
        public void HelloCarriesListFieldsAndEvents()
        {
            var hello = AgentRegistry.ToHello(AgentRegistry.Build(new[] { typeof(FakeHeist) }), "P");
            var guards = hello.Observes.Single(o => o.Path == "guards");
            Assert.AreEqual(ValueKinds.List, guards.ValueType);
            Assert.AreEqual("float", guards.Fields.Single(f => f.Path == "distance").ValueType);
            Assert.AreEqual(2, hello.Events.Count);
            Assert.IsNull(hello.Observes.Single(o => o.Path == "guards").Fields.Single(f => f.Path == "sees_player").Fields);
        }

        [Test]
        public void SnapshotOfListObserve()
        {
            var model = AgentRegistry.Build(new[] { typeof(FakeHeist) });
            var heist = new FakeHeist();
            var s = SnapshotBuilder.Build(model, t => t == typeof(FakeHeist) ? heist : null);
            Assert.AreEqual("[{\"distance\":4.25,\"sees_player\":false},{\"distance\":9.0,\"sees_player\":true}]",
                s["guards"].ToString(Formatting.None));
        }

        [Test]
        public void SnapshotListIsCappedWithOneWarning()
        {
            var model = AgentRegistry.Build(new[] { typeof(FakeManyGuards) });
            var warnings = new List<string>();
            var s = SnapshotBuilder.Build(model, t => new FakeManyGuards(), warnings.Add);
            Assert.AreEqual(SnapshotBuilder.MaxListItems, ((Newtonsoft.Json.Linq.JArray)s["crowd"]).Count);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("more than 16", warnings[0]);
        }

        [Test]
        public void NullListBecomesEmptyArray()
        {
            var model = AgentRegistry.Build(new[] { typeof(FakeHeist) });
            var heist = new FakeHeist { GuardList = null };
            var s = SnapshotBuilder.Build(model, t => heist);
            Assert.AreEqual("[]", s["guards"].ToString(Formatting.None));
        }

        [Test]
        public void EventOnlyClassIsAnnotated()
        {
            Assert.IsTrue(TypeScanner.IsAnnotated(typeof(FakeDupEvents)));
            Assert.IsTrue(TypeScanner.IsAnnotated(typeof(FakeManyGuards)));
        }
    }
}
