using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AgentPlay
{
    public static class TypeScanner
    {
        const BindingFlags MemberFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        static readonly Assembly RuntimeAssembly = typeof(AgentObserveAttribute).Assembly;

        /// <summary>Types carrying AgentPlay attributes in game assemblies. Test assemblies are skipped.</summary>
        public static List<Type> FindAnnotatedTypes()
        {
            var result = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!ShouldScan(asm)) continue;
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }
                foreach (var t in types)
                    if (IsAnnotated(t)) result.Add(t);
            }
            return result;
        }

        public static bool ShouldScan(Assembly asm)
        {
            if (asm == RuntimeAssembly || asm.IsDynamic) return false;
            string runtimeName = RuntimeAssembly.GetName().Name;
            bool referencesRuntime = false;
            foreach (var r in asm.GetReferencedAssemblies())
            {
                if (r.Name == "nunit.framework") return false;
                if (r.Name == runtimeName) referencesRuntime = true;
            }
            return referencesRuntime;
        }

        public static bool IsAnnotated(Type t)
        {
            if (t.IsDefined(typeof(AgentGameAttribute), false)) return true;
            foreach (var m in t.GetMembers(MemberFlags))
                if (m.IsDefined(typeof(AgentObserveAttribute), false) || m.IsDefined(typeof(AgentActionAttribute), false))
                    return true;
            return false;
        }
    }
}
