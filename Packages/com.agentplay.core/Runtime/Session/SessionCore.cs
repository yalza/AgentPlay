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
        readonly IPlanExecutor _executor;
        readonly StepPolicy _step = new StepPolicy();
        readonly SequenceCounter _seq = new SequenceCounter();
        readonly List<string> _recent = new List<string>();
        PauseReason _pauseReason;
        bool _agentControl;
        bool _repauseOnResume;

        public string Mode => _step.Mode;
        public int EventCount { get; private set; }
        public IReadOnlyList<string> RecentEvents => _recent;
        public bool AwaitingResume { get; private set; }

        public SessionCore(HelloMessage hello, Func<JObject> snapshot, ISessionHost host, TraceWriter trace,
            double snapshotIntervalSeconds, IPlanExecutor executor = null)
        {
            _hello = hello;
            _snapshot = snapshot;
            _host = host;
            _trace = trace;
            _executor = executor;
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
        }

        /// <summary>
        /// Called once per game frame after the executor ran; decides turn-based stops (spec M2 section 7).
        /// <paramref name="importantEvent"/> is true when the events the executor evaluated this frame included an important one.
        /// </summary>
        public void OnFrameEnd(IReadOnlyList<ExecutorOutput> outputs, bool importantEvent, IReadOnlyList<string> importantEvents = null)
        {
            bool ended = false;
            bool ruleFired = false;
            double t = _host.GameTime;
            foreach (var o in outputs)
            {
                if (o.Kind == ExecutorOutputKinds.Waiting)
                {
                    Send(MessageTypes.Waiting, new WaitingMessage { T = t });
                    continue;
                }
                if (o.Kind == ExecutorOutputKinds.ReflexFired)
                {
                    var handled = importantEvents != null ? new List<string>(importantEvents) : new List<string>();
                    _trace?.WriteReflex(o.ReflexId, t, handled);
                    Send(MessageTypes.Reflex, new ReflexMessage { Id = o.ReflexId, T = t, Frame = _host.Frame, HandledEvents = handled });
                    ruleFired = true;   // the frame was handled in-game: an important event does not pause it
                    continue;
                }
                if (o.Kind == ExecutorOutputKinds.ReflexEnded)
                {
                    _trace?.WriteReflexEnd(o.ReflexId, t, o.Reason);
                    Send(MessageTypes.ReflexEnd, new ReflexEndMessage { Id = o.ReflexId, T = t, Status = o.Reason });
                    continue;
                }
                var status = new PlanStatusMessage { PlanId = o.PlanId, Status = o.Kind, Step = o.Step, Reason = o.Reason };
                _trace?.WritePlanStatus(status, t);
                Send(MessageTypes.PlanStatus, status);
                if (o.Kind == PlanStatuses.PlanDone || o.Kind == PlanStatuses.PlanFailed) ended = true;
                if (o.Kind == PlanStatuses.RuleFired) ruleFired = true;
            }

            var reason = PauseReason.None;
            if (ended) reason = _step.OnPlanEnded();
            else if (importantEvent && !ruleFired) reason = _step.OnImportantEvent();
            if (reason != PauseReason.None) Pause(reason);
        }

        /// <summary>Call every Editor update. gameRunning = Play mode and not paused.</summary>
        public void Tick(bool gameRunning)
        {
            if (!gameRunning) return;
            AwaitingResume = false;   // running means someone resumed, possibly by hand
            _repauseOnResume = false;

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
                        _repauseOnResume = false;
                        _host.SetPaused(false);
                        Pause(PauseReason.Mode);   // realtime: tells the runner to start
                    }
                    return;

                case MessageTypes.Resume:
                    var resume = env.As<ResumeMessage>();
                    if (_repauseOnResume)
                    {
                        _repauseOnResume = false;
                        AnnouncePause();   // the plan sent with this resume was rejected; stay paused
                        return;
                    }
                    _step.OnResume(_host.GameTime, resume.MaxGameSeconds);
                    AwaitingResume = false;
                    _host.SetPaused(false);
                    return;

                case MessageTypes.GetState:
                    Send(MessageTypes.State, new StateMessage { T = _host.GameTime, Frame = _host.Frame, State = _snapshot() });
                    return;

                case MessageTypes.Plan:
                    HandlePlan(env);
                    return;

                case MessageTypes.StopPlan:
                    _executor?.Stop();
                    Send(MessageTypes.Waiting, new WaitingMessage { T = _host.GameTime });
                    return;

                default:
                    SendError("Unknown message type '" + env.Type + "'");
                    return;
            }
        }

        void HandlePlan(Envelope env)
        {
            var plan = env.As<PlanMessage>().Plan;
            double t = _host.GameTime;
            var errors = PlanValidator.Validate(plan, _hello);
            if (errors.Count > 0)
            {
                var status = new PlanStatusMessage
                {
                    PlanId = plan?.Id,
                    Status = PlanStatuses.PlanFailed,
                    Step = 0,
                    Reason = "invalid: " + string.Join("; ", errors),
                };
                _trace?.WritePlanStatus(status, t);
                Send(MessageTypes.PlanStatus, status);
                if (AwaitingResume)
                {
                    _pauseReason = PauseReason.Plan;
                }
                else
                {
                    var reason = _step.OnPlanEnded();
                    if (reason != PauseReason.None) Pause(reason);
                }
                // The runner sends resume right after every plan; it must not run the game without one.
                _repauseOnResume = AwaitingResume && _step.Mode == Modes.TurnBased;
                return;
            }
            if (_executor == null)
            {
                SendError("This session has no plan executor");
                return;
            }
            _repauseOnResume = false;
            if (!_agentControl)
            {
                _agentControl = true;
                _trace?.WriteControl("agent", t);
            }
            _trace?.WritePlan(env.Body["plan"] as JObject, t);
            _executor.Load(plan);
        }

        void Pause(PauseReason reason)
        {
            if (_step.Mode == Modes.Realtime)
            {
                Send(MessageTypes.Attention, new AttentionMessage
                {
                    Reason = reason.ToString().ToLowerInvariant(),
                    T = _host.GameTime,
                    State = _snapshot(),
                    TimeSplit = Split(),
                });
                return;
            }
            if (AwaitingResume) return;
            AwaitingResume = true;
            _pauseReason = reason;
            _host.SetPaused(true);
            AnnouncePause();
        }

        void AnnouncePause()
        {
            Send(MessageTypes.Paused, new PausedMessage
            {
                Reason = _pauseReason.ToString().ToLowerInvariant(),
                T = _host.GameTime,
                State = _snapshot(),
                TimeSplit = Split(),
            });
        }

        TimeSplit Split()
        {
            var live = _executor?.TimeSplit;
            return live == null ? new TimeSplit() : new TimeSplit { Safe = live.Safe, Plan = live.Plan, Reflex = live.Reflex };
        }

        void SendError(string message) => Send(MessageTypes.Error, new ErrorMessage { Message = message });

        void Send<T>(string type, T dto) => _host.Send(MessageCodec.Encode(type, _seq.Next(), dto));
    }
}
