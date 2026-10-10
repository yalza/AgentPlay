using System;
using System.Reflection;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AgentPlay
{
    public static class SnapshotBuilder
    {
        /// <summary>Reads every observe into a flat object keyed by path. Unreadable values become null.</summary>
        public static JObject Build(RegistryModel model, Func<Type, object> resolveInstance, Action<string> warn = null)
        {
            var o = new JObject();
            foreach (var e in model.Observes)
                o[e.Path] = Read(e, resolveInstance, warn);
            return o;
        }

        static JToken Read(ObserveEntry e, Func<Type, object> resolveInstance, Action<string> warn)
        {
            object target = null;
            if (!e.IsStatic)
            {
                target = resolveInstance(e.DeclaringType);
                if (target == null) return JValue.CreateNull();
            }

            object value;
            try
            {
                value = e.Member is FieldInfo f ? f.GetValue(target) : ((PropertyInfo)e.Member).GetValue(target);
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                warn?.Invoke("Observe '" + e.Path + "' threw: " + inner.Message);
                return JValue.CreateNull();
            }
            return ToToken(e.ValueKind, value);
        }

        static JToken ToToken(string kind, object value)
        {
            if (value == null) return JValue.CreateNull();
            switch (kind)
            {
                case ValueKinds.Float: return Number(Convert.ToDouble(value));
                case ValueKinds.Int: return new JValue(Convert.ToInt64(value));
                case ValueKinds.Bool: return new JValue((bool)value);
                case ValueKinds.String: return new JValue((string)value);
                case ValueKinds.Enum: return new JValue(value.ToString());
                case ValueKinds.Vector3:
                    var v = (Vector3)value;
                    return new JArray(Number(v.x), Number(v.y), Number(v.z));
                default: return JValue.CreateNull();
            }
        }

        static JToken Number(double d)
        {
            var c = UnityJson.Clean(d);
            return c.HasValue ? new JValue(c.Value) : JValue.CreateNull();
        }
    }
}
