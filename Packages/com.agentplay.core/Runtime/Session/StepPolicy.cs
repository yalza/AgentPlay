using AgentPlay.Contract;

namespace AgentPlay
{
    public enum PauseReason { None, Mode, Event, Budget }

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

        public PauseReason OnImportantEvent()
        {
            if (Mode != Modes.TurnBased) return PauseReason.None;
            _budgetEnd = double.PositiveInfinity;
            return PauseReason.Event;
        }

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
    }
}
