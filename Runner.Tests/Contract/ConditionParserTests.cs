using AgentPlay.Contract;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class ConditionParserTests
    {
        [Theory]
        [InlineData("a.b < 3", "(a.b < 3)")]
        [InlineData("a || b && !c", "(a || (b && !(c)))")]
        [InlineData("(a || b) && c", "((a || b) && c)")]
        [InlineData("min(guards.distance) >= 2.5", "(min(guards.distance) >= 2.5)")]
        [InlineData("event:guard.spotted && count(guards) == 0", "(event:guard.spotted && (count(guards) == 0))")]
        [InlineData("player.zone == 'exit'", "(player.zone == 'exit')")]
        [InlineData("-1.5 < x", "(-1.5 < x)")]
        [InlineData("a != b", "(a != b)")]
        [InlineData("!a", "!(a)")]
        [InlineData("  any( guards.sees_player )  ", "any(guards.sees_player)")]
        [InlineData("true || false", "(true || false)")]
        [InlineData("x_1 <= 0.25", "(x_1 <= 0.25)")]
        public void ParsesToCanonicalForm(string text, string expected)
        {
            Assert.Equal(expected, ConditionParser.Parse(text).ToString());
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("a <")]
        [InlineData("a < < b")]
        [InlineData("min(")]
        [InlineData("min(a")]
        [InlineData("min a")]
        [InlineData("'abc")]
        [InlineData("event:")]
        [InlineData("Player.X > 1")]
        [InlineData("a b")]
        [InlineData("foo(a)")]
        [InlineData("1.2.3 > 0")]
        [InlineData("a..b > 1")]
        [InlineData("(a > 1")]
        [InlineData("a < b < c")]
        [InlineData("a # b")]
        public void RejectsInvalidText(string text)
        {
            Assert.Throws<ConditionException>(() => ConditionParser.Parse(text));
        }

        [Theory]
        [InlineData(200, '(')]
        [InlineData(200, '!')]
        public void DeepNestingIsRejectedInsteadOfOverflowingTheStack(int depth, char opener)
        {
            var text = opener == '('
                ? new string('(', depth) + "a > 1" + new string(')', depth)
                : new string('!', depth) + "a";
            var e = Assert.Throws<ConditionException>(() => ConditionParser.Parse(text));
            Assert.Contains("nested too deeply", e.Message);
        }

        [Fact]
        public void VeryLongConditionIsRejected()
        {
            var text = string.Join(" && ", System.Linq.Enumerable.Repeat("a > 1", 400));
            var e = Assert.Throws<ConditionException>(() => ConditionParser.Parse(text));
            Assert.Contains("longer than", e.Message);
        }

        [Fact]
        public void ModerateNestingStillParses()
        {
            Assert.Equal("(a > 1)", ConditionParser.Parse(new string('(', 20) + "a > 1" + new string(')', 20)).ToString());
        }

        [Fact]
        public void ErrorReportsPosition()
        {
            var e = Assert.Throws<ConditionException>(() => ConditionParser.Parse("a > 1 && #"));
            Assert.Equal(9, e.Position);
        }
    }
}
