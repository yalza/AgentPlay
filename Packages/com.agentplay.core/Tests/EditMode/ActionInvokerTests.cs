using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class FakeActor
    {
        public readonly List<string> Calls = new List<string>();
        public AgentActionContext LastContext;

        [AgentAction("move")]
        public AgentActionStatus Move(AgentActionContext ctx, string zone, float speed, int times, bool run)
        {
            LastContext = ctx;
            Calls.Add(zone + "|" + speed.ToString(CultureInfo.InvariantCulture) + "|" + times + "|" + run);
            return AgentActionStatus.Running;
        }

        [AgentAction("boom")] public AgentActionStatus Boom() => throw new InvalidOperationException("kaput");

        [AgentAction("ping")] public static AgentActionStatus Ping() => AgentActionStatus.Done;
    }

    public class ActionInvokerTests
    {
        FakeActor _actor;
        ActionInvoker _invoker;

        [SetUp]
        public void SetUp()
        {
            _actor = new FakeActor();
            _invoker = new ActionInvoker(AgentRegistry.Build(new[] { typeof(FakeActor) }), t => t == typeof(FakeActor) ? _actor : null);
        }

        static JObject Args(string json) => JObject.Parse(json);

        [Test]
        public void ConvertsArgumentsAndPassesContext()
        {
            var ctx = new AgentActionContext();
            var status = _invoker.Run("move", Args("{\"zone\":\"a\",\"speed\":1.5,\"times\":3,\"run\":true}"), ctx);
            Assert.AreEqual(AgentActionStatus.Running, status);
            CollectionAssert.AreEqual(new[] { "a|1.5|3|True" }, _actor.Calls);
            Assert.AreSame(ctx, _actor.LastContext);
        }

        [Test]
        public void IntegerArgumentIsAcceptedForFloat()
        {
            _invoker.Run("move", Args("{\"zone\":\"a\",\"speed\":2,\"times\":1,\"run\":false}"), new AgentActionContext());
            CollectionAssert.AreEqual(new[] { "a|2|1|False" }, _actor.Calls);
        }

        [TestCase("{\"speed\":1,\"times\":1,\"run\":true}", "missing argument 'zone'")]
        [TestCase("{\"zone\":5,\"speed\":1,\"times\":1,\"run\":true}", "argument 'zone' expected a string")]
        [TestCase("{\"zone\":\"a\",\"speed\":\"x\",\"times\":1,\"run\":true}", "argument 'speed' expected a number")]
        [TestCase("{\"zone\":\"a\",\"speed\":1,\"times\":2.5,\"run\":true}", "argument 'times' expected a whole number")]
        [TestCase("{\"zone\":\"a\",\"speed\":1,\"times\":1,\"run\":\"yes\"}", "argument 'run' expected true or false")]
        [TestCase("{\"zone\":\"a\",\"speed\":1,\"times\":99999999999,\"run\":true}", "argument 'times'")]
        public void BadArgumentsThrowActionException(string json, string expected)
        {
            var e = Assert.Throws<AgentActionException>(() => _invoker.Run("move", Args(json), new AgentActionContext()));
            StringAssert.Contains(expected, e.Message);
        }

        [Test]
        public void ThrowingActionIsWrapped()
        {
            var e = Assert.Throws<AgentActionException>(() => _invoker.Run("boom", null, new AgentActionContext()));
            StringAssert.Contains("kaput", e.Message);
        }

        [Test]
        public void UnknownActionAndMissingInstance()
        {
            StringAssert.Contains("Unknown action 'fly'",
                Assert.Throws<AgentActionException>(() => _invoker.Run("fly", null, new AgentActionContext())).Message);
            var orphan = new ActionInvoker(AgentRegistry.Build(new[] { typeof(FakeActor) }), t => null);
            StringAssert.Contains("No FakeActor instance",
                Assert.Throws<AgentActionException>(() => orphan.Run("move", Args("{\"zone\":\"a\",\"speed\":1,\"times\":1,\"run\":true}"), new AgentActionContext())).Message);
        }

        [Test]
        public void StaticActionNeedsNoInstance()
        {
            var orphan = new ActionInvoker(AgentRegistry.Build(new[] { typeof(FakeActor) }), t => null);
            Assert.AreEqual(AgentActionStatus.Done, orphan.Run("ping", null, new AgentActionContext()));
            Assert.IsTrue(orphan.Has("ping"));
            Assert.IsFalse(orphan.Has("fly"));
            Assert.IsFalse(orphan.Has(null));
        }
    }
}
