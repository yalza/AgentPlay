using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgentPlay.Contract;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json;

namespace AgentPlay.Runner.Learning
{
    /// <summary>The reflect command (spec M3 section 6): learns from play-tagged traces into the playbook and proposes reflexes.</summary>
    public sealed class Reflector
    {
        public const int ExitOk = 0;
        public const int ExitBadInput = 2;
        public const int ExitModelFailed = 4;
        const int MaxAttempts = 3;

        public const string Instructions =
@"You review one recorded session of a video game and keep the game's playbook of lessons up to date. The playbook helps an AI agent play this game better next time.

You get the game description, the current lessons, and the session as a timeline: state changes, events, and (when the agent played) its plans and reflexes. When the player is a human, infer what they did from how the state changed.

For each lesson the session gives real evidence for, answer one entry:
- confirmed: the session shows an existing lesson working again. Use its id.
- contradicted: the session shows an existing lesson failing or being wrong. Use its id.
- new: a useful lesson not in the playbook. Leave id empty. Do not create a new lesson that says the same as an existing one; confirm the existing one instead.
A lesson says what to do in which situation, in one or two sentences, at most 300 characters. Only write lessons that would change how a player acts. Do not write lessons about the timeline format.

reflex: when a lesson is a simple, immediate reaction that is safe every time its condition is true, also write it as a reflex: a condition in the game's condition language and one action with its arguments. Prefer conditions on events (event:name) or simple observe comparisons. Otherwise set reflex to null.

Return an empty lessons list if the session teaches nothing new.";

        readonly IModelClient _client;
        readonly RunnerConfig _config;
        readonly Action<string> _log;

        /// <summary>Wait before retrying a transient model failure; tests set 0.</summary>
        public int RetryDelayMs { get; set; } = 5000;

        public Reflector(IModelClient client, RunnerConfig config, Action<string> log)
        {
            _client = client;
            _config = config;
            _log = log;
        }

