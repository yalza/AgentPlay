using System;
using System.Collections.Generic;

namespace AgentPlay.Editor
{
    /// <summary>Calls hooks around an AutoPlay run: BeforePlay in order until one fails, AfterPlay on the started ones in reverse.</summary>
    public static class AutoPlayHooks
    {
        public static bool Before(IReadOnlyList<IAgentAutoPlayHook> hooks, Action<string> logError, out List<IAgentAutoPlayHook> started)
        {
            started = new List<IAgentAutoPlayHook>();
            foreach (var hook in hooks)
            {
                try
                {
                    hook.BeforePlay();
                    started.Add(hook);
                }
                catch (Exception e)
                {
                    logError("[AgentPlay] AutoPlay hook " + hook.GetType().FullName + " failed in BeforePlay: " + e);
                    return false;
                }
            }
            return true;
        }

        public static void After(IReadOnlyList<IAgentAutoPlayHook> started, Action<string> logError)
        {
            for (int i = started.Count - 1; i >= 0; i--)
            {
                try
                {
                    started[i].AfterPlay();
                }
                catch (Exception e)
                {
                    logError("[AgentPlay] AutoPlay hook " + started[i].GetType().FullName + " failed in AfterPlay: " + e);
                }
            }
        }
    }
}
