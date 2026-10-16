using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner
{
    public sealed class CheckItem
    {
        public string Name;
        public bool Passed;
        public string Detail = "";
    }

    /// <summary>
    /// Checks a game's adapter from what the game says about itself (spec M4b section 3.2): the hello message, two
    /// state snapshots taken a moment apart, and any error message Unity sent. Pure; CheckCommand does the I/O.
    /// </summary>
    public static class AdapterCheck
    {
        static readonly string[] ParamTypes = { ValueKinds.Float, ValueKinds.Int, ValueKinds.Bool, ValueKinds.String };

        public static List<CheckItem> Run(HelloMessage hello, IReadOnlyList<JObject> snapshots, IReadOnlyList<string> unityErrors)
        {
            var items = new List<CheckItem>
            {
                Item("has observes", hello.Observes.Count > 0, hello.Observes.Count + " observe(s)"),
                Item("has actions", hello.Actions.Count > 0, hello.Actions.Count + " action(s)"),
            };
            var safe = hello.Actions.Where(a => a.Safe).Select(a => a.Name).ToList();
            items.Add(Item("exactly one safe action", safe.Count == 1, safe.Count == 0 ? "none" : string.Join(", ", safe)));
            items.Add(Item("declares game.won", hello.Events.Any(e => e.Name == ReservedEvents.GameWon), ""));
            items.Add(Item("brief is not empty", !string.IsNullOrWhiteSpace(hello.Brief), "write AgentPlay/brief.md"));
            var badParams = hello.Actions
                .SelectMany(a => a.Params.Where(p => !ParamTypes.Contains(p.ValueType)).Select(p => a.Name + "(" + p.Name + ": " + p.ValueType + ")"))
                .ToList();
            items.Add(Item("action parameter types are supported", badParams.Count == 0, string.Join(", ", badParams)));
            items.Add(Item("two state snapshots received", snapshots.Count >= 2, snapshots.Count + " received"));
            foreach (var o in hello.Observes)
            {
                bool hasValue = snapshots.Any(s => s.TryGetValue(o.Path, out var v) && v.Type != JTokenType.Null);
                items.Add(Item("observe " + o.Path + " has a value", hasValue, hasValue ? "" : "null or missing in every snapshot"));
            }
            items.Add(Item("no error from Unity", unityErrors.Count == 0, string.Join(" | ", unityErrors)));
            return items;
        }

        static CheckItem Item(string name, bool passed, string detail) => new CheckItem { Name = name, Passed = passed, Detail = detail ?? "" };
    }
}
