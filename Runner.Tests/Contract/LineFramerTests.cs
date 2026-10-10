using System.Text;
using AgentPlay.Contract;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class LineFramerTests
    {
        static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

        [Fact]
        public void SplitsCompleteLines()
        {
            var lines = new LineFramer().Push(B("a\nb\n"), 0, 4);
            Assert.Equal(new[] { "a", "b" }, lines);
        }

        [Fact]
        public void KeepsPartialLineUntilNewline()
        {
            var f = new LineFramer();
            Assert.Empty(f.Push(B("ab"), 0, 2));
            Assert.Equal(new[] { "abc" }, f.Push(B("c\n"), 0, 2));
        }

        [Fact]
        public void TrimsCarriageReturn()
        {
            Assert.Equal(new[] { "x" }, new LineFramer().Push(B("x\r\n"), 0, 3));
        }

        [Fact]
        public void ReassemblesMultiByteCharacterSplitAcrossChunks()
        {
            var bytes = B("đá\n");
            var f = new LineFramer();
            Assert.Empty(f.Push(bytes, 0, 1));
            Assert.Equal(new[] { "đá" }, f.Push(bytes, 1, bytes.Length - 1));
        }

        [Fact]
        public void RespectsOffsetAndCount()
        {
            Assert.Equal(new[] { "a" }, new LineFramer().Push(B("xxa\nyy"), 2, 2));
        }

        [Fact]
        public void ThrowsWhenLineExceedsLimit()
        {
            var f = new LineFramer(4);
            Assert.Throws<LineTooLongException>(() => f.Push(B("12345"), 0, 5));
        }
    }
}
