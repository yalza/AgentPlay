using System.IO;
using System.Linq;
using AgentPlay.Contract;
using UnityEditor;
using UnityEngine;

namespace AgentPlay.Editor
{
    public sealed class AgentPlayWindow : EditorWindow
    {
        static readonly string[] StatusOrder =
            { ReflexStatuses.Proposed, ReflexStatuses.NeedsReview, ReflexStatuses.Active, ReflexStatuses.Suspended, ReflexStatuses.Rejected };

        Vector2 _scroll;
        ReflexBook _book;
        string _launchError;
        int? _seenExitCode;
        string _gameVersion;

        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        [MenuItem("Window/AgentPlay")]
        static void Open() => GetWindow<AgentPlayWindow>("AgentPlay");

        void OnEnable() => Reload();

        void OnFocus() => Reload();

        void OnInspectorUpdate()
        {
            if (ReflectLauncher.LastExitCode != _seenExitCode && !ReflectLauncher.IsRunning)
            {
                _seenExitCode = ReflectLauncher.LastExitCode;
                Reload();   // the reflector may have dropped proposals into the Inbox
            }
            Repaint();
        }

        void Reload()
        {
            if (EditorApplication.isPlaying) return;
            _book = ReflexBook.Load(ProjectRoot);
            _book.ImportInbox();
        }

        ReflexBook Book => EditorApplication.isPlaying && AgentPlaySessionController.Book != null ? AgentPlaySessionController.Book : _book;

        string GameVersion
        {
            get
            {
                if (_gameVersion == null)
                    _gameVersion = AgentRegistry.ToHello(AgentRegistry.Build(TypeScanner.FindAnnotatedTypes()), Application.productName).Version ?? "";
                return _gameVersion;
            }
        }

        void OnGUI()
        {
            bool playing = EditorApplication.isPlaying;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

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

            foreach (var error in AgentPlaySessionController.RegistryErrors)
                EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.LabelField("Recent events", EditorStyles.boldLabel);
            foreach (var line in AgentPlaySessionController.RecentEvents)
                EditorGUILayout.LabelField(line);

            DrawLearning(playing);
            DrawReflexes(playing);
            EditorGUILayout.EndScrollView();
        }

        void DrawLearning(bool playing)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Learning", EditorStyles.boldLabel);
            AgentPlaySettings.RunnerPath = EditorGUILayout.TextField(new GUIContent("Runner path", "AgentPlay.Runner.dll or the folder that holds it"),
                AgentPlaySettings.RunnerPath);
            EditorGUILayout.LabelField("Play sessions to learn", ReflectLauncher.CountPendingPlayTraces(ProjectRoot).ToString());
            using (new EditorGUI.DisabledScope(playing || ReflectLauncher.IsRunning))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(ReflectLauncher.IsRunning ? "Learning..." : "Learn from play sessions"))
                    _launchError = ReflectLauncher.Start(AgentPlaySettings.RunnerPath, ProjectRoot);
                if (GUILayout.Button("Open playbook"))
                {
                    string playbook = Path.Combine(ProjectRoot, "AgentPlay", "playbook.md");
                    if (File.Exists(playbook)) EditorUtility.OpenWithDefaultApp(playbook);
                    else _launchError = "No playbook yet: learn from a play session first";
                }
            }
            if (_launchError != null) EditorGUILayout.HelpBox(_launchError, MessageType.Warning);
            if (ReflectLauncher.LastExitCode.HasValue)
                EditorGUILayout.LabelField("Last run", "exit " + ReflectLauncher.LastExitCode + ": " + ReflectLauncher.LastSummary, EditorStyles.wordWrappedLabel);
        }

        void DrawReflexes(bool playing)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Reflexes", EditorStyles.boldLabel);
            var book = Book;
            if (book == null) return;
            if (book.LoadError != null) EditorGUILayout.HelpBox(book.LoadError, MessageType.Error);
            if (book.Reflexes.Count == 0) EditorGUILayout.LabelField("No reflexes yet.");
            if (playing) EditorGUILayout.HelpBox("Reflexes can be reviewed when Play mode ends.", MessageType.None);
            foreach (var status in StatusOrder)
                foreach (var r in book.Reflexes.Where(x => x.Status == status).ToList())
                    DrawReflex(book, r, playing);
        }

        void DrawReflex(ReflexBook book, Reflex r, bool playing)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(r.Id + "  [" + r.Status + "]", EditorStyles.boldLabel);
                if (!string.IsNullOrWhiteSpace(r.Text)) EditorGUILayout.LabelField(r.Text, EditorStyles.wordWrappedLabel);
                string args = r.Args == null || !r.Args.HasValues ? "" : r.Args.ToString(Newtonsoft.Json.Formatting.None);
                EditorGUILayout.LabelField("when " + r.When + "  do " + r.Do + "(" + args + ")", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("fired " + r.Stats.Fired + ", good " + r.Stats.Good + ", bad " + r.Stats.Bad
                    + (string.IsNullOrEmpty(r.Note) ? "" : "  (" + r.Note + ")"), EditorStyles.wordWrappedMiniLabel);
                bool changed = false;
                using (new EditorGUI.DisabledScope(playing))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if ((r.Status == ReflexStatuses.Proposed || r.Status == ReflexStatuses.NeedsReview) && GUILayout.Button("Approve")) changed = book.Approve(r.Id, GameVersion);
                    if (r.Status == ReflexStatuses.Active && GUILayout.Button("Disable")) changed = book.Disable(r.Id);
                    if (r.Status == ReflexStatuses.Suspended && GUILayout.Button("Re-enable")) changed = book.Reenable(r.Id, GameVersion);
                    if (r.Status != ReflexStatuses.Rejected && r.Status != ReflexStatuses.Active && GUILayout.Button("Reject")) changed = book.Reject(r.Id);
                }
                // The reflex moved to another status group: stop this GUI pass so layout and repaint stay in step.
                if (changed) GUIUtility.ExitGUI();
            }
        }
    }
}
