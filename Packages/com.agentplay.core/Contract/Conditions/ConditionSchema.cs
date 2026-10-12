using System.Collections.Generic;

namespace AgentPlay.Contract
{
    /// <summary>Static type check of a condition against the observes and events a game declared in hello.</summary>
    public sealed class ConditionSchema
    {
        const string Number = "number";
        const string Bool = "bool";
        const string Text = "string";
        const string Other = "other";
        const string Unknown = "unknown";

        readonly Dictionary<string, string> _scalars = new Dictionary<string, string>();
        readonly Dictionary<string, Dictionary<string, string>> _lists = new Dictionary<string, Dictionary<string, string>>();
        readonly HashSet<string> _events = new HashSet<string>();

        public static ConditionSchema FromHello(HelloMessage hello)
        {
            var schema = new ConditionSchema();
            if (hello == null) return schema;
            foreach (var o in hello.Observes)
            {
                if (o.ValueType == ValueKinds.List)
                {
                    var fields = new Dictionary<string, string>();
                    if (o.Fields != null)
                        foreach (var f in o.Fields) fields[f.Path] = f.ValueType;
                    schema._lists[o.Path] = fields;
                }
                else
                {
                    schema._scalars[o.Path] = o.ValueType;
                }
            }
            if (hello.Events != null)
                foreach (var e in hello.Events) schema._events.Add(e.Name);
            return schema;
        }

        public List<string> Check(CondNode node)
        {
            var errors = new List<string>();
            var type = TypeOf(node, errors);
            if (type != Bool && type != Unknown) errors.Add("Condition must be true/false, not a " + type);
            return errors;
        }

        string TypeOf(CondNode node, List<string> errors)
        {
            switch (node)
            {
                case LiteralNode l:
                    return l.Value is double ? Number : l.Value is bool ? Bool : Text;
                case EventNode e:
                    if (!_events.Contains(e.Name)) errors.Add("Unknown event '" + e.Name + "'");
                    return Bool;
                case PathNode p:
                    return PathType(p.Path, errors);
                case CallNode c:
                    return CallType(c, errors);
                case NotNode n:
                {
                    var t = TypeOf(n.Operand, errors);
                    if (t != Bool && t != Unknown) errors.Add("'!' needs a boolean, got " + t);
                    return Bool;
                }
                case BinaryNode b when b.Op == "&&" || b.Op == "||":
                {
                    var l = TypeOf(b.Left, errors);
                    var r = TypeOf(b.Right, errors);
                    if ((l != Bool && l != Unknown) || (r != Bool && r != Unknown))
                        errors.Add("'" + b.Op + "' needs booleans on both sides");
                    return Bool;
                }
                case BinaryNode b:
                    return CompareType(b, errors);
                default:
                    errors.Add("Unsupported expression");
                    return Unknown;
            }
        }

        string PathType(string path, List<string> errors)
        {
            if (_scalars.TryGetValue(path, out var kind)) return KindToType(kind);
            if (FindList(path, out _, out _))
            {
                errors.Add("List '" + path + "' can only be used inside min/max/count/any");
                return Unknown;
            }
            errors.Add("Unknown observe '" + path + "'");
            return Unknown;
        }

        static string KindToType(string kind)
        {
            if (kind == ValueKinds.Float || kind == ValueKinds.Int) return Number;
            if (kind == ValueKinds.Bool) return Bool;
            if (kind == ValueKinds.String || kind == ValueKinds.Enum) return Text;
            return Other;
        }

        bool FindList(string path, out string listPath, out string field)
        {
            var segments = path.Split('.');
            for (int k = segments.Length; k >= 1; k--)
            {
                var prefix = string.Join(".", segments, 0, k);
                if (_lists.ContainsKey(prefix))
                {
                    listPath = prefix;
                    field = k < segments.Length ? string.Join(".", segments, k, segments.Length - k) : null;
                    return true;
                }
            }
            listPath = null;
            field = null;
            return false;
        }

        string CallType(CallNode c, List<string> errors)
        {
            string result = c.Function == "any" ? Bool : Number;
            if (!FindList(c.Path, out var list, out var field))
            {
                errors.Add("'" + c.Path + "' is not a list observe");
                return result;
            }
            string fieldType = null;
            if (field != null)
            {
                if (!_lists[list].TryGetValue(field, out var kind))
                {
                    errors.Add("List '" + list + "' has no field '" + field + "'");
                    return result;
                }
                fieldType = KindToType(kind);
            }
            switch (c.Function)
            {
                case "min":
                case "max":
                    if (field == null) errors.Add(c.Function + "() needs a numeric field, e.g. " + c.Function + "(" + list + ".distance)");
                    else if (fieldType != Number) errors.Add(c.Function + "() needs a numeric field, '" + field + "' is " + fieldType);
                    break;
                case "count":
                    if (field != null && fieldType != Bool) errors.Add("count() needs a boolean field, '" + field + "' is " + fieldType);
                    break;
                default:
                    if (field != null && fieldType != Bool) errors.Add("any() needs a boolean field, '" + field + "' is " + fieldType);
                    break;
            }
            return result;
        }

        string CompareType(BinaryNode b, List<string> errors)
        {
            var l = TypeOf(b.Left, errors);
            var r = TypeOf(b.Right, errors);
            if (l == Unknown || r == Unknown) return Bool;
            if (l == Other || r == Other)
            {
                errors.Add("vector3 values cannot be compared");
                return Bool;
            }
            if (l != r)
            {
                errors.Add("Cannot compare " + l + " with " + r);
                return Bool;
            }
            if (l != Number && b.Op != "==" && b.Op != "!=") errors.Add("Only == and != work on " + l + " values");
            return Bool;
        }
    }
}
