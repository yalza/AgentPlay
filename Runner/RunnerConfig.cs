using System;
using System.IO;
using Newtonsoft.Json;

namespace AgentPlay.Runner
{
    /// <summary>Model settings from runner.json next to the runner; command-line flags override them.</summary>
    public sealed class RunnerConfig
    {
        public static readonly string[] Efforts = { "low", "medium", "high", "xhigh", "max" };

        [JsonProperty("model")] public string Model = "claude-opus-5-5";
        [JsonProperty("effort")] public string Effort = "high";
        [JsonProperty("max_tokens")] public int MaxTokens = 16000;
        [JsonProperty("max_calls")] public int MaxCalls = 40;

        public static RunnerConfig Load(string path)
        {
            if (!File.Exists(path)) return new RunnerConfig();
            return JsonConvert.DeserializeObject<RunnerConfig>(File.ReadAllText(path)) ?? new RunnerConfig();
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Model)) throw new ArgumentException("runner config: model is required");
            if (Array.IndexOf(Efforts, Effort) < 0) throw new ArgumentException("runner config: effort must be one of " + string.Join(", ", Efforts));
            if (MaxTokens <= 0) throw new ArgumentException("runner config: max_tokens must be positive");
            if (MaxCalls <= 0) throw new ArgumentException("runner config: max_calls must be positive");
        }
    }
}
