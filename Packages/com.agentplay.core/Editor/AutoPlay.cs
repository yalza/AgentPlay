using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AgentPlay.Editor
{
    /// <summary>
    /// Run with: Unity.exe -projectPath &lt;game&gt; -executeMethod AgentPlay.Editor.AutoPlay.Run
    /// Runs every IAgentAutoPlayHook.BeforePlay, opens AGENTPLAY_AUTOPLAY_SCENE (or keeps the open scene), enters
    /// Play mode and quits 2 s after game.won (exit 0) or game.lost (3), at AGENTPLAY_AUTOPLAY_SECONDS (1, default
    /// 240), or when Play mode ends without a result (1). A failing hook, in BeforePlay or AfterPlay, exits 2. AfterPlay
    /// runs after Play mode ends. AGENTPLAY_SESSION_TAG (play|debug) sets the session tag. State lives in SessionState
    /// so it survives the domain reloads around Play mode.
    /// </summary>
    [InitializeOnLoad]
    public static class AutoPlay
    {
        const string DeadlineKey = "AgentPlay.AutoPlay.Deadline";
        const string ResultKey = "AgentPlay.AutoPlay.Result";
        const string EndedKey = "AgentPlay.AutoPlay.EndedAt";
        const string HooksKey = "AgentPlay.AutoPlay.Hooks";
        const string QuitKey = "AgentPlay.AutoPlay.QuitCode";
        const string EnteredKey = "AgentPlay.AutoPlay.EnteredPlay";

        /// <summary>
        /// True while an AutoPlay run is in progress in this Editor session (SessionState is empty after an Editor
        /// restart). Hooks use it to tell a crashed run's leftovers from a run in progress.
        /// </summary>
        public static bool RunActive =>
            SessionState.GetFloat(DeadlineKey, 0f) > 0f || SessionState.GetInt(QuitKey, -1) >= 0 || SessionState.GetString(HooksKey, "").Length > 0;

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
            try
            {
                Start();
            }
            catch (Exception e)
            {
                // Never leave the Editor open and waiting: a batch run would hang until someone kills it.
                Debug.LogError("[AgentPlay] AutoPlay could not start: " + e);
                Finish(AutoPlayRules.ExitHookFailed);
            }
        }

        static void Start()
        {
            if (!int.TryParse(Environment.GetEnvironmentVariable("AGENTPLAY_AUTOPLAY_SECONDS"), out var seconds) || seconds <= 0) seconds = 240;
            SessionState.EraseString(ResultKey);
            SessionState.EraseFloat(EndedKey);
            SessionState.EraseInt(QuitKey);
            SessionState.EraseBool(EnteredKey);
            AgentPlaySettings.SessionTag = Environment.GetEnvironmentVariable("AGENTPLAY_SESSION_TAG") == "play" ? "play" : "debug";

            var types = AutoPlayHooks.Discover(TypeCache.GetTypesDerivedFrom<IAgentAutoPlayHook>(), t => AutoPlayHooks.IsAgentPlayTestAssembly(t.Assembly), out var skipped);
            foreach (var t in skipped.Where(t => !AutoPlayHooks.IsAgentPlayTestAssembly(t.Assembly)))
                Debug.LogWarning("[AgentPlay] AutoPlay ignores hook " + t.FullName + ": it needs a public parameterless constructor");
            var hooks = types.Select(t => (IAgentAutoPlayHook)Activator.CreateInstance(t)).ToList();
            bool ok = AutoPlayHooks.Before(hooks, Debug.LogError, out var started);
            // Type names survive the domain reloads around Play mode; AfterPlay recreates the hooks from them.
            SessionState.SetString(HooksKey, string.Join("\n", started.Select(h => h.GetType().AssemblyQualifiedName)));
            if (!ok)
            {
                Finish(AutoPlayRules.ExitHookFailed);
                return;
            }
            foreach (var h in started) Debug.Log("[AgentPlay] AutoPlay hook ready: " + h.GetType().FullName);

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
                SessionState.EraseBool(EnteredKey);
                // A hook that cannot give the user's state back must not hide behind the run's result.
                if (!RunAfterHooks()) quit = AutoPlayRules.ExitHookFailed;
                Debug.Log("[AgentPlay] AutoPlay quitting with " + quit);
                EditorApplication.Exit(quit);
                return;
            }
            float deadline = SessionState.GetFloat(DeadlineKey, 0f);
            if (deadline <= 0f) return;
            bool playing = EditorApplication.isPlaying;
            if (playing) SessionState.SetBool(EnteredKey, true);
            // Entering or leaving Play mode is in progress: decide once the Editor has settled.
            if (!playing && EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool entered = SessionState.GetBool(EnteredKey, false);
            string result = SessionState.GetString(ResultKey, "");
            int? code = AutoPlayRules.Decide(EditorApplication.timeSinceStartup, deadline, result.Length == 0 ? null : result,
                SessionState.GetFloat(EndedKey, 0f), playing, entered);
            if (code == null) return;
            if (code == AutoPlayRules.ExitDeadline)
                Debug.Log(!playing && entered ? "[AgentPlay] AutoPlay: Play mode ended without a result" : "[AgentPlay] AutoPlay deadline reached");
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

        static bool RunAfterHooks()
        {
            string names = SessionState.GetString(HooksKey, "");
            var started = new List<IAgentAutoPlayHook>();
            bool ok = true;
            foreach (var name in names.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var type = Type.GetType(name);
                if (type == null)
                {
                    ok = false;
                    Debug.LogError("[AgentPlay] AutoPlay cannot find hook " + name + " to run its AfterPlay");
                    continue;
                }
                started.Add((IAgentAutoPlayHook)Activator.CreateInstance(type));
            }
            ok &= AutoPlayHooks.After(started, Debug.LogError);
            SessionState.EraseString(HooksKey);
            return ok;
        }
    }
}
