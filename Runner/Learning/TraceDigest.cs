using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AgentPlay.Contract;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner.Learning
{
    public sealed class TraceDigestResult
    {
        public HelloMessage Hello;
        public string Who;
        public string Tag;
        public bool AgentControlled;
        /// <summary>The digest; null when Error is set.</summary>
        public string Text;
        public string Error;
    }

    /// <summary>
    /// Turns a trace into a compact timeline for the reflector (spec M3 section 6.2): state changes between
    /// snapshots, events, plans and reflexes. Numbers are rounded to one decimal so jitter does not show as change.
    /// </summary>
    public static class TraceDigest
    {
        sealed class Item
        {
            public string Text;
            public bool IsState;
        }

        /// <summary>The header line of a trace file, or null when the file cannot be read as a trace.</summary>
        public static JObject ReadHeader(string path)
        {
            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var o = JObject.Parse(line);
                    return (string)o["type"] == "header" ? o : null;
                }
            }
            catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException)
            {
            }
            return null;
        }

        /// <summary>
        /// Waits until no trace in the folder is still open for writing. Unity closes the trace when Play mode ends,
        /// a moment after game over, and until then the file cannot be read. False when one is still open at the timeout.
        /// </summary>
        public static async Task<bool> WaitForTracesAsync(string tracesDir, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                if (!Directory.Exists(tracesDir) || Directory.GetFiles(tracesDir, "*.ndjson").All(CanRead)) return true;
                if (DateTime.UtcNow >= deadline) return false;
                await Task.Delay(100);
            }
        }

        static bool CanRead(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) return true;
            }
            catch (FileNotFoundException)
            {
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        public static TraceDigestResult Build(IEnumerable<string> lines, int maxChars)
        {
            var result = new TraceDigestResult();
            var items = new List<Item>();
            var previous = new Dictionary<string, string>();
            JObject header = null;
            double length = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                JObject o;
                try
                {
                    o = JObject.Parse(line);
                }
                catch (JsonException)
                {
                    continue;
                }
                string type = o["type"]?.Type == JTokenType.String ? (string)o["type"] : null;
                if (header == null)
                {
                    if (type != "header")
                    {
                        result.Error = "the trace does not start with a header";
                        return result;
                    }
                    header = o;
                    continue;
                }

                try
                {
                    AddLine(o, type, items, previous, result, ref length);
                }
                catch (Exception e) when (e is FormatException || e is InvalidCastException || e is ArgumentException || e is OverflowException)
                {
                    // A line with odd field types is skipped; the rest of the trace still teaches something.
                }
            }

            if (header == null)
            {
                result.Error = "the trace is empty";
                return result;
            }
            if (!(header["hello"] is JObject hello))
            {
                result.Error = "the trace has no hello in its header (recorded before AgentPlay M3)";
                return result;
            }
            result.Hello = hello.ToObject<HelloMessage>(ContractJson.Serializer);
            result.Who = (string)header["who"];
            result.Tag = (string)header["tag"];

            string top = "Game: " + (string)header["game"] + " (version " + (string)header["version"] + "). Player: " + result.Who
                + ". Session tag: " + result.Tag + ". Length: " + length.ToString("0.0", CultureInfo.InvariantCulture) + " game seconds.";
            result.Text = Fit(top, items, maxChars);
            return result;
        }

        static void AddLine(JObject o, string type, List<Item> items, Dictionary<string, string> previous, TraceDigestResult result, ref double length)
        {
            double t = o["t"] != null && o["t"].Type != JTokenType.Null ? (double)o["t"] : 0;
            if (t > length) length = t;
            string at = "t=" + t.ToString("0.00", CultureInfo.InvariantCulture) + " ";
            switch (type)
            {
                case "snapshot":
                    AddState(items, at, o["state"] as JObject, previous);
                    break;
                case "event":
                    items.Add(new Item { Text = at + "event " + (string)o["name"] + " " + Rounded(o["payload"]).ToString(Formatting.None) });
                    AddState(items, at, o["state"] as JObject, previous);
                    break;
                case "control":
                    if ((string)o["who"] == "agent")
                    {
                        result.AgentControlled = true;
                        items.Add(new Item { Text = at + "agent takes control" });
                    }
                    break;
                case "plan":
                    items.Add(new Item { Text = at + DescribePlan(o["plan"] as JObject) });
                    break;
                case "plan_status":
                    string reason = (string)o["reason"];
                    items.Add(new Item { Text = at + "plan " + (string)o["plan_id"] + " " + (string)o["status"] + " step=" + (o["step"]?.Type == JTokenType.Integer ? ((int)o["step"]).ToString(CultureInfo.InvariantCulture) : "?")
                        + (string.IsNullOrEmpty(reason) ? "" : " " + reason) });
                    break;
                case "reflex":
                    var handled = (o["handled_events"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
                    items.Add(new Item { Text = at + "reflex " + (string)o["id"] + " fired" + (handled.Count > 0 ? " (handled " + string.Join(",", handled) + ")" : "") });
                    break;
                case "reflex_end":
                    items.Add(new Item { Text = at + "reflex " + (string)o["id"] + " " + (string)o["status"] });
                    break;
            }
        }

        static void AddState(List<Item> items, string at, JObject state, Dictionary<string, string> previous)
        {
            if (state == null) return;
            var sb = new StringBuilder();
            foreach (var p in state.Properties())
            {
                string value = Render(p.Value);
                if (previous.TryGetValue(p.Name, out var old) && old == value) continue;
                previous[p.Name] = value;
                sb.Append(' ').Append(p.Name).Append('=').Append(value);
            }
            if (sb.Length > 0) items.Add(new Item { Text = at + "state" + sb, IsState = true });
        }

        /// <summary>Drops every second state line (keeping the first) until the text fits, then cuts if it still does not.</summary>
        static string Fit(string top, List<Item> items, int maxChars)
        {
            string text = Join(top, items);
            while (text.Length > maxChars)
            {
                var states = items.Where(i => i.IsState).ToList();
                if (states.Count <= 1) break;
                for (int k = 1; k < states.Count; k += 2) items.Remove(states[k]);
                text = Join(top, items);
            }
            if (text.Length <= maxChars) return text;
            const string cut = "\n(truncated)";
            return text.Substring(0, Math.Max(0, maxChars - cut.Length)) + cut;
        }

        static string Join(string top, List<Item> items)
        {
            var sb = new StringBuilder(top);
            foreach (var i in items) sb.Append('\n').Append(i.Text);
            return sb.ToString();
        }

        static string DescribePlan(JObject plan)
        {
            if (plan == null) return "plan (unreadable)";
            var sb = new StringBuilder("plan ").Append((string)plan["id"]).Append(" \"").Append((string)plan["intent"]).Append("\": ");
            var steps = (plan["steps"] as JArray ?? new JArray()).OfType<JObject>().Select(s =>
            {
                string until = (string)s["until"];
                return Call(s) + (string.IsNullOrWhiteSpace(until) ? "" : " until " + until);
            });
            sb.Append(string.Join("; ", steps));
            var rules = (plan["rules"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            if (rules.Count > 0)
                sb.Append(" | rules: ").Append(string.Join("; ", rules.Select(r => (string)r["when"] + " -> " + Call(r) + " then " + (string)r["then"])));
            return sb.ToString();
        }

        static string Call(JObject o)
        {
            var args = o["args"] as JObject;
            string list = args == null ? "" : string.Join(", ", args.Properties().Select(p => p.Name + ":" + Render(p.Value)));
            return (string)o["do"] + "(" + list + ")";
        }

        static string Render(JToken v)
        {
            if (v == null) return "null";
            switch (v.Type)
            {
                case JTokenType.Null: return "null";
                case JTokenType.Float: return Number((double)v);
                case JTokenType.Integer: return ((long)v).ToString(CultureInfo.InvariantCulture);
                case JTokenType.Boolean: return (bool)v ? "true" : "false";
                case JTokenType.String: return "'" + (string)v + "'";
                case JTokenType.Object:
                    var o = (JObject)v;
                    if (o.Count == 3 && o["x"] != null && o["y"] != null && o["z"] != null)
                        return "(" + Render(o["x"]) + "," + Render(o["y"]) + "," + Render(o["z"]) + ")";
                    return "{" + string.Join(",", o.Properties().Select(p => p.Name + ":" + Render(p.Value))) + "}";
                case JTokenType.Array:
                    return "[" + string.Join(", ", v.Select(Render)) + "]";
                default:
                    return v.ToString(Formatting.None);
            }
        }

        static string Number(double d)
        {
            double r = Math.Round(d, 1);
            if (r == 0) r = 0;   // no "-0.0"
            return r.ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>A copy with every float rounded to one decimal, for payloads printed as JSON.</summary>
        static JToken Rounded(JToken v)
        {
            if (v == null || v.Type == JTokenType.Null) return new JObject();
            var copy = v.DeepClone();
            if (copy is JValue single)
            {
                if (single.Type == JTokenType.Float) single.Value = Math.Round((double)single, 1);
                return copy;
            }
            foreach (var value in ((JContainer)copy).Descendants().OfType<JValue>().Where(x => x.Type == JTokenType.Float).ToList())
                value.Value = Math.Round((double)value, 1);
            return copy;
        }
    }
}
