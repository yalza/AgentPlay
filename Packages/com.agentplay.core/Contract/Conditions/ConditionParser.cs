using System;
using System.Globalization;

namespace AgentPlay.Contract
{
    /// <summary>Recursive-descent parser for plan conditions (spec M2 section 5).</summary>
    public static class ConditionParser
    {
        public static readonly string[] Functions = { "min", "max", "count", "any" };

        static readonly string[] CompareOps = { "<=", ">=", "==", "!=", "<", ">" };

        public static CondNode Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ConditionException("Empty condition", 0);
            var p = new Impl(text);
            var node = p.ParseOr();
            p.SkipWhitespace();
            if (!p.AtEnd) throw new ConditionException("Unexpected '" + p.Current + "'", p.Index);
            return node;
        }

        sealed class Impl
        {
            readonly string _s;
            public int Index;

            public Impl(string s)
            {
                _s = s;
            }

            public bool AtEnd => Index >= _s.Length;
            public char Current => _s[Index];

            char At(int offset) => Index + offset < _s.Length ? _s[Index + offset] : '\0';

            public void SkipWhitespace()
            {
                while (!AtEnd && char.IsWhiteSpace(Current)) Index++;
            }

            bool Match(string token)
            {
                SkipWhitespace();
                if (Index + token.Length > _s.Length) return false;
                if (string.CompareOrdinal(_s, Index, token, 0, token.Length) != 0) return false;
                Index += token.Length;
                return true;
            }

            public CondNode ParseOr()
            {
                var left = ParseAnd();
                while (Match("||")) left = new BinaryNode("||", left, ParseAnd());
                return left;
            }

            CondNode ParseAnd()
            {
                var left = ParseUnary();
                while (Match("&&")) left = new BinaryNode("&&", left, ParseUnary());
                return left;
            }

            CondNode ParseUnary()
            {
                SkipWhitespace();
                if (!AtEnd && Current == '!' && At(1) != '=')
                {
                    Index++;
                    return new NotNode(ParseUnary());
                }
                return ParseCompare();
            }

            CondNode ParseCompare()
            {
                var left = ParseTerm();
                foreach (var op in CompareOps)
                    if (Match(op)) return new BinaryNode(op, left, ParseTerm());
                return left;
            }

            CondNode ParseTerm()
            {
                SkipWhitespace();
                if (AtEnd) throw new ConditionException("Expected a value", Index);
                char c = Current;
                if (c == '(')
                {
                    Index++;
                    var inner = ParseOr();
                    if (!Match(")")) throw new ConditionException("Expected ')'", Index);
                    return inner;
                }
                if (c == '\'') return ParseString();
                if (char.IsDigit(c) || (c == '-' && (char.IsDigit(At(1)) || At(1) == '.')) || (c == '.' && char.IsDigit(At(1))))
                    return ParseNumber();
                if (IsIdentStart(c)) return ParseWord();
                throw new ConditionException("Unexpected '" + c + "'", Index);
            }

            CondNode ParseString()
            {
                int start = Index;
                Index++;
                int end = _s.IndexOf('\'', Index);
                if (end < 0) throw new ConditionException("Unterminated string", start);
                var value = _s.Substring(Index, end - Index);
                Index = end + 1;
                return new LiteralNode(value);
            }

            CondNode ParseNumber()
            {
                int start = Index;
                if (Current == '-') Index++;
                while (!AtEnd && (char.IsDigit(Current) || Current == '.')) Index++;
                var text = _s.Substring(start, Index - start);
                if (!double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d))
                    throw new ConditionException("Invalid number '" + text + "'", start);
                return new LiteralNode(d);
            }

            CondNode ParseWord()
            {
                int start = Index;
                string word = ReadWord();
                if (word == "true") return new LiteralNode(true);
                if (word == "false") return new LiteralNode(false);
                if (word == "event" && !AtEnd && Current == ':')
                {
                    Index++;
                    int nameStart = Index;
                    while (!AtEnd && IsEventChar(Current)) Index++;
                    if (Index == nameStart) throw new ConditionException("Expected an event name", nameStart);
                    return new EventNode(_s.Substring(nameStart, Index - nameStart));
                }
                if (Array.IndexOf(Functions, word) >= 0)
                {
                    if (!Match("(")) throw new ConditionException("Expected '(' after " + word, Index);
                    SkipWhitespace();
                    int pathStart = Index;
                    var path = ReadWord();
                    CheckPath(path, pathStart);
                    if (!Match(")")) throw new ConditionException("Expected ')'", Index);
                    return new CallNode(word, path);
                }
                CheckPath(word, start);
                return new PathNode(word);
            }

            string ReadWord()
            {
                int start = Index;
                while (!AtEnd && IsWordChar(Current)) Index++;
                return _s.Substring(start, Index - start);
            }

            static bool IsIdentStart(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
            static bool IsWordChar(char c) => IsIdentStart(c) || char.IsDigit(c) || c == '.';
            static bool IsEventChar(char c) => (c >= 'a' && c <= 'z') || char.IsDigit(c) || c == '_' || c == '.';

            static void CheckPath(string path, int position)
            {
                bool ok = path.Length > 0;
                if (ok)
                {
                    foreach (var segment in path.Split('.'))
                    {
                        if (segment.Length == 0 || !(segment[0] == '_' || (segment[0] >= 'a' && segment[0] <= 'z')))
                        {
                            ok = false;
                            break;
                        }
                        foreach (var ch in segment)
                        {
                            if (!(ch == '_' || char.IsDigit(ch) || (ch >= 'a' && ch <= 'z')))
                            {
                                ok = false;
                                break;
                            }
                        }
                        if (!ok) break;
                    }
                }
                if (!ok) throw new ConditionException("Invalid path '" + path + "' (use lowercase segments separated by dots)", position);
            }
        }
    }
}
