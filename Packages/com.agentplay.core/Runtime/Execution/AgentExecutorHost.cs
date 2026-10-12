using System;
using System.Collections.Generic;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AgentPlay
{
    public interface IPlanExecutor
    {
        void Load(Plan plan);
        void Stop();
    }

    /// <summary>
    /// Hidden object that ticks the executor once per frame after game scripts, then reports the frame to the session.
    /// Created and destroyed by the Editor session controller.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [AddComponentMenu("")]
    public sealed class AgentExecutorHost : MonoBehaviour, IPlanExecutor
    {
        ExecutorCore _core;
        Func<JObject> _snapshot;
        Action<IReadOnlyList<ExecutorOutput>, bool> _onFrame;
        HashSet<string> _collecting = new HashSet<string>();
        HashSet<string> _frameEvents = new HashSet<string>();
        bool _collectingImportant;

        /// <param name="onFrame">Outputs of the frame, and whether the events it evaluated included an important one.</param>
        public static AgentExecutorHost Create(ExecutorCore core, Func<JObject> snapshot, Action<IReadOnlyList<ExecutorOutput>, bool> onFrame)
        {
            var go = new GameObject("AgentPlay Executor") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            var host = go.AddComponent<AgentExecutorHost>();
            host._core = core;
            host._snapshot = snapshot;
            host._onFrame = onFrame;
            AgentEvents.Emitted += host.OnEvent;
            return host;
        }

        void OnEvent(AgentEventRecord record)
        {
            _collecting.Add(record.Name);
            if (record.Important) _collectingImportant = true;
        }

        void Update()
        {
            if (_core == null) return;
            // Events emitted by actions during this tick belong to the next frame, for rules and for pausing alike.
            var events = _collecting;
            _collecting = _frameEvents;
            _collecting.Clear();
            _frameEvents = events;
            bool important = _collectingImportant;
            _collectingImportant = false;
            var outputs = _core.Tick(Time.deltaTime, _snapshot, _frameEvents);
            _onFrame?.Invoke(outputs, important);
        }

        public void Load(Plan plan) => _core?.Load(plan);

        public void Stop() => _core?.Stop();

        public void Shutdown()
        {
            AgentEvents.Emitted -= OnEvent;
            _core = null;
            _onFrame = null;
            if (this != null) DestroyImmediate(gameObject);
        }
    }
}
