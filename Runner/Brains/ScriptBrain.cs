using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner.Brains
{
    /// <summary>Plays a fixed list of plans from a JSON file {"plans": [...]}, in order. For tests without a model.</summary>
    public sealed class ScriptBrain : IBrain
    {
        readonly Queue<Plan> _plans;

        public ScriptBrain(IEnumerable<Plan> plans)
        {
            _plans = new Queue<Plan>(plans);
        }

        public int Remaining => _plans.Count;

        public static ScriptBrain Load(string path)
        {
            var root = JObject.Parse(File.ReadAllText(path));
            if (!(root["plans"] is JArray array)) throw new InvalidDataException(path + ": expected {\"plans\": [...]}");
            var plans = new List<Plan>();
            foreach (var item in array) plans.Add(item.ToObject<Plan>(ContractJson.Serializer));
            return new ScriptBrain(plans);
        }

        public Task<Plan> NextPlanAsync(BrainContext context, CancellationToken ct) =>
            Task.FromResult(_plans.Count > 0 ? _plans.Dequeue() : null);
    }
}
