using AgentPlay.Contract;

namespace AgentPlay.Editor
{
    /// <summary>When AutoPlay stops and with which exit code (spec M4b section 3.1). Pure, so EditMode tests cover it.</summary>
    public static class AutoPlayRules
    {
        public const int ExitWon = 0, ExitDeadline = 1, ExitHookFailed = 2, ExitLost = 3;
        public const double EndDelaySeconds = 2.0;

        /// <summary>The exit code once the run should stop, or null to keep playing.</summary>
        public static int? Decide(double now, double deadline, string result, double endedAt)
        {
            if (result != null) return now - endedAt >= EndDelaySeconds ? (result == "won" ? ExitWon : ExitLost) : (int?)null;
            return now >= deadline ? ExitDeadline : (int?)null;
        }

        /// <summary>The round's result after an event; the first game.won or game.lost wins.</summary>
        public static string ResultFor(string eventName, string current)
        {
            if (current != null) return current;
            if (eventName == ReservedEvents.GameWon) return "won";
            if (eventName == ReservedEvents.GameLost) return "lost";
            return null;
        }
    }
}
