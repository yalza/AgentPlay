using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AgentPlay.Contract;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner.Learning
{
    /// <summary>A reflex the reflector suggested for a lesson; proposed once the lesson is confirmed often enough.</summary>
    public sealed class ReflexCandidate
    {
        [JsonProperty("when")] public string When;
        [JsonProperty("do")] public string Do;
        [JsonProperty("args")] public JObject Args = new JObject();
    }

    public sealed class Lesson
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("text")] public string Text;
        [JsonProperty("game_version")] public string GameVersion;
        [JsonProperty("confirmations")] public int Confirmations;
        [JsonProperty("contradictions")] public int Contradictions;
        /// <summary>Trace file names that confirmed or contradicted this lesson.</summary>
        [JsonProperty("sources")] public List<string> Sources = new List<string>();
        [JsonProperty("reflex")] public ReflexCandidate Reflex;
        [JsonProperty("reflex_proposed")] public bool ReflexProposed;
        [JsonProperty("updated")] public string Updated;
    }

    public sealed class Playbook
    {
        public const int MaxLessons = 50;

        [JsonProperty("version")] public int Version = 1;
        [JsonProperty("next_id")] public int NextId = 1;
        [JsonProperty("lessons")] public List<Lesson> Lessons = new List<Lesson>();

        public static int Score(Lesson l) => l.Confirmations - l.Contradictions;
    }

    /// <summary>AgentPlay/playbook.json (source of truth) and playbook.md (rendered). The runner is the only writer.</summary>
    public static class PlaybookStore
    {
        public static string JsonPath(string root) => Path.Combine(root, "AgentPlay", "playbook.json");

        public static string MarkdownPath(string root) => Path.Combine(root, "AgentPlay", "playbook.md");

        /// <summary>An empty playbook when the file is missing; InvalidDataException when it cannot be read.</summary>
        public static Playbook Load(string root)
        {
            string path = JsonPath(root);
            if (!File.Exists(path)) return new Playbook();
            try
            {
                var pb = JsonConvert.DeserializeObject<Playbook>(File.ReadAllText(path)) ?? new Playbook();
                if (pb.Lessons == null) pb.Lessons = new List<Lesson>();
                return pb;
            }
            catch (JsonException e)
            {
                throw new InvalidDataException("Cannot read " + path + ": " + e.Message + ". Fix or delete the file.", e);
            }
        }

        public static void Save(string root, Playbook pb)
        {
            AtomicFile.WriteAllText(JsonPath(root), JsonConvert.SerializeObject(pb, Formatting.Indented));
            AtomicFile.WriteAllText(MarkdownPath(root), RenderMarkdown(pb));
        }

        /// <summary>Best lessons first (confirmations minus contradictions), at most <paramref name="max"/>.</summary>
        public static List<Lesson> ForPrompt(Playbook pb, int max = 20) =>
            pb.Lessons.OrderByDescending(Playbook.Score).ThenBy(l => l.Id, StringComparer.Ordinal).Take(max).ToList();

        public static string RenderMarkdown(Playbook pb)
        {
            var sb = new StringBuilder("# Playbook\n\nGenerated from playbook.json by AgentPlay; edits here are overwritten.\n");
            if (pb.Lessons.Count == 0) sb.Append("\n(no lessons yet)\n");
            foreach (var l in ForPrompt(pb, int.MaxValue))
            {
                sb.Append("\n## ").Append(l.Id).Append('\n');
                sb.Append(l.Text).Append("\n\n");
                sb.Append("- confirmed ").Append(l.Confirmations).Append(", contradicted ").Append(l.Contradictions)
                  .Append(", game version ").Append(l.GameVersion ?? "-").Append('\n');
                if (l.Reflex != null)
                    sb.Append("- reflex: when ").Append(l.Reflex.When).Append(" do ").Append(l.Reflex.Do)
                      .Append('(').Append(l.Reflex.Args == null ? "" : l.Reflex.Args.ToString(Formatting.None)).Append(')')
                      .Append(l.ReflexProposed ? " (proposed)" : "").Append('\n');
                sb.Append("- sources: ").Append(string.Join(", ", l.Sources)).Append('\n');
            }
            return sb.ToString();
        }
    }

    /// <summary>AgentPlay/reflected.json: trace file names the reflector already learned from.</summary>
    public static class ReflectedLedger
    {
        static string PathOf(string root) => Path.Combine(root, "AgentPlay", "reflected.json");

        public static HashSet<string> Load(string root)
        {
            string path = PathOf(root);
            var names = File.Exists(path) ? JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(path)) ?? new List<string>() : new List<string>();
            return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        }

        public static void Save(string root, HashSet<string> names) =>
            AtomicFile.WriteAllText(PathOf(root), JsonConvert.SerializeObject(names.OrderBy(n => n, StringComparer.Ordinal).ToList(), Formatting.Indented));
    }
}
