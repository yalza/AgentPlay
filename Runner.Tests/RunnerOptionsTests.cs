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

        [Fact]
        public void ReflectCommandParses()
        {
            var o = RunnerOptions.Parse(new[] { "reflect", "--project", "C:/g", "--trace", "a.ndjson", "--trace", "b.ndjson", "--force" });
            Assert.Equal("reflect", o.Command);
            Assert.Equal("C:/g", o.ProjectDir);
            Assert.Equal(new[] { "a.ndjson", "b.ndjson" }, o.Traces);
            Assert.True(o.Force);
        }

        [Fact]
        public void ReflectNeedsProject() => Assert.Throws<System.ArgumentException>(() => RunnerOptions.Parse(new[] { "reflect" }));

        [Fact]
        public void ReflectRejectsPlayOptions() => Assert.Throws<System.ArgumentException>(() => RunnerOptions.Parse(new[] { "reflect", "--project", "C:/g", "--brain", "opus" }));

        [Fact]
        public void NoReflectFlag() => Assert.True(RunnerOptions.Parse(new[] { "--brain", "opus", "--no-reflect" }).NoReflect);

        [Fact]
        public void ReflectorSmokeFlag() => Assert.True(RunnerOptions.Parse(new[] { "--reflector-smoke" }).ReflectorSmoke);

        [Fact]
        public void RealtimeWithBrainIsAllowed()
        {
            var o = RunnerOptions.Parse(new[] { "--brain", "opus", "--mode", "realtime" });
            Assert.Equal("realtime", o.Mode);
            Assert.True(o.Prefetch);
            Assert.True(o.Router);
        }

        [Fact]
        public void BrainDefaultsToTurnBased() => Assert.Equal("turn_based", RunnerOptions.Parse(new[] { "--brain", "opus" }).Mode);

        [Fact]
        public void NoPrefetchAndRouterOff()
        {
            var o = RunnerOptions.Parse(new[] { "--brain", "opus", "--mode", "realtime", "--no-prefetch", "--router", "off" });
            Assert.False(o.Prefetch);
            Assert.False(o.Router);
        }

        [Fact]
        public void ScriptBrainDoesNotPrefetch() => Assert.False(RunnerOptions.Parse(new[] { "--brain", "script", "x.json", "--mode", "realtime" }).Prefetch);

        [Fact]
        public void RouterValueIsChecked() => Assert.Throws<System.ArgumentException>(() => RunnerOptions.Parse(new[] { "--brain", "opus", "--router", "maybe" }));
    }
}
