using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner.Brains
{
    public sealed class ModelRequest
    {
        /// <summary>Static per game; cached by the API.</summary>
        public string System;
        public string User;
        /// <summary>JSON schema the reply must follow (structured outputs).</summary>
        public JObject Schema;
    }

    public sealed class ModelReply
    {
        public string Text;
        public string StopReason;
        public string Model;
        public long InputTokens;
        public long OutputTokens;
        public long CacheReadTokens;
        public long CacheWriteTokens;
        public long LatencyMs;
    }

    public interface IModelClient
    {
        Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct);
    }

    /// <summary>A failed model call. Retryable ones (network, overload, rate limit, 5xx) are tried again; others stop the brain.</summary>
    public sealed class ModelCallException : Exception
    {
        public bool Retryable { get; }

        public ModelCallException(string message, Exception inner, bool retryable = true) : base(message, inner)
        {
            Retryable = retryable;
        }
    }

    public sealed class CallRecord
    {
        public DateTime Time;
        public string Model;
        public long LatencyMs;
        public long InputTokens;
        public long OutputTokens;
        public long CacheReadTokens;
        public long CacheWriteTokens;
        public string StopReason;
        public string PlanId;
        public bool Valid;
        public string Error;
    }
}
