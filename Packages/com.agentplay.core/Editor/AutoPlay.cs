using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AgentPlay.Editor
{
    /// <summary>
    /// Run with: Unity.exe -projectPath &lt;game&gt; -executeMethod AgentPlay.Editor.AutoPlay.Run
    /// Runs every IAgentAutoPlayHook.BeforePlay, opens AGENTPLAY_AUTOPLAY_SCENE (or keeps the open scene), enters
    /// Play mode and quits 2 s after game.won (exit 0) or game.lost (3), or at AGENTPLAY_AUTOPLAY_SECONDS (1, default
    /// 240). A failing hook exits 2. AfterPlay runs after Play mode ends. AGENTPLAY_SESSION_TAG (play|debug) sets the
    /// session tag. State lives in SessionState so it survives the domain reloads around Play mode.
    /// </summary>
    [InitializeOnLoad]
    public static class AutoPlay
    {
        const string DeadlineKey = "AgentPlay.AutoPlay.Deadline";
        const string ResultKey = "AgentPlay.AutoPlay.Result";
        const string EndedKey = "AgentPlay.AutoPlay.EndedAt";
        const string HooksKey = "AgentPlay.AutoPlay.Hooks";
        const string QuitKey = "AgentPlay.AutoPlay.QuitCode";

        static AutoPlay()
        {
            EditorApplication.update += Watch;
            // Listen only while a run is active, so Emit stays free (no payload serialization) the rest of the time.
            if (SessionState.GetFloat(DeadlineKey, 0f) > 0f) Listen(true);
        }

        static void Listen(bool on)
        {
            AgentEvents.Emitted -= OnEvent;
            if (on) AgentEvents.Emitted += OnEvent;
        }

        public static void Run()
        {
            if (!int.TryParse(Environment.GetEnvironmentVariable("AGENTPLAY_AUTOPLAY_SECONDS"), out var seconds) || seconds <= 0) seconds = 240;
            SessionState.EraseString(ResultKey);
            SessionState.EraseFloat(EndedKey);
            SessionState.EraseInt(QuitKey);
            AgentPlaySettings.SessionTag = Environment.GetEnvironmentVariable("AGENTPLAY_SESSION_TAG") == "play" ? "play" : "debug";

            var hooks = TypeCache.GetTypesDerivedFrom<IAgentAutoPlayHook>()
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .Select(t => (IAgentAutoPlayHook)Activator.CreateInstance(t))
                .ToList();
            bool ok = AutoPlayHooks.Before(hooks, Debug.LogError, out var started);
            // Type names survive the domain reloads around Play mode; AfterPlay recreates the hooks from them.
            SessionState.SetString(HooksKey, string.Join("\n", started.Select(h => h.GetType().AssemblyQualifiedName)));
            foreach (var h in started) Debug.Log("[AgentPlay] AutoPlay hook ready: " + h.GetType().FullName);
            if (!ok)
            {
                Finish(AutoPlayRules.ExitHookFailed);
                return;
            }

            string scene = Environment.GetEnvironmentVariable("AGENTPLAY_AUTOPLAY_SCENE");
            if (!string.IsNullOrEmpty(scene)) EditorSceneManager.OpenScene(scene);
            SessionState.SetFloat(DeadlineKey, (float)(EditorApplication.timeSinceStartup + seconds));
            Listen(true);
            Debug.Log("[AgentPlay] AutoPlay for at most " + seconds + " s, session tag " + AgentPlaySettings.SessionTag);
            EditorApplication.EnterPlaymode();
        }

        static void OnEvent(AgentEventRecord e)
        {
            if (SessionState.GetFloat(DeadlineKey, 0f) <= 0f) return;
            string current = SessionState.GetString(ResultKey, "");
            string next = AutoPlayRules.ResultFor(e.Name, current.Length == 0 ? null : current);
            if (next == null || next == current) return;
            SessionState.SetString(ResultKey, next);
            SessionState.SetFloat(EndedKey, (float)EditorApplication.timeSinceStartup);
        }

        static void Watch()
        {
            int quit = SessionState.GetInt(QuitKey, -1);
            if (quit >= 0)
            {
                // Leave Play mode first; hooks and exit run once the editor is back in Edit mode.
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                SessionState.EraseInt(QuitKey);
                RunAfterHooks();
                Debug.Log("[AgentPlay] AutoPlay quitting with " + quit);
                EditorApplication.Exit(quit);
                return;
            }
            float deadline = SessionState.GetFloat(DeadlineKey, 0f);
            if (deadline <= 0f || !EditorApplication.isPlaying) return;
            string result = SessionState.GetString(ResultKey, "");
            int? code = AutoPlayRules.Decide(EditorApplication.timeSinceStartup, deadline, result.Length == 0 ? null : result, SessionState.GetFloat(EndedKey, 0f));
            if (code == null) return;
            if (code == AutoPlayRules.ExitDeadline) Debug.Log("[AgentPlay] AutoPlay deadline reached");
            Finish(code.Value);
        }

        static void Finish(int code)
        {
            SessionState.EraseFloat(DeadlineKey);
            Listen(false);
            SessionState.SetInt(QuitKey, code);
            EditorApplication.isPaused = false;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }

        static void RunAfterHooks()
        {
            string names = SessionState.GetString(HooksKey, "");
            SessionState.EraseString(HooksKey);
            var started = names.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Type.GetType)
                .Where(t => t != null)
                .Select(t => (IAgentAutoPlayHook)Activator.CreateInstance(t))
                .ToList();
            AutoPlayHooks.After(started, Debug.LogError);
        }
    }
}
