using System;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace AgentPlay
{
    /// <summary>Finds the first live scene instance of a UnityEngine.Object type, caching it until destroyed.</summary>
    public sealed class UnityInstanceResolver
    {
        readonly Dictionary<Type, Object> _cache = new Dictionary<Type, Object>();

        public object Resolve(Type type)
        {
            if (!typeof(Object).IsAssignableFrom(type)) return null;
            if (_cache.TryGetValue(type, out var cached) && cached != null) return cached;
            var found = Object.FindFirstObjectByType(type);
            _cache[type] = found;
            // A destroyed Unity object compares equal to null but is not a C# null; return a real null.
            return found != null ? found : null;
        }
    }
}
