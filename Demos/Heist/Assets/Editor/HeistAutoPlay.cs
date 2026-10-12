using System;
using UnityEditor;
using UnityEngine;

namespace HeistDemo
{
    /// <summary>
    /// Run with: Unity.exe -projectPath Demos/Heist -executeMethod HeistDemo.HeistAutoPlay.Run
    /// Enters Play mode and quits 2 s after the round ends (exit 0 won, 3 lost) or at AGENTPLAY_AUTOPLAY_SECONDS (exit 1).
    /// </summary>
    [InitializeOnLoad]
    public static class HeistAutoPlay
    {
        const string DeadlineKey = "HeistDemo.AutoPlay.Deadline";
        const string EndedKey = "HeistDemo.AutoPlay.EndedAt";

        static HeistAutoPlay()
        {
            EditorApplication.update += Watch;
        }

        public static void Run()
        {
            if (!int.TryParse(Environment.GetEnvironmentVariable("AGENTPLAY_AUTOPLAY_SECONDS"), out var seconds) || seconds <= 0) seconds = 240;
            SessionState.SetFloat(DeadlineKey, (float)(EditorApplication.timeSinceStartup + seconds));
            SessionState.EraseFloat(EndedKey);
            Debug.Log("[Heist] AutoPlay for at most " + seconds + " s");
            EditorApplication.EnterPlaymode();
        }

        static void Watch()
        {
            float deadline = SessionState.GetFloat(DeadlineKey, 0f);
            if (deadline <= 0f) return;
            double now = EditorApplication.timeSinceStartup;
            if (EditorApplication.isPlaying && HeistGame.Result != null)
            {
                float endedAt = SessionState.GetFloat(EndedKey, 0f);
                if (endedAt <= 0f)
                {
                    SessionState.SetFloat(EndedKey, (float)now);
                    return;
                }
                if (now - endedAt >= 2.0) Quit(HeistGame.Result == "won" ? 0 : 3);
                return;
            }
            if (now >= deadline)
            {
                Debug.Log("[Heist] AutoPlay deadline reached");
                Quit(1);
            }
        }

        static void Quit(int code)
        {
            SessionState.EraseFloat(DeadlineKey);
            SessionState.EraseFloat(EndedKey);
            Debug.Log("[Heist] AutoPlay quitting with " + code);
            EditorApplication.isPaused = false;
            EditorApplication.ExitPlaymode();
            EditorApplication.delayCall += () => EditorApplication.Exit(code);
        }
    }
}
