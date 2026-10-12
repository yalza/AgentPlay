using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Contract
{
    /// <summary>Checks a plan against the actions, observes and events a game declared. Used by Unity and the runner.</summary>
    public static class PlanValidator
    {
        public static List<string> Validate(Plan plan, HelloMessage hello)
        {
            var errors = new List<string>();
            if (plan == null)
            {
                errors.Add("plan: missing");
                return errors;
            }
            if (string.IsNullOrWhiteSpace(plan.Id)) errors.Add("id: required");
            if (plan.Steps == null || plan.Steps.Count == 0) errors.Add("steps: at least one step is required");
            else if (plan.Steps.Count > PlanLimits.MaxSteps) errors.Add("steps: at most " + PlanLimits.MaxSteps + " steps");
            if (plan.Rules != null && plan.Rules.Count > PlanLimits.MaxRules) errors.Add("rules: at most " + PlanLimits.MaxRules + " rules");
            if (!(plan.TimeoutSeconds > 0 && plan.TimeoutSeconds <= PlanLimits.MaxTimeoutSeconds))
                errors.Add("timeout_s: must be greater than 0 and at most " + PlanLimits.MaxTimeoutSeconds);

            var schema = ConditionSchema.FromHello(hello);
            var actions = new Dictionary<string, ActionInfo>();
            if (hello != null)
                foreach (var a in hello.Actions) actions[a.Name] = a;

            if (plan.Steps != null)
            {
                for (int i = 0; i < plan.Steps.Count; i++)
                {
                    var at = "steps[" + i + "]";
                    var step = plan.Steps[i];
                    if (step == null)
                    {
                        errors.Add(at + ": missing");
                        continue;
                    }
                    CheckAction(at, step.Do, step.Args, actions, errors);
                    if (!string.IsNullOrWhiteSpace(step.Until)) CheckCondition(at + ".until", step.Until, schema, errors);
                }
            }
            if (plan.Rules != null)
            {
                for (int i = 0; i < plan.Rules.Count; i++)
                {
                    var at = "rules[" + i + "]";
                    var rule = plan.Rules[i];
                    if (rule == null)
                    {
                        errors.Add(at + ": missing");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(rule.When)) errors.Add(at + ".when: required");
                    else CheckCondition(at + ".when", rule.When, schema, errors);
                    CheckAction(at, rule.Do, rule.Args, actions, errors);
                    if (!RuleThen.IsKnown(rule.Then)) errors.Add(at + ".then: must be resume, next, replan or abort");
                }
            }
            return errors;
        }

        static void CheckCondition(string at, string text, ConditionSchema schema, List<string> errors)
        {
            CondNode node;
            try
            {
                node = ConditionParser.Parse(text);
            }
            catch (ConditionException e)
            {
                errors.Add(at + ": " + e.Message);
                return;
            }
            foreach (var error in schema.Check(node)) errors.Add(at + ": " + error);
        }

        static void CheckAction(string at, string name, JObject args, Dictionary<string, ActionInfo> actions, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add(at + ".do: required");
                return;
            }
            if (!actions.TryGetValue(name, out var action))
            {
                errors.Add(at + ".do: unknown action '" + name + "'");
                return;
            }
            var known = new HashSet<string>();
            foreach (var p in action.Params)
            {
                known.Add(p.Name);
                var token = args?[p.Name];
                if (token == null || token.Type == JTokenType.Null)
                {
                    errors.Add(at + ".args." + p.Name + ": required");
                    continue;
                }
                if (!Matches(p.ValueType, token)) errors.Add(at + ".args." + p.Name + ": expected " + p.ValueType);
            }
            if (args != null)
                foreach (var prop in args.Properties())
                    if (!known.Contains(prop.Name)) errors.Add(at + ".args." + prop.Name + ": unknown parameter");
        }

        static bool Matches(string kind, JToken token)
        {
            switch (kind)
            {
                case ValueKinds.Float: return token.Type == JTokenType.Float || token.Type == JTokenType.Integer;
                case ValueKinds.Int: return token.Type == JTokenType.Integer;
                case ValueKinds.Bool: return token.Type == JTokenType.Boolean;
                case ValueKinds.String: return token.Type == JTokenType.String;
                default: return false;
            }
        }
    }
}
