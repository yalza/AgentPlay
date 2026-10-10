namespace AgentPlay.Contract
{
    public static class ProtocolVersion
    {
        public const int Current = 1;
    }

    public static class MessageTypes
    {
        // Unity -> runner
        public const string Hello = "hello";
        public const string State = "state";
        public const string Event = "event";
        public const string Paused = "paused";
        public const string Error = "error";
        // Runner -> Unity
        public const string SetMode = "set_mode";
        public const string Resume = "resume";
        public const string GetState = "get_state";
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
    }
}
