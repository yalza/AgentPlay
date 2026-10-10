using System.Linq;
using AgentPlay.Contract;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class RegistryTests
    {
        [Test]
        public void CollectsObservesSortedByPathWithKinds()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeGame) });
            CollectionAssert.IsEmpty(m.Errors);
            CollectionAssert.AreEqual(
                new[] { "game.round", "noise.level", "noise.value", "player.name", "player.pos", "sack.full", "sack.tiles" },
                m.Observes.Select(o => o.Path).ToArray());
            var kinds = m.Observes.ToDictionary(o => o.Path, o => o.ValueKind);
            Assert.AreEqual(ValueKinds.Float, kinds["noise.value"]);
            Assert.AreEqual(ValueKinds.Int, kinds["sack.tiles"]);
            Assert.AreEqual(ValueKinds.Bool, kinds["sack.full"]);
            Assert.AreEqual(ValueKinds.String, kinds["player.name"]);
            Assert.AreEqual(ValueKinds.Enum, kinds["noise.level"]);
            Assert.AreEqual(ValueKinds.Vector3, kinds["player.pos"]);
            Assert.IsTrue(m.Observes.Single(o => o.Path == "game.round").IsStatic);
        }

        [Test]
        public void CollectsActionsWithParamsAndSafeFlag()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeGame) });
            CollectionAssert.AreEqual(new[] { "goto", "hide" }, m.Actions.Select(a => a.Name).ToArray());
            var go = m.Actions[0];
            CollectionAssert.AreEqual(new[] { "zone:string", "speed:float" }, go.Params.Select(p => p.Name + ":" + p.ValueType).ToArray());
            Assert.IsFalse(go.Safe);
            Assert.IsTrue(m.Actions[1].Safe);
        }

        [Test]
        public void ReadsGameNameAndStaticVersion()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeGame) });
            Assert.AreEqual("FakeGame", m.GameName);
            Assert.AreEqual("7", m.GameVersion);
        }

        [Test]
        public void ResolvesVersionFromStaticMethod()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeDynamicVersion) });
            Assert.AreEqual("hash-abc", m.GameVersion);
            CollectionAssert.IsEmpty(m.Errors);
        }

        [Test]
        public void ReportsThrowingVersionMemberWithoutThrowing()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeThrowingVersion) });
            Assert.AreEqual("", m.GameVersion);
            Assert.IsTrue(m.Errors.Any(e => e.Contains("no config")), string.Join("\n", m.Errors));
        }

        [Test]
        public void ReportsEveryInvalidDeclarationAndSkipsIt()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeGame), typeof(FakeBroken) });
            string all = string.Join("\n", m.Errors);
            StringAssert.Contains("Duplicate observe path 'noise.value'", all);
            StringAssert.Contains("empty path", all);
            StringAssert.Contains("unsupported type Object", all);
            StringAssert.Contains("'write.only'", all);
            StringAssert.Contains("'wrong.return'", all);
            StringAssert.Contains("parameter 'v'", all);
            StringAssert.Contains("Duplicate action 'goto'", all);
            StringAssert.Contains("Only one safe action", all);
            Assert.AreEqual(7, m.Observes.Count, "broken observes must be skipped");
            Assert.AreEqual(2, m.Actions.Count, "broken actions must be skipped");
        }

        [Test]
        public void ReportsSecondAgentGame()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeGame), typeof(FakeSecondGame) });
            Assert.AreEqual("FakeGame", m.GameName);
            Assert.IsTrue(m.Errors.Any(e => e.Contains("More than one [AgentGame]")));
        }

        [Test]
        public void ToHelloUsesFallbackNameWhenNoAgentGame()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeThrowingGetter) });
            var hello = AgentRegistry.ToHello(m, "ProductName");
            Assert.AreEqual(ProtocolVersion.Current, hello.Protocol);
            Assert.AreEqual("ProductName", hello.Game);
            Assert.AreEqual("", hello.Version);
            CollectionAssert.AreEqual(new[] { Modes.TurnBased, Modes.Realtime }, hello.Modes);
            Assert.AreEqual(2, hello.Observes.Count);
        }

        [Test]
        public void ScannerSkipsTestAssembliesAndRuntimeItself()
        {
            Assert.IsFalse(TypeScanner.ShouldScan(typeof(FakeGame).Assembly), "test assembly references nunit");
            Assert.IsFalse(TypeScanner.ShouldScan(typeof(AgentObserveAttribute).Assembly), "runtime assembly");
            CollectionAssert.DoesNotContain(TypeScanner.FindAnnotatedTypes(), typeof(FakeGame));
        }

        [Test]
        public void IsAnnotatedDetectsAnyAgentAttribute()
        {
            Assert.IsTrue(TypeScanner.IsAnnotated(typeof(FakeGame)));
            Assert.IsTrue(TypeScanner.IsAnnotated(typeof(FakeSecondGame)));
            Assert.IsTrue(TypeScanner.IsAnnotated(typeof(FakeThrowingGetter)));
            Assert.IsFalse(TypeScanner.IsAnnotated(typeof(string)));
        }
    }
}
