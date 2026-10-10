using System;
using UnityEditor;
using UnityEngine;

namespace AgentPlayTestbed
{
    /// <summary>
    /// Run with: Unity.exe -projectPath Testbed -executeMethod AgentPlayTestbed.AutoPlay.Run
    /// Enters Play mode, forces one domain reload while playing, then quits after AGENTPLAY_AUTOPLAY_SECONDS.
    /// </summary>
    [InitializeOnLoad]
    public static class AutoPlay
    {
        const string DeadlineKey = "AgentPlayTestbed.AutoPlay.Deadline";
        const string ReloadKey = "AgentPlayTestbed.AutoPlay.ReloadAt";

        static AutoPlay()
        {
            EditorApplication.update += Watch;
        }

        public static void Run()
        {
            int seconds = 45;
            int.TryParse(Environment.GetEnvironmentVariable("AGENTPLAY_AUTOPLAY_SECONDS"), out seconds);
            if (seconds <= 0) seconds = 45;
            double now = EditorApplication.timeSinceStartup;
            SessionState.SetFloat(DeadlineKey, (float)(now + seconds));
            SessionState.SetFloat(ReloadKey, (float)(now + seconds / 2.0));
            Debug.Log("[Testbed] AutoPlay for " + seconds + " s");
            EditorApplication.EnterPlaymode();
        }

        static void Watch()
        {
            double now = EditorApplication.timeSinceStartup;

            float reloadAt = SessionState.GetFloat(ReloadKey, 0f);
            if (reloadAt > 0f && now >= reloadAt && EditorApplication.isPlaying)
            {
                SessionState.EraseFloat(ReloadKey);
                Debug.Log("[Testbed] Requesting a domain reload during Play mode");
                EditorUtility.RequestScriptReload();
            }

            float deadline = SessionState.GetFloat(DeadlineKey, 0f);
            if (deadline <= 0f || now < deadline) return;
            SessionState.EraseFloat(DeadlineKey);
            Debug.Log("[Testbed] AutoPlay deadline reached; quitting");
            EditorApplication.isPaused = false;
            EditorApplication.ExitPlaymode();
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
    }
}
