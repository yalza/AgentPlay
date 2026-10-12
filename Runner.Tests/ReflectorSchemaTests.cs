using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using AgentPlay.Runner.Learning;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class ReflectorSchemaTests
    {
        static HelloMessage Hello()
        {
            var hello = new HelloMessage { Game = "G" };
            hello.Actions.Add(new ActionInfo { Name = "hide_nearest" });
            hello.Actions.Add(new ActionInfo { Name = "goto", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } } });
            hello.Actions.Add(new ActionInfo { Name = "stop", Safe = true });
            return hello;
        }

        [Fact]
        public void ReflexIsNullOrANonSafeActionVariant()
        {
            var schema = ReflectorSchema.Build(Hello());
            var lesson = schema["properties"]["lessons"]["items"];
            var variants = (JArray)lesson["properties"]["reflex"]["anyOf"];
            Assert.Equal("null", (string)variants[0]["type"]);
            Assert.Equal(new[] { "hide_nearest", "goto" }, variants.Skip(1).Select(v => (string)v["properties"]["do"]["const"]));
            Assert.Equal("zone", (string)variants[2]["properties"]["args"]["required"][0]);
            Assert.False((bool)schema["additionalProperties"]);
            Assert.False((bool)lesson["additionalProperties"]);
            Assert.Equal(new[] { "new", "confirmed", "contradicted" }, lesson["properties"]["verdict"]["enum"].Values<string>());
            Assert.Equal(new[] { "id", "verdict", "text", "evidence", "reflex" }, lesson["required"].Values<string>());
        }

        [Fact]
        public void OutputParsesAndValidates()
        {
            var output = ReflectorOutput.Parse("{\"summary\":\"s\",\"lessons\":[{\"id\":\"\",\"verdict\":\"new\",\"text\":\"Hide\",\"evidence\":\"t=2\",\"reflex\":null}]}");
            Assert.Empty(output.Validate());
            Assert.Null(output.Lessons[0].Reflex);
            var bad = ReflectorOutput.Parse("{\"summary\":\"s\",\"lessons\":[{\"id\":\"\",\"verdict\":\"maybe\",\"text\":\"\",\"evidence\":\"\",\"reflex\":null}]}");
            var errors = bad.Validate();
            Assert.Contains("lessons[0].verdict: must be new, confirmed or contradicted", errors);
            Assert.Contains("lessons[0].text: required", errors);
            Assert.Contains("lessons[0].text: at most 300 characters",
                ReflectorOutput.Parse("{\"summary\":\"s\",\"lessons\":[{\"id\":\"\",\"verdict\":\"new\",\"text\":\"" + new string('x', 301) + "\",\"evidence\":\"\",\"reflex\":null}]}").Validate());
        }
    }
}
