using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Contract
{
    public interface IConditionContext
    {
        /// <summary>Value of a flat snapshot key, or null.</summary>
        JToken Lookup(string key);
        bool HasEvent(string name);
    }

    public sealed class SnapshotContext : IConditionContext
    {
        readonly JObject _snapshot;
        readonly ICollection<string> _events;

        public SnapshotContext(JObject snapshot, ICollection<string> events)
        {
            _snapshot = snapshot;
            _events = events;
        }

        public JToken Lookup(string key) => _snapshot?[key];

        public bool HasEvent(string name) => _events != null && _events.Contains(name);
    }

    /// <summary>Evaluates parsed conditions. Unknown or mistyped values make comparisons false; it never throws.</summary>
    public static class ConditionEvaluator
    {
        public static bool Evaluate(CondNode node, IConditionContext context) => Value(node, context) is bool b && b;

        static object Value(CondNode node, IConditionContext ctx)
        {
            switch (node)
            {
                case LiteralNode l: return l.Value;
                case EventNode e: return ctx.HasEvent(e.Name);
                case PathNode p: return Scalar(ctx.Lookup(p.Path));
                case CallNode c: return Call(c, ctx);
                case NotNode n: return Value(n.Operand, ctx) is bool b ? !b : (object)null;
                case BinaryNode bin when bin.Op == "&&": return Truth(Value(bin.Left, ctx)) && Truth(Value(bin.Right, ctx));
                case BinaryNode bin when bin.Op == "||": return Truth(Value(bin.Left, ctx)) || Truth(Value(bin.Right, ctx));
                case BinaryNode bin: return Compare(bin.Op, Value(bin.Left, ctx), Value(bin.Right, ctx));
                default: return null;
            }
        }

        static bool Truth(object value) => value is bool b && b;

        static object Scalar(JToken token)
        {
            if (token == null) return null;
            switch (token.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float: return (double)token;
                case JTokenType.Boolean: return (bool)token;
                case JTokenType.String: return (string)token;
                default: return null;
            }
        }

        static object Compare(string op, object left, object right)
        {
            if (left is double a && right is double b)
            {
                switch (op)
                {
                    case "<": return a < b;
                    case "<=": return a <= b;
                    case ">": return a > b;
                    case ">=": return a >= b;
                    case "==": return a == b;
                    case "!=": return a != b;
                }
            }
            if ((left is bool || left is string) && right != null && left.GetType() == right.GetType())
            {
                bool equal = Equals(left, right);
                if (op == "==") return equal;
                if (op == "!=") return !equal;
            }
            return false;   // unknown or mismatched operands: the comparison is false (so "!(missing > 0)" is true)
        }

        static object Call(CallNode call, IConditionContext ctx)
        {
            if (!FindList(ctx, call.Path, out var list, out var field)) return null;
            switch (call.Function)
            {
                case "count":
                    if (field == null) return (double)list.Count;
                    int count = 0;
                    foreach (var item in list) if (Truth(Scalar(Field(item, field)))) count++;
                    return (double)count;
                case "any":
                    if (field == null) return list.Count > 0;
                    foreach (var item in list) if (Truth(Scalar(Field(item, field)))) return true;
                    return false;
                case "min":
                case "max":
                    if (field == null) return null;
                    bool min = call.Function == "min";
                    double best = min ? double.PositiveInfinity : double.NegativeInfinity;
                    foreach (var item in list)
                    {
                        if (Scalar(Field(item, field)) is double d) best = min ? Math.Min(best, d) : Math.Max(best, d);
                    }
                    return best;
                default:
                    return null;
            }
        }

        /// <summary>Element field; elements that are not objects have no fields (never throws).</summary>
        static JToken Field(JToken item, string field) => item is JObject o ? o[field] : null;

        /// <summary>Finds the longest prefix of <paramref name="path"/> whose snapshot value is an array.</summary>
        public static bool FindList(IConditionContext ctx, string path, out JArray list, out string field)
        {
            var segments = path.Split('.');
            for (int k = segments.Length; k >= 1; k--)
            {
                if (ctx.Lookup(string.Join(".", segments, 0, k)) is JArray array)
                {
                    list = array;
                    field = k < segments.Length ? string.Join(".", segments, k, segments.Length - k) : null;
                    return true;
                }
            }
            list = null;
            field = null;
            return false;
        }
    }
}
