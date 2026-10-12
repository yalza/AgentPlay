using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentPlay.Contract;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner.Brains
{
    /// <summary>Asks a Claude model for each plan; validates, retries once on a bad plan, and respects a call budget.</summary>
    public sealed class OpusBrain : IBrain
    {
        readonly IModelClient _client;
        readonly int _maxCalls;
        readonly Action<string> _log;
        HelloMessage _helloForPrompt;
        string _system;
        JObject _schema;

        public int Calls { get; private set; }
        public long InputTokens { get; private set; }
        public long OutputTokens { get; private set; }
        public long CacheReadTokens { get; private set; }
        public long CacheWriteTokens { get; private set; }

        /// <summary>Waits before retrying a failed call; the last value repeats.</summary>
        public int[] RetryDelaysMs { get; set; } = { 2000, 10000 };
        /// <summary>Transient failures in a row that end the run, so a dead backend does not drain the call budget.</summary>
        public const int MaxFailuresInARow = 3;

        public event Action<BrainContext, CallRecord> Called;

        public OpusBrain(IModelClient client, int maxCalls, Action<string> log)
        {
            _client = client;
            _maxCalls = maxCalls;
            _log = log;
        }

        public async Task<Plan> NextPlanAsync(BrainContext context, CancellationToken ct)
        {
            if (!ReferenceEquals(_helloForPrompt, context.Hello))
            {
                _helloForPrompt = context.Hello;
                _system = PromptBuilder.BuildSystem(context.Hello);
                _schema = PlanSchema.Build(context.Hello);
            }
            string turn = PromptBuilder.BuildTurn(context);
            string feedback = null;
            string lastError = null;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                var request = new ModelRequest { System = _system, User = feedback == null ? turn : turn + "\n\n" + feedback, Schema = _schema };
                var reply = await CallWithRetry(request, context, ct);
                if (reply == null) return null;

                var record = new CallRecord
                {
                    Time = DateTime.Now,
                    Model = reply.Model,
                    LatencyMs = reply.LatencyMs,
                    InputTokens = reply.InputTokens,
                    OutputTokens = reply.OutputTokens,
                    CacheReadTokens = reply.CacheReadTokens,
                    CacheWriteTokens = reply.CacheWriteTokens,
                    StopReason = reply.StopReason,
                };
                var plan = Parse(reply, context, out lastError);
                record.Valid = plan != null;
                record.Error = lastError;
                if (plan != null)
                {
                    plan.Id = "p" + (context.PlansSent + 1);
                    record.PlanId = plan.Id;
                }
                Called?.Invoke(context, record);
                _log("model " + reply.Model + " " + reply.LatencyMs + " ms, in " + reply.InputTokens + " (cache " + reply.CacheReadTokens
                     + ") out " + reply.OutputTokens + (plan != null ? ", plan ok" : ", rejected: " + lastError));
                if (plan != null) return plan;
                feedback = "Your previous answer was rejected: " + lastError + "\nSend a corrected plan.";
            }

            context.StopReason = "the model produced two invalid plans in a row: " + lastError;
            return null;
        }

        static Plan Parse(ModelReply reply, BrainContext context, out string error)
        {
            if (string.Equals(reply.StopReason, "refusal", StringComparison.OrdinalIgnoreCase))
            {
                error = "the model declined to answer (refusal)";
                return null;
            }
            Plan plan;
            try
            {
                plan = JObject.Parse(reply.Text ?? "").ToObject<Plan>(ContractJson.Serializer);
            }
            catch (JsonException e)
            {
                error = "the answer is not valid plan JSON: " + e.Message;
                return null;
            }
            var errors = PlanValidator.Validate(plan, context.Hello);
            if (errors.Count > 0)
            {
                error = string.Join("; ", errors);
                return null;
            }
            error = null;
            return plan;
        }

        async Task<ModelReply> CallWithRetry(ModelRequest request, BrainContext context, CancellationToken ct)
        {
            for (int attempt = 0; ; attempt++)
            {
                if (Calls >= _maxCalls)
                {
                    context.StopReason = "call budget of " + _maxCalls + " reached";
                    return null;
                }
                Calls++;
                try
                {
                    var reply = await _client.CompleteAsync(request, ct);
                    InputTokens += reply.InputTokens;
                    OutputTokens += reply.OutputTokens;
                    CacheReadTokens += reply.CacheReadTokens;
                    CacheWriteTokens += reply.CacheWriteTokens;
                    return reply;
                }
                catch (ModelCallException e) when (!e.Retryable)
                {
                    context.StopReason = "model call failed: " + e.Message;
                    _log(context.StopReason);
                    return null;
                }
                catch (ModelCallException e)
                {
                    if (attempt + 1 >= MaxFailuresInARow)
                    {
                        context.StopReason = "model call failed " + MaxFailuresInARow + " times in a row: " + e.Message;
                        _log(context.StopReason);
                        return null;
                    }
                    int delay = RetryDelaysMs[Math.Min(attempt, RetryDelaysMs.Length - 1)];
                    _log("model call failed (" + e.Message + "); retrying in " + delay + " ms");
                    if (delay > 0) await Task.Delay(delay, ct);
                }
            }
        }
    }
}
