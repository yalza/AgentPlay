using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace AgentPlay.Runner.Brains
{
    /// <summary>
    /// Calls the Messages API through the official C# SDK: structured outputs for the plan, a cached
    /// system prompt, explicit effort, and server-side refusal fallbacks. The beta endpoint is used for the fallbacks.
    /// </summary>
    public sealed class AnthropicModelClient : IModelClient
    {
        const string FallbackBeta = "server-side-fallback-2026-07-01";

        readonly AnthropicClient _client = new AnthropicClient();
        readonly RunnerConfig _config;

        public AnthropicModelClient(RunnerConfig config)
        {
            _config = config;
        }

        public async Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct)
        {
            var parameters = new MessageCreateParams
            {
                Model = _config.Model,
                MaxTokens = _config.MaxTokens,
                Betas = [FallbackBeta],
                Fallbacks = new BetaFallbacksParam(JsonSerializer.SerializeToElement("default")),
                System = new List<BetaTextBlockParam>
                {
                    new() { Text = request.System, CacheControl = new BetaCacheControlEphemeral() },
                },
                Messages = [new() { Role = Role.User, Content = request.User }],
                OutputConfig = new BetaOutputConfig
                {
                    Effort = ToEffort(_config.Effort),
                    Format = new BetaJsonOutputFormat
                    {
                        Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.Schema.ToString()),
                    },
                },
            };

            var watch = Stopwatch.StartNew();
            BetaMessage message;
            try
            {
                message = await _client.Beta.Messages.Create(parameters, cancellationToken: ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (AnthropicRateLimitException e)
            {
                throw new ModelCallException("rate limited: " + e.Message, e);
            }
            catch (Anthropic4xxException e) when ((int)e.StatusCode != 408 && (int)e.StatusCode != 409)
            {
                // Bad request, authentication, permission, not found: retrying cannot help. 408 and 409 are transient.
                throw new ModelCallException(e.GetType().Name + ": " + e.Message, e, retryable: false);
            }
            catch (Exception e)
            {
                throw new ModelCallException(e.GetType().Name + ": " + e.Message, e);
            }
            watch.Stop();

            return new ModelReply
            {
                Text = string.Concat(message.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text)),
                StopReason = message.StopReason?.ToString(),
                Model = message.Model.ToString(),
                InputTokens = message.Usage.InputTokens,
                OutputTokens = message.Usage.OutputTokens,
                CacheReadTokens = message.Usage.CacheReadInputTokens ?? 0,
                CacheWriteTokens = message.Usage.CacheCreationInputTokens ?? 0,
                LatencyMs = watch.ElapsedMilliseconds,
            };
        }

        static Effort ToEffort(string effort)
        {
            switch (effort)
            {
                case "low": return Effort.Low;
                case "medium": return Effort.Medium;
                case "xhigh": return Effort.Xhigh;
                case "max": return Effort.Max;
                default: return Effort.High;
            }
        }
    }
}
