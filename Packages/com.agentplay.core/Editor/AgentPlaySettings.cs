using UnityEditor;
using UnityEngine;

namespace AgentPlay.Editor
{
    /// <summary>Per-project Editor preferences.</summary>
    public static class AgentPlaySettings
    {
        public static readonly string[] Tags = { "debug", "play" };

        static string Key(string name) => "AgentPlay:" + Application.dataPath + ":" + name;

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(Key("Enabled"), true);
            set => EditorPrefs.SetBool(Key("Enabled"), value);
        }

        public static string SessionTag
        {
            get
            {
                string tag = EditorPrefs.GetString(Key("SessionTag"), "debug");
                return System.Array.IndexOf(Tags, tag) >= 0 ? tag : "debug";
            }
            set => EditorPrefs.SetString(Key("SessionTag"), value);
        }
    }
}
