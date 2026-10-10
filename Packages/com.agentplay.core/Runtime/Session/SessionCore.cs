using System;
using System.Collections.Generic;
using System.Globalization;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;

namespace AgentPlay
{
    public interface ISessionHost
    {
        double GameTime { get; }
        long Frame { get; }
        void SetPaused(bool paused);
        /// <summary>Sends one encoded line; drops it silently when no runner is connected.</summary>
        void Send(string line);
    }

    /// <summary>Protocol and turn-based logic for one Play-mode session. Main thread only.</summary>
    public sealed class SessionCore
    {
        public const int RecentLimit = 10;

        readonly HelloMessage _hello;
        readonly Func<JObject> _snapshot;
        readonly ISessionHost _host;
        readonly TraceWriter _trace;
        readonly IntervalTimer _snapshotTimer;
        readonly StepPolicy _step = new StepPolicy();
        readonly SequenceCounter _seq = new SequenceCounter();
        readonly List<string> _recent = new List<string>();
        PauseReason _pauseReason;

        public string Mode => _step.Mode;
        public int EventCount { get; private set; }
        public IReadOnlyList<string> RecentEvents => _recent;
        public bool AwaitingResume { get; private set; }

        public SessionCore(HelloMessage hello, Func<JObject> snapshot, ISessionHost host, TraceWriter trace, double snapshotIntervalSeconds)
        {
            _hello = hello;
            _snapshot = snapshot;
            _host = host;
            _trace = trace;
            _snapshotTimer = new IntervalTimer(snapshotIntervalSeconds);
        }

        /// <summary>Sends hello; a runner attaching while the game waits for resume also learns that it is paused.</summary>
        public void OnConnected()
        {
            Send(MessageTypes.Hello, _hello);
            if (AwaitingResume) AnnouncePause();
        }

        public void OnLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            Envelope env;
            try
            {
                env = MessageCodec.Decode(line);
            }
            catch (Exception e)   // ProtocolException, or any codec slip: never let runner input throw into the Editor
            {
                SendError(e.Message);
                return;
            }
            try
            {
                Dispatch(env);
            }
            catch (Exception e)
            {
                SendError("Failed to handle '" + env.Type + "': " + e.Message);
            }
        }

        public void OnEvent(AgentEventRecord rec)
        {
            EventCount++;
            _recent.Add(rec.Time.ToString("0.00", CultureInfo.InvariantCulture) + "s " + rec.Name + (rec.Important ? " !" : ""));
            if (_recent.Count > RecentLimit) _recent.RemoveAt(0);

            var state = _snapshot();
            _trace?.WriteEvent(rec, state);
            Send(MessageTypes.Event, new EventMessage
            {
                Name = rec.Name,
                T = rec.Time,
                Frame = rec.Frame,
                Important = rec.Important,
                Outcome = rec.Outcome.ToString().ToLowerInvariant(),
                Payload = rec.Payload,
                State = state,
            });

            if (rec.Important)
            {
                var reason = _step.OnImportantEvent();
                if (reason != PauseReason.None) Pause(reason);
            }
        }

        /// <summary>Call every Editor update. gameRunning = Play mode and not paused.</summary>
        public void Tick(bool gameRunning)
        {
            if (!gameRunning) return;
            AwaitingResume = false;   // running means someone resumed, possibly by hand

            double t = _host.GameTime;
            if (_snapshotTimer.Tick(t)) _trace?.WriteSnapshot(t, _host.Frame, _snapshot());
            var reason = _step.OnTick(t);
            if (reason != PauseReason.None) Pause(reason);
        }

        void Dispatch(Envelope env)
        {
            switch (env.Type)
            {
                case MessageTypes.SetMode:
                    var mode = env.As<SetModeMessage>().Mode;
                    if (!_step.SetMode(mode))
                    {
                        SendError("Unknown mode '" + mode + "'");
                        return;
                    }
                    if (mode == Modes.TurnBased)
                    {
                        if (AwaitingResume) AnnouncePause();
                        else Pause(PauseReason.Mode);
                    }
                    else
                    {
                        AwaitingResume = false;
                        _host.SetPaused(false);
                    }
                    return;

                case MessageTypes.Resume:
                    var resume = env.As<ResumeMessage>();
                    _step.OnResume(_host.GameTime, resume.MaxGameSeconds);
                    AwaitingResume = false;
                    _host.SetPaused(false);
                    return;

                case MessageTypes.GetState:
                    Send(MessageTypes.State, new StateMessage { T = _host.GameTime, Frame = _host.Frame, State = _snapshot() });
                    return;

                default:
                    SendError("Unknown message type '" + env.Type + "'");
                    return;
            }
        }

        void Pause(PauseReason reason)
        {
            if (AwaitingResume) return;
            AwaitingResume = true;
            _pauseReason = reason;
            _host.SetPaused(true);
            AnnouncePause();
        }

        void AnnouncePause()
        {
            Send(MessageTypes.Paused, new PausedMessage { Reason = _pauseReason.ToString().ToLowerInvariant(), T = _host.GameTime });
        }

        void SendError(string message) => Send(MessageTypes.Error, new ErrorMessage { Message = message });

        void Send<T>(string type, T dto) => _host.Send(MessageCodec.Encode(type, _seq.Next(), dto));
    }
}
