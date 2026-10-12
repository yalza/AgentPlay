using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AgentPlay.Contract;
using UnityEngine;
using EventInfo = AgentPlay.Contract.EventInfo;

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
        /// <summary>List observes only: element type and its fields (paths relative to the element).</summary>
        public Type ElementType;
        public List<ObserveEntry> Fields;
    }

    public sealed class ActionEntry
    {
        public string Name;
        public string Description;
        public bool Safe;
        public Type DeclaringType;
        public MethodInfo Method;
        public bool TakesContext;
        public List<ActionParamInfo> Params = new List<ActionParamInfo>();
    }

    public sealed class RegistryModel
    {
        public readonly List<ObserveEntry> Observes = new List<ObserveEntry>();
        public readonly List<ActionEntry> Actions = new List<ActionEntry>();
        public readonly List<EventInfo> Events = new List<EventInfo>();
        public readonly List<string> Errors = new List<string>();
        /// <summary>Null when no type carries [AgentGame].</summary>
        public string GameName;
        public string GameVersion = "";

        public string SafeAction
        {
            get
            {
                foreach (var a in Actions)
                    if (a.Safe) return a.Name;
                return null;
            }
        }
    }

    public static class AgentRegistry
    {
        const BindingFlags MemberFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        const BindingFlags ElementFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static RegistryModel Build(IEnumerable<Type> types)
        {
            var model = new RegistryModel();
            var ordered = types.Distinct().OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
            var observeOwners = new Dictionary<string, string>();
            var actionOwners = new Dictionary<string, string>();
            var eventNames = new HashSet<string>();
            string safeAction = null;

            var elementTypes = new HashSet<Type>();
            foreach (var type in ordered)
                foreach (var member in type.GetMembers(MemberFlags))
                    if (member.IsDefined(typeof(AgentObserveListAttribute), false) && ElementTypeOf(MemberType(member)) is Type element)
                        elementTypes.Add(element);

            foreach (var type in ordered)
            {
                ReadGame(type, model);
                ReadEvents(type, model, eventNames);
                foreach (var member in type.GetMembers(MemberFlags).OrderBy(m => m.Name, StringComparer.Ordinal))
                {
                    var observe = member.GetCustomAttribute<AgentObserveAttribute>(false);
                    if (observe != null && (member is FieldInfo || member is PropertyInfo) && !elementTypes.Contains(type))
                    {
                        var entry = MakeObserve(type, member, observe.Path, observe.Description, model.Errors);
                        if (entry != null) Register(entry, model, observeOwners);
                    }

                    var list = member.GetCustomAttribute<AgentObserveListAttribute>(false);
                    if (list != null && (member is FieldInfo || member is PropertyInfo))
                        AddObserveList(type, member, list, model, observeOwners);

                    var action = member.GetCustomAttribute<AgentActionAttribute>(false);
                    if (action != null && member is MethodInfo method)
                        AddAction(type, method, action, model, actionOwners, ref safeAction);
                }
            }

            model.Observes.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            model.Actions.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            model.Events.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
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
            foreach (var o in model.Observes) hello.Observes.Add(ToInfo(o));
            foreach (var a in model.Actions)
                hello.Actions.Add(new ActionInfo { Name = a.Name, Description = a.Description, Safe = a.Safe, Params = new List<ActionParamInfo>(a.Params) });
            hello.Events.AddRange(model.Events);
            hello.Modes.Add(Modes.TurnBased);
            hello.Modes.Add(Modes.Realtime);
            return hello;
        }

        static ObserveInfo ToInfo(ObserveEntry o)
        {
            var info = new ObserveInfo { Path = o.Path, ValueType = o.ValueKind, Description = o.Description };
            if (o.Fields != null) info.Fields = o.Fields.Select(ToInfo).ToList();
            return info;
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

        static Type MemberType(MemberInfo member) =>
            member is FieldInfo f ? f.FieldType : member is PropertyInfo p ? p.PropertyType : null;

        static Type ElementTypeOf(Type type)
        {
            if (type == null || type == typeof(string)) return null;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return type.GetGenericArguments()[0];
            foreach (var i in type.GetInterfaces())
                if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return i.GetGenericArguments()[0];
            return null;
        }

        /// <summary>Validates one observe member; returns null (and records an error) when it cannot be read.</summary>
        static ObserveEntry MakeObserve(Type type, MemberInfo member, string path, string description, List<string> errors)
        {
            string where = type.Name + "." + member.Name;
            if (string.IsNullOrWhiteSpace(path))
            {
                errors.Add("Observe on " + where + " has an empty path");
                return null;
            }
            if (!TryReadable(member, out var valueType, out var isStatic))
            {
                errors.Add("Observe '" + path + "' on " + where + " needs a readable, non-indexed property");
                return null;
            }
            string kind = KindOf(valueType);
            if (kind == null)
            {
                errors.Add("Observe '" + path + "' on " + where + " has unsupported type " + valueType.Name);
                return null;
            }
            return new ObserveEntry
            {
                Path = path,
                Description = description,
                DeclaringType = type,
                Member = member,
                ValueKind = kind,
                IsStatic = isStatic,
            };
        }

        static bool TryReadable(MemberInfo member, out Type valueType, out bool isStatic)
        {
            if (member is FieldInfo field)
            {
                valueType = field.FieldType;
                isStatic = field.IsStatic;
                return true;
            }
            var prop = (PropertyInfo)member;
            var getter = prop.GetGetMethod(true);
            valueType = prop.PropertyType;
            isStatic = getter != null && getter.IsStatic;
            return getter != null && prop.GetIndexParameters().Length == 0;
        }

        static void Register(ObserveEntry entry, RegistryModel model, Dictionary<string, string> owners)
        {
            string where = entry.DeclaringType.Name + "." + entry.Member.Name;
            if (owners.TryGetValue(entry.Path, out var first))
            {
                model.Errors.Add("Duplicate observe path '" + entry.Path + "' on " + first + " and " + where);
                return;
            }
            owners[entry.Path] = where;
            model.Observes.Add(entry);
        }

        static void AddObserveList(Type type, MemberInfo member, AgentObserveListAttribute attr, RegistryModel model, Dictionary<string, string> owners)
        {
            string where = type.Name + "." + member.Name;
            if (string.IsNullOrWhiteSpace(attr.Path))
            {
                model.Errors.Add("Observe list on " + where + " has an empty path");
                return;
            }
            if (!TryReadable(member, out var valueType, out var isStatic))
            {
                model.Errors.Add("Observe list '" + attr.Path + "' on " + where + " needs a readable, non-indexed property");
                return;
            }
            var element = ElementTypeOf(valueType);
            if (element == null)
            {
                model.Errors.Add("Observe list '" + attr.Path + "' must be an IEnumerable<T> (" + where + " is " + valueType.Name + ")");
                return;
            }
            var fields = new List<ObserveEntry>();
            foreach (var m in element.GetMembers(ElementFlags).OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                var observe = m.GetCustomAttribute<AgentObserveAttribute>(true);
                if (observe == null || !(m is FieldInfo || m is PropertyInfo)) continue;
                var field = MakeObserve(element, m, observe.Path, observe.Description, model.Errors);
                if (field != null) fields.Add(field);
            }
            if (fields.Count == 0)
            {
                model.Errors.Add("Observe list '" + attr.Path + "': element type " + element.Name + " has no [AgentObserve] members");
                return;
            }
            fields.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            Register(new ObserveEntry
            {
                Path = attr.Path,
                Description = attr.Description,
                DeclaringType = type,
                Member = member,
                ValueKind = ValueKinds.List,
                IsStatic = isStatic,
                ElementType = element,
                Fields = fields,
            }, model, owners);
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

            var parameters = method.GetParameters();
            bool takesContext = parameters.Length > 0 && parameters[0].ParameterType == typeof(AgentActionContext);
            var ps = new List<ActionParamInfo>();
            for (int i = takesContext ? 1 : 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p.ParameterType == typeof(AgentActionContext))
                {
                    model.Errors.Add("Action '" + attr.Name + "' on " + where + ": AgentActionContext must be the first parameter");
                    return;
                }
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
                if (ps.Count > 0)
                {
                    model.Errors.Add("Safe action '" + attr.Name + "' must take no parameters");
                    return;
                }
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
                TakesContext = takesContext,
                Params = ps,
            });
        }

        static void ReadEvents(Type type, RegistryModel model, HashSet<string> names)
        {
            foreach (var e in type.GetCustomAttributes<AgentEventInfoAttribute>(false))
            {
                if (string.IsNullOrWhiteSpace(e.Name))
                {
                    model.Errors.Add("Event on " + type.Name + " has an empty name");
                    continue;
                }
                if (!names.Add(e.Name))
                {
                    model.Errors.Add("Duplicate event '" + e.Name + "' (" + type.Name + ")");
                    continue;
                }
                model.Events.Add(new EventInfo
                {
                    Name = e.Name,
                    Description = e.Description,
                    Important = e.Important,
                    Outcome = e.Outcome.ToString().ToLowerInvariant(),
                });
            }
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
