using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace AgentPlay.Editor
{
    /// <summary>Runs "dotnet AgentPlay.Runner.dll reflect" in the background for the window's learn button.</summary>
    public static class ReflectLauncher
    {
        const string RunnerDll = "AgentPlay.Runner.dll";
        static readonly Regex PlayTrace = new Regex(@"-play(-\d+)?\.ndjson$", RegexOptions.IgnoreCase);
        static readonly object LogLock = new object();
        static Process _process;
        static string _lastLine;

        public static bool IsRunning { get; private set; }
        public static int? LastExitCode { get; private set; }
        public static string LastSummary { get; private set; }
        public static string LogPath { get; private set; }

        /// <summary><paramref name="runnerPath"/> is the runner dll or the folder that holds it.</summary>
        public static ProcessStartInfo BuildStartInfo(string runnerPath, string projectRoot)
        {
            return new ProcessStartInfo("dotnet")
            {
                Arguments = "\"" + ResolveDll(runnerPath) + "\" reflect --project \"" + projectRoot + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            };
        }

        /// <summary>Starts the reflector; returns an error to show, or null when it started.</summary>
        public static string Start(string runnerPath, string projectRoot)
        {
            if (IsRunning) return "Already running";
            if (string.IsNullOrWhiteSpace(runnerPath) || !File.Exists(ResolveDll(runnerPath)))
                return "Set the Runner path (" + RunnerDll + " or its folder) first";

            string runs = Path.Combine(projectRoot, "AgentPlay", "Runs");
            Directory.CreateDirectory(runs);
            LogPath = Path.Combine(runs, "reflect-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log");
            _lastLine = null;
            LastSummary = null;
            try
            {
                _process = new Process { StartInfo = BuildStartInfo(runnerPath, projectRoot), EnableRaisingEvents = true };
                _process.OutputDataReceived += (_, e) => Append(e.Data);
                _process.ErrorDataReceived += (_, e) => Append(e.Data);
                _process.Exited += (_, __) => OnExited();
                _process.Start();
                _process.StandardInput.Close();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                IsRunning = true;
                return null;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is IOException)
            {
                return "Cannot start dotnet: " + e.Message;
            }
        }

        /// <summary>Play-tagged traces that are not in AgentPlay/reflected.json yet.</summary>
        public static int CountPendingPlayTraces(string projectRoot)
        {
            string traces = Path.Combine(projectRoot, "AgentPlay", "Traces");
            if (!Directory.Exists(traces)) return 0;
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string ledger = Path.Combine(projectRoot, "AgentPlay", "reflected.json");
            try
            {
                if (File.Exists(ledger))
                    foreach (var name in JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(ledger)) ?? new List<string>())
                        done.Add(name);
            }
            catch (Exception e) when (e is JsonException || e is IOException)
            {
            }
            int count = 0;
            foreach (var path in Directory.GetFiles(traces, "*.ndjson"))
            {
                string name = Path.GetFileName(path);
                if (PlayTrace.IsMatch(name) && !done.Contains(name)) count++;
            }
            return count;
        }

        static string ResolveDll(string runnerPath)
        {
            if (string.IsNullOrWhiteSpace(runnerPath)) return "";
            return Directory.Exists(runnerPath) ? Path.Combine(runnerPath, RunnerDll) : runnerPath;
        }

        static void Append(string line)
        {
            if (line == null) return;
            lock (LogLock)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
                _lastLine = line;
                if (line.Contains("reflect done")) LastSummary = line;
            }
        }

        static void OnExited()
        {
            lock (LogLock)
            {
                LastExitCode = _process.ExitCode;
                if (LastSummary == null) LastSummary = _lastLine ?? "(no output)";
                IsRunning = false;
            }
        }
    }
}
