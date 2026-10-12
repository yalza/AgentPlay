using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class PlanSchemaTests
    {
        static HelloMessage Hello()
        {
            var h = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G" };
            h.Actions.Add(new ActionInfo { Name = "goto", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } } });
            h.Actions.Add(new ActionInfo { Name = "repeat", Params = new List<ActionParamInfo>
            {
                new ActionParamInfo { Name = "times", ValueType = ValueKinds.Int },
                new ActionParamInfo { Name = "speed", ValueType = ValueKinds.Float },
                new ActionParamInfo { Name = "loud", ValueType = ValueKinds.Bool },
            } });
            h.Actions.Add(new ActionInfo { Name = "stop", Safe = true });
            return h;
        }

        static IEnumerable<JObject> AllObjects(JToken t)
        {
            if (t is JObject o)
            {
                yield return o;
                foreach (var p in o.Properties())
                    foreach (var inner in AllObjects(p.Value)) yield return inner;
            }
            else if (t is JArray a)
            {
                foreach (var item in a)
                    foreach (var inner in AllObjects(item)) yield return inner;
            }
        }

        [Fact]
        public void TopLevelRequiresEveryPlanField()
        {
            var s = PlanSchema.Build(Hello());
            Assert.Equal("object", (string)s["type"]);
            Assert.False((bool)s["additionalProperties"]);
            Assert.Equal(new[] { "id", "intent", "steps", "rules", "timeout_s" }, s["required"].Select(x => (string)x));
            Assert.Equal("number", (string)s["properties"]["timeout_s"]["type"]);
        }

        [Fact]
        public void StepsAreOneVariantPerActionWithExactArgs()
        {
            var variants = (JArray)PlanSchema.Build(Hello())["properties"]["steps"]["items"]["anyOf"];
            Assert.Equal(3, variants.Count);
            var go = variants.First(v => (string)v["properties"]["do"]["const"] == "goto");
            Assert.Equal(new[] { "do", "args", "until" }, go["required"].Select(x => (string)x));
            Assert.Equal("string", (string)go["properties"]["args"]["properties"]["zone"]["type"]);
            Assert.Equal(new[] { "zone" }, go["properties"]["args"]["required"].Select(x => (string)x));
            var rep = variants.First(v => (string)v["properties"]["do"]["const"] == "repeat");
            Assert.Equal("integer", (string)rep["properties"]["args"]["properties"]["times"]["type"]);
            Assert.Equal("number", (string)rep["properties"]["args"]["properties"]["speed"]["type"]);
            Assert.Equal("boolean", (string)rep["properties"]["args"]["properties"]["loud"]["type"]);
            var stop = variants.First(v => (string)v["properties"]["do"]["const"] == "stop");
            Assert.Empty((JObject)stop["properties"]["args"]["properties"]);
        }

        [Fact]
        public void RulesCarryWhenAndThen()
        {
            var variants = (JArray)PlanSchema.Build(Hello())["properties"]["rules"]["items"]["anyOf"];
            var go = variants.First(v => (string)v["properties"]["do"]["const"] == "goto");
            Assert.Equal(new[] { "when", "do", "args", "then" }, go["required"].Select(x => (string)x));
            Assert.Equal(new[] { "resume", "next", "replan", "abort" }, go["properties"]["then"]["enum"].Select(x => (string)x));
        }

        [Fact]
        public void EveryObjectForbidsAdditionalProperties()
        {
            foreach (var o in AllObjects(PlanSchema.Build(Hello())))
                if ((string)o["type"] == "object")
                    Assert.False((bool)o["additionalProperties"], o.ToString());
        }

        [Fact]
        public void SameHelloGivesIdenticalSchema()
        {
            Assert.Equal(PlanSchema.Build(Hello()).ToString(), PlanSchema.Build(Hello()).ToString());
        }
    }
}
