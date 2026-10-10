using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AgentPlay.Contract;
using UnityEngine;

namespace AgentPlay
{
    public sealed class ObserveEntry
    {
        public string Path;
        public string Description;
        public Type DeclaringType;
        public MemberInfo Member;
        public string ValueKind;
        public bool IsStatic;
    }

    public sealed class ActionEntry
    {
        public string Name;
        public string Description;
        public bool Safe;
        public Type DeclaringType;
        public MethodInfo Method;
        public List<ActionParamInfo> Params = new List<ActionParamInfo>();
    }

    public sealed class RegistryModel
    {
        public readonly List<ObserveEntry> Observes = new List<ObserveEntry>();
        public readonly List<ActionEntry> Actions = new List<ActionEntry>();
        public readonly List<string> Errors = new List<string>();
        /// <summary>Null when no type carries [AgentGame].</summary>
        public string GameName;
        public string GameVersion = "";
    }

    public static class AgentRegistry
    {
        const BindingFlags MemberFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        public static RegistryModel Build(IEnumerable<Type> types)
        {
            var model = new RegistryModel();
            var observeOwners = new Dictionary<string, string>();
            var actionOwners = new Dictionary<string, string>();
            string safeAction = null;

            foreach (var type in types.Distinct().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                ReadGame(type, model);
                foreach (var member in type.GetMembers(MemberFlags).OrderBy(m => m.Name, StringComparer.Ordinal))
                {
                    var observe = member.GetCustomAttribute<AgentObserveAttribute>(false);
                    if (observe != null && (member is FieldInfo || member is PropertyInfo))
                        AddObserve(type, member, observe, model, observeOwners);

                    var action = member.GetCustomAttribute<AgentActionAttribute>(false);
                    if (action != null && member is MethodInfo method)
                        AddAction(type, method, action, model, actionOwners, ref safeAction);
                }
            }

            model.Observes.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            model.Actions.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return model;
        }

        public static HelloMessage ToHello(RegistryModel model, string fallbackGameName)
        {
            var hello = new HelloMessage
            {
                Protocol = ProtocolVersion.Current,
                Game = model.GameName ?? fallbackGameName,
                Version = model.GameVersion ?? "",
            };
            foreach (var o in model.Observes)
                hello.Observes.Add(new ObserveInfo { Path = o.Path, ValueType = o.ValueKind, Description = o.Description });
            foreach (var a in model.Actions)
                hello.Actions.Add(new ActionInfo { Name = a.Name, Description = a.Description, Safe = a.Safe, Params = new List<ActionParamInfo>(a.Params) });
            hello.Modes.Add(Modes.TurnBased);
            hello.Modes.Add(Modes.Realtime);
            return hello;
        }

