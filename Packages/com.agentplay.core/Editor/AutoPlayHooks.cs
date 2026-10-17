using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AgentPlay.Editor
{
    /// <summary>
    /// Finds and calls hooks around an AutoPlay run: BeforePlay in order until one fails, then AfterPlay in reverse on
    /// every hook whose BeforePlay was called (including the one that failed, so it can undo what it already changed).
    /// </summary>
    public static class AutoPlayHooks
    {
        /// <summary>
        /// Hook types AutoPlay may create: concrete, with a public parameterless constructor, not rejected by
        /// <paramref name="skip"/>. <paramref name="skipped"/> lists the concrete types left out, so the caller can
        /// warn about them instead of dropping a game's hook silently. Sorted by full name.
        /// </summary>
        public static List<Type> Discover(IEnumerable<Type> types, Func<Type, bool> skip, out List<Type> skipped)
        {
            var concrete = types.Where(t => typeof(IAgentAutoPlayHook).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();
            skipped = concrete.Where(t => t.GetConstructor(Type.EmptyTypes) == null || skip(t)).ToList();
            var left = skipped;
            return concrete.Where(t => !left.Contains(t)).ToList();
        }

        /// <summary>
        /// True for AgentPlay's own test assemblies (their fake hooks compile into a game that has the test framework
        /// and links the package). A game's own assemblies are never excluded, even when they reference NUnit.
        /// </summary>
        public static bool IsAgentPlayTestAssembly(Assembly assembly) =>
            assembly.GetName().Name.StartsWith("AgentPlay.Tests", StringComparison.Ordinal);

        public static bool Before(IReadOnlyList<IAgentAutoPlayHook> hooks, Action<string> logError, out List<IAgentAutoPlayHook> started)
        {
            started = new List<IAgentAutoPlayHook>();
            foreach (var hook in hooks)
            {
                started.Add(hook);
                try
                {
                    hook.BeforePlay();
                }
                catch (Exception e)
                {
                    logError("[AgentPlay] AutoPlay hook " + hook.GetType().FullName + " failed in BeforePlay: " + e);
                    return false;
                }
            }
            return true;
        }

        /// <summary>Calls AfterPlay in reverse order; false when any of them threw (each is still called).</summary>
        public static bool After(IReadOnlyList<IAgentAutoPlayHook> started, Action<string> logError)
        {
            bool ok = true;
            for (int i = started.Count - 1; i >= 0; i--)
            {
                try
                {
                    started[i].AfterPlay();
                }
                catch (Exception e)
                {
                    ok = false;
                    logError("[AgentPlay] AutoPlay hook " + started[i].GetType().FullName + " failed in AfterPlay: " + e);
                }
            }
            return ok;
        }
    }
}
