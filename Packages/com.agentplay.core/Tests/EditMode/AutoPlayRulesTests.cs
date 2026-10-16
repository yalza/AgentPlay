using AgentPlay.Editor;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class AutoPlayRulesTests
    {
        [Test]
        public void KeepsPlayingBeforeTheDeadlineWithNoResult() => Assert.IsNull(AutoPlayRules.Decide(10, 100, null, 0));

        [Test]
        public void DeadlineWithNoResultExitsOne() => Assert.AreEqual(AutoPlayRules.ExitDeadline, AutoPlayRules.Decide(100, 100, null, 0));

        [Test]
        public void WaitsTwoSecondsAfterTheResult()
        {
            Assert.IsNull(AutoPlayRules.Decide(51.9, 100, "won", 50));
            Assert.AreEqual(AutoPlayRules.ExitWon, AutoPlayRules.Decide(52, 100, "won", 50));
            Assert.AreEqual(AutoPlayRules.ExitLost, AutoPlayRules.Decide(52, 100, "lost", 50));
        }

        [Test]
        public void ResultBeatsTheDeadline() => Assert.AreEqual(AutoPlayRules.ExitWon, AutoPlayRules.Decide(200, 100, "won", 50));

        [Test]
        public void FirstResultWins()
        {
            Assert.AreEqual("won", AutoPlayRules.ResultFor("game.won", null));
            Assert.AreEqual("won", AutoPlayRules.ResultFor("game.lost", "won"));
            Assert.AreEqual("lost", AutoPlayRules.ResultFor("game.won", "lost"));
            Assert.IsNull(AutoPlayRules.ResultFor("item.picked", null));
        }
    }
}
