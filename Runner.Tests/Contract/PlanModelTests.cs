using System.Collections.Generic;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class PlanModelTests
    {
        const string PlanJson =
            "{\"id\":\"p-1\",\"intent\":\"grab\",\"timeout_s\":30," +
            "\"steps\":[{\"do\":\"goto\",\"args\":{\"zone\":\"item_a\"},\"until\":\"x > 1\"}]," +
            "\"rules\":[{\"when\":\"event:guard.spotted\",\"do\":\"hide\",\"args\":{\"zone\":\"cover_1\"},\"then\":\"replan\"}]}";

        [Fact]
        public void PlanParsesFromSnakeCaseJson()
        {
            var plan = JObject.Parse(PlanJson).ToObject<Plan>(ContractJson.Serializer);
            Assert.Equal("p-1", plan.Id);
            Assert.Equal(30.0, plan.TimeoutSeconds);
            Assert.Equal("goto", plan.Steps[0].Do);
            Assert.Equal("item_a", (string)plan.Steps[0].Args["zone"]);
            Assert.Equal("x > 1", plan.Steps[0].Until);
            Assert.Equal("replan", plan.Rules[0].Then);
            Assert.Equal("cover_1", (string)plan.Rules[0].Args["zone"]);
        }

        [Fact]
        public void PlanMessageRoundTrips()
        {
            var plan = JObject.Parse(PlanJson).ToObject<Plan>(ContractJson.Serializer);
            var line = MessageCodec.Encode(MessageTypes.Plan, 3, new PlanMessage { Plan = plan });
            Assert.Contains("\"timeout_s\":30", line);
            var back = MessageCodec.Decode(line).As<PlanMessage>().Plan;
            Assert.Equal("hide", back.Rules[0].Do);
        }

        [Fact]
        public void PlanDefaultsToEmptyListsAndSixtySeconds()
        {
            var plan = JObject.Parse("{\"id\":\"p\"}").ToObject<Plan>(ContractJson.Serializer);
            Assert.Empty(plan.Steps);
            Assert.Empty(plan.Rules);
            Assert.Equal(60.0, plan.TimeoutSeconds);
        }

        [Fact]
        public void HelloV2CarriesBriefEventsAndListFields()
        {
            var hello = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G", Brief = "Steal things", ProjectRoot = "C:/Game" };
            hello.Observes.Add(new ObserveInfo
            {
                Path = "guards",
                ValueType = ValueKinds.List,
                Fields = new List<ObserveInfo> { new ObserveInfo { Path = "distance", ValueType = ValueKinds.Float } },
            });
            hello.Events.Add(new EventInfo { Name = "guard.spotted", Important = true, Outcome = "bad" });

            var back = MessageCodec.Decode(MessageCodec.Encode(MessageTypes.Hello, 1, hello)).As<HelloMessage>();

            Assert.Equal(2, back.Protocol);
            Assert.Equal("Steal things", back.Brief);
            Assert.Equal("C:/Game", back.ProjectRoot);
            Assert.Equal("distance", back.Observes[0].Fields[0].Path);
            Assert.True(back.Events[0].Important);
        }

        [Fact]
        public void PausedCarriesState()
        {
            var line = MessageCodec.Encode(MessageTypes.Paused, 1, new PausedMessage { Reason = "plan", T = 2, State = new JObject { ["x"] = 1 } });
            Assert.Equal(1, (int)MessageCodec.Decode(line).As<PausedMessage>().State["x"]);
        }

        [Fact]
        public void PlanStatusUsesSnakeCase()
        {
            var line = MessageCodec.Encode(MessageTypes.PlanStatus, 1,
                new PlanStatusMessage { PlanId = "p", Status = PlanStatuses.PlanFailed, Step = 2, Reason = "timeout" });
            var o = JObject.Parse(line);
            Assert.Equal("p", (string)o["plan_id"]);
            Assert.Equal("plan_failed", (string)o["status"]);
            Assert.Equal(2, (int)o["step"]);
        }

        [Theory]
        [InlineData("resume", true)]
        [InlineData("next", true)]
        [InlineData("replan", true)]
        [InlineData("abort", true)]
        [InlineData("retry", false)]
        [InlineData(null, false)]
        public void RuleThenKnowsFourValues(string value, bool known)
        {
            Assert.Equal(known, RuleThen.IsKnown(value));
        }
    }
}
