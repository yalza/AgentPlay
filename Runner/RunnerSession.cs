using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AgentPlay.Contract;
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

    /// <summary>Reacts to Unity messages. Pure: printing and sending are done by the caller.</summary>
    public sealed class RunnerSession
    {
        readonly RunnerOptions _options;
        readonly Action<string> _print;

        public bool HasHello { get; private set; }
        public HelloMessage Hello { get; private set; }

        public RunnerSession(RunnerOptions options, Action<string> print)
        {
            _options = options;
            _print = print;
        }

        public void Reset()
        {
            HasHello = false;
            Hello = null;
        }

        public IReadOnlyList<Outgoing> Handle(Envelope env)
        {
            var outgoing = new List<Outgoing>();
            switch (env.Type)
            {
                case MessageTypes.Hello:
                    Hello = env.As<HelloMessage>();
                    HasHello = true;
                    if (Hello.Protocol != ProtocolVersion.Current)
                        _print("WARNING: Unity speaks protocol " + Hello.Protocol + ", runner speaks " + ProtocolVersion.Current);
                    _print("hello: game=" + Hello.Game + " version=" + Hello.Version + " observes=" + Hello.Observes.Count + " actions=" + Hello.Actions.Count);
                    foreach (var o in Hello.Observes)
                        _print("  observe " + o.Path + " : " + o.ValueType);
                    foreach (var a in Hello.Actions)
                        _print("  action " + a.Name + "(" + string.Join(", ", a.Params.Select(p => p.Name + ":" + p.ValueType)) + ")" + (a.Safe ? " [safe]" : ""));
                    if (_options.Mode != null)
                        outgoing.Add(new Outgoing { Type = MessageTypes.SetMode, Body = new SetModeMessage { Mode = _options.Mode } });
                    break;

                case MessageTypes.Event:
                    var e = env.As<EventMessage>();
                    _print("event t=" + Fmt(e.T) + " frame=" + e.Frame + " " + e.Name + (e.Important ? " (important)" : "") + " payload=" + Compact(e.Payload));
                    break;

                case MessageTypes.Paused:
                    var p = env.As<PausedMessage>();
                    _print("paused t=" + Fmt(p.T) + " reason=" + p.Reason);
                    if (_options.AutoResume)
                        outgoing.Add(new Outgoing
                        {
                            Type = MessageTypes.Resume,
                            Body = new ResumeMessage { MaxGameSeconds = _options.ResumeSeconds },
                            DelayMs = _options.ThinkMs,
                        });
                    break;

                case MessageTypes.State:
                    var s = env.As<StateMessage>();
                    _print("state t=" + Fmt(s.T) + " " + Compact(s.State));
                    break;

                case MessageTypes.Error:
                    _print("unity error: " + env.As<ErrorMessage>().Message);
                    break;

                default:
                    _print("unknown message '" + env.Type + "'");
                    break;
            }
            return outgoing;
        }

        static string Fmt(double d) => d.ToString("0.00", CultureInfo.InvariantCulture);

        static string Compact(JObject o) => o == null ? "null" : o.ToString(Formatting.None);
    }
}
