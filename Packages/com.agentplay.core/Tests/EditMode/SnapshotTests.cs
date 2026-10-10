using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AgentPlay.Tests
{
    public class SnapshotTests
    {
        [Test]
        public void BuildsFlatSnapshotWithRoundedValues()
        {
            var model = AgentRegistry.Build(new[] { typeof(FakeGame) });
            var game = new FakeGame();
            var s = SnapshotBuilder.Build(model, t => t == typeof(FakeGame) ? game : null);

            Assert.AreEqual(12.346, (double)s["noise.value"], 1e-9);
            Assert.AreEqual(4, (int)s["sack.tiles"]);
            Assert.AreEqual(false, (bool)s["sack.full"]);
            Assert.AreEqual("thief", (string)s["player.name"]);
            Assert.AreEqual("Alert", (string)s["noise.level"]);
            Assert.AreEqual("[1.0,2.0,3.0]", s["player.pos"].ToString(Formatting.None));
            Assert.AreEqual(2, (int)s["game.round"]);
        }

        [Test]
        public void MissingInstanceGivesNullButStaticStillReads()
        {
            var model = AgentRegistry.Build(new[] { typeof(FakeGame) });
            var s = SnapshotBuilder.Build(model, t => null);
            Assert.AreEqual(JTokenType.Null, s["noise.value"].Type);
            Assert.AreEqual(2, (int)s["game.round"]);
        }

        [Test]
        public void ThrowingGetterAndNaNBecomeNullAndWarn()
        {
            var model = AgentRegistry.Build(new[] { typeof(FakeThrowingGetter) });
            var warnings = new List<string>();
            var s = SnapshotBuilder.Build(model, t => new FakeThrowingGetter(), warnings.Add);
            Assert.AreEqual(JTokenType.Null, s["explodes"].Type);
            Assert.AreEqual(JTokenType.Null, s["not.a.number"].Type);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("kaboom", warnings[0]);
        }

        [Test]
        public void PayloadSerializesVector3AndWrapsNonObjects()
        {
            Assert.IsNull(UnityJson.ToPayload(null));
            Assert.AreEqual("{\"at\":[1.0,2.0,3.0],\"n\":5}",
                UnityJson.ToPayload(new { at = new Vector3(1, 2, 3), n = 5 }).ToString(Formatting.None));
            Assert.AreEqual("{\"value\":5}", UnityJson.ToPayload(5).ToString(Formatting.None));
            Assert.AreEqual("{\"value\":\"hi\"}", UnityJson.ToPayload("hi").ToString(Formatting.None));
            Assert.AreEqual("{\"level\":\"Alert\"}", UnityJson.ToPayload(new { level = FakeLevel.Alert }).ToString(Formatting.None));
            var jo = new JObject { ["k"] = 1 };
            Assert.AreSame(jo, UnityJson.ToPayload(jo));
        }

        [Test]
        public void PayloadThatCannotSerializeBecomesErrorObject()
        {
            var p = UnityJson.ToPayload(new { bad = new ThrowsOnGet() });
            StringAssert.Contains("could not be serialized", (string)p["error"]);
        }

        [Test]
        public void UnityObjectPayloadBecomesAReference()
        {
            var go = new GameObject("fake");
            try
            {
                // Built-in component: the Editor-only test assembly cannot AddComponent its own MonoBehaviours.
                var c = go.AddComponent<LineRenderer>();
                Assert.IsTrue(c != null, "component must exist for this test to mean anything");
                var nested = UnityJson.ToPayload(new { target = c });
                Assert.AreEqual("LineRenderer", (string)nested["target"]["type"]);
                Assert.AreEqual("fake", (string)nested["target"]["name"]);
                Assert.AreEqual(c.GetInstanceID(), (int)nested["target"]["id"]);
                var direct = UnityJson.ToPayload(c);
                Assert.AreEqual("fake", (string)direct["name"]);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ColorAndQuaternionSerializeAsArrays()
        {
            Assert.AreEqual("{\"c\":[1.0,0.0,0.0,1.0],\"q\":[0.0,0.0,0.0,1.0]}",
                UnityJson.ToPayload(new { c = Color.red, q = Quaternion.identity }).ToString(Formatting.None));
        }

        [Test]
        public void OversizedPayloadIsReplacedByAnError()
        {
            var p = UnityJson.ToPayload(new { s = new string('x', UnityJson.MaxPayloadChars + 1) });
            StringAssert.Contains("too large", (string)p["error"]);
        }

        [Test]
        public void ResolverFindsSceneInstanceAndForgetsDestroyedOne()
        {
            var go = new GameObject("fake");
            try
            {
                var c = go.AddComponent<LineRenderer>();
                Assert.IsTrue(c != null, "component must exist for this test to mean anything");
                var r = new UnityInstanceResolver();
                Assert.AreSame(c, r.Resolve(typeof(LineRenderer)));
                Object.DestroyImmediate(go);
                go = null;
                Assert.IsNull(r.Resolve(typeof(LineRenderer)));
                Assert.IsNull(r.Resolve(typeof(FakeGame)), "plain C# types cannot be resolved");
            }
            finally
            {
                if (go != null) Object.DestroyImmediate(go);
            }
        }

        class ThrowsOnGet
        {
            public int Value => throw new System.InvalidOperationException("nope");
        }
    }
}
