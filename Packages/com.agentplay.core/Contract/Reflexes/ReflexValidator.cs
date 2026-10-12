using System.Collections.Generic;

namespace AgentPlay.Contract
{
    /// <summary>Checks a reflex against the actions, observes and events a game declared.</summary>
    public static class ReflexValidator
    {
        public static List<string> Validate(Reflex reflex, HelloMessage hello)
        {
            var errors = new List<string>();
            if (reflex == null)
            {
                errors.Add("reflex: missing");
                return errors;
            }
            if (string.IsNullOrWhiteSpace(reflex.Id)) errors.Add("id: required");
            if (string.IsNullOrWhiteSpace(reflex.When)) errors.Add("when: required");
            else PlanValidator.CheckCondition("when", reflex.When, ConditionSchema.FromHello(hello), errors);

            var actions = new Dictionary<string, ActionInfo>();
            if (hello != null)
                foreach (var a in hello.Actions) actions[a.Name] = a;
            if (!string.IsNullOrWhiteSpace(reflex.Do) && actions.TryGetValue(reflex.Do, out var action) && action.Safe)
                errors.Add("do: the safe action cannot be a reflex");
            else
                PlanValidator.CheckAction("", reflex.Do, reflex.Args, actions, errors);

            if (!(reflex.CooldownSeconds >= 0 && reflex.CooldownSeconds <= ReflexLimits.MaxCooldownSeconds))
                errors.Add("cooldown_s: must be between 0 and " + ReflexLimits.MaxCooldownSeconds);
            return errors;
        }
    }
}