        public async Task<int> RunAsync(string projectRoot, IReadOnlyList<string> traces, bool force, CancellationToken ct)
        {
            string agentPlay = Path.Combine(projectRoot ?? "", "AgentPlay");
            if (string.IsNullOrEmpty(projectRoot) || !Directory.Exists(agentPlay))
            {
                _log("reflect: no AgentPlay folder in " + projectRoot);
                return ExitBadInput;
            }
            Playbook playbook;
            try
            {
                playbook = PlaybookStore.Load(projectRoot);
            }
            catch (InvalidDataException e)
            {
                _log("reflect: " + e.Message);
                return ExitBadInput;
            }
            var ledger = ReflectedLedger.Load(projectRoot);

            int done = 0, proposals = 0;
            foreach (var path in Candidates(projectRoot, traces, force, ledger))
            {
                string name = Path.GetFileName(path);
                var digest = TraceDigest.Build(File.ReadLines(path), _config.Reflector.MaxDigestChars);
                if (digest.Error != null)
                {
                    _log(name + ": skipped (" + digest.Error + ")");
                    ledger.Add(name);
                    ReflectedLedger.Save(projectRoot, ledger);
                    continue;
                }

                var request = BuildRequest(playbook, digest);
                var (output, error) = await AskAsync(request, ct);
                if (output == null)
                {
                    _log(name + ": " + error);
                    return ExitModelFailed;
                }

                var merge = PlaybookMerger.Merge(playbook, output, name, digest.Hello, _config.Reflector.MinConfirmations, DateTime.UtcNow);
                foreach (var reflex in merge.Proposals)
                {
                    string file = Path.Combine(agentPlay, "Inbox", "reflex-" + reflex.LessonId + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json");
                    AtomicFile.WriteAllText(file, JsonConvert.SerializeObject(reflex, Formatting.Indented, ContractJson.Settings));
                }
                PlaybookStore.Save(projectRoot, playbook);
                ledger.Add(name);
                ReflectedLedger.Save(projectRoot, ledger);

                done++;
                proposals += merge.Proposals.Count;
                string summary = output.Summary ?? "";
                _log(name + ": " + (summary.Length > 200 ? summary.Substring(0, 200) + "..." : summary));
                _log("lessons: " + merge.New + " new, " + merge.Confirmed + " confirmed, " + merge.Contradicted + " contradicted; proposals: "
                    + (merge.Proposals.Count == 0 ? "none" : string.Join(", ", merge.Proposals.Select(p => p.Id))));
                foreach (var note in merge.Notes) _log("note: " + note);
            }
            _log("reflect done: " + done + " trace(s), " + proposals + " proposal(s)");
            return ExitOk;
        }

        IEnumerable<string> Candidates(string projectRoot, IReadOnlyList<string> traces, bool force, HashSet<string> ledger)
        {
            IEnumerable<string> paths = traces != null && traces.Count > 0
                ? traces
                : Directory.Exists(Path.Combine(projectRoot, "AgentPlay", "Traces"))
                    ? Directory.GetFiles(Path.Combine(projectRoot, "AgentPlay", "Traces"), "*.ndjson").OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
                    : Enumerable.Empty<string>();
            int taken = 0;
            foreach (var path in paths)
            {
                if (taken == _config.Reflector.MaxTracesPerRun) yield break;
                string name = Path.GetFileName(path);
                if (!File.Exists(path))
                {
                    _log(name + ": skipped (file not found)");
                    continue;
                }
                var header = TraceDigest.ReadHeader(path);
                string tag = (string)header?["tag"];
                if (tag != "play")
                {
                    _log(name + ": skipped (tag " + (tag ?? "unknown") + ")");
                    continue;
                }
                if (!force && ledger.Contains(name)) continue;
                taken++;
                yield return path;
            }
        }

        ModelRequest BuildRequest(Playbook playbook, TraceDigestResult digest)
        {
            var user = new StringBuilder("# Current lessons\n");
            if (playbook.Lessons.Count == 0) user.Append("(no lessons yet)\n");
            foreach (var l in PlaybookStore.ForPrompt(playbook, Playbook.MaxLessons))
            {
                user.Append("- ").Append(l.Id).Append(" [confirmed ").Append(l.Confirmations).Append("x, contradicted ").Append(l.Contradictions).Append("x] ").Append(l.Text);
                if (l.Reflex != null)
                    user.Append(" | reflex: when ").Append(l.Reflex.When).Append(" do ").Append(l.Reflex.Do).Append('(')
                        .Append(l.Reflex.Args == null ? "" : string.Join(", ", l.Reflex.Args.Properties().Select(p => p.Name + ": " + p.Value.ToString(Formatting.None))))
                        .Append(')');
                user.Append('\n');
            }
            user.Append("\n# Session\n").Append(digest.Text).Append("\n\nAnswer with the lessons this session supports or contradicts.");

            return new ModelRequest
            {
                System = Instructions + "\n\n" + PromptBuilder.BuildGameSection(digest.Hello),
                User = user.ToString(),
                Schema = ReflectorSchema.Build(digest.Hello),
                Effort = _config.Reflector.Effort,
            };
        }

        /// <summary>Returns the parsed answer, or null and the reason. Retries transient failures and one invalid answer.</summary>
        async Task<(ReflectorOutput, string)> AskAsync(ModelRequest request, CancellationToken ct)
        {
            string feedback = null;
            for (int answer = 0; answer < 2; answer++)
            {
                var current = feedback == null ? request : new ModelRequest
                {
                    System = request.System,
                    User = request.User + "\n\nYour previous answer was rejected: " + feedback + "\nAnswer again.",
                    Schema = request.Schema,
                    Effort = request.Effort,
                };
                ModelReply reply = null;
                for (int attempt = 1; reply == null; attempt++)
                {
                    try
                    {
                        reply = await _client.CompleteAsync(current, ct);
                    }
                    catch (ModelCallException e) when (e.Retryable && attempt < MaxAttempts)
                    {
                        _log("model call failed (" + e.Message + "); retrying in " + RetryDelayMs + " ms");
                        if (RetryDelayMs > 0) await Task.Delay(RetryDelayMs, ct);
                    }
                    catch (ModelCallException e)
                    {
                        return (null, "model call failed: " + e.Message);
                    }
                }
                _log("reflector model " + reply.Model + " " + reply.LatencyMs + " ms, out " + reply.OutputTokens);
                try
                {
                    var output = ReflectorOutput.Parse(reply.Text);
                    var errors = output.Validate();
                    if (errors.Count == 0) return (output, null);
                    feedback = string.Join("; ", errors);
                }
                catch (JsonException e)
                {
                    feedback = "the answer is not valid JSON: " + e.Message;
                }
            }
            return (null, "the model gave two invalid answers: " + feedback);
        }
    }
}
