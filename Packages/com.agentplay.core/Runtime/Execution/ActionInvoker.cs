using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace AgentPlay
{
    public sealed class AgentActionException : Exception
    {
        public AgentActionException(string message) : base(message) { }
    }

    public interface IActionRunner
    {
        bool Has(string action);
        /// <summary>Runs one frame of an action. Throws AgentActionException on any failure to call it.</summary>
        AgentActionStatus Run(string action, JObject args, AgentActionContext context);
    }

    /// <summary>Calls [AgentAction] methods by reflection with arguments converted from JSON.</summary>
    public sealed class ActionInvoker : IActionRunner
    {
        readonly Dictionary<string, ActionEntry> _byName = new Dictionary<string, ActionEntry>();
        readonly Func<Type, object> _resolveInstance;

        public ActionInvoker(RegistryModel model, Func<Type, object> resolveInstance)
        {
            foreach (var a in model.Actions) _byName[a.Name] = a;
            _resolveInstance = resolveInstance;
        }

        public bool Has(string action) => action != null && _byName.ContainsKey(action);

        public AgentActionStatus Run(string action, JObject args, AgentActionContext context)
        {
            if (action == null || !_byName.TryGetValue(action, out var entry))
                throw new AgentActionException("Unknown action '" + action + "'");

            object target = null;
            if (!entry.Method.IsStatic)
            {
                target = _resolveInstance(entry.DeclaringType);
                if (target == null) throw new AgentActionException("No " + entry.DeclaringType.Name + " instance for action '" + action + "'");
            }

            var parameters = entry.Method.GetParameters();
            var values = new object[parameters.Length];
            int first = 0;
            if (entry.TakesContext)
            {
                values[0] = context ?? new AgentActionContext();
                first = 1;
            }
            for (int i = first; i < parameters.Length; i++) values[i] = Convert(args, parameters[i], action);

            try
            {
                return (AgentActionStatus)entry.Method.Invoke(target, values);
            }
            catch (TargetInvocationException e)
            {
                throw new AgentActionException("Action '" + action + "' threw: " + (e.InnerException ?? e).Message);
            }
        }

        static object Convert(JObject args, ParameterInfo p, string action)
        {
            var token = args?[p.Name];
            if (token == null || token.Type == JTokenType.Null)
                throw new AgentActionException("Action '" + action + "': missing argument '" + p.Name + "'");
            try
            {
                if (p.ParameterType == typeof(string))
                {
                    if (token.Type != JTokenType.String) throw new FormatException("expected a string");
                    return (string)token;
                }
                if (p.ParameterType == typeof(bool))
                {
                    if (token.Type != JTokenType.Boolean) throw new FormatException("expected true or false");
                    return (bool)token;
                }
                if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) throw new FormatException("expected a number");
                double d = (double)token;
                if (p.ParameterType == typeof(float)) return (float)d;
                if (p.ParameterType == typeof(double)) return d;
                if (d != Math.Floor(d)) throw new FormatException("expected a whole number");
                return System.Convert.ChangeType((long)d, p.ParameterType, CultureInfo.InvariantCulture);
            }
            catch (Exception e) when (e is FormatException || e is OverflowException || e is InvalidCastException)
            {
                throw new AgentActionException("Action '" + action + "': argument '" + p.Name + "' " + e.Message);
            }
        }
    }
}
