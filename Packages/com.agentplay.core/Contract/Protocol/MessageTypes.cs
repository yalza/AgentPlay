namespace AgentPlay.Contract
{
    public static class ProtocolVersion
    {
        public const int Current = 3;
    }

    public static class MessageTypes
    {
        // Unity -> runner
        public const string Hello = "hello";
        public const string State = "state";
        public const string Event = "event";
        public const string Paused = "paused";
        public const string Error = "error";
        public const string PlanStatus = "plan_status";
        public const string Waiting = "waiting";
        public const string Reflex = "reflex";
        public const string ReflexEnd = "reflex_end";
        // Runner -> Unity
        public const string SetMode = "set_mode";
        public const string Resume = "resume";
        public const string GetState = "get_state";
        public const string Plan = "plan";
        public const string StopPlan = "stop_plan";
    }

    public static class Modes
    {
        public const string TurnBased = "turn_based";
        public const string Realtime = "realtime";

        public static bool IsKnown(string mode) => mode == TurnBased || mode == Realtime;
    }

    public static class ValueKinds
    {
        public const string Float = "float";
        public const string Int = "int";
        public const string Bool = "bool";
        public const string String = "string";
        public const string Enum = "enum";
        public const string Vector3 = "vector3";
        public const string List = "list";
    }

    public static class PlanStatuses
    {
        public const string StepDone = "step_done";
        public const string PlanDone = "plan_done";
        public const string PlanFailed = "plan_failed";
        public const string RuleFired = "rule_fired";
    }

    /// <summary>Event names every game uses to end a round; the runner stops asking the model after either.</summary>
    public static class ReservedEvents
    {
        public const string GameWon = "game.won";
        public const string GameLost = "game.lost";
    }
}
