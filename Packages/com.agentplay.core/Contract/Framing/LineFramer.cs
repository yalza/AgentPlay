using System;
using System.Collections.Generic;
using System.Text;

namespace AgentPlay.Contract
{
    /// <summary>Splits a UTF-8 byte stream into '\n'-terminated lines. Not thread-safe.</summary>
    public sealed class LineFramer
    {
        public const int DefaultMaxLineBytes = 1 << 20;

        readonly List<byte> _buffer = new List<byte>();
        readonly int _maxLineBytes;

        public LineFramer(int maxLineBytes = DefaultMaxLineBytes)
        {
            _maxLineBytes = maxLineBytes;
        }

        /// <summary>Appends bytes and returns every completed line, without the '\n' and a trailing '\r'.</summary>
        public IReadOnlyList<string> Push(byte[] data, int offset, int count)
        {
            var lines = new List<string>();
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                byte b = data[i];
                if (b == (byte)'\n')
                {
                    int len = _buffer.Count;
                    if (len > 0 && _buffer[len - 1] == (byte)'\r') len--;
                    lines.Add(Encoding.UTF8.GetString(_buffer.ToArray(), 0, len));
                    _buffer.Clear();
                    continue;
                }
                if (_buffer.Count >= _maxLineBytes) throw new LineTooLongException(_maxLineBytes);
                _buffer.Add(b);
            }
            return lines;
        }
    }

    public sealed class LineTooLongException : Exception
    {
        public LineTooLongException(int limit) : base("Line exceeds " + limit + " bytes") { }
    }
}
