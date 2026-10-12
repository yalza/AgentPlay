using System.Globalization;
using System.Linq;
using System.Text;
using AgentPlay.Contract;
using Newtonsoft.Json;

namespace AgentPlay.Runner.Brains
{
    /// <summary>
    /// Builds the model prompt. The system part depends only on the game's hello, so it is byte-identical
    /// across turns and can be cached; everything that changes goes into the turn message.
    /// </summary>
    public static class PromptBuilder
    {
        public const int MaxHistory = 5;

        const string Instructions =
@"You control the player character of a video game through AgentPlay. The game is paused while you think, so take the time you need: nothing happens until you answer.

Each turn you receive the current state and what happened since your last plan. Answer with exactly one plan in the required JSON format. The game runs your plan frame by frame until it finishes, fails, or something important happens; then it pauses and asks you again.

How a plan runs:
- steps run in order. A step calls its action every frame until the action reports it is done. If ""until"" is a non-empty condition, the step also ends as soon as that condition becomes true. Use an empty string when the step should simply run to completion.
- rules are checked every frame before the current step. A rule fires once when its ""when"" condition turns true, runs its action to completion, then does ""then"": resume (restart the current step), next (skip to the next step), replan (stop and ask you again), or abort (stop).
- If an action fails, the plan fails and you are asked again with the reason.
- timeout_s limits the plan in game seconds (at most 600). A plan has 1 to 12 steps and at most 8 rules.

Conditions compare observe paths with numbers, true/false or 'single-quoted strings' using < <= > >= == !=, and combine with && || ! and parentheses. event:name is true in the frame that event happens. For list observes use min(list.field), max(list.field), count(list), count(list.boolfield) or any(list.boolfield); a list field cannot be used directly. A missing value makes a comparison false.

Good plans are short (2 to 6 steps), use rules to react to danger, and use until conditions to wait for the right moment. Plans are cheap: when the situation is unclear, make a short plan and look again. The plan id can be any short string; the runner renumbers it.";

        public static string BuildSystem(HelloMessage hello)
        {
            var sb = new StringBuilder();
            sb.Append(Instructions).Append("\n\n");
            sb.Append("# Game: ").Append(hello.Game);
            if (!string.IsNullOrEmpty(hello.Version)) sb.Append(" (version ").Append(hello.Version).Append(')');
            sb.Append("\n\n").Append(string.IsNullOrWhiteSpace(hello.Brief) ? "(The game sent no description.)" : hello.Brief.Trim()).Append("\n\n");

            sb.Append("## Observes (the state you receive)\n");
            foreach (var o in hello.Observes)
            {
                sb.Append("- ").Append(o.Path).Append(" (").Append(o.ValueType).Append(')').Append(Describe(o.Description)).Append('\n');
                if (o.Fields != null)
                    foreach (var f in o.Fields)
                        sb.Append("  - ").Append(f.Path).Append(" (").Append(f.ValueType).Append(')').Append(Describe(f.Description)).Append('\n');
            }

            sb.Append("\n## Actions\n");
            foreach (var a in hello.Actions)
            {
                sb.Append("- ").Append(a.Name).Append('(')
                  .Append(string.Join(", ", a.Params.Select(p => p.Name + ": " + p.ValueType)))
                  .Append(')').Append(Describe(a.Description));
                if (a.Safe) sb.Append(" [runs whenever there is no plan]");
                sb.Append('\n');
            }

            sb.Append("\n## Events\n");
            if (hello.Events == null || hello.Events.Count == 0) sb.Append("(none declared)\n");
            else
                foreach (var e in hello.Events)
                {
                    sb.Append("- ").Append(e.Name).Append(Describe(e.Description));
                    if (e.Important) sb.Append(" [important]");
                    if (!string.IsNullOrEmpty(e.Outcome) && e.Outcome != "neutral") sb.Append(" [").Append(e.Outcome).Append(']');
                    sb.Append('\n');
                }
            sb.Append("\nImportant events pause the game and ask you again unless a rule of your plan reacts to them. game.won and game.lost end the round.\n");
            return sb.ToString();
        }

        public static string BuildTurn(BrainContext context)
        {
            var sb = new StringBuilder();
            sb.Append("# Situation\n");
            sb.Append("Paused because: ").Append(context.PauseReason ?? "unknown").Append('\n');
            sb.Append("(mode = start of play, plan = your last plan finished or failed, event = an important event, budget = time budget used up)\n\n");
            sb.Append("State:\n```json\n").Append(context.State == null ? "{}" : context.State.ToString(Formatting.Indented)).Append("\n```\n\n");

            sb.Append("Events since your last plan (oldest first):\n");
            if (context.RecentEvents.Count == 0) sb.Append("(none)\n");
            foreach (var e in context.RecentEvents)
                sb.Append("- t=").Append(e.T.ToString("0.00", CultureInfo.InvariantCulture)).Append(' ').Append(e.Name)
                  .Append(' ').Append(e.Payload == null ? "{}" : e.Payload.ToString(Formatting.None)).Append('\n');

            sb.Append("\nYour last plan: ");
            if (context.LastPlan == null) sb.Append("No plan yet.\n");
            else
            {
                sb.Append(context.LastPlan.Id).Append(" (").Append(context.LastPlan.Intent).Append(")\n");
                foreach (var s in context.PlanStatuses)
                {
                    sb.Append("- ").Append(s.Status).Append(" at step ").Append(s.Step);
                    if (!string.IsNullOrEmpty(s.Reason)) sb.Append(": ").Append(s.Reason);
                    sb.Append('\n');
                }
            }

            var history = context.History.Skip(System.Math.Max(0, context.History.Count - MaxHistory)).ToList();
            if (history.Count > 0)
            {
                sb.Append("\nEarlier plans (oldest first):\n");
                foreach (var h in history) sb.Append("- ").Append(h).Append('\n');
            }
            sb.Append("\nAnswer with your next plan.");
            return sb.ToString();
        }

        static string Describe(string description) => string.IsNullOrWhiteSpace(description) ? "" : ": " + description.Trim();
    }
}
