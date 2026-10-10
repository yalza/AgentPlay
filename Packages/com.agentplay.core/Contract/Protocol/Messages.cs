using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Contract
{
    public sealed class ObserveInfo
    {
        [JsonProperty("path")] public string Path;
        [JsonProperty("type")] public string ValueType;
        [JsonProperty("description")] public string Description;
    }

    public sealed class ActionParamInfo
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("type")] public string ValueType;
    }

    public sealed class ActionInfo
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;
        [JsonProperty("safe")] public bool Safe;
        [JsonProperty("params")] public List<ActionParamInfo> Params = new List<ActionParamInfo>();
    }

    public sealed class HelloMessage
    {
        [JsonProperty("protocol")] public int Protocol;
        [JsonProperty("game")] public string Game;
        [JsonProperty("version")] public string Version;
        [JsonProperty("observes")] public List<ObserveInfo> Observes = new List<ObserveInfo>();
        [JsonProperty("actions")] public List<ActionInfo> Actions = new List<ActionInfo>();
        [JsonProperty("modes")] public List<string> Modes = new List<string>();
    }

    public sealed class EventMessage
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("t")] public double T;
        [JsonProperty("frame")] public long Frame;
        [JsonProperty("important")] public bool Important;
        [JsonProperty("outcome")] public string Outcome;
        [JsonProperty("payload")] public JObject Payload;
        [JsonProperty("state")] public JObject State;
    }

    public sealed class StateMessage
    {
        [JsonProperty("t")] public double T;
        [JsonProperty("frame")] public long Frame;
        [JsonProperty("state")] public JObject State;
    }

    public sealed class PausedMessage
    {
        /// <summary>"mode", "event" or "budget".</summary>
        [JsonProperty("reason")] public string Reason;
        [JsonProperty("t")] public double T;
    }

    public sealed class SetModeMessage
    {
        [JsonProperty("mode")] public string Mode;
    }

    public sealed class ResumeMessage
    {
        /// <summary>Game seconds to run before pausing again; 0 or less means until the next important event.</summary>
        [JsonProperty("max_game_seconds")] public double MaxGameSeconds;
    }

    public sealed class ErrorMessage
    {
        [JsonProperty("message")] public string Message;
    }
}
