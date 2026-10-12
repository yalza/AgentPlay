using System;
using System.Globalization;
using AgentPlay.Contract;

namespace AgentPlay.Runner
{
    public sealed class RunnerOptions
    {
        public const string Usage =
            "Usage: AgentPlay.Runner [--brain script <file> | --brain opus [--backend claude-code|api] [--model M] [--effort E] [--max-calls N]] [--smoke] " +
            "[--mode turn_based|realtime] [--auto-resume] [--resume-seconds N] [--think-ms N] [--ports A-B] [--idle-exit SECONDS]";

        public string Host = "127.0.0.1";
        public int FirstPort = 47800;
        public int LastPort = 47809;
        public string Mode;
        public bool AutoResume;
        public double ResumeSeconds = 3;
        public int ThinkMs;
        public double IdleExitSeconds;
        /// <summary>null keeps the M1 behaviour (print and optionally auto-resume); "script" plays plans from a file.</summary>
        public string Brain;
        public string ScriptPath;
        /// <summary>Overrides for runner.json (null keeps the file's value).</summary>
        public string Model;
        public string Effort;
        public int? MaxCalls;
        public string Backend;
        /// <summary>Make one model call against a made-up game and exit.</summary>
        public bool Smoke;

        public static RunnerOptions Parse(string[] args)
        {
            var o = new RunnerOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--brain":
                        var brain = Value(args, ref i, a);
                        if (brain == "script")
                        {
                            o.Brain = brain;
                            o.ScriptPath = Value(args, ref i, "--brain script");
                        }
                        else if (brain == "opus")
                        {
                            o.Brain = brain;
                        }
                        else
                        {
                            throw new ArgumentException("Unknown brain: " + brain);
                        }
                        break;
                    case "--mode":
                        o.Mode = Value(args, ref i, a);
                        if (!Modes.IsKnown(o.Mode)) throw new ArgumentException("Unknown mode: " + o.Mode);
                        break;
                    case "--model":
                        o.Model = Value(args, ref i, a);
                        break;
                    case "--effort":
                        o.Effort = Value(args, ref i, a);
                        if (Array.IndexOf(RunnerConfig.Efforts, o.Effort) < 0)
                            throw new ArgumentException("--effort must be one of " + string.Join(", ", RunnerConfig.Efforts));
                        break;
                    case "--max-calls":
                        o.MaxCalls = (int)Number(Value(args, ref i, a), a);
                        if (o.MaxCalls <= 0) throw new ArgumentException("--max-calls must be positive");
                        break;
                    case "--backend":
                        o.Backend = Value(args, ref i, a);
                        if (Array.IndexOf(RunnerConfig.Backends, o.Backend) < 0)
                            throw new ArgumentException("--backend must be one of " + string.Join(", ", RunnerConfig.Backends));
                        break;
                    case "--smoke":
                        o.Smoke = true;
                        break;
                    case "--auto-resume":
                        o.AutoResume = true;
                        break;
                    case "--resume-seconds":
                        o.ResumeSeconds = Number(Value(args, ref i, a), a);
                        break;
                    case "--think-ms":
                        o.ThinkMs = (int)Number(Value(args, ref i, a), a);
                        break;
                    case "--idle-exit":
                        o.IdleExitSeconds = Number(Value(args, ref i, a), a);
                        break;
                    case "--ports":
                        var parts = Value(args, ref i, a).Split('-');
                        if (parts.Length != 2) throw new ArgumentException("--ports expects A-B");
                        o.FirstPort = (int)Number(parts[0], a);
                        o.LastPort = (int)Number(parts[1], a);
                        if (o.FirstPort > o.LastPort) throw new ArgumentException("--ports: first port is greater than last port");
                        break;
                    default:
                        throw new ArgumentException("Unknown argument: " + a);
                }
            }
            if (o.Brain != null)
            {
                if (o.Mode == Modes.Realtime) throw new ArgumentException("--brain plays in turn_based mode only");
                if (o.AutoResume) throw new ArgumentException("--brain decides when to resume; drop --auto-resume");
                o.Mode = Modes.TurnBased;
            }
            return o;
        }

        static string Value(string[] args, ref int i, string name)
        {
            if (i + 1 >= args.Length) throw new ArgumentException(name + " needs a value");
            return args[++i];
        }

        static double Number(string text, string name)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || d < 0)
                throw new ArgumentException(name + " expects a non-negative number, got '" + text + "'");
            return d;
        }
    }
}
