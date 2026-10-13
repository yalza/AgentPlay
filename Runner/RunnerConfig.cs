using System;
using System.IO;
using Newtonsoft.Json;

namespace AgentPlay.Runner
{
    /// <summary>Model settings from runner.json next to the runner; command-line flags override them.</summary>
    public sealed class RunnerConfig
    {
        public static readonly string[] Efforts = { "low", "medium", "high", "xhigh", "max" };
        /// <summary>claude-code: the Claude Code CLI with the signed-in subscription; api: the Messages API with an API key.</summary>
        public static readonly string[] Backends = { "claude-code", "api" };

        [JsonProperty("backend")] public string Backend = "claude-code";

        [JsonProperty("model")] public string Model = "claude-opus-5-5";
        [JsonProperty("effort")] public string Effort = "high";
        [JsonProperty("max_tokens")] public int MaxTokens = 16000;
        [JsonProperty("max_calls")] public int MaxCalls = 40;
        [JsonProperty("reflector")] public ReflectorConfig Reflector = new ReflectorConfig();
        [JsonProperty("router")] public AgentPlay.Runner.Brains.RouterConfig Router = new AgentPlay.Runner.Brains.RouterConfig();

        public static RunnerConfig Load(string path)
        {
            if (!File.Exists(path)) return new RunnerConfig();
            return JsonConvert.DeserializeObject<RunnerConfig>(File.ReadAllText(path)) ?? new RunnerConfig();
        }

        const int PlanLimitsMaxSteps = AgentPlay.Contract.PlanLimits.MaxSteps;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Model)) throw new ArgumentException("runner config: model is required");
            if (Array.IndexOf(Efforts, Effort) < 0) throw new ArgumentException("runner config: effort must be one of " + string.Join(", ", Efforts));
            if (MaxTokens <= 0) throw new ArgumentException("runner config: max_tokens must be positive");
            if (MaxCalls <= 0) throw new ArgumentException("runner config: max_calls must be positive");
            if (Array.IndexOf(Backends, Backend) < 0) throw new ArgumentException("runner config: backend must be one of " + string.Join(", ", Backends));
            if (Reflector == null) Reflector = new ReflectorConfig();
            if (Router == null) Router = new AgentPlay.Runner.Brains.RouterConfig();
            if (Array.IndexOf(Efforts, Router.FastEffort) < 0) throw new ArgumentException("runner config: router.fast_effort must be one of " + string.Join(", ", Efforts));
            if (string.IsNullOrWhiteSpace(Router.FastModel)) throw new ArgumentException("runner config: router.fast_model is required");
            if (Router.FastMaxSteps < 1 || Router.FastMaxSteps > PlanLimitsMaxSteps) throw new ArgumentException("runner config: router.fast_max_steps must be 1 to " + PlanLimitsMaxSteps);
            if (Array.IndexOf(Efforts, Reflector.Effort) < 0) throw new ArgumentException("runner config: reflector.effort must be one of " + string.Join(", ", Efforts));
            if (Reflector.MinConfirmations < 1) throw new ArgumentException("runner config: reflector.min_confirmations must be at least 1");
            if (Reflector.MaxDigestChars < 1000) throw new ArgumentException("runner config: reflector.max_digest_chars must be at least 1000");
            if (Reflector.MaxTracesPerRun < 1) throw new ArgumentException("runner config: reflector.max_traces_per_run must be at least 1");
        }
    }

    /// <summary>Settings of the reflect command (spec M3 section 6).</summary>
    public sealed class ReflectorConfig
    {
        [JsonProperty("effort")] public string Effort = "high";
        /// <summary>Confirmations a lesson needs (and no contradiction) before its reflex is proposed.</summary>
        [JsonProperty("min_confirmations")] public int MinConfirmations = 3;
        [JsonProperty("max_digest_chars")] public int MaxDigestChars = 60000;
        [JsonProperty("max_traces_per_run")] public int MaxTracesPerRun = 5;
    }
}
