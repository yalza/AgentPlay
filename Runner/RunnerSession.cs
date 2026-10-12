using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AgentPlay.Contract;
using AgentPlay.Runner.Brains;
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
        /// <summary>Game time of the pause the last plan answered; a repeat announcement of it is ignored.</summary>
        double? _answeredPauseT;

        public bool HasHello { get; private set; }
        public HelloMessage Hello { get; private set; }
        public bool DecisionPending { get; private set; }
        /// <summary>"won" or "lost" once the game emitted game.won / game.lost.</summary>
        public string GameResult { get; private set; }
        public bool Finished { get; private set; }
        public int ExitCode { get; private set; }
        /// <summary>Exit code when the runner stops: the result if the run finished, else 6 in brain mode (Unity went away).</summary>
        public int ExitCodeOnStop => Finished ? ExitCode : BrainMode ? ExitNotFinished : 0;

        bool BrainMode => _options.Brain != null;

        public RunnerSession(RunnerOptions options, Action<string> print)
        {
            _options = options;
            _print = print;
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
            _context.LastPlan = plan;
            _answeredPauseT = _context.PauseT;
            _context.RecentEvents.Clear();
            _context.PlanStatuses.Clear();
            _print("sent plan " + plan.Id + ": " + plan.Intent);
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
                    _print("event t=" + Fmt(e.T) + " frame=" + e.Frame + " " + e.Name + (e.Important ? " (important)" : "") + " payload=" + Compact(e.Payload));
                    _context.RecentEvents.Add(e);
                    if (_context.RecentEvents.Count > MaxRecentEvents) _context.RecentEvents.RemoveAt(0);
                    if (e.Name == ReservedEvents.GameWon) GameResult = "won";
                    else if (e.Name == ReservedEvents.GameLost) GameResult = "lost";
                    break;

                case MessageTypes.Paused:
                    var p = env.As<PausedMessage>();
                    _print("paused t=" + Fmt(p.T) + " reason=" + p.Reason);
                    if (BrainMode)
                    {
                        if (GameResult != null)
                        {
                            _print("game over: " + GameResult);
                            Finish(GameResult == "won" ? ExitWon : ExitLost);
                        }
                        else if (_answeredPauseT == p.T)
                        {
                            // Unity announces a pause again on set_mode or reconnect; the plan already sent answers it.
                            _print("(already answered this pause)");
                        }
                        else
                        {
                            _context.State = p.State;
                            _context.PauseReason = p.Reason;
                            _context.PauseT = p.T;
                            DecisionPending = true;
                        }
                    }
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
                    _print("plan_status " + s.PlanId + " " + s.Status + " step=" + s.Step + " reason=" + s.Reason);
                    _context.PlanStatuses.Add(s);
                    if ((s.Status == PlanStatuses.PlanDone || s.Status == PlanStatuses.PlanFailed) && _context.LastPlan != null && s.PlanId == _context.LastPlan.Id)
                        _context.History.Add(_context.LastPlan.Id + ": " + _context.LastPlan.Intent + " -> " + s.Status
                            + (string.IsNullOrEmpty(s.Reason) ? "" : " (" + s.Reason + ")"));
                    if (s.Status == PlanStatuses.PlanDone || s.Status == PlanStatuses.PlanFailed) _answeredPauseT = null;
                    break;

                case MessageTypes.Reflex:
                    var rx = env.As<ReflexMessage>();
                    _print("reflex " + rx.Id + " t=" + Fmt(rx.T) + (rx.HandledEvents != null && rx.HandledEvents.Count > 0 ? " handled " + string.Join(",", rx.HandledEvents) : ""));
                    break;

                case MessageTypes.ReflexEnd:
                    var rxe = env.As<ReflexEndMessage>();
                    _print("reflex_end " + rxe.Id + " t=" + Fmt(rxe.T) + " " + rxe.Status);
                    break;

                case MessageTypes.Waiting:
                    _print("waiting");
                    break;

                case MessageTypes.State:
                    var st = env.As<StateMessage>();
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
