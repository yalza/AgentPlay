using System;
using System.Globalization;
using AgentPlay.Contract;

namespace AgentPlay.Runner
{
    public sealed class RunnerOptions
    {
        public const string Usage =
            "Usage: AgentPlay.Runner [--mode turn_based|realtime] [--auto-resume] [--resume-seconds N] " +
            "[--think-ms N] [--ports A-B] [--idle-exit SECONDS]";

        public string Host = "127.0.0.1";
        public int FirstPort = 47800;
        public int LastPort = 47809;
        public string Mode;
        public bool AutoResume;
        public double ResumeSeconds = 3;
        public int ThinkMs;
        public double IdleExitSeconds;

        public static RunnerOptions Parse(string[] args)
        {
            var o = new RunnerOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--mode":
                        o.Mode = Value(args, ref i, a);
                        if (!Modes.IsKnown(o.Mode)) throw new ArgumentException("Unknown mode: " + o.Mode);
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
