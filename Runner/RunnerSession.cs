using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AgentPlay.Contract;
using AgentPlay.Runner.Brains;
using AgentPlay.Runner.Learning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner
{
    public sealed class Outgoing
    {
        public string Type;
        public object Body;
        public int DelayMs;
    }

    /// <summary>Reacts to Unity messages. Pure: printing, sending and asking the brain are done by the caller.</summary>
    public sealed class RunnerSession
    {
        public const int MaxRecentEvents = 20;
        public const int ExitWon = 0;
        public const int ExitLost = 3;
        public const int ExitBrainStopped = 4;
        public const int ExitProtocol = 5;
        public const int ExitNotFinished = 6;

        readonly RunnerOptions _options;
        readonly Action<string> _print;
        readonly BrainContext _context = new BrainContext();
        readonly TelemetryRecorder _telemetry;
        readonly Func<HelloMessage, Playbook> _loadPlaybook;
        /// <summary>Game time of the pause the last plan answered; a repeat announcement of it is ignored.</summary>
        double? _answeredPauseT;

        public bool HasHello { get; private set; }
        public HelloMessage Hello { get; private set; }
        public bool DecisionPending { get; private set; }
        /// <summary>"won" or "lost" once the game emitted game.won / game.lost.</summary>
        public string GameResult { get; private set; }
        public bool Finished { get; private set; }
        public bool Realtime => _options.Mode == Modes.Realtime;
        /// <summary>The plan that ended last (plan_done or plan_failed) and whether it succeeded.</summary>
        public string LastEndedPlanId { get; private set; }
        public bool LastEndedDone { get; private set; }
        /// <summary>Newest game state and time seen in any message (look-ahead requests use them).</summary>
        public JObject LatestState { get; private set; }
        public double LatestT { get; private set; }
        string _prefetchOpportunity;
        bool _invalidated;
        public int ExitCode { get; private set; }
        /// <summary>Exit code when the runner stops: the result if the run finished, else 6 in brain mode (Unity went away).</summary>
        public int ExitCodeOnStop => Finished ? ExitCode : BrainMode ? ExitNotFinished : 0;

        bool BrainMode => _options.Brain != null;

        public RunnerSession(RunnerOptions options, Action<string> print, TelemetryRecorder telemetry = null, Func<HelloMessage, Playbook> loadPlaybook = null)
        {
            _loadPlaybook = loadPlaybook;
            _options = options;
            _print = print;
            _telemetry = telemetry;
        }

        public void Reset()
        {
            HasHello = false;
            Hello = null;
            DecisionPending = false;
            GameResult = null;
            _answeredPauseT = null;
        }

        public IReadOnlyList<Outgoing> Handle(Envelope env)
        {
            var outgoing = new List<Outgoing>();
            try
            {
                Dispatch(env, outgoing);
            }
            catch (Exception e) when (e is JsonException || e is FormatException || e is InvalidCastException || e is ArgumentException)
            {
                // Field type mismatches from Unity must not crash the runner.
                _print("bad '" + env.Type + "' message from Unity: " + e.Message);
                outgoing.Clear();
            }
            return outgoing;
        }

        /// <summary>The live context, without taking the pending decision.</summary>
        public BrainContext PeekContext() => _context;

        /// <summary>Plan id whose last step just started, once; null otherwise.</summary>
        public string TakePrefetchOpportunity()
        {
            var id = _prefetchOpportunity;
            _prefetchOpportunity = null;
            return id;
        }

        /// <summary>True once after a surprise (important event attention, failed plan) that makes a look-ahead plan stale.</summary>
        public bool TakeInvalidated()
        {
            bool was = _invalidated;
            _invalidated = false;
            return was;
        }

        public BrainContext TakeDecisionContext()
        {
            DecisionPending = false;
            return _context;
        }

        /// <summary>Turns the brain's answer into messages: the plan, then resume. Null or invalid plans finish the run.</summary>
        public IReadOnlyList<Outgoing> OnPlan(Plan plan)
        {
            if (plan == null)
            {
                _print("brain stopped: " + (_context.StopReason ?? "no more plans"));
                Finish(ExitBrainStopped);
                return new Outgoing[0];
            }
            var errors = PlanValidator.Validate(plan, Hello);
            if (errors.Count > 0)
            {
                _print("brain produced an invalid plan " + plan.Id + ": " + string.Join("; ", errors));
                Finish(ExitBrainStopped);
                return new Outgoing[0];
            }
            _context.PlansSent++;
            _telemetry?.OnPlanSent();
            _context.LastPlan = plan;
            _answeredPauseT = _context.PauseT;
            _context.RecentEvents.Clear();
            _context.PlanStatuses.Clear();
            _print("sent plan " + plan.Id + ": " + plan.Intent);
            if (Realtime)
                return new[] { new Outgoing { Type = MessageTypes.Plan, Body = new PlanMessage { Plan = plan }, DelayMs = _options.ThinkMs } };
            return new[]
            {
                new Outgoing { Type = MessageTypes.Plan, Body = new PlanMessage { Plan = plan }, DelayMs = _options.ThinkMs },
                new Outgoing { Type = MessageTypes.Resume, Body = new ResumeMessage { MaxGameSeconds = 0 } },
            };
        }

        void Dispatch(Envelope env, List<Outgoing> outgoing)
        {
            switch (env.Type)
            {
                case MessageTypes.Hello:
                    Hello = env.As<HelloMessage>();
                    HasHello = true;
                    _context.Hello = Hello;
                    _telemetry?.OnHello(Hello);
                    _context.Playbook = _loadPlaybook?.Invoke(Hello);
                    PrintHello();
                    if (Hello.Protocol != ProtocolVersion.Current)
                    {
                        _print("WARNING: Unity speaks protocol " + Hello.Protocol + ", runner speaks " + ProtocolVersion.Current);
                        if (BrainMode)
                        {
                            Finish(ExitProtocol);
                            break;
                        }
                    }
                    if (_options.Mode != null)
                        outgoing.Add(new Outgoing { Type = MessageTypes.SetMode, Body = new SetModeMessage { Mode = _options.Mode } });
                    break;

                case MessageTypes.Event:
                    var e = env.As<EventMessage>();
                    Latest(e.State, e.T);
                    _telemetry?.OnEvent(e);
                    _print("event t=" + Fmt(e.T) + " frame=" + e.Frame + " " + e.Name + (e.Important ? " (important)" : "") + " payload=" + Compact(e.Payload));
                    _context.RecentEvents.Add(e);
                    if (_context.RecentEvents.Count > MaxRecentEvents) _context.RecentEvents.RemoveAt(0);
                    if (e.Name == ReservedEvents.GameWon) GameResult = "won";
                    else if (e.Name == ReservedEvents.GameLost) GameResult = "lost";
                    break;

                case MessageTypes.Attention:
                    var at = env.As<AttentionMessage>();
                    Latest(at.State, at.T);
                    _telemetry?.OnAttention(at);
                    _print("attention t=" + Fmt(at.T) + " reason=" + at.Reason);
                    if (at.Reason == "event") _invalidated = true;
                    if (BrainMode) OnDecisionMessage(at.Reason, at.T, at.State);
                    break;

                case MessageTypes.Paused:
                    var p = env.As<PausedMessage>();
                    Latest(p.State, p.T);
                    _telemetry?.OnPaused(p);
                    _print("paused t=" + Fmt(p.T) + " reason=" + p.Reason);
                    if (BrainMode) OnDecisionMessage(p.Reason, p.T, p.State);
                    else if (_options.AutoResume)
                    {
                        outgoing.Add(new Outgoing
                        {
                            Type = MessageTypes.Resume,
                            Body = new ResumeMessage { MaxGameSeconds = _options.ResumeSeconds },
                            DelayMs = _options.ThinkMs,
                        });
                    }
                    break;

                case MessageTypes.PlanStatus:
                    var s = env.As<PlanStatusMessage>();
                    _telemetry?.OnPlanStatus(s);
                    if (s.Status == PlanStatuses.PlanDone || s.Status == PlanStatuses.PlanFailed)
                    {
                        LastEndedPlanId = s.PlanId;
                        LastEndedDone = s.Status == PlanStatuses.PlanDone;
                        if (!LastEndedDone) _invalidated = true;
                    }
                    else if (s.Status == PlanStatuses.StepDone && _context.LastPlan != null && s.PlanId == _context.LastPlan.Id
                             && s.Step == _context.LastPlan.Steps.Count - 2)
                    {
                        _prefetchOpportunity = s.PlanId;
                    }
                    _print("plan_status " + s.PlanId + " " + s.Status + " step=" + s.Step + " reason=" + s.Reason);
                    _context.PlanStatuses.Add(s);
                    if ((s.Status == PlanStatuses.PlanDone || s.Status == PlanStatuses.PlanFailed) && _context.LastPlan != null && s.PlanId == _context.LastPlan.Id)
                        _context.History.Add(_context.LastPlan.Id + ": " + _context.LastPlan.Intent + " -> " + s.Status
                            + (string.IsNullOrEmpty(s.Reason) ? "" : " (" + s.Reason + ")"));
                    if (s.Status == PlanStatuses.PlanDone || s.Status == PlanStatuses.PlanFailed) _answeredPauseT = null;
                    break;

                case MessageTypes.Reflex:
                    var rx = env.As<ReflexMessage>();
                    _telemetry?.OnReflex(rx);
                    _print("reflex " + rx.Id + " t=" + Fmt(rx.T) + (rx.HandledEvents != null && rx.HandledEvents.Count > 0 ? " handled " + string.Join(",", rx.HandledEvents) : ""));
                    break;

                case MessageTypes.ReflexEnd:
                    var rxe = env.As<ReflexEndMessage>();
                    _telemetry?.OnReflexEnd(rxe);
                    _print("reflex_end " + rxe.Id + " t=" + Fmt(rxe.T) + " " + rxe.Status);
                    break;

                case MessageTypes.Waiting:
                    _print("waiting");
                    break;

                case MessageTypes.State:
                    var st = env.As<StateMessage>();
                    Latest(st.State, st.T);
                    _print("state t=" + Fmt(st.T) + " " + Compact(st.State));
                    break;

                case MessageTypes.Error:
                    _print("unity error: " + env.As<ErrorMessage>().Message);
                    break;

                default:
                    _print("unknown message '" + env.Type + "'");
                    break;
            }
        }

        void Latest(JObject state, double t)
        {
            if (state != null) LatestState = state;
            if (t > LatestT) LatestT = t;
        }

        /// <summary>paused (turn-based) or attention (realtime): the game wants a decision.</summary>
        void OnDecisionMessage(string reason, double t, JObject state)
        {
            if (GameResult != null)
            {
                _print("game over: " + GameResult);
                Finish(GameResult == "won" ? ExitWon : ExitLost);
            }
            else if (_answeredPauseT == t)
            {
                // Unity announces a pause again on set_mode or reconnect; the plan already sent answers it.
                _print("(already answered this pause)");
            }
            else
            {
                _context.State = state;
                _context.PauseReason = reason;
                _context.PauseT = t;
                DecisionPending = true;
            }
        }

        void PrintHello()
        {
            _print("hello: game=" + Hello.Game + " version=" + Hello.Version + " observes=" + Hello.Observes.Count + " actions=" + Hello.Actions.Count);
            foreach (var o in Hello.Observes)
            {
                var fields = o.Fields != null && o.Fields.Count > 0
                    ? " [" + string.Join(", ", o.Fields.Select(f => f.Path + ":" + f.ValueType)) + "]"
                    : "";
                _print("  observe " + o.Path + " : " + o.ValueType + fields);
            }
            foreach (var a in Hello.Actions)
                _print("  action " + a.Name + "(" + string.Join(", ", a.Params.Select(x => x.Name + ":" + x.ValueType)) + ")" + (a.Safe ? " [safe]" : ""));
            if (Hello.Events != null)
                foreach (var ev in Hello.Events)
                    _print("  event " + ev.Name + (ev.Important ? " (important)" : ""));
            if (BrainMode && string.IsNullOrWhiteSpace(Hello.Brief))
                _print("WARNING: the game sent no brief (AgentPlay/brief.md)");
        }

        void Finish(int code)
        {
            Finished = true;
            ExitCode = code;
            DecisionPending = false;
        }

        static string Fmt(double d) => d.ToString("0.00", CultureInfo.InvariantCulture);

        static string Compact(JObject o) => o == null ? "null" : o.ToString(Formatting.None);
    }
}
