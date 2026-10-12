using System;
using System.IO;
using AgentPlay.Editor;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class ReflectLauncherTests
    {
        [Test]
        public void StartInfoRunsReflectForTheProject()
        {
            var info = ReflectLauncher.BuildStartInfo("C:/r/AgentPlay.Runner.dll", "C:/game");
            Assert.AreEqual("dotnet", info.FileName);
            StringAssert.Contains("\"C:/r/AgentPlay.Runner.dll\" reflect --project \"C:/game\"", info.Arguments);
            Assert.IsFalse(info.UseShellExecute);
        }

        [Test]
        public void FolderPathFindsTheDll()
        {
            var dir = Path.Combine(Path.GetTempPath(), "agentplay-runner-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "AgentPlay.Runner.dll"), "");
            StringAssert.Contains(Path.Combine(dir, "AgentPlay.Runner.dll"), ReflectLauncher.BuildStartInfo(dir, "C:/game").Arguments);
        }

        [Test]
        public void StartWithoutRunnerExplainsWhereToSetIt()
        {
            StringAssert.Contains("Runner path", ReflectLauncher.Start("", "C:/game"));
            StringAssert.Contains("Runner path", ReflectLauncher.Start("C:/does/not/exist/AgentPlay.Runner.dll", "C:/game"));
        }

        [Test]
        public void CountsPendingPlayTraces()
        {
            var root = Path.Combine(Path.GetTempPath(), "agentplay-pending-" + Guid.NewGuid().ToString("N"));
            var traces = Path.Combine(root, "AgentPlay", "Traces");
            Directory.CreateDirectory(traces);
            File.WriteAllText(Path.Combine(traces, "20261013-100000-human-play.ndjson"), "");
            File.WriteAllText(Path.Combine(traces, "20261013-100100-human-play-2.ndjson"), "");
            File.WriteAllText(Path.Combine(traces, "20261013-100200-human-debug.ndjson"), "");
            File.WriteAllText(Path.Combine(root, "AgentPlay", "reflected.json"), "[\"20261013-100000-human-play.ndjson\"]");
            Assert.AreEqual(1, ReflectLauncher.CountPendingPlayTraces(root));
            Assert.AreEqual(0, ReflectLauncher.CountPendingPlayTraces(Path.Combine(root, "missing")));
        }

        [Test]
        public void RunnerThatExitsAtOnceIsReportedFinished()
        {
            var dir = Path.Combine(Path.GetTempPath(), "agentplay-badrunner-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "AgentPlay"));
            File.WriteAllText(Path.Combine(dir, "AgentPlay.Runner.dll"), "not a dll");
            Assert.IsNull(ReflectLauncher.Start(dir, dir));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (ReflectLauncher.IsRunning && watch.Elapsed.TotalSeconds < 30) System.Threading.Thread.Sleep(50);
            Assert.IsFalse(ReflectLauncher.IsRunning, "the launcher must notice the exit");
            Assert.AreNotEqual(0, ReflectLauncher.LastExitCode);
            Assert.IsNotEmpty(ReflectLauncher.LastSummary);
        }
    }
}
