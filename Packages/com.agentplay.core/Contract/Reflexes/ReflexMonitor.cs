using System.Collections.Generic;

namespace AgentPlay.Contract
{
    public struct ReflexOutcome
    {
        public string Id;
        public bool Bad;
    }

    /// <summary>
    /// Scores reflex firings: a firing is bad when a bad-outcome event happens within the evaluation window
    /// or the reflex itself fails, and good when the window ends quietly. Windows still open when the session ends are dropped.
    /// </summary>
    public sealed class ReflexMonitor
    {
        struct Window
        {
            public string Id;
            public double Start;
        }

        readonly ReflexPolicy _policy;
        readonly List<Window> _open = new List<Window>();
        readonly List<ReflexOutcome> _resolved = new List<ReflexOutcome>();

        public ReflexMonitor(ReflexPolicy policy)
        {
            _policy = policy ?? new ReflexPolicy();
        }

        public void OnFired(string id, double t) => _open.Add(new Window { Id = id, Start = t });

        /// <summary>The reflex's own action failed: its latest open window is bad.</summary>
        public void OnFailed(string id)
        {
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                if (_open[i].Id != id) continue;
                _resolved.Add(new ReflexOutcome { Id = id, Bad = true });
                _open.RemoveAt(i);
                return;
            }
        }

        public void OnBadEvent()
        {
            foreach (var w in _open) _resolved.Add(new ReflexOutcome { Id = w.Id, Bad = true });
            _open.Clear();
        }

        /// <summary>Returns outcomes resolved since the last call; windows older than the policy count as good.</summary>
        public List<ReflexOutcome> Tick(double t)
        {
            for (int i = 0; i < _open.Count; i++)
            {
                if (t - _open[i].Start < _policy.EvalWindowSeconds) continue;
                _resolved.Add(new ReflexOutcome { Id = _open[i].Id, Bad = false });
                _open.RemoveAt(i);
                i--;
            }
            var result = new List<ReflexOutcome>(_resolved);
            _resolved.Clear();
            return result;
        }
    }
}
