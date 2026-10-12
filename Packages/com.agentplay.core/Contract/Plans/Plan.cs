using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Contract
{
    public sealed class Plan
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("intent")] public string Intent;
        [JsonProperty("steps")] public List<PlanStep> Steps = new List<PlanStep>();
        [JsonProperty("rules")] public List<PlanRule> Rules = new List<PlanRule>();
        [JsonProperty("timeout_s")] public double TimeoutSeconds = 60;
    }

    public sealed class PlanStep
    {
        [JsonProperty("do")] public string Do;
        [JsonProperty("args")] public JObject Args;
        /// <summary>Optional condition; when true the step ends even if the action is still running.</summary>
        [JsonProperty("until")] public string Until;
    }

    public sealed class PlanRule
    {
        [JsonProperty("when")] public string When;
        [JsonProperty("do")] public string Do;
        [JsonProperty("args")] public JObject Args;
        [JsonProperty("then")] public string Then;
    }

    public static class RuleThen
    {
        public const string Resume = "resume";
        public const string Next = "next";
        public const string Replan = "replan";
        public const string Abort = "abort";

        public static bool IsKnown(string value) => value == Resume || value == Next || value == Replan || value == Abort;
    }

    public static class PlanLimits
    {
        public const int MaxSteps = 12;
        public const int MaxRules = 8;
        public const double MaxTimeoutSeconds = 600;
    }
}
