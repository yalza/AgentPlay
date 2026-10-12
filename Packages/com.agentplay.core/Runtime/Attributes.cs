using System;

namespace AgentPlay
{
    /// <summary>Exposes a field or property to the agent under a stable path.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class AgentObserveAttribute : Attribute
    {
        public string Path { get; }
        public string Description { get; }

        public AgentObserveAttribute(string path, string description = "")
        {
            Path = path;
            Description = description ?? "";
        }
    }

    /// <summary>
    /// Exposes an IEnumerable&lt;T&gt; as a list observe. T's [AgentObserve] members become the element fields,
    /// and T contributes no top-level observes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class AgentObserveListAttribute : Attribute
    {
        public string Path { get; }
        public string Description { get; }

        public AgentObserveListAttribute(string path, string description = "")
        {
            Path = path;
            Description = description ?? "";
        }
    }

    /// <summary>
    /// Marks a method the agent may call every frame until it returns Done or Failed. Parameters: an optional
    /// AgentActionContext first, then float/int/bool/string.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class AgentActionAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }
        /// <summary>The single default action run while no plan is available. Must take no parameters.</summary>
        public bool Safe { get; set; }

        public AgentActionAttribute(string name, string description = "")
        {
            Name = name;
            Description = description ?? "";
        }
    }

    /// <summary>Declares an event the game emits, so plans can react to it with "event:name".</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class AgentEventInfoAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }
        public bool Important { get; set; }
        public AgentOutcome Outcome { get; set; }

        public AgentEventInfoAttribute(string name, string description = "")
        {
            Name = name;
            Description = description ?? "";
        }
    }

    /// <summary>Names the game. Version is literal, or read from a static string property/method named by VersionMember.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AgentGameAttribute : Attribute
    {
        public string Name { get; }
        public string Version { get; set; }
        public string VersionMember { get; set; }

        public AgentGameAttribute(string name)
        {
            Name = name;
        }
    }

    /// <summary>Optional first parameter of an action: lets it tell its first frame from later ones.</summary>
    public sealed class AgentActionContext
    {
        public bool IsFirstFrame { get; set; } = true;
        /// <summary>Game seconds since the action started.</summary>
        public double Elapsed { get; set; }
    }

    public enum AgentActionStatus { Running, Done, Failed }

    public enum AgentOutcome { Neutral, Good, Bad }
}