        public static string KindOf(Type t)
        {
            if (t == typeof(float) || t == typeof(double)) return ValueKinds.Float;
            if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)) return ValueKinds.Int;
            if (t == typeof(bool)) return ValueKinds.Bool;
            if (t == typeof(string)) return ValueKinds.String;
            if (t.IsEnum) return ValueKinds.Enum;
            if (t == typeof(Vector3)) return ValueKinds.Vector3;
            return null;
        }

        static void AddObserve(Type type, MemberInfo member, AgentObserveAttribute attr, RegistryModel model, Dictionary<string, string> owners)
        {
            string where = type.Name + "." + member.Name;
            if (string.IsNullOrWhiteSpace(attr.Path))
            {
                model.Errors.Add("Observe on " + where + " has an empty path");
                return;
            }

            Type valueType;
            bool isStatic;
            if (member is FieldInfo field)
            {
                valueType = field.FieldType;
                isStatic = field.IsStatic;
            }
            else
            {
                var prop = (PropertyInfo)member;
                var getter = prop.GetGetMethod(true);
                if (getter == null || prop.GetIndexParameters().Length > 0)
                {
                    model.Errors.Add("Observe '" + attr.Path + "' on " + where + " needs a readable, non-indexed property");
                    return;
                }
                valueType = prop.PropertyType;
                isStatic = getter.IsStatic;
            }

            string kind = KindOf(valueType);
            if (kind == null)
            {
                model.Errors.Add("Observe '" + attr.Path + "' on " + where + " has unsupported type " + valueType.Name);
                return;
            }
            if (owners.TryGetValue(attr.Path, out var first))
            {
                model.Errors.Add("Duplicate observe path '" + attr.Path + "' on " + first + " and " + where);
                return;
            }
            owners[attr.Path] = where;
            model.Observes.Add(new ObserveEntry
            {
                Path = attr.Path,
                Description = attr.Description,
                DeclaringType = type,
                Member = member,
                ValueKind = kind,
                IsStatic = isStatic,
            });
        }

        static void AddAction(Type type, MethodInfo method, AgentActionAttribute attr, RegistryModel model,
            Dictionary<string, string> owners, ref string safeAction)
        {
            string where = type.Name + "." + method.Name;
            if (string.IsNullOrWhiteSpace(attr.Name))
            {
                model.Errors.Add("Action on " + where + " has an empty name");
                return;
            }
            if (method.ReturnType != typeof(AgentActionStatus))
            {
                model.Errors.Add("Action '" + attr.Name + "' on " + where + " must return AgentActionStatus");
                return;
            }

            var ps = new List<ActionParamInfo>();
            foreach (var p in method.GetParameters())
            {
                string kind = KindOf(p.ParameterType);
                if (kind == null || kind == ValueKinds.Enum || kind == ValueKinds.Vector3)
                {
                    model.Errors.Add("Action '" + attr.Name + "' on " + where + ": parameter '" + p.Name + "' has unsupported type " + p.ParameterType.Name);
                    return;
                }
                ps.Add(new ActionParamInfo { Name = p.Name, ValueType = kind });
            }

            if (owners.TryGetValue(attr.Name, out var first))
            {
                model.Errors.Add("Duplicate action '" + attr.Name + "' on " + first + " and " + where);
                return;
            }
            if (attr.Safe)
            {
                if (safeAction != null)
                {
                    model.Errors.Add("Only one safe action is allowed: '" + safeAction + "' and '" + attr.Name + "'");
                    return;
                }
                safeAction = attr.Name;
            }

            owners[attr.Name] = where;
            model.Actions.Add(new ActionEntry
            {
                Name = attr.Name,
                Description = attr.Description,
                Safe = attr.Safe,
                DeclaringType = type,
                Method = method,
                Params = ps,
            });
        }

        static void ReadGame(Type type, RegistryModel model)
        {
            var game = type.GetCustomAttribute<AgentGameAttribute>(false);
            if (game == null) return;
            if (model.GameName != null)
            {
                model.Errors.Add("More than one [AgentGame]: '" + model.GameName + "' and '" + game.Name + "'");
                return;
            }
            model.GameName = game.Name;
            model.GameVersion = ResolveVersion(type, game, model.Errors);
        }

        static string ResolveVersion(Type type, AgentGameAttribute game, List<string> errors)
        {
            if (game.Version != null) return game.Version;
            if (string.IsNullOrEmpty(game.VersionMember)) return "";

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            string where = type.Name + "." + game.VersionMember;
            try
            {
                var prop = type.GetProperty(game.VersionMember, flags);
                if (prop != null && prop.PropertyType == typeof(string))
                    return (string)prop.GetValue(null) ?? "";
                var method = type.GetMethod(game.VersionMember, flags, null, Type.EmptyTypes, null);
                if (method != null && method.ReturnType == typeof(string))
                    return (string)method.Invoke(null, null) ?? "";
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                errors.Add("[AgentGame] version member " + where + " threw: " + inner.Message);
                return "";
            }
            errors.Add("[AgentGame] version member " + where + " must be a static string property or a parameterless static method returning string");
            return "";
        }
    }
}
