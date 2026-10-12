using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class PlanValidatorTests
    {
        static HelloMessage Hello()
        {
            var h = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G" };
            h.Observes.Add(new ObserveInfo { Path = "noise.value", ValueType = ValueKinds.Float });
            h.Observes.Add(new ObserveInfo { Path = "player.zone", ValueType = ValueKinds.String });
            h.Observes.Add(new ObserveInfo { Path = "player.hidden", ValueType = ValueKinds.Bool });
            h.Observes.Add(new ObserveInfo { Path = "player.pos", ValueType = ValueKinds.Vector3 });
            h.Observes.Add(new ObserveInfo
            {
                Path = "guards",
                ValueType = ValueKinds.List,
                Fields = new List<ObserveInfo>
                {
                    new ObserveInfo { Path = "distance", ValueType = ValueKinds.Float },
                    new ObserveInfo { Path = "sees_player", ValueType = ValueKinds.Bool },
                },
            });
            h.Actions.Add(new ActionInfo { Name = "goto", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } } });
            h.Actions.Add(new ActionInfo { Name = "wait", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "seconds", ValueType = ValueKinds.Float } } });
            h.Actions.Add(new ActionInfo { Name = "repeat", Params = new List<ActionParamInfo>
            {
                new ActionParamInfo { Name = "times", ValueType = ValueKinds.Int },
                new ActionParamInfo { Name = "loud", ValueType = ValueKinds.Bool },
            } });
            h.Actions.Add(new ActionInfo { Name = "stop", Safe = true });
            h.Events.Add(new EventInfo { Name = "guard.spotted", Important = true, Outcome = "bad" });
            return h;
        }

        static Plan Parse(string json) => JObject.Parse(json).ToObject<Plan>(ContractJson.Serializer);

        static List<string> Errors(string json) => PlanValidator.Validate(Parse(json), Hello());

        const string Valid =
            "{\"id\":\"p\",\"steps\":[{\"do\":\"goto\",\"args\":{\"zone\":\"a\"},\"until\":\"min(guards.distance) < 3\"}," +
            "{\"do\":\"repeat\",\"args\":{\"times\":2,\"loud\":false}},{\"do\":\"stop\"}]," +
            "\"rules\":[{\"when\":\"event:guard.spotted || any(guards.sees_player)\",\"do\":\"wait\",\"args\":{\"seconds\":1.5},\"then\":\"replan\"}]}";

        [Fact]
        public void ValidPlanHasNoErrors()
        {
            Assert.Empty(Errors(Valid));
        }

        [Fact]
        public void NullPlanIsReported()
        {
            Assert.Contains("plan: missing", PlanValidator.Validate(null, Hello()));
        }

        [Theory]
        [InlineData("{\"steps\":[{\"do\":\"stop\"}]}", "id: required")]
        [InlineData("{\"id\":\"p\",\"steps\":[]}", "steps: at least one step")]
        [InlineData("{\"id\":\"p\",\"timeout_s\":0,\"steps\":[{\"do\":\"stop\"}]}", "timeout_s:")]
        [InlineData("{\"id\":\"p\",\"timeout_s\":601,\"steps\":[{\"do\":\"stop\"}]}", "timeout_s:")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"fly\"}]}", "steps[0].do: unknown action 'fly'")]
        [InlineData("{\"id\":\"p\",\"steps\":[{}]}", "steps[0].do: required")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"goto\"}]}", "steps[0].args.zone: required")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"goto\",\"args\":{\"zone\":5}}]}", "steps[0].args.zone: expected string")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"goto\",\"args\":{\"zone\":\"a\",\"fast\":true}}]}", "steps[0].args.fast: unknown parameter")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"repeat\",\"args\":{\"times\":2.5,\"loud\":true}}]}", "steps[0].args.times: expected int")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"repeat\",\"args\":{\"times\":2,\"loud\":\"yes\"}}]}", "steps[0].args.loud: expected bool")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"wait\",\"args\":{\"seconds\":\"1\"}}]}", "steps[0].args.seconds: expected float")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"stop\",\"until\":\"a <\"}]}", "steps[0].until: Expected a value")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"stop\"}],\"rules\":[{\"do\":\"stop\",\"then\":\"next\"}]}", "rules[0].when: required")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"stop\"}],\"rules\":[{\"when\":\"player.hidden\",\"do\":\"stop\",\"then\":\"retry\"}]}", "rules[0].then: must be")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"stop\"}],\"rules\":[{\"when\":\"player.hidden\",\"then\":\"next\"}]}", "rules[0].do: required")]
        public void ReportsStructuralErrors(string json, string expected)
        {
            var errors = Errors(json);
            Assert.True(errors.Any(e => e.Contains(expected)), "expected '" + expected + "' in: " + string.Join(" | ", errors));
        }

        [Theory]
        [InlineData("noise.value > 'a'", "Cannot compare number with string")]
        [InlineData("guards.distance < 3", "can only be used inside min/max/count/any")]
        [InlineData("min(guards.sees_player) < 1", "needs a numeric field")]
        [InlineData("min(guards) < 1", "needs a numeric field")]
        [InlineData("any(guards.distance)", "needs a boolean field")]
        [InlineData("count(guards.distance) > 1", "needs a boolean field")]
        [InlineData("min(guards.speed) < 1", "has no field 'speed'")]
        [InlineData("min(noise.value) < 1", "is not a list observe")]
        [InlineData("player.zone < 'a'", "Only == and != work on string")]
        [InlineData("player.hidden > true", "Only == and != work on bool")]
        [InlineData("player.pos == 1", "vector3 values cannot be compared")]
        [InlineData("noise.value", "must be true/false")]
        [InlineData("event:nope", "Unknown event 'nope'")]
        [InlineData("nope.x > 1", "Unknown observe 'nope.x'")]
        [InlineData("!noise.value", "'!' needs a boolean")]
        [InlineData("noise.value && player.hidden", "needs booleans on both sides")]
        public void ReportsConditionTypeErrors(string condition, string expected)
        {
            var json = "{\"id\":\"p\",\"steps\":[{\"do\":\"stop\",\"until\":" + Newtonsoft.Json.JsonConvert.ToString(condition) + "}]}";
            var errors = Errors(json);
            Assert.True(errors.Any(e => e.StartsWith("steps[0].until: ") && e.Contains(expected)),
                "expected '" + expected + "' in: " + string.Join(" | ", errors));
        }

        [Fact]
        public void TooManyStepsAndRules()
        {
            var plan = new Plan { Id = "p" };
            for (int i = 0; i < PlanLimits.MaxSteps + 1; i++) plan.Steps.Add(new PlanStep { Do = "stop" });
            for (int i = 0; i < PlanLimits.MaxRules + 1; i++) plan.Rules.Add(new PlanRule { When = "player.hidden", Do = "stop", Then = "next" });
            var errors = PlanValidator.Validate(plan, Hello());
            Assert.Contains(errors, e => e.StartsWith("steps: at most"));
            Assert.Contains(errors, e => e.StartsWith("rules: at most"));
        }
    }
}
