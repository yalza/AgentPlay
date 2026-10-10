using System;
using AgentPlay.Runner;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class RunnerOptionsTests
    {
        [Fact]
        public void DefaultsWithoutArgs()
        {
            var o = RunnerOptions.Parse(Array.Empty<string>());
            Assert.Equal("127.0.0.1", o.Host);
            Assert.Equal(47800, o.FirstPort);
            Assert.Equal(47809, o.LastPort);
            Assert.Null(o.Mode);
            Assert.False(o.AutoResume);
            Assert.Equal(3.0, o.ResumeSeconds);
            Assert.Equal(0, o.ThinkMs);
            Assert.Equal(0.0, o.IdleExitSeconds);
        }

        [Fact]
        public void ParsesEveryOption()
        {
            var o = RunnerOptions.Parse(new[]
            {
                "--mode", "turn_based", "--auto-resume", "--resume-seconds", "2.5",
                "--think-ms", "3000", "--ports", "47850-47852", "--idle-exit", "15",
            });
            Assert.Equal("turn_based", o.Mode);
            Assert.True(o.AutoResume);
            Assert.Equal(2.5, o.ResumeSeconds);
            Assert.Equal(3000, o.ThinkMs);
            Assert.Equal(47850, o.FirstPort);
            Assert.Equal(47852, o.LastPort);
            Assert.Equal(15.0, o.IdleExitSeconds);
        }

        [Theory]
        [InlineData("--mode fast")]
        [InlineData("--think-ms -1")]
        [InlineData("--ports 47809-47800")]
        [InlineData("--ports abc")]
        [InlineData("--resume-seconds")]
        [InlineData("--unknown")]
        public void RejectsInvalidArguments(string commandLine)
        {
            Assert.Throws<ArgumentException>(() => RunnerOptions.Parse(commandLine.Split(' ')));
        }
    }
}
