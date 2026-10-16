using AgentPlay.Contract;

namespace AgentPlay
{
    public enum PauseReason { None, Mode, Event, Budget, Plan }

    /// <summary>Decides when turn-based play must stop and wait for the runner.</summary>
    public sealed class StepPolicy
    {
        double _budgetEnd = double.PositiveInfinity;

        public string Mode { get; private set; } = Modes.Realtime;

        public bool SetMode(string mode)
        {
            if (!Modes.IsKnown(mode)) return false;
            Mode = mode;
            _budgetEnd = double.PositiveInfinity;
            return true;
        }

        public PauseReason OnImportantEvent() => Stop(PauseReason.Event);

        /// <summary>A plan finished or failed (including a rejected plan).</summary>
        public PauseReason OnPlanEnded() => Stop(PauseReason.Plan);

        public PauseReason OnTick(double gameTime)
        {
            if (Mode != Modes.TurnBased || gameTime < _budgetEnd) return PauseReason.None;
            _budgetEnd = double.PositiveInfinity;
            return PauseReason.Budget;
        }

        public void OnResume(double gameTime, double maxGameSeconds)
        {
            _budgetEnd = maxGameSeconds > 0 ? gameTime + maxGameSeconds : double.PositiveInfinity;
        }

        /// <summary>The reason in both modes: turn-based pauses on it, realtime sends attention.</summary>
        PauseReason Stop(PauseReason reason)
        {
            _budgetEnd = double.PositiveInfinity;
            return reason;
        }
    }
}
