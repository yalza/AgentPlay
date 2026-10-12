using System;
using System.Collections;
using System.Reflection;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AgentPlay
{
    public static class SnapshotBuilder
    {
        public const int MaxListItems = 16;

        /// <summary>Reads every observe into a flat object keyed by path. Unreadable values become null.</summary>
        public static JObject Build(RegistryModel model, Func<Type, object> resolveInstance, Action<string> warn = null)
        {
            var o = new JObject();
            foreach (var e in model.Observes)
            {
                object target = null;
                if (!e.IsStatic)
                {
                    target = resolveInstance(e.DeclaringType);
                    if (target == null)
                    {
                        o[e.Path] = JValue.CreateNull();
                        continue;
                    }
                }
                o[e.Path] = e.ValueKind == ValueKinds.List ? ReadList(e, target, warn) : Read(e, target, warn);
            }
            return o;
        }

        /// <summary>Null when the list cannot be read (conditions over it are then false); [] when it is empty or null.</summary>
        static JToken ReadList(ObserveEntry e, object target, Action<string> warn)
        {
            var array = new JArray();
            if (!ReadRaw(e, target, warn, out var raw)) return JValue.CreateNull();
            if (!(raw is IEnumerable items)) return array;
            foreach (var item in items)
            {
                if (item == null) continue;
                if (array.Count == MaxListItems)
                {
                    warn?.Invoke("Observe list '" + e.Path + "' has more than " + MaxListItems + " items; extra items are dropped");
                    break;
                }
                var element = new JObject();
                foreach (var field in e.Fields) element[field.Path] = Read(field, item, warn);
                array.Add(element);
            }
            return array;
        }

        static JToken Read(ObserveEntry e, object target, Action<string> warn) =>
            ReadRaw(e, target, warn, out var value) ? ToToken(e.ValueKind, value) : JValue.CreateNull();

        static bool ReadRaw(ObserveEntry e, object target, Action<string> warn, out object value)
        {
            try
            {
                value = e.Member is FieldInfo f ? f.GetValue(target) : ((PropertyInfo)e.Member).GetValue(target);
                return true;
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                warn?.Invoke("Observe '" + e.Path + "' threw: " + inner.Message);
                value = null;
                return false;
            }
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
