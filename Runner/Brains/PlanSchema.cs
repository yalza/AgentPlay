using AgentPlay.Contract;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner.Brains
{
    /// <summary>
    /// JSON schema of a plan for one game, for structured outputs. Structured outputs require
    /// additionalProperties:false on every object, so each action becomes its own anyOf variant
    /// with exact arguments. Limits (step count, timeout range) are checked by PlanValidator instead.
    /// </summary>
    public static class PlanSchema
    {
        public static JObject Build(HelloMessage hello)
        {
            var stepVariants = new JArray();
            var ruleVariants = new JArray();
            foreach (var action in hello.Actions)
            {
                stepVariants.Add(Closed(
                    new JObject
                    {
                        ["do"] = new JObject { ["const"] = action.Name },
                        ["args"] = Args(action),
                        ["until"] = new JObject { ["type"] = "string", ["description"] = "Condition that ends the step early; empty string for none" },
                    },
                    "do", "args", "until"));
                ruleVariants.Add(Closed(
                    new JObject
                    {
                        ["when"] = new JObject { ["type"] = "string" },
                        ["do"] = new JObject { ["const"] = action.Name },
                        ["args"] = Args(action),
                        ["then"] = new JObject { ["enum"] = new JArray(RuleThen.Resume, RuleThen.Next, RuleThen.Replan, RuleThen.Abort) },
                    },
                    "when", "do", "args", "then"));
            }

            return Closed(
                new JObject
                {
                    ["id"] = new JObject { ["type"] = "string" },
                    ["intent"] = new JObject { ["type"] = "string", ["description"] = "One sentence: what this plan tries to achieve" },
                    ["steps"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["anyOf"] = stepVariants } },
                    ["rules"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["anyOf"] = ruleVariants } },
                    ["timeout_s"] = new JObject { ["type"] = "number", ["description"] = "Game seconds before the plan fails; at most 600" },
                },
                "id", "intent", "steps", "rules", "timeout_s");
        }

        internal static JObject Args(ActionInfo action)
        {
            var properties = new JObject();
            var names = new JArray();
            foreach (var p in action.Params)
            {
                properties[p.Name] = new JObject { ["type"] = JsonType(p.ValueType) };
                names.Add(p.Name);
            }
            return new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = names,
                ["additionalProperties"] = false,
            };
        }

        internal static JObject Closed(JObject properties, params string[] required) => new JObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JArray(required),
            ["additionalProperties"] = false,
        };

        static string JsonType(string kind)
        {
            switch (kind)
            {
                case ValueKinds.Int: return "integer";
                case ValueKinds.Float: return "number";
                case ValueKinds.Bool: return "boolean";
                default: return "string";
            }
        }
    }
}
