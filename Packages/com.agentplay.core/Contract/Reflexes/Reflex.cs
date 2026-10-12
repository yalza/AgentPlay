using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Contract
{
    public static class ReflexStatuses
    {
        public const string Proposed = "proposed";
        public const string Active = "active";
        public const string Rejected = "rejected";
        public const string Suspended = "suspended";
        public const string NeedsReview = "needs_review";
    }

    public static class ReflexLimits
    {
        public const int MaxActive = 16;
        public const double MaxCooldownSeconds = 60;
        /// <summary>A reflex action running longer than this (game seconds) fails.</summary>
        public const double MaxRunSeconds = 15;
    }

    public sealed class ReflexStats
    {
        [JsonProperty("fired")] public int Fired;
        [JsonProperty("good")] public int Good;
        [JsonProperty("bad")] public int Bad;
    }

    /// <summary>A learned rule the game runs by itself, without asking the model (spec M3 section 3).</summary>
    public sealed class Reflex
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("lesson_id")] public string LessonId;
        [JsonProperty("text")] public string Text;
        [JsonProperty("when")] public string When;
        [JsonProperty("do")] public string Do;
        [JsonProperty("args")] public JObject Args = new JObject();
        [JsonProperty("cooldown_s")] public double CooldownSeconds = 1;
        [JsonProperty("status")] public string Status = ReflexStatuses.Proposed;
        [JsonProperty("game_version")] public string GameVersion;
        [JsonProperty("stats")] public ReflexStats Stats = new ReflexStats();
        /// <summary>Why the reflex last changed status.</summary>
        [JsonProperty("note")] public string Note = "";
    }

    public sealed class ReflexPolicy
    {
        [JsonProperty("eval_window_s")] public double EvalWindowSeconds = 10;
        [JsonProperty("min_fires")] public int MinFires = 5;
        [JsonProperty("max_fail_rate")] public double MaxFailRate = 0.3;
    }

    /// <summary>Contents of AgentPlay/reflexes.json in the game project.</summary>
    public sealed class ReflexFile
    {
        [JsonProperty("version")] public int Version = 1;
        [JsonProperty("policy")] public ReflexPolicy Policy = new ReflexPolicy();
        [JsonProperty("reflexes")] public List<Reflex> Reflexes = new List<Reflex>();
    }

    /// <summary>What hello tells the runner about a loaded reflex.</summary>
    public sealed class ReflexInfo
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("text")] public string Text;
        [JsonProperty("when")] public string When;
        [JsonProperty("do")] public string Do;
        [JsonProperty("args")] public JObject Args;

        public static ReflexInfo From(Reflex r) => new ReflexInfo { Id = r.Id, Text = r.Text, When = r.When, Do = r.Do, Args = r.Args };
    }
}
