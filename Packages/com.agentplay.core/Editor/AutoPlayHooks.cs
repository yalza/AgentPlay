using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AgentPlay.Editor
{
    /// <summary>Calls hooks around an AutoPlay run: BeforePlay in order until one fails, AfterPlay on the started ones in reverse.</summary>
    public static class AutoPlayHooks
    {
        /// <summary>
        /// Hook types AutoPlay may create: concrete, with a public parameterless constructor, not rejected by
        /// <paramref name="skip"/> (AutoPlay rejects test assemblies, whose fake hooks a game with the test framework
        /// would otherwise pick up). Sorted by full name.
        /// </summary>
        public static List<Type> Discover(IEnumerable<Type> types, Func<Type, bool> skip) =>
            types.Where(t => typeof(IAgentAutoPlayHook).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface
                             && t.GetConstructor(Type.EmptyTypes) != null && !skip(t))
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();

        /// <summary>True for an assembly that references NUnit (a test assembly).</summary>
        public static bool IsTestAssembly(Assembly assembly) =>
            assembly.GetReferencedAssemblies().Any(a => string.Equals(a.Name, "nunit.framework", StringComparison.OrdinalIgnoreCase));

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
