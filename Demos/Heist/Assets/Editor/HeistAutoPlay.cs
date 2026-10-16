using System;
using AgentPlay.Contract;
using AgentPlay.Editor;
using UnityEditor;
using UnityEngine;

namespace HeistDemo
{
    /// <summary>
    /// Run with: Unity.exe -projectPath Demos/Heist -executeMethod HeistDemo.HeistAutoPlay.Run
    /// Delegates to AgentPlay.Editor.AutoPlay: enters Play mode and quits 2 s after the round ends (exit 0 won, 3 lost)
    /// or at AGENTPLAY_AUTOPLAY_SECONDS (exit 1). AGENTPLAY_SESSION_TAG (play|debug, default debug) sets the session
    /// tag; AGENTPLAY_APPROVE_PROPOSED=1 approves every proposed reflex first. ApproveOnly does the approval and exits
    /// without playing.
    /// </summary>
    public static class HeistAutoPlay
    {
        public static void Run()
        {
            if (Environment.GetEnvironmentVariable("AGENTPLAY_APPROVE_PROPOSED") == "1") ApproveProposed();
            AutoPlay.Run();
        }

        public static void ApproveOnly()
        {
            int approved = ApproveProposed();
            EditorApplication.Exit(approved > 0 ? 0 : 1);
        }

        static int ApproveProposed()
        {
            var book = ReflexBook.Load(System.IO.Directory.GetParent(Application.dataPath).FullName);
            book.ImportInbox();
            int approved = 0;
            foreach (var r in book.Reflexes)
            {
                if (r.Status != ReflexStatuses.Proposed) continue;
                if (book.Approve(r.Id, "m3"))
                {
                    approved++;
                    Debug.Log("[Heist] approved reflex " + r.Id);
                }
            }
            if (approved == 0) Debug.Log("[Heist] no proposed reflex to approve");
            return approved;
        }
    }
}
