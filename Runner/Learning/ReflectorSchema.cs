using System.Collections.Generic;
using AgentPlay.Contract;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner.Learning
{
    /// <summary>
    /// JSON schema of the reflector's answer for one game (structured outputs: additionalProperties:false everywhere).
    /// A lesson's reflex is null or one variant per non-safe action with exact arguments.
    /// </summary>
    public static class ReflectorSchema
    {
        public static JObject Build(HelloMessage hello)
        {
            var reflexVariants = new JArray { new JObject { ["type"] = "null" } };
            foreach (var action in hello.Actions)
            {
                if (action.Safe) continue;
                reflexVariants.Add(PlanSchema.Closed(
                    new JObject
                    {
                        ["when"] = new JObject { ["type"] = "string", ["description"] = "Condition in the game's condition language" },
                        ["do"] = new JObject { ["const"] = action.Name },
                        ["args"] = PlanSchema.Args(action),
                    },
                    "when", "do", "args"));
            }

            var lesson = PlanSchema.Closed(
                new JObject
                {
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Id of an existing lesson; empty for a new one" },
                    ["verdict"] = new JObject { ["enum"] = new JArray(ReflectorLesson.New, ReflectorLesson.Confirmed, ReflectorLesson.Contradicted) },
                    ["text"] = new JObject { ["type"] = "string", ["description"] = "What to do in which situation, at most 300 characters" },
                    ["evidence"] = new JObject { ["type"] = "string", ["description"] = "Times and events in this session that support the verdict" },
                    ["reflex"] = new JObject { ["anyOf"] = reflexVariants },
                },
                "id", "verdict", "text", "evidence", "reflex");

            return PlanSchema.Closed(
                new JObject
                {
                    ["summary"] = new JObject { ["type"] = "string", ["description"] = "One paragraph: what happened in this session" },
                    ["lessons"] = new JObject { ["type"] = "array", ["items"] = lesson },
                },
                "summary", "lessons");
        }
    }

    public sealed class ReflectorLesson
    {
        public const string New = "new";
        public const string Confirmed = "confirmed";
        public const string Contradicted = "contradicted";
        public const int MaxTextLength = 300;

        [JsonProperty("id")] public string Id;
        [JsonProperty("verdict")] public string Verdict;
        [JsonProperty("text")] public string Text;
        [JsonProperty("evidence")] public string Evidence;
        [JsonProperty("reflex")] public ReflexCandidate Reflex;
    }

    public sealed class ReflectorOutput
    {
        [JsonProperty("summary")] public string Summary;
        [JsonProperty("lessons")] public List<ReflectorLesson> Lessons = new List<ReflectorLesson>();

        /// <summary>Throws JsonException when the text is not an answer object.</summary>
        public static ReflectorOutput Parse(string text)
        {
            var output = JObject.Parse(text ?? "").ToObject<ReflectorOutput>(ContractJson.Serializer);
            if (output.Lessons == null) output.Lessons = new List<ReflectorLesson>();
            return output;
        }

        public List<string> Validate()
        {
            var errors = new List<string>();
            for (int i = 0; i < Lessons.Count; i++)
            {
                var l = Lessons[i];
                string at = "lessons[" + i + "]";
                if (l == null)
                {
                    errors.Add(at + ": missing");
                    continue;
                }
                if (l.Verdict != ReflectorLesson.New && l.Verdict != ReflectorLesson.Confirmed && l.Verdict != ReflectorLesson.Contradicted)
                    errors.Add(at + ".verdict: must be new, confirmed or contradicted");
                if (string.IsNullOrWhiteSpace(l.Text)) errors.Add(at + ".text: required");
                else if (l.Text.Length > ReflectorLesson.MaxTextLength) errors.Add(at + ".text: at most " + ReflectorLesson.MaxTextLength + " characters");
            }
            return errors;
        }
    }
}
