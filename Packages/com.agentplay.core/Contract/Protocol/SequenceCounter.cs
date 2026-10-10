using System.Threading;

namespace AgentPlay.Contract
{
    public sealed class SequenceCounter
    {
        long _last;

        public long Next() => Interlocked.Increment(ref _last);
    }
}
