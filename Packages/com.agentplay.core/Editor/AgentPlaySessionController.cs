using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AgentPlay.Editor
{
    /// <summary>Starts a session when Play mode begins and tears it down when it ends or the domain reloads.</summary>
    [InitializeOnLoad]
    public static class AgentPlaySessionController
    {
        const double SnapshotIntervalSeconds = 2.0;
        const int SnapshotWarnBytes = 4096;
        const string Who = "human";   // M1: the agent does not play yet

        static BridgeServer _server;
        static SessionCore _core;
        static TraceWriter _trace;
        static RegistryModel _model;
        static UnityInstanceResolver _resolver;
        static bool _previousRunInBackground;
        static readonly HashSet<string> Warned = new HashSet<string>();
        static readonly UnityHost Host = new UnityHost();

        public static bool IsActive { get; private set; }
        public static int Port => _server?.Port ?? 0;
        public static bool IsRunnerConnected => _server != null && _server.IsClientConnected;
        public static string Mode => _core?.Mode;
        public static int EventCount => _core?.EventCount ?? 0;
        public static IReadOnlyList<string> RecentEvents => _core?.RecentEvents ?? (IReadOnlyList<string>)Array.Empty<string>();
        public static string TracePath { get; private set; }
        public static IReadOnlyList<string> RegistryErrors => _model?.Errors ?? (IReadOnlyList<string>)Array.Empty<string>();

        static AgentPlaySessionController()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += End;
            EditorApplication.quitting += End;
            EditorApplication.update += Tick;
            if (EditorApplication.isPlaying) Begin();   // domain reloaded while playing
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode) Begin();
            else if (change == PlayModeStateChange.ExitingPlayMode) End();
        }

        static void Begin()
        {
            if (IsActive || !AgentPlaySettings.Enabled) return;

            var sw = Stopwatch.StartNew();
            _model = AgentRegistry.Build(TypeScanner.FindAnnotatedTypes());
            sw.Stop();
            Debug.Log("[AgentPlay] Registry: " + _model.Observes.Count + " observes, " + _model.Actions.Count + " actions in " + sw.ElapsedMilliseconds + " ms");
            foreach (var error in _model.Errors) Debug.LogError("[AgentPlay] " + error);

            _resolver = new UnityInstanceResolver();
            Warned.Clear();

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string tag = AgentPlaySettings.SessionTag;
            TracePath = TracePaths.Build(projectRoot, DateTime.Now, Who, tag, File.Exists);
            _trace = TracePaths.Open(TracePath);
            var hello = AgentRegistry.ToHello(_model, Application.productName);
            _trace.WriteHeader(hello.Game, hello.Version, Who, tag, DateTime.UtcNow);

            _server = new BridgeServer();
            Host.Server = _server;
            _core = new SessionCore(hello, Snapshot, Host, _trace, SnapshotIntervalSeconds);

            if (_server.Start())
                Debug.Log("[AgentPlay] Bridge listening on 127.0.0.1:" + _server.Port);
            else
                Debug.LogError("[AgentPlay] No free port in " + BridgeServer.FirstPort + "-" + BridgeServer.LastPort + "; runner cannot connect");

            // The agent keeps playing while the Editor window is unfocused; restored in End().
            _previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;

            AgentEvents.MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            AgentEvents.Emitted -= OnEvent;
            AgentEvents.Emitted += OnEvent;
            IsActive = true;
        }

        static void End()
        {
            if (!IsActive) return;
            AgentEvents.Emitted -= OnEvent;
            Application.runInBackground = _previousRunInBackground;
            _server?.Dispose();
            _trace?.Dispose();
            _server = null;
            Host.Server = null;
            _trace = null;
            _core = null;
            IsActive = false;
        }

        static void Tick()
        {
            if (!IsActive) return;
            while (_server.TryGetSignal(out var signal))
                if (signal == BridgeSignal.Connected) _core.OnConnected();
            while (_server.TryReceive(out var line))
                _core.OnLine(line);
            _core.Tick(EditorApplication.isPlaying && !EditorApplication.isPaused);
        }

        static void OnEvent(AgentEventRecord record) => _core?.OnEvent(record);

        static JObject Snapshot()
        {
            var s = SnapshotBuilder.Build(_model, _resolver.Resolve, WarnOnce);
            if (!Warned.Contains("size") && s.ToString(Formatting.None).Length > SnapshotWarnBytes)
            {
                Warned.Add("size");
                Debug.LogWarning("[AgentPlay] Snapshot is larger than " + SnapshotWarnBytes + " bytes; consider observing fewer values");
            }
            return s;
        }

        static void WarnOnce(string message)
        {
            if (Warned.Add(message)) Debug.LogWarning("[AgentPlay] " + message);
        }

        sealed class UnityHost : ISessionHost
        {
            public BridgeServer Server;
            public double GameTime => Time.timeAsDouble;
            public long Frame => Time.frameCount;
            public void SetPaused(bool paused) => EditorApplication.isPaused = paused;
            public void Send(string line) => Server?.TrySend(line);
        }
    }
}
