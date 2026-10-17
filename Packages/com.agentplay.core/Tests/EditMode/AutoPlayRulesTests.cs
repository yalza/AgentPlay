using AgentPlay.Editor;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class AutoPlayRulesTests
    {
        [Test]
        public void KeepsPlayingBeforeTheDeadlineWithNoResult() => Assert.IsNull(AutoPlayRules.Decide(10, 100, null, 0, true, true));

        [Test]
        public void DeadlineWithNoResultExitsOne() => Assert.AreEqual(AutoPlayRules.ExitDeadline, AutoPlayRules.Decide(100, 100, null, 0, true, true));

        [Test]
        public void WaitsTwoSecondsAfterTheResult()
        {
            Assert.IsNull(AutoPlayRules.Decide(51.9, 100, "won", 50, true, true));
            Assert.AreEqual(AutoPlayRules.ExitWon, AutoPlayRules.Decide(52, 100, "won", 50, true, true));
            Assert.AreEqual(AutoPlayRules.ExitLost, AutoPlayRules.Decide(52, 100, "lost", 50, true, true));
        }

        [Test]
        public void ResultBeatsTheDeadline() => Assert.AreEqual(AutoPlayRules.ExitWon, AutoPlayRules.Decide(200, 100, "won", 50, true, true));

        [Test]
        public void DeadlineAppliesWhilePlayModeHasNotStarted() =>
            // Play mode refused (compile errors) or never entered: the run must still end.
            Assert.AreEqual(AutoPlayRules.ExitDeadline, AutoPlayRules.Decide(100, 100, null, 0, false, false));

        [Test]
        public void WaitsWhilePlayModeIsStarting() => Assert.IsNull(AutoPlayRules.Decide(10, 100, null, 0, false, false));

        [Test]
        public void LeavingPlayModeWithoutAResultEndsTheRun() =>
            // The game quit Play mode itself, or someone stopped it: nothing will ever end the round.
            Assert.AreEqual(AutoPlayRules.ExitDeadline, AutoPlayRules.Decide(10, 100, null, 0, false, true));

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
