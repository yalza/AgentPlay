namespace AgentPlay
{
    public sealed class IntervalTimer
    {
        readonly double _interval;
        double _next = double.NegativeInfinity;

        public IntervalTimer(double intervalSeconds)
        {
            _interval = intervalSeconds;
        }

        /// <summary>True on the first call, then once each time <paramref name="now"/> passes the next deadline.</summary>
        public bool Tick(double now)
        {
            if (now < _next) return false;
            _next = now + _interval;
            return true;
        }
    }
}
