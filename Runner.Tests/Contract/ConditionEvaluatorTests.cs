using System.Collections.Generic;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class ConditionEvaluatorTests
    {
        static readonly JObject Snapshot = JObject.Parse(
            "{\"noise.value\":40,\"player.zone\":\"exit\",\"player.hidden\":true,\"maybe\":null,\"pos\":[1,2,3]," +
            "\"guards\":[{\"distance\":6.5,\"sees_player\":false},{\"distance\":2.0,\"sees_player\":true}]," +
            "\"empty\":[]}");

        static bool Eval(string text, params string[] events) =>
            ConditionEvaluator.Evaluate(ConditionParser.Parse(text), new SnapshotContext(Snapshot, new HashSet<string>(events)));

        [Theory]
        [InlineData("noise.value > 30", true)]
        [InlineData("noise.value >= 40 && noise.value <= 40", true)]
        [InlineData("noise.value < 40", false)]
        [InlineData("player.zone == 'exit'", true)]
        [InlineData("player.zone != 'start'", true)]
        [InlineData("player.hidden", true)]
        [InlineData("!player.hidden", false)]
        [InlineData("player.hidden == true", true)]
        [InlineData("min(guards.distance) < 3", true)]
        [InlineData("max(guards.distance) > 6", true)]
        [InlineData("count(guards) == 2", true)]
        [InlineData("count(guards.sees_player) == 1", true)]
        [InlineData("any(guards.sees_player)", true)]
        [InlineData("any(guards)", true)]
        [InlineData("missing.path > 0", false)]
        [InlineData("!(missing.path > 0)", true)]
        [InlineData("!missing.path", false)]
        [InlineData("maybe == 1", false)]
        [InlineData("pos > 1", false)]
        [InlineData("player.zone > 1", false)]
        [InlineData("noise.value > 'a'", false)]
        [InlineData("min(empty.d) < 1000", false)]
        [InlineData("max(empty.d) > -1000", false)]
        [InlineData("min(guards) < 3", false)]
        [InlineData("any(empty)", false)]
        [InlineData("count(empty) == 0", true)]
        [InlineData("count(nothing) == 0", false)]
        [InlineData("true || missing > 1", true)]
        [InlineData("false && true", false)]
        public void EvaluatesAgainstSnapshot(string text, bool expected)
        {
            Assert.Equal(expected, Eval(text));
        }

        [Fact]
        public void EventIsTrueOnlyWhenEmittedThisFrame()
        {
            Assert.True(Eval("event:guard.spotted", "guard.spotted"));
            Assert.False(Eval("event:guard.spotted", "other"));
            Assert.False(Eval("event:guard.spotted"));
        }

        [Fact]
        public void NullSnapshotAndNullEventsAreTreatedAsEmpty()
        {
            var ctx = new SnapshotContext(null, null);
            Assert.False(ConditionEvaluator.Evaluate(ConditionParser.Parse("x > 1"), ctx));
            Assert.False(ConditionEvaluator.Evaluate(ConditionParser.Parse("event:a"), ctx));
        }

        [Fact]
        public void FindListSplitsLongestListPrefix()
        {
            var ctx = new SnapshotContext(Snapshot, null);
            Assert.True(ConditionEvaluator.FindList(ctx, "guards.distance", out var list, out var field));
            Assert.Equal(2, list.Count);
            Assert.Equal("distance", field);
            Assert.True(ConditionEvaluator.FindList(ctx, "guards", out _, out var none));
            Assert.Null(none);
            Assert.False(ConditionEvaluator.FindList(ctx, "noise.value", out _, out _));
        }
    }
}
