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

    /// <summary>Marks a method the agent may call. It must return AgentActionStatus and take only float/int/bool/string.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class AgentActionAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }
        /// <summary>The single default action run while no plan is available.</summary>
        public bool Safe { get; set; }

        public AgentActionAttribute(string name, string description = "")
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

    public enum AgentActionStatus { Running, Done, Failed }

    public enum AgentOutcome { Neutral, Good, Bad }
}
