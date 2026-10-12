using System;
using System.Globalization;

namespace AgentPlay.Contract
{
    public abstract class CondNode
    {
    }

    public sealed class LiteralNode : CondNode
    {
        /// <summary>double, bool or string.</summary>
        public readonly object Value;

        public LiteralNode(object value)
        {
            Value = value;
        }

        public override string ToString()
        {
            switch (Value)
            {
                case double d: return d.ToString("R", CultureInfo.InvariantCulture);
                case bool b: return b ? "true" : "false";
                default: return "'" + Value + "'";
            }
        }
    }

    public sealed class EventNode : CondNode
    {
        public readonly string Name;

        public EventNode(string name)
        {
            Name = name;
        }

        public override string ToString() => "event:" + Name;
    }

    public sealed class PathNode : CondNode
    {
        public readonly string Path;

        public PathNode(string path)
        {
            Path = path;
        }

        public override string ToString() => Path;
    }

    public sealed class CallNode : CondNode
    {
        public readonly string Function;
        public readonly string Path;

        public CallNode(string function, string path)
        {
            Function = function;
            Path = path;
        }

        public override string ToString() => Function + "(" + Path + ")";
    }

    public sealed class NotNode : CondNode
    {
        public readonly CondNode Operand;

        public NotNode(CondNode operand)
        {
            Operand = operand;
        }

        public override string ToString() => "!(" + Operand + ")";
    }

    public sealed class BinaryNode : CondNode
    {
        /// <summary>"&amp;&amp;", "||", "&lt;", "&lt;=", "&gt;", "&gt;=", "==" or "!=".</summary>
        public readonly string Op;
        public readonly CondNode Left;
        public readonly CondNode Right;

        public BinaryNode(string op, CondNode left, CondNode right)
        {
            Op = op;
            Left = left;
            Right = right;
        }

        public override string ToString() => "(" + Left + " " + Op + " " + Right + ")";
    }

    public sealed class ConditionException : Exception
    {
        public int Position { get; }

        public ConditionException(string message, int position) : base(message + " at " + position)
        {
            Position = position;
        }
    }
}
