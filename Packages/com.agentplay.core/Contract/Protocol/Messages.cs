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
        /// <summary>Element fields when ValueType is "list"; null otherwise.</summary>
        [JsonProperty("fields")] public List<ObserveInfo> Fields;
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

    public sealed class EventInfo
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;
        [JsonProperty("important")] public bool Important;
        /// <summary>"neutral", "good" or "bad".</summary>
        [JsonProperty("outcome")] public string Outcome;
    }

    public sealed class HelloMessage
    {
        [JsonProperty("protocol")] public int Protocol;
        [JsonProperty("game")] public string Game;
        [JsonProperty("version")] public string Version;
        /// <summary>Contents of AgentPlay/brief.md in the game project; empty when missing.</summary>
        [JsonProperty("brief")] public string Brief = "";
        [JsonProperty("project_root")] public string ProjectRoot;
        [JsonProperty("observes")] public List<ObserveInfo> Observes = new List<ObserveInfo>();
        [JsonProperty("actions")] public List<ActionInfo> Actions = new List<ActionInfo>();
        [JsonProperty("events")] public List<EventInfo> Events = new List<EventInfo>();
        [JsonProperty("modes")] public List<string> Modes = new List<string>();
        /// <summary>"play" (learn from this session) or "debug".</summary>
        [JsonProperty("session_tag")] public string SessionTag = "debug";
        /// <summary>Reflexes the game runs by itself in this session.</summary>
        [JsonProperty("reflexes")] public List<ReflexInfo> Reflexes = new List<ReflexInfo>();
        /// <summary>Attribute declarations the registry rejected (and left out of this hello), one message each.</summary>
        [JsonProperty("registry_errors")] public List<string> RegistryErrors = new List<string>();
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
        /// <summary>"mode", "event", "budget" or "plan".</summary>
        [JsonProperty("reason")] public string Reason;
        [JsonProperty("t")] public double T;
        [JsonProperty("state")] public JObject State;
        [JsonProperty("time_split")] public TimeSplit TimeSplit;
    }

    /// <summary>Realtime mode: sent where turn-based mode would pause; the game keeps running its safe action.</summary>
    public sealed class AttentionMessage
    {
        /// <summary>"mode", "event" or "plan".</summary>
        [JsonProperty("reason")] public string Reason;
        [JsonProperty("t")] public double T;
        [JsonProperty("state")] public JObject State;
        [JsonProperty("time_split")] public TimeSplit TimeSplit;
    }

    /// <summary>Game seconds since the agent took control, by what was running.</summary>
    public sealed class TimeSplit
    {
        [JsonProperty("safe")] public double Safe;
        [JsonProperty("plan")] public double Plan;
        [JsonProperty("reflex")] public double Reflex;
    }

    public sealed class SetModeMessage
    {
        [JsonProperty("mode")] public string Mode;
    }

    public sealed class ResumeMessage
    {
        /// <summary>Game seconds to run before pausing again; 0 or less means until the next stop condition.</summary>
        [JsonProperty("max_game_seconds")] public double MaxGameSeconds;
    }

    public sealed class ErrorMessage
    {
        [JsonProperty("message")] public string Message;
    }

    public sealed class PlanMessage
    {
        [JsonProperty("plan")] public Plan Plan;
    }

    public sealed class PlanStatusMessage
    {
        [JsonProperty("plan_id")] public string PlanId;
        [JsonProperty("status")] public string Status;
        [JsonProperty("step")] public int Step;
        [JsonProperty("reason")] public string Reason;
    }

    public sealed class ReflexMessage
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("t")] public double T;
        [JsonProperty("frame")] public long Frame;
        /// <summary>Important events of the frame the reflex fired in; the game did not pause for them.</summary>
        [JsonProperty("handled_events")] public List<string> HandledEvents = new List<string>();
    }

    public sealed class ReflexEndMessage
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("t")] public double T;
        /// <summary>"done" or "failed:reason".</summary>
        [JsonProperty("status")] public string Status;
    }

    public sealed class WaitingMessage
    {
        [JsonProperty("t")] public double T;
    }
}
