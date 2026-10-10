using UnityEditor;
using UnityEngine;

namespace AgentPlay.Editor
{
    public sealed class AgentPlayWindow : EditorWindow
    {
        Vector2 _scroll;

        [MenuItem("Window/AgentPlay")]
        static void Open() => GetWindow<AgentPlayWindow>("AgentPlay");

        void OnInspectorUpdate() => Repaint();

        void OnGUI()
        {
            bool playing = EditorApplication.isPlaying;

            using (new EditorGUI.DisabledScope(playing))
            {
                AgentPlaySettings.Enabled = EditorGUILayout.Toggle("Enabled", AgentPlaySettings.Enabled);
                int index = System.Array.IndexOf(AgentPlaySettings.Tags, AgentPlaySettings.SessionTag);
                int picked = EditorGUILayout.Popup(new GUIContent("Session tag", "play = learn from this session, debug = do not learn"),
                    index, AgentPlaySettings.Tags);
                if (picked != index) AgentPlaySettings.SessionTag = AgentPlaySettings.Tags[picked];
            }
            if (playing) EditorGUILayout.HelpBox("Settings apply from the next Play session.", MessageType.None);

            EditorGUILayout.Space();
            string status = !playing ? "Not playing"
                : !AgentPlaySessionController.IsActive ? "Disabled"
                : AgentPlaySessionController.IsRunnerConnected ? "Runner connected on port " + AgentPlaySessionController.Port
                : "Listening on port " + AgentPlaySessionController.Port;
            EditorGUILayout.LabelField("Status", status);
            EditorGUILayout.LabelField("Mode", AgentPlaySessionController.Mode ?? "-");
            EditorGUILayout.LabelField("Events", AgentPlaySessionController.EventCount.ToString());
            EditorGUILayout.LabelField("Trace", AgentPlaySessionController.TracePath ?? "-");

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var error in AgentPlaySessionController.RegistryErrors)
                EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.LabelField("Recent events", EditorStyles.boldLabel);
            foreach (var line in AgentPlaySessionController.RecentEvents)
                EditorGUILayout.LabelField(line);
            EditorGUILayout.EndScrollView();
        }
    }
}
