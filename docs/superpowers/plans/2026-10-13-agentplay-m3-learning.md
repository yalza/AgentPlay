# AgentPlay M3: Learning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Learn from `play` sessions (human or agent) into a playbook, propose reflexes from well-confirmed lessons, and run approved reflexes inside the Unity Executor so they replace model calls.

**Architecture:** Reflexes are a layer of `ExecutorCore` (priority reflex > plan rule > step), loaded by the Editor from `AgentPlay/reflexes.json`, scored by a pure `ReflexMonitor`. The runner gains a `reflect` command: trace → `TraceDigest` → Opus (structured output) → `PlaybookMerger` → `playbook.json`/`.md` + Inbox proposals. Telemetry is written by the runner after every brain run. One writer per file (spec section 2).

**Tech Stack:** Unity 6000.0.77f1 (C# 9, NUnit EditMode), .NET 9 runner (xUnit), Newtonsoft.Json, Claude Code CLI (`claude -p`) as model backend.

**Spec:** `docs/superpowers/specs/2026-10-13-agentplay-m3-design.md`

## Global Constraints

- Protocol version becomes 3 (`ProtocolVersion.Current = 3`); Unity and runner change together.
- `Packages/com.agentplay.core` and `Runner/` must not mention Testbed, Heist or TileThieves (`Tools/check-generic.sh`).
- Contract code is shared by Unity (C# 9, `noEngineReferences`) and .NET 9: no newer C# syntax than C# 9 in `Packages/`, no `System.Linq` allocations in per-frame Unity code paths.
- One writer per file: Unity writes `reflexes.json` and moves Inbox files; the runner writes `playbook.json`, `playbook.md`, `reflected.json`, `Telemetry/*.json`, `Inbox/*.json`.
- All JSON files under `AgentPlay/` are written atomically (temp file in the same folder, then replace).
- Reflex limits: at most 16 active reflexes loaded; `cooldown_s` in [0, 60], default 1; a reflex action running longer than 15 game seconds fails.
- Policy defaults: `eval_window_s` 10, `min_fires` 5, `max_fail_rate` 0.3. Reflector defaults: effort `high`, `min_confirmations` 3, `max_digest_chars` 60000, `max_traces_per_run` 5. Playbook keeps at most 50 lessons; the brain prompt shows at most 20.
- Snapshot interval: 0.5 s for tag `play`, 2 s for `debug`.
- Window copy is English. Comments and identifiers are English. Docs/notes are Vietnamese.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; use `-m subject -m trailer`.
- New Unity package files need `.meta` files: they are generated when Testbed or Heist opens the package (run `Tools/unity-tests.sh`), then committed. Never hand-edit a `.meta`.
- Tests: `dotnet test AgentPlay.sln` (from repo root), `bash Tools/unity-tests.sh` (Testbed closed), E2E scripts with Unity closed.

## Review Focus

1. A reflex whose action is removed or renamed in a later game build: it must be skipped with a Console warning, not crash the session or wipe `reflexes.json` (Task 5 test `PrepareSessionSkipsInvalidReflex`).
2. `reflexes.json` edited by hand into invalid JSON: Unity must not overwrite it at the end of Play mode (Task 5 test `CorruptFileIsNotOverwritten`).
3. The Reflector answering `confirmed` with a lesson id that does not exist: it becomes a new lesson, never an exception (Task 9 test `UnknownConfirmedIdBecomesNewLesson`).
4. A reflex firing while no plan is loaded but control was taken (agent waiting between plans in realtime mode): it must run and then return to the safe action (Task 3 test `ReflexRunsBetweenPlansThenSafeResumes`).
5. Inbox file with a reflex id that already exists in `reflexes.json`: must not duplicate or reset its stats (Task 5 test `InboxDoesNotDuplicateExistingId`).

---

## File map

| File | Responsibility |
| --- | --- |
| `Packages/com.agentplay.core/Contract/Reflexes/Reflex.cs` | DTOs: `Reflex`, `ReflexStats`, `ReflexPolicy`, `ReflexFile`, `ReflexInfo`, `ReflexStatuses`, `ReflexLimits` |
| `Packages/com.agentplay.core/Contract/Reflexes/ReflexValidator.cs` | Validate a reflex against a hello |
| `Packages/com.agentplay.core/Contract/Reflexes/ReflexLifecycle.cs` | Status transitions and outcome bookkeeping |
| `Packages/com.agentplay.core/Contract/Reflexes/ReflexMonitor.cs` | Evaluation windows → good/bad outcomes |
| `Packages/com.agentplay.core/Contract/Files/AtomicFile.cs` | Atomic text write |
| `Packages/com.agentplay.core/Contract/Plans/PlanValidator.cs` | Expose `CheckCondition`/`CheckAction` as `internal` for reuse |
| `Packages/com.agentplay.core/Contract/Protocol/*` | Protocol 3: hello fields, `reflex`/`reflex_end` messages |
| `Packages/com.agentplay.core/Runtime/Execution/ExecutorCore.cs` | Reflex layer |
| `Packages/com.agentplay.core/Runtime/Execution/AgentExecutorHost.cs` | Pass important event names; `SetReflexes`/`RemoveReflex` |
| `Packages/com.agentplay.core/Runtime/Session/SessionCore.cs` | Send `reflex`/`reflex_end`, no pause when a reflex handled the frame |
| `Packages/com.agentplay.core/Runtime/Trace/TraceWriter.cs` | Header with hello, `reflex`/`reflex_end` lines |
| `Packages/com.agentplay.core/Editor/ReflexBook.cs` | Load/save `reflexes.json`, Inbox import, transitions, session preparation |
| `Packages/com.agentplay.core/Editor/ReflectLauncher.cs` | Run `dotnet <runner> reflect` in the background |
| `Packages/com.agentplay.core/Editor/AgentPlaySessionController.cs` | Wire reflexes, monitor, snapshot interval, session tag |
| `Packages/com.agentplay.core/Editor/AgentPlayWindow.cs`, `AgentPlaySettings.cs` | Learning + Reflexes sections, runner path |
| `Runner/Telemetry.cs` | `TelemetryRecorder` and report writing |
| `Runner/Learning/Playbook.cs` | `Playbook`, `Lesson`, `ReflexCandidate`, `PlaybookStore`, `ReflectedLedger` |
| `Runner/Learning/TraceDigest.cs` | Trace → compact text |
| `Runner/Learning/ReflectorSchema.cs` | Structured-output schema for the Reflector |
| `Runner/Learning/PlaybookMerger.cs` | Merge model output into the playbook, produce proposals |
| `Runner/Learning/Reflector.cs` | The `reflect` command logic |
| `Runner/Brains/PromptBuilder.cs` | Game section reusable; playbook + reflex sections |
| `Runner/RunnerOptions.cs`, `Runner/Program.cs`, `Runner/RunnerSession.cs`, `Runner/RunnerConfig.cs`, `Runner/runner.json` | Command, auto-reflect, telemetry, config |
| `Demos/Heist/Assets/Heist/*` | Version m3, `hide_nearest`, keyboard, `HeistPilot`, AutoPlay hooks |
| `Tools/e2e-m3-reflex.sh`, `Tools/verify_m3_reflex.py`, `Tools/e2e-m3-learn.sh`, `Tools/verify_m3_learn.py`, `Demos/Heist/AgentPlay/scripts/reflex.json`, `Demos/Heist/AgentPlay/fixtures/reflexes-active.json` | E2E |

---

### Task 1: Contract reflex model, validator, lifecycle, monitor

**Files:**
- Create: `Packages/com.agentplay.core/Contract/Reflexes/Reflex.cs`, `ReflexValidator.cs`, `ReflexLifecycle.cs`, `ReflexMonitor.cs`, `Packages/com.agentplay.core/Contract/Files/AtomicFile.cs`
- Modify: `Packages/com.agentplay.core/Contract/Plans/PlanValidator.cs` (make `CheckCondition`, `CheckAction` `internal static`)
- Test: `Runner.Tests/Contract/ReflexTests.cs`

**Interfaces:**
- Produces:
  - `Reflex { string Id, LessonId, Text, When, Do; JObject Args; double CooldownSeconds = 1; string Status, GameVersion, Note; ReflexStats Stats }` (JSON names `id, lesson_id, text, when, do, args, cooldown_s, status, game_version, note, stats`)
  - `ReflexStats { int Fired, Good, Bad }`, `ReflexPolicy { double EvalWindowSeconds = 10; int MinFires = 5; double MaxFailRate = 0.3 }`, `ReflexFile { int Version = 1; ReflexPolicy Policy; List<Reflex> Reflexes }`
  - `ReflexInfo { string Id, Text, When, Do; JObject Args }` and `static ReflexInfo From(Reflex r)`
  - `ReflexStatuses.Proposed/Active/Rejected/Suspended/NeedsReview`, `ReflexLimits.MaxActive = 16, MaxCooldownSeconds = 60, MaxRunSeconds = 15`
  - `ReflexValidator.Validate(Reflex, HelloMessage) : List<string>`
  - `ReflexLifecycle.Approve(Reflex, string version) : bool`, `Reject(Reflex) : bool`, `Disable(Reflex) : bool`, `Reenable(Reflex, string version) : bool`, `MarkVersionChanges(ReflexFile, string version) : List<string>` (ids moved to needs_review), `RecordFired(Reflex)`, `RecordOutcome(Reflex, bool bad, ReflexPolicy) : bool` (true when it suspended the reflex)
  - `ReflexMonitor(ReflexPolicy)`, `OnFired(string id, double t)`, `OnFailed(string id)`, `OnBadEvent()`, `Tick(double t) : List<ReflexOutcome>`; `struct ReflexOutcome { string Id; bool Bad; }`
  - `AtomicFile.WriteAllText(string path, string text)`

- [ ] **Step 1: Write the failing tests**

```csharp
// Runner.Tests/Contract/ReflexTests.cs
using System.Collections.Generic;
using AgentPlay.Contract;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class ReflexTests
    {
        static HelloMessage Hello()
        {
            var h = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G", Version = "v1" };
            h.Observes.Add(new ObserveInfo { Path = "player.hidden", ValueType = ValueKinds.Bool });
            h.Events.Add(new EventInfo { Name = "guard.spotted", Important = true, Outcome = "bad" });
            h.Actions.Add(new ActionInfo { Name = "hide_nearest" });
            h.Actions.Add(new ActionInfo { Name = "goto", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } } });
            h.Actions.Add(new ActionInfo { Name = "stop", Safe = true });
            return h;
        }

        static Reflex Good() => new Reflex { Id = "R-L1", When = "event:guard.spotted", Do = "hide_nearest", Args = new JObject(), Status = ReflexStatuses.Proposed };

        [Fact]
        public void ValidReflexHasNoErrors() => Assert.Empty(ReflexValidator.Validate(Good(), Hello()));

        [Theory]
        [InlineData("", "event:guard.spotted", "hide_nearest", 1, "id: required")]
        [InlineData("R", "", "hide_nearest", 1, "when: required")]
        [InlineData("R", "nope > 1", "hide_nearest", 1, "when: Unknown observe 'nope'")]
        [InlineData("R", "event:guard.spotted", "fly", 1, "do: unknown action 'fly'")]
        [InlineData("R", "event:guard.spotted", "stop", 1, "do: the safe action cannot be a reflex")]
        [InlineData("R", "event:guard.spotted", "hide_nearest", 61, "cooldown_s: must be between 0 and 60")]
        [InlineData("R", "event:guard.spotted", "goto", 1, "args.zone: required")]
        public void InvalidReflexesAreReported(string id, string when, string action, double cooldown, string expected)
        {
            var r = new Reflex { Id = id, When = when, Do = action, Args = new JObject(), CooldownSeconds = cooldown };
            Assert.Contains(ReflexValidator.Validate(r, Hello()), e => e.Contains(expected));
        }

        [Fact]
        public void FileRoundTripsWithSnakeCaseNames()
        {
            var file = new ReflexFile();
            file.Reflexes.Add(Good());
            string json = JsonConvert.SerializeObject(file, ContractJson.Settings);
            Assert.Contains("\"cooldown_s\":1", json);
            Assert.Contains("\"eval_window_s\":10", json);
            var back = JsonConvert.DeserializeObject<ReflexFile>(json, ContractJson.Settings);
            Assert.Equal("R-L1", back.Reflexes[0].Id);
            Assert.Equal(0.3, back.Policy.MaxFailRate);
        }

        [Fact]
        public void ApproveRejectDisableReenable()
        {
            var r = Good();
            Assert.True(ReflexLifecycle.Approve(r, "v1"));
            Assert.Equal(ReflexStatuses.Active, r.Status);
            Assert.Equal("v1", r.GameVersion);
            Assert.False(ReflexLifecycle.Approve(r, "v1"));          // already active
            Assert.True(ReflexLifecycle.Disable(r));
            Assert.Equal(ReflexStatuses.Suspended, r.Status);
            Assert.Equal("disabled by user", r.Note);
            r.Stats.Fired = 7; r.Stats.Bad = 4;
            Assert.True(ReflexLifecycle.Reenable(r, "v2"));
            Assert.Equal(ReflexStatuses.Active, r.Status);
            Assert.Equal(0, r.Stats.Fired);
            Assert.Equal("v2", r.GameVersion);
            Assert.True(ReflexLifecycle.Reject(r));
            Assert.Equal(ReflexStatuses.Rejected, r.Status);
            Assert.False(ReflexLifecycle.Reject(r));
        }

        [Fact]
        public void VersionChangeMovesActiveToNeedsReview()
        {
            var file = new ReflexFile();
            var a = Good(); ReflexLifecycle.Approve(a, "v1");
            var p = Good(); p.Id = "R-L2";
            file.Reflexes.Add(a); file.Reflexes.Add(p);
            Assert.Empty(ReflexLifecycle.MarkVersionChanges(file, "v1"));
            Assert.Equal(new[] { "R-L1" }, ReflexLifecycle.MarkVersionChanges(file, "v2"));
            Assert.Equal(ReflexStatuses.NeedsReview, a.Status);
            Assert.Equal("game version changed from v1 to v2", a.Note);
            Assert.Equal(ReflexStatuses.Proposed, p.Status);
            Assert.True(ReflexLifecycle.Approve(a, "v2"));
        }

        [Fact]
        public void SuspendsWhenFailRateExceedsPolicyAfterMinFires()
        {
            var r = Good(); ReflexLifecycle.Approve(r, "v1");
            var policy = new ReflexPolicy();
            bool[] outcomes = { false, true, false, true };   // 2 bad of 4: not enough fires yet
            foreach (var bad in outcomes) { ReflexLifecycle.RecordFired(r); Assert.False(ReflexLifecycle.RecordOutcome(r, bad, policy)); }
            ReflexLifecycle.RecordFired(r);
            Assert.True(ReflexLifecycle.RecordOutcome(r, false, policy));   // 2/5 = 0.4 > 0.3
            Assert.Equal(ReflexStatuses.Suspended, r.Status);
            Assert.Equal("fail rate 0.40 after 5 outcomes", r.Note);
            Assert.Equal(5, r.Stats.Fired);
            Assert.Equal(3, r.Stats.Good);
        }

        [Fact]
        public void MonitorResolvesWindows()
        {
            var m = new ReflexMonitor(new ReflexPolicy { EvalWindowSeconds = 10 });
            m.OnFired("A", 1);
            m.OnFired("B", 5);
            Assert.Empty(m.Tick(8));
            m.OnBadEvent();                       // both windows are open: both bad
            var bad = m.Tick(8.1);
            Assert.Equal(2, bad.Count);
            Assert.All(bad, o => Assert.True(o.Bad));
            m.OnFired("A", 20);
            Assert.Empty(m.Tick(29.9));
            var good = m.Tick(30);
            Assert.Single(good);
            Assert.False(good[0].Bad);
            m.OnFired("C", 40);
            m.OnFailed("C");
            var failed = m.Tick(40.1);
            Assert.Single(failed);
            Assert.True(failed[0].Bad);
            Assert.Empty(m.Tick(60));            // C was already resolved
        }

        [Fact]
        public void AtomicWriteReplacesExistingFile()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agentplay-atomic-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "a.json");
            AtomicFile.WriteAllText(path, "one");
            AtomicFile.WriteAllText(path, "two");
            Assert.Equal("two", System.IO.File.ReadAllText(path));
            Assert.Single(System.IO.Directory.GetFiles(dir));
        }
    }
}
```

`ContractJson` currently only exposes `Serializer`. Add `public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Include, FloatParseHandling = FloatParseHandling.Double };` in `Contract/Protocol/ContractJson.cs` and build `Serializer` with `JsonSerializer.Create(Settings)`.

- [ ] **Step 2: Run the tests and see them fail**

Run: `dotnet test AgentPlay.sln --filter ReflexTests`
Expected: build errors (types do not exist).

- [ ] **Step 3: Implement**

```csharp
// Contract/Reflexes/Reflex.cs
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Contract
{
    public static class ReflexStatuses
    {
        public const string Proposed = "proposed";
        public const string Active = "active";
        public const string Rejected = "rejected";
        public const string Suspended = "suspended";
        public const string NeedsReview = "needs_review";
    }

    public static class ReflexLimits
    {
        public const int MaxActive = 16;
        public const double MaxCooldownSeconds = 60;
        public const double MaxRunSeconds = 15;
    }

    public sealed class ReflexStats
    {
        [JsonProperty("fired")] public int Fired;
        [JsonProperty("good")] public int Good;
        [JsonProperty("bad")] public int Bad;
    }

    /// <summary>A learned rule the game runs by itself, without asking the model (spec M3 section 3).</summary>
    public sealed class Reflex
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("lesson_id")] public string LessonId;
        [JsonProperty("text")] public string Text;
        [JsonProperty("when")] public string When;
        [JsonProperty("do")] public string Do;
        [JsonProperty("args")] public JObject Args = new JObject();
        [JsonProperty("cooldown_s")] public double CooldownSeconds = 1;
        [JsonProperty("status")] public string Status = ReflexStatuses.Proposed;
        [JsonProperty("game_version")] public string GameVersion;
        [JsonProperty("stats")] public ReflexStats Stats = new ReflexStats();
        [JsonProperty("note")] public string Note = "";
    }

    public sealed class ReflexPolicy
    {
        [JsonProperty("eval_window_s")] public double EvalWindowSeconds = 10;
        [JsonProperty("min_fires")] public int MinFires = 5;
        [JsonProperty("max_fail_rate")] public double MaxFailRate = 0.3;
    }

    public sealed class ReflexFile
    {
        [JsonProperty("version")] public int Version = 1;
        [JsonProperty("policy")] public ReflexPolicy Policy = new ReflexPolicy();
        [JsonProperty("reflexes")] public List<Reflex> Reflexes = new List<Reflex>();
    }

    /// <summary>What hello tells the runner about a loaded reflex.</summary>
    public sealed class ReflexInfo
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("text")] public string Text;
        [JsonProperty("when")] public string When;
        [JsonProperty("do")] public string Do;
        [JsonProperty("args")] public JObject Args;

        public static ReflexInfo From(Reflex r) => new ReflexInfo { Id = r.Id, Text = r.Text, When = r.When, Do = r.Do, Args = r.Args };
    }
}
```

```csharp
// Contract/Reflexes/ReflexValidator.cs
using System.Collections.Generic;

namespace AgentPlay.Contract
{
    public static class ReflexValidator
    {
        public static List<string> Validate(Reflex reflex, HelloMessage hello)
        {
            var errors = new List<string>();
            if (reflex == null)
            {
                errors.Add("reflex: missing");
                return errors;
            }
            if (string.IsNullOrWhiteSpace(reflex.Id)) errors.Add("id: required");
            if (string.IsNullOrWhiteSpace(reflex.When)) errors.Add("when: required");
            else PlanValidator.CheckCondition("when", reflex.When, ConditionSchema.FromHello(hello), errors);

            var actions = new Dictionary<string, ActionInfo>();
            if (hello != null)
                foreach (var a in hello.Actions) actions[a.Name] = a;
            if (!string.IsNullOrWhiteSpace(reflex.Do) && actions.TryGetValue(reflex.Do, out var action) && action.Safe)
                errors.Add("do: the safe action cannot be a reflex");
            else
                PlanValidator.CheckAction("", reflex.Do, reflex.Args, actions, errors);

            if (!(reflex.CooldownSeconds >= 0 && reflex.CooldownSeconds <= ReflexLimits.MaxCooldownSeconds))
                errors.Add("cooldown_s: must be between 0 and " + ReflexLimits.MaxCooldownSeconds);
            return errors;
        }
    }
}
```

`PlanValidator.CheckAction` builds messages as `at + ".do: ..."` and `at + ".args.x: ..."`. With `at == ""` they start with a dot; change both helpers to join with `Join(at, "do")` where `static string Join(string at, string field) => at.Length == 0 ? field : at + "." + field;` so plan messages stay `steps[0].do: ...` and reflex messages are `do: ...`. Make both helpers `internal static`.

```csharp
// Contract/Reflexes/ReflexLifecycle.cs
using System.Collections.Generic;
using System.Globalization;

namespace AgentPlay.Contract
{
    /// <summary>Status transitions of reflexes (spec M3 section 3.4). Returns false when the transition does not apply.</summary>
    public static class ReflexLifecycle
    {
        public static bool Approve(Reflex r, string version)
        {
            if (r.Status != ReflexStatuses.Proposed && r.Status != ReflexStatuses.NeedsReview) return false;
            r.Status = ReflexStatuses.Active;
            r.GameVersion = version;
            r.Note = "";
            return true;
        }

        public static bool Reject(Reflex r)
        {
            if (r.Status == ReflexStatuses.Rejected) return false;
            r.Status = ReflexStatuses.Rejected;
            r.Note = "rejected by user";
            return true;
        }

        public static bool Disable(Reflex r)
        {
            if (r.Status != ReflexStatuses.Active) return false;
            r.Status = ReflexStatuses.Suspended;
            r.Note = "disabled by user";
            return true;
        }

        public static bool Reenable(Reflex r, string version)
        {
            if (r.Status != ReflexStatuses.Suspended) return false;
            r.Status = ReflexStatuses.Active;
            r.GameVersion = version;
            r.Stats = new ReflexStats();
            r.Note = "";
            return true;
        }

        public static List<string> MarkVersionChanges(ReflexFile file, string version)
        {
            var changed = new List<string>();
            foreach (var r in file.Reflexes)
            {
                if (r.Status != ReflexStatuses.Active || r.GameVersion == version) continue;
                r.Note = "game version changed from " + r.GameVersion + " to " + version;
                r.Status = ReflexStatuses.NeedsReview;
                changed.Add(r.Id);
            }
            return changed;
        }

        public static void RecordFired(Reflex r) => r.Stats.Fired++;

        /// <summary>Counts one resolved firing; suspends the reflex when its fail rate is above the policy.</summary>
        public static bool RecordOutcome(Reflex r, bool bad, ReflexPolicy policy)
        {
            if (bad) r.Stats.Bad++;
            else r.Stats.Good++;
            int resolved = r.Stats.Good + r.Stats.Bad;
            if (r.Status != ReflexStatuses.Active || resolved < policy.MinFires) return false;
            double rate = (double)r.Stats.Bad / resolved;
            if (rate <= policy.MaxFailRate) return false;
            r.Status = ReflexStatuses.Suspended;
            r.Note = "fail rate " + rate.ToString("0.00", CultureInfo.InvariantCulture) + " after " + resolved + " outcomes";
            return true;
        }
    }
}
```

```csharp
// Contract/Reflexes/ReflexMonitor.cs
using System.Collections.Generic;

namespace AgentPlay.Contract
{
    public struct ReflexOutcome
    {
        public string Id;
        public bool Bad;
    }

    /// <summary>
    /// Scores reflex firings: a firing is bad when a bad-outcome event happens within the evaluation window
    /// or the reflex itself fails, and good when the window ends quietly. Windows open at session end are dropped.
    /// </summary>
    public sealed class ReflexMonitor
    {
        struct Window
        {
            public string Id;
            public double Start;
        }

        readonly ReflexPolicy _policy;
        readonly List<Window> _open = new List<Window>();
        readonly List<ReflexOutcome> _resolved = new List<ReflexOutcome>();

        public ReflexMonitor(ReflexPolicy policy)
        {
            _policy = policy ?? new ReflexPolicy();
        }

        public void OnFired(string id, double t) => _open.Add(new Window { Id = id, Start = t });

        public void OnFailed(string id)
        {
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                if (_open[i].Id != id) continue;
                _resolved.Add(new ReflexOutcome { Id = id, Bad = true });
                _open.RemoveAt(i);
                return;
            }
        }

        public void OnBadEvent()
        {
            foreach (var w in _open) _resolved.Add(new ReflexOutcome { Id = w.Id, Bad = true });
            _open.Clear();
        }

        /// <summary>Returns outcomes resolved since the last call; expired windows count as good.</summary>
        public List<ReflexOutcome> Tick(double t)
        {
            for (int i = 0; i < _open.Count; i++)
            {
                if (t - _open[i].Start < _policy.EvalWindowSeconds) continue;
                _resolved.Add(new ReflexOutcome { Id = _open[i].Id, Bad = false });
                _open.RemoveAt(i);
                i--;
            }
            var result = new List<ReflexOutcome>(_resolved);
            _resolved.Clear();
            return result;
        }
    }
}
```

```csharp
// Contract/Files/AtomicFile.cs
using System.IO;
using System.Text;

namespace AgentPlay.Contract
{
    public static class AtomicFile
    {
        /// <summary>Writes through a temp file in the same folder so a reader never sees half a file.</summary>
        public static void WriteAllText(string path, string text)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(dir);
            string temp = Path.Combine(dir, "." + Path.GetFileName(path) + "." + System.Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }
}
```

Note: `ReflexMonitor.Tick` mutates `_open` inside a `for` loop with `i--`; this is per Editor update, not per frame inside the game loop, and allocates one small list per call. Acceptable (Editor code path).

- [ ] **Step 4: Run tests until they pass**

Run: `dotnet test AgentPlay.sln`
Expected: all pass (219 old + new).

- [ ] **Step 5: Commit**

```bash
git add Packages/com.agentplay.core/Contract Runner.Tests/Contract/ReflexTests.cs
git commit -m "feat(contract): reflex model, validator, lifecycle and outcome monitor" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(.meta files for the new Contract files are added in Task 3 after Unity generates them.)

---

### Task 2: Protocol 3 messages

**Files:**
- Modify: `Contract/Protocol/MessageTypes.cs`, `Contract/Protocol/Messages.cs`
- Modify: `Runner/RunnerSession.cs` (print `reflex`, `reflex_end`)
- Test: `Runner.Tests/RunnerSessionTests.cs`, `Runner.Tests/Contract/ProtocolV3Tests.cs`

**Interfaces:**
- Produces: `ProtocolVersion.Current = 3`; `MessageTypes.Reflex = "reflex"`, `MessageTypes.ReflexEnd = "reflex_end"`; `HelloMessage.SessionTag` (`session_tag`, default `"debug"`), `HelloMessage.Reflexes` (`List<ReflexInfo>`, `reflexes`); `ReflexMessage { string Id; double T; long Frame; List<string> HandledEvents }` (`id, t, frame, handled_events`); `ReflexEndMessage { string Id; double T; string Status }`.

- [ ] **Step 1: Failing tests**

```csharp
// Runner.Tests/Contract/ProtocolV3Tests.cs
using AgentPlay.Contract;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class ProtocolV3Tests
    {
        [Fact]
        public void HelloCarriesSessionTagAndReflexes()
        {
            var hello = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G", SessionTag = "play" };
            hello.Reflexes.Add(new ReflexInfo { Id = "R-L1", When = "event:x", Do = "hide" });
            string line = MessageCodec.Encode(MessageTypes.Hello, 1, hello);
            Assert.Contains("\"session_tag\":\"play\"", line);
            var back = MessageCodec.Decode(line).As<HelloMessage>();
            Assert.Equal(3, back.Protocol);
            Assert.Equal("R-L1", back.Reflexes[0].Id);
        }

        [Fact]
        public void ReflexMessagesRoundTrip()
        {
            var msg = new ReflexMessage { Id = "R-L1", T = 3.5, Frame = 40, HandledEvents = { "guard.spotted" } };
            var back = MessageCodec.Decode(MessageCodec.Encode(MessageTypes.Reflex, 2, msg)).As<ReflexMessage>();
            Assert.Equal(new[] { "guard.spotted" }, back.HandledEvents);
            var end = MessageCodec.Decode(MessageCodec.Encode(MessageTypes.ReflexEnd, 3, new ReflexEndMessage { Id = "R-L1", T = 4, Status = "done" })).As<ReflexEndMessage>();
            Assert.Equal("done", end.Status);
        }
    }
}
```

Add to `RunnerSessionTests` (follow the file's existing helpers for building envelopes and options):

```csharp
[Fact]
public void ReflexMessagesArePrinted()
{
    var printed = new List<string>();
    var session = new RunnerSession(RunnerOptions.Parse(new string[0]), printed.Add);
    session.Handle(MessageCodec.Decode(MessageCodec.Encode(MessageTypes.Reflex, 1,
        new ReflexMessage { Id = "R-L1", T = 2, Frame = 9, HandledEvents = { "guard.spotted" } })));
    session.Handle(MessageCodec.Decode(MessageCodec.Encode(MessageTypes.ReflexEnd, 2,
        new ReflexEndMessage { Id = "R-L1", T = 3, Status = "done" })));
    Assert.Contains(printed, l => l == "reflex R-L1 t=2.00 handled guard.spotted");
    Assert.Contains(printed, l => l == "reflex_end R-L1 t=3.00 done");
}
```

- [ ] **Step 2: Run, expect build failure.** `dotnet test AgentPlay.sln --filter "ProtocolV3Tests|ReflexMessagesArePrinted"`

- [ ] **Step 3: Implement**

In `MessageTypes.cs`: `Current = 3`; add `public const string Reflex = "reflex"; public const string ReflexEnd = "reflex_end";` under "Unity -> runner".

In `Messages.cs` add to `HelloMessage`:

```csharp
/// <summary>"play" (learn from this session) or "debug".</summary>
[JsonProperty("session_tag")] public string SessionTag = "debug";
/// <summary>Reflexes the game runs by itself in this session.</summary>
[JsonProperty("reflexes")] public List<ReflexInfo> Reflexes = new List<ReflexInfo>();
```

and new classes:

```csharp
public sealed class ReflexMessage
{
    [JsonProperty("id")] public string Id;
    [JsonProperty("t")] public double T;
    [JsonProperty("frame")] public long Frame;
    /// <summary>Important events of the frame the reflex fired in; the game did not pause for them.</summary>
    [JsonProperty("handled_events")] public List<string> HandledEvents = new List<string>();
}

public sealed class ReflexEndMessage
{
    [JsonProperty("id")] public string Id;
    [JsonProperty("t")] public double T;
    /// <summary>"done" or "failed:reason".</summary>
    [JsonProperty("status")] public string Status;
}
```

In `RunnerSession.Dispatch` add:

```csharp
case MessageTypes.Reflex:
    var rx = env.As<ReflexMessage>();
    _print("reflex " + rx.Id + " t=" + Fmt(rx.T) + (rx.HandledEvents.Count > 0 ? " handled " + string.Join(",", rx.HandledEvents) : ""));
    break;

case MessageTypes.ReflexEnd:
    var rxe = env.As<ReflexEndMessage>();
    _print("reflex_end " + rxe.Id + " t=" + Fmt(rxe.T) + " " + rxe.Status);
    break;
```

Fix any existing test that asserts protocol 2.

- [ ] **Step 4:** `dotnet test AgentPlay.sln` — all pass.
- [ ] **Step 5: Commit** `feat(protocol): v3 with session tag, reflexes in hello and reflex messages`

---

### Task 3: Reflex layer in ExecutorCore

**Files:**
- Modify: `Packages/com.agentplay.core/Runtime/Execution/ExecutorCore.cs`, `AgentExecutorHost.cs`
- Test: `Packages/com.agentplay.core/Tests/EditMode/ExecutorReflexTests.cs`

**Interfaces:**
- Consumes: `Reflex`, `ReflexLimits` (Task 1).
- Produces:
  - `ExecutorOutputKinds.ReflexFired = "reflex_fired"`, `ExecutorOutputKinds.ReflexEnded = "reflex_ended"`; `ExecutorOutput.ReflexId` (string); for `reflex_ended`, `Reason` is `"done"` or `"failed:<why>"`.
  - `ExecutorCore.SetReflexes(IReadOnlyList<Reflex> reflexes)` (replaces the list; conditions parsed once), `ExecutorCore.RemoveReflex(string id)`, `ExecutorCore.ReflexRunning` (bool).
  - `IPlanExecutor` gains `SetReflexes` and `RemoveReflex`; `AgentExecutorHost` forwards them.
  - `AgentExecutorHost.Create(ExecutorCore, Func<JObject>, Action<IReadOnlyList<ExecutorOutput>, bool, IReadOnlyList<string>>)`: third callback argument is the names of important events evaluated this frame.

Semantics (spec 3.3): reflexes only when `ControlTaken`; rising edge + cooldown (measured on executor time since the reflex ended); first pending reflex in list order fires when none is running; it interrupts step, rule action or safe action; while it runs no rules/until are evaluated, plan timeout keeps running; on end, the interrupted context is reset; failure or exception or > 15 s game time = `failed`, the plan continues.

- [ ] **Step 1: Failing tests** (same `FakeRunner` pattern as `ExecutorCoreTests`; copy the nested class into the new file)

```csharp
// Tests/EditMode/ExecutorReflexTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class ExecutorReflexTests
    {
        sealed class FakeRunner : IActionRunner
        {
            public readonly Dictionary<string, Func<AgentActionContext, AgentActionStatus>> Behaviour = new Dictionary<string, Func<AgentActionContext, AgentActionStatus>>();
            public readonly List<string> Calls = new List<string>();
            public readonly List<bool> FirstFrames = new List<bool>();
            public bool Has(string action) => Behaviour.ContainsKey(action);
            public AgentActionStatus Run(string action, JObject args, AgentActionContext context)
            {
                Calls.Add(action);
                FirstFrames.Add(context.IsFirstFrame);
                return Behaviour[action](context);
            }
        }

        FakeRunner _runner;
        ExecutorCore _core;
        JObject _state;
        HashSet<string> _events;

        [SetUp]
        public void SetUp()
        {
            _runner = new FakeRunner();
            _runner.Behaviour["safe"] = _ => AgentActionStatus.Done;
            _runner.Behaviour["walk"] = _ => AgentActionStatus.Running;
            _runner.Behaviour["hide"] = ctx => ctx.Elapsed >= 0.15 ? AgentActionStatus.Done : AgentActionStatus.Running;
            _core = new ExecutorCore(_runner, "safe");
            _state = new JObject { ["danger"] = false };
            _events = new HashSet<string>();
        }

        IReadOnlyList<ExecutorOutput> Tick(double dt = 0.1)
        {
            var o = _core.Tick(dt, () => _state, _events);
            _events.Clear();
            return o;
        }

        static string Kinds(IEnumerable<ExecutorOutput> o) => string.Join(",", o.Select(x => x.Kind));

        static Reflex R(string id, string when, string action, double cooldown = 0) =>
            new Reflex { Id = id, When = when, Do = action, Args = new JObject(), CooldownSeconds = cooldown, Status = ReflexStatuses.Active };

        static Plan WalkPlan(params PlanRule[] rules) =>
            new Plan { Id = "p", Steps = new List<PlanStep> { new PlanStep { Do = "walk" } }, Rules = rules.ToList(), TimeoutSeconds = 60 };

        [Test]
        public void ReflexInterruptsStepThenStepRestarts()
        {
            _core.SetReflexes(new[] { R("R1", "event:spotted", "hide") });
            _core.Load(WalkPlan());
            Tick();
            _events.Add("spotted");
            var fired = Tick();
            Assert.AreEqual("reflex_fired", Kinds(fired));
            Assert.AreEqual("R1", fired[0].ReflexId);
            Assert.IsTrue(_core.ReflexRunning);
            Tick();
            var ended = Tick();
            Assert.AreEqual("reflex_ended", Kinds(ended));
            Assert.AreEqual("done", ended[0].Reason);
            Tick();
            CollectionAssert.AreEqual(new[] { "walk", "hide", "hide", "hide", "walk" }, _runner.Calls);
            Assert.IsTrue(_runner.FirstFrames.Last(), "the interrupted step restarts from its first frame");
        }

        [Test]
        public void NoReflexBeforeControlIsTaken()
        {
            _core.SetReflexes(new[] { R("R1", "event:spotted", "hide") });
            _events.Add("spotted");
            Assert.AreEqual("", Kinds(Tick()));
            CollectionAssert.IsEmpty(_runner.Calls);
        }

        [Test]
        public void ReflexRunsBetweenPlansThenSafeResumes()
        {
            _runner.Behaviour["once"] = _ => AgentActionStatus.Done;
            _core.SetReflexes(new[] { R("R1", "danger == true", "hide") });
            _core.Load(new Plan { Id = "p", Steps = new List<PlanStep> { new PlanStep { Do = "once" } }, TimeoutSeconds = 60 });
            Tick();                                   // plan done, no plan loaded now
            _state["danger"] = true;
            Assert.AreEqual("reflex_fired", Kinds(Tick()));
            Tick();
            Assert.AreEqual("reflex_ended", Kinds(Tick()));
            Tick();
            Assert.AreEqual("safe", _runner.Calls.Last());
        }

        [Test]
        public void RulesWaitWhileReflexRuns()
        {
            _core.SetReflexes(new[] { R("R1", "event:spotted", "hide") });
            _core.Load(WalkPlan(new PlanRule { When = "danger == true", Do = "hide", Then = RuleThen.Replan }));
            Tick();
            _events.Add("spotted");
            _state["danger"] = true;
            var first = Tick();
            Assert.AreEqual("reflex_fired", Kinds(first), "reflex outranks the rule in the same frame");
            Tick();
            Assert.AreEqual("reflex_ended", Kinds(Tick()));
            var after = Tick();
            Assert.AreEqual("rule_fired", Kinds(after), "the rule's rising edge is seen once the reflex ended");
        }

        [Test]
        public void CooldownBlocksRefiring()
        {
            _runner.Behaviour["hide"] = _ => AgentActionStatus.Done;
            _core.SetReflexes(new[] { R("R1", "event:spotted", "hide", cooldown: 1) });
            _core.Load(WalkPlan());
            _events.Add("spotted");
            Assert.AreEqual("reflex_fired,reflex_ended", Kinds(Tick()));
            _events.Add("spotted");
            Assert.AreEqual("", Kinds(Tick()), "inside the cooldown");
            for (int i = 0; i < 10; i++) Tick();
            _events.Add("spotted");
            Assert.AreEqual("reflex_fired,reflex_ended", Kinds(Tick()));
        }

        [Test]
        public void FailingReflexDoesNotFailThePlan()
        {
            _runner.Behaviour["hide"] = _ => throw new InvalidOperationException("boom");
            _core.SetReflexes(new[] { R("R1", "event:spotted", "hide") });
            _core.Load(WalkPlan());
            _events.Add("spotted");
            var o = Tick();
            Assert.AreEqual("reflex_fired,reflex_ended", Kinds(o));
            StringAssert.StartsWith("failed:action_error:hide", o[1].Reason);
            Assert.AreEqual("p", _core.PlanId);
        }

        [Test]
        public void ReflexOverRunTimeFails()
        {
            _runner.Behaviour["hide"] = _ => AgentActionStatus.Running;
            _core.SetReflexes(new[] { R("R1", "event:spotted", "hide") });
            _core.Load(new Plan { Id = "p", Steps = new List<PlanStep> { new PlanStep { Do = "walk" } }, TimeoutSeconds = 600 });
            _events.Add("spotted");
            Tick(1);
            IReadOnlyList<ExecutorOutput> o = null;
            for (int i = 0; i < 20 && (o == null || Kinds(o) != "reflex_ended"); i++) o = Tick(1);
            Assert.AreEqual("reflex_ended", Kinds(o));
            Assert.AreEqual("failed:timeout", o[0].Reason);
        }

        [Test]
        public void RemovedReflexStopsFiring()
        {
            _core.SetReflexes(new[] { R("R1", "event:spotted", "hide") });
            _core.Load(WalkPlan());
            _core.RemoveReflex("R1");
            _events.Add("spotted");
            Assert.AreEqual("", Kinds(Tick()));
        }

        [Test]
        public void NoSnapshotWithoutPlanOrReflexes()
        {
            int snapshots = 0;
            _core.Tick(0.1, () => { snapshots++; return _state; }, _events);
            Assert.AreEqual(0, snapshots);
        }
    }
}
```

- [ ] **Step 2:** `bash Tools/unity-tests.sh` → compile errors (expected).

- [ ] **Step 3: Implement** — changes to `ExecutorCore`:

```csharp
public static class ExecutorOutputKinds
{
    public const string Waiting = "waiting";
    public const string ReflexFired = "reflex_fired";
    public const string ReflexEnded = "reflex_ended";
}
// ExecutorOutput: add
public string ReflexId;
```

Fields and methods to add:

```csharp
sealed class ReflexSlot
{
    public Reflex Reflex;
    public CondNode When;
    public bool WasTrue;
    public bool Pending;
    public double ReadyAt;
}

readonly List<ReflexSlot> _reflexes = new List<ReflexSlot>();
readonly AgentActionContext _reflexContext = new AgentActionContext();
ReflexSlot _activeReflex;
double _time;

public bool ReflexRunning => _activeReflex != null;

/// <summary>Replaces the reflexes. Each must already be validated; conditions are parsed here once.</summary>
public void SetReflexes(IReadOnlyList<Reflex> reflexes)
{
    _reflexes.Clear();
    _activeReflex = null;
    if (reflexes == null) return;
    foreach (var r in reflexes) _reflexes.Add(new ReflexSlot { Reflex = r, When = ConditionParser.Parse(r.When) });
}

public void RemoveReflex(string id)
{
    for (int i = _reflexes.Count - 1; i >= 0; i--)
    {
        if (_reflexes[i].Reflex.Id != id) continue;
        if (_activeReflex == _reflexes[i]) _activeReflex = null;
        _reflexes.RemoveAt(i);
    }
}
```

New `Tick`:

```csharp
public IReadOnlyList<ExecutorOutput> Tick(double deltaTime, Func<JObject> snapshot, ICollection<string> events)
{
    _time += deltaTime;
    bool reflexes = ControlTaken && _reflexes.Count > 0;
    if (_plan == null && !reflexes)
    {
        RunSafe(deltaTime);
        return None;
    }

    var outputs = new List<ExecutorOutput>();
    var context = new SnapshotContext(snapshot(), events);
    if (reflexes)
    {
        UpdateReflexEdges(context);
        if (_activeReflex == null) StartPendingReflex(outputs);
    }

    if (_activeReflex != null)
    {
        if (_plan != null)
        {
            _planTime += deltaTime;
            if (_planTime > _plan.TimeoutSeconds) End(outputs, PlanStatuses.PlanFailed, "timeout");
        }
        RunReflex(deltaTime, outputs);
        return outputs;
    }

    if (_plan == null)
    {
        RunSafe(deltaTime);
        return outputs;
    }
    _planTime += deltaTime;
    if (_planTime > _plan.TimeoutSeconds)
    {
        End(outputs, PlanStatuses.PlanFailed, "timeout");
        return outputs;
    }
    // (existing rule loop and RunRule/RunStep, unchanged, using `context`)
    ...
    return outputs;
}

void UpdateReflexEdges(SnapshotContext context)
{
    foreach (var slot in _reflexes)
    {
        bool now = ConditionEvaluator.Evaluate(slot.When, context);
        if (now && !slot.WasTrue && _time >= slot.ReadyAt) slot.Pending = true;
        slot.WasTrue = now;
    }
}

void StartPendingReflex(List<ExecutorOutput> outputs)
{
    foreach (var slot in _reflexes)
    {
        if (!slot.Pending) continue;
        slot.Pending = false;
        _activeReflex = slot;
        Reset(_reflexContext);
        outputs.Add(new ExecutorOutput { Kind = ExecutorOutputKinds.ReflexFired, PlanId = _plan?.Id, ReflexId = slot.Reflex.Id });
        return;
    }
}

void RunReflex(double deltaTime, List<ExecutorOutput> outputs)
{
    var slot = _activeReflex;
    string result = null;
    if (!TryRun(slot.Reflex.Do, slot.Reflex.Args, _reflexContext, deltaTime, out var status, out var error)) result = "failed:" + error;
    else if (status == AgentActionStatus.Failed) result = "failed:action_failed:" + slot.Reflex.Do;
    else if (status == AgentActionStatus.Done) result = "done";
    else if (_reflexContext.Elapsed > ReflexLimits.MaxRunSeconds) result = "failed:timeout";
    if (result == null) return;

    _activeReflex = null;
    slot.ReadyAt = _time + slot.Reflex.CooldownSeconds;
    outputs.Add(new ExecutorOutput { Kind = ExecutorOutputKinds.ReflexEnded, PlanId = _plan?.Id, ReflexId = slot.Reflex.Id, Reason = result });
    // Whatever the reflex interrupted starts its action again.
    Reset(_stepContext);
    Reset(_ruleContext);
    Reset(_safeContext);
}
```

Notes:
- The "pending only if past cooldown" check makes an edge during the cooldown be ignored (not queued), matching `CooldownBlocksRefiring`.
- `ReflexOverRunTimeFails`: `Elapsed` only grows after the first frame (`TryRun` adds `deltaTime` when not first frame), so with dt = 1 the reflex fails on the frame where `Elapsed > 15`.
- `Load()` must not clear reflexes. `Stop()` must not clear an active reflex (a plan stop does not stop a reflex).

In `AgentExecutorHost`: collect important event names in a reusable `List<string>` (two buffers swapped like the event sets, cleared each frame, no per-frame allocation), change `_onFrame` to `Action<IReadOnlyList<ExecutorOutput>, bool, IReadOnlyList<string>>`, call `_onFrame?.Invoke(outputs, important, _frameImportant)`. Add `SetReflexes`/`RemoveReflex` forwarding to `_core` and to `IPlanExecutor`.

Update the existing call site in `AgentPlaySessionController` to `(outputs, important, names) => _core?.OnFrameEnd(outputs, important, names)` (SessionCore gains the parameter in Task 4; for this task add `IReadOnlyList<string> importantEvents = null` to `SessionCore.OnFrameEnd` and ignore it).

- [ ] **Step 4:** `bash Tools/unity-tests.sh` → all pass (108 old + 9 new). Then `dotnet test AgentPlay.sln` still passes.
- [ ] **Step 5: Commit** (include `.meta` files Unity generated for Task 1 and 3 files: `git status --short Packages/` and add every new `.meta`)

`feat(runtime): reflex layer in the executor (priority over rules, cooldown, interrupt and resume)`

---

### Task 4: SessionCore and trace support for reflexes

**Files:**
- Modify: `Runtime/Session/SessionCore.cs`, `Runtime/Trace/TraceWriter.cs`
- Test: `Tests/EditMode/SessionCoreReflexTests.cs`, `Tests/EditMode/TraceTests.cs` (add cases)

**Interfaces:**
- Consumes: `ExecutorOutputKinds.ReflexFired/ReflexEnded`, `ExecutorOutput.ReflexId` (Task 3); `ReflexMessage`, `ReflexEndMessage` (Task 2).
- Produces:
  - `SessionCore.OnFrameEnd(IReadOnlyList<ExecutorOutput> outputs, bool importantEvent, IReadOnlyList<string> importantEvents = null)`: on `reflex_fired` sends `reflex` with `HandledEvents = importantEvents` (copy), writes trace `reflex`, and the frame does not pause for an important event; on `reflex_ended` sends `reflex_end` and writes trace `reflex_end`.
  - `TraceWriter.WriteHeader(string game, string version, string who, string tag, DateTime startedUtc, HelloMessage hello = null)` adds `"hello"` (serialized with `ContractJson.Serializer`) when given.
  - `TraceWriter.WriteReflex(string id, double t, IReadOnlyList<string> handled)`, `TraceWriter.WriteReflexEnd(string id, double t, string status)`.

- [ ] **Step 1: Failing tests.** Look at `SessionCoreTests`/`SessionCorePlanTests` for the fake host (`FakeHost` with `Sent` lines and `Paused` flag) and reuse it.

```csharp
[Test]
public void ImportantEventHandledByReflexDoesNotPause()
{
    // Arrange a turn-based session that already resumed (as in SessionCorePlanTests), then:
    var outputs = new List<ExecutorOutput> { new ExecutorOutput { Kind = ExecutorOutputKinds.ReflexFired, ReflexId = "R1", PlanId = "p" } };
    _core.OnFrameEnd(outputs, true, new List<string> { "guard.spotted" });
    Assert.IsFalse(_host.Paused);
    var reflex = _host.Sent.Select(MessageCodec.Decode).Single(e => e.Type == MessageTypes.Reflex).As<ReflexMessage>();
    Assert.AreEqual("R1", reflex.Id);
    CollectionAssert.AreEqual(new[] { "guard.spotted" }, reflex.HandledEvents);
}

[Test]
public void ReflexEndIsSentAndTraced()
{
    var outputs = new List<ExecutorOutput> { new ExecutorOutput { Kind = ExecutorOutputKinds.ReflexEnded, ReflexId = "R1", Reason = "done" } };
    _core.OnFrameEnd(outputs, false);
    var end = _host.Sent.Select(MessageCodec.Decode).Single(e => e.Type == MessageTypes.ReflexEnd).As<ReflexEndMessage>();
    Assert.AreEqual("done", end.Status);
    StringAssert.Contains("\"type\":\"reflex_end\"", _traceText.ToString());
}

[Test]
public void ImportantEventWithoutReflexStillPauses()
{
    _core.OnFrameEnd(new List<ExecutorOutput>(), true, new List<string> { "guard.spotted" });
    Assert.IsTrue(_host.Paused);
}
```

TraceTests additions:

```csharp
[Test]
public void HeaderCarriesHello()
{
    var text = new StringWriter();
    var w = new TraceWriter(text);
    w.WriteHeader("G", "v1", "human", "play", DateTime.UtcNow, new HelloMessage { Game = "G", SessionTag = "play" });
    var header = JObject.Parse(text.ToString().Split('\n')[0]);
    Assert.AreEqual("G", (string)header["hello"]["game"]);
    Assert.AreEqual("play", (string)header["hello"]["session_tag"]);
}

[Test]
public void ReflexLines()
{
    var text = new StringWriter();
    var w = new TraceWriter(text);
    w.WriteReflex("R1", 2.5, new List<string> { "guard.spotted" });
    w.WriteReflexEnd("R1", 3, "done");
    var lines = text.ToString().Trim().Split('\n').Select(JObject.Parse).ToList();
    Assert.AreEqual("reflex", (string)lines[0]["type"]);
    Assert.AreEqual("guard.spotted", (string)lines[0]["handled_events"][0]);
    Assert.AreEqual("done", (string)lines[1]["status"]);
}
```

- [ ] **Step 2:** `bash Tools/unity-tests.sh` → failures.
- [ ] **Step 3: Implement.** In `OnFrameEnd`:

```csharp
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
```

`TraceWriter`:

```csharp
public void WriteHeader(string game, string version, string who, string tag, DateTime startedUtc, HelloMessage hello = null)
{
    var o = new JObject { ...existing fields... };
    if (hello != null) o["hello"] = JObject.FromObject(hello, ContractJson.Serializer);
    WriteLine(o);
}

public void WriteReflex(string id, double t, IReadOnlyList<string> handled) =>
    WriteLine(new JObject { ["type"] = "reflex", ["t"] = t, ["id"] = id, ["handled_events"] = new JArray(handled) });

public void WriteReflexEnd(string id, double t, string status) =>
    WriteLine(new JObject { ["type"] = "reflex_end", ["t"] = t, ["id"] = id, ["status"] = status });
```

(`new JArray(handled)` takes `object` content; pass `handled.ToArray()` cast via a loop if the overload picks the wrong constructor — `var arr = new JArray(); foreach (var h in handled) arr.Add(h);`.)

- [ ] **Step 4:** Unity tests pass; `dotnet test` pass.
- [ ] **Step 5: Commit** `feat(session): report reflexes to the runner and the trace; no pause when a reflex handled the frame`

---

### Task 5: ReflexBook and Editor session wiring

**Files:**
- Create: `Packages/com.agentplay.core/Editor/ReflexBook.cs`
- Modify: `Editor/AgentPlaySessionController.cs`, `Editor/AgentPlaySettings.cs`
- Test: `Tests/EditMode/ReflexBookTests.cs`

**Interfaces:**
- Consumes: Task 1 types, `AtomicFile`, `ReflexValidator`, `ReflexLifecycle`, `ReflexMonitor`; Task 3 `SetReflexes`/`RemoveReflex`; Task 4 header with hello.
- Produces (`namespace AgentPlay.Editor`):
  - `ReflexBook.Load(string projectRoot) : ReflexBook`; properties `string FilePath`, `ReflexFile File`, `string LoadError`, `IReadOnlyList<Reflex> Reflexes`.
  - `int ImportInbox()`; `bool Save()` (false and no write when `LoadError != null`).
  - `List<Reflex> PrepareSession(HelloMessage hello, Action<string> warn)`: runs `MarkVersionChanges`, then returns active reflexes that pass `ReflexValidator` (at most 16, warns for the rest); saves when anything changed.
  - `Find(string id)`, `Approve(string id, string version)`, `Reject(string id)`, `Disable(string id)`, `Reenable(string id, string version)` — each saves on success and returns bool.
  - `AgentPlaySettings.RunnerPath` (string, EditorPrefs), `AgentPlaySettings.SessionTag` already exists (public setter used by Heist AutoPlay).
  - `AgentPlaySessionController.Book` (static `ReflexBook`, for the window).

- [ ] **Step 1: Failing tests**

```csharp
// Tests/EditMode/ReflexBookTests.cs
using System;
using System.IO;
using System.Linq;
using AgentPlay.Contract;
using AgentPlay.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class ReflexBookTests
    {
        string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "agentplay-book-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "AgentPlay"));
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_root, true);

        static HelloMessage Hello(string version = "v1")
        {
            var h = new HelloMessage { Game = "G", Version = version };
            h.Events.Add(new EventInfo { Name = "guard.spotted" });
            h.Actions.Add(new ActionInfo { Name = "hide_nearest" });
            return h;
        }

        static Reflex R(string id, string status = ReflexStatuses.Proposed, string action = "hide_nearest") =>
            new Reflex { Id = id, When = "event:guard.spotted", Do = action, Status = status, GameVersion = "v1" };

        void WriteInbox(string name, object content) =>
            File.WriteAllText(Path.Combine(_root, "AgentPlay", "Inbox", name), JsonConvert.SerializeObject(content, ContractJson.Settings));

        [Test]
        public void MissingFileGivesEmptyBook()
        {
            var book = ReflexBook.Load(_root);
            Assert.IsNull(book.LoadError);
            Assert.AreEqual(0, book.Reflexes.Count);
        }

        [Test]
        public void InboxIsImportedOnceAndMovedAway()
        {
            Directory.CreateDirectory(Path.Combine(_root, "AgentPlay", "Inbox"));
            WriteInbox("reflex-L1.json", R("R-L1"));
            var book = ReflexBook.Load(_root);
            Assert.AreEqual(1, book.ImportInbox());
            Assert.AreEqual(ReflexStatuses.Proposed, book.Find("R-L1").Status);
            Assert.IsTrue(File.Exists(Path.Combine(_root, "AgentPlay", "Inbox", "imported", "reflex-L1.json")));
            Assert.AreEqual(1, ReflexBook.Load(_root).Reflexes.Count, "import saved the file");
        }

        [Test]
        public void InboxDoesNotDuplicateExistingId()
        {
            var book = ReflexBook.Load(_root);
            Directory.CreateDirectory(Path.Combine(_root, "AgentPlay", "Inbox"));
            WriteInbox("a.json", R("R-L1"));
            book.ImportInbox();
            book.Approve("R-L1", "v1");
            book.Find("R-L1").Stats.Fired = 3;
            book.Save();
            WriteInbox("b.json", R("R-L1"));
            Assert.AreEqual(0, book.ImportInbox());
            Assert.AreEqual(1, book.Reflexes.Count);
            Assert.AreEqual(3, book.Find("R-L1").Stats.Fired);
        }

        [Test]
        public void BrokenInboxFileGoesToRejected()
        {
            Directory.CreateDirectory(Path.Combine(_root, "AgentPlay", "Inbox"));
            File.WriteAllText(Path.Combine(_root, "AgentPlay", "Inbox", "bad.json"), "{ not json");
            var book = ReflexBook.Load(_root);
            Assert.AreEqual(0, book.ImportInbox());
            Assert.IsTrue(File.Exists(Path.Combine(_root, "AgentPlay", "Inbox", "rejected", "bad.json")));
        }

        [Test]
        public void CorruptFileIsNotOverwritten()
        {
            string path = Path.Combine(_root, "AgentPlay", "reflexes.json");
            File.WriteAllText(path, "{ broken");
            var book = ReflexBook.Load(_root);
            Assert.IsNotNull(book.LoadError);
            Assert.IsFalse(book.Save());
            Assert.AreEqual("{ broken", File.ReadAllText(path));
        }

        [Test]
        public void PrepareSessionMarksVersionChangesAndReturnsValidActive()
        {
            var book = ReflexBook.Load(_root);
            book.File.Reflexes.Add(R("R-old", ReflexStatuses.Active));
            book.Save();
            var loaded = book.PrepareSession(Hello("v2"), _ => { });
            Assert.AreEqual(0, loaded.Count);
            Assert.AreEqual(ReflexStatuses.NeedsReview, ReflexBook.Load(_root).Find("R-old").Status);
        }

        [Test]
        public void PrepareSessionSkipsInvalidReflex()
        {
            var book = ReflexBook.Load(_root);
            book.File.Reflexes.Add(R("R-ok", ReflexStatuses.Active));
            book.File.Reflexes.Add(R("R-gone", ReflexStatuses.Active, action: "removed_action"));
            string warned = null;
            var loaded = book.PrepareSession(Hello(), w => warned = w);
            CollectionAssert.AreEqual(new[] { "R-ok" }, loaded.Select(r => r.Id));
            StringAssert.Contains("R-gone", warned);
            Assert.AreEqual(ReflexStatuses.Active, book.Find("R-gone").Status, "status is kept");
        }

        [Test]
        public void PrepareSessionCapsActiveReflexes()
        {
            var book = ReflexBook.Load(_root);
            for (int i = 0; i < 20; i++) book.File.Reflexes.Add(R("R" + i, ReflexStatuses.Active));
            Assert.AreEqual(ReflexLimits.MaxActive, book.PrepareSession(Hello(), _ => { }).Count);
        }
    }
}
```

- [ ] **Step 2:** `bash Tools/unity-tests.sh` → compile errors.
- [ ] **Step 3: Implement `ReflexBook`**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using AgentPlay.Contract;
using Newtonsoft.Json;

namespace AgentPlay.Editor
{
    /// <summary>The project's AgentPlay/reflexes.json. Unity is its only writer (spec M3 section 2).</summary>
    public sealed class ReflexBook
    {
        public string FilePath { get; private set; }
        public string InboxDir { get; private set; }
        public ReflexFile File { get; private set; } = new ReflexFile();
        /// <summary>Set when the file exists but cannot be read; the book then never writes.</summary>
        public string LoadError { get; private set; }
        public IReadOnlyList<Reflex> Reflexes => File.Reflexes;

        public static ReflexBook Load(string projectRoot)
        {
            var book = new ReflexBook
            {
                FilePath = Path.Combine(projectRoot, "AgentPlay", "reflexes.json"),
                InboxDir = Path.Combine(projectRoot, "AgentPlay", "Inbox"),
            };
            if (!System.IO.File.Exists(book.FilePath)) return book;
            try
            {
                book.File = JsonConvert.DeserializeObject<ReflexFile>(System.IO.File.ReadAllText(book.FilePath), ContractJson.Settings) ?? new ReflexFile();
                if (book.File.Reflexes == null) book.File.Reflexes = new List<Reflex>();
                if (book.File.Policy == null) book.File.Policy = new ReflexPolicy();
                foreach (var r in book.File.Reflexes) if (r.Stats == null) r.Stats = new ReflexStats();
            }
            catch (Exception e) when (e is JsonException || e is IOException)
            {
                book.File = new ReflexFile();
                book.LoadError = "Cannot read " + book.FilePath + ": " + e.Message;
            }
            return book;
        }

        public Reflex Find(string id)
        {
            foreach (var r in File.Reflexes) if (r.Id == id) return r;
            return null;
        }

        public bool Save()
        {
            if (LoadError != null) return false;
            AtomicFile.WriteAllText(FilePath, JsonConvert.SerializeObject(File, Formatting.Indented, ContractJson.Settings));
            return true;
        }

        /// <summary>Moves Inbox/*.json into the book as proposed reflexes. Returns how many were added.</summary>
        public int ImportInbox()
        {
            if (LoadError != null || !Directory.Exists(InboxDir)) return 0;
            int added = 0;
            foreach (var path in Directory.GetFiles(InboxDir, "*.json"))
            {
                Reflex r = null;
                try { r = JsonConvert.DeserializeObject<Reflex>(System.IO.File.ReadAllText(path), ContractJson.Settings); }
                catch (JsonException) { }
                if (r == null || string.IsNullOrWhiteSpace(r.Id))
                {
                    MoveTo(path, "rejected");
                    continue;
                }
                if (Find(r.Id) == null)
                {
                    r.Status = ReflexStatuses.Proposed;
                    r.Stats = new ReflexStats();
                    File.Reflexes.Add(r);
                    added++;
                }
                MoveTo(path, "imported");
            }
            if (added > 0) Save();
            return added;
        }

        /// <summary>Called at Play start: version check, then the valid active reflexes to load (at most ReflexLimits.MaxActive).</summary>
        public List<Reflex> PrepareSession(HelloMessage hello, Action<string> warn)
        {
            var result = new List<Reflex>();
            if (LoadError != null)
            {
                warn(LoadError);
                return result;
            }
            if (ReflexLifecycle.MarkVersionChanges(File, hello.Version).Count > 0) Save();
            foreach (var r in File.Reflexes)
            {
                if (r.Status != ReflexStatuses.Active) continue;
                var errors = ReflexValidator.Validate(r, hello);
                if (errors.Count > 0)
                {
                    warn("Reflex " + r.Id + " is not loaded: " + string.Join("; ", errors));
                    continue;
                }
                if (result.Count == ReflexLimits.MaxActive)
                {
                    warn("Only " + ReflexLimits.MaxActive + " reflexes can be active; " + r.Id + " is not loaded");
                    continue;
                }
                result.Add(r);
            }
            return result;
        }

        public bool Approve(string id, string version) => Apply(id, r => ReflexLifecycle.Approve(r, version));
        public bool Reject(string id) => Apply(id, ReflexLifecycle.Reject);
        public bool Disable(string id) => Apply(id, ReflexLifecycle.Disable);
        public bool Reenable(string id, string version) => Apply(id, r => ReflexLifecycle.Reenable(r, version));

        bool Apply(string id, Func<Reflex, bool> change)
        {
            var r = Find(id);
            if (r == null || !change(r)) return false;
            Save();
            return true;
        }

        void MoveTo(string path, string sub)
        {
            string dir = Path.Combine(InboxDir, sub);
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, Path.GetFileName(path));
            if (System.IO.File.Exists(target)) System.IO.File.Delete(target);
            System.IO.File.Move(path, target);
        }
    }
}
```

`AgentPlaySettings`: add

```csharp
/// <summary>Path to AgentPlay.Runner.dll, used by the "Learn from play sessions" button.</summary>
public static string RunnerPath
{
    get => EditorPrefs.GetString(Key("RunnerPath"), "");
    set => EditorPrefs.SetString(Key("RunnerPath"), value ?? "");
}
```

`AgentPlaySessionController` changes:
- Replace `const double SnapshotIntervalSeconds = 2.0;` with `const double DebugSnapshotSeconds = 2.0; const double PlaySnapshotSeconds = 0.5;` and pass `tag == "play" ? PlaySnapshotSeconds : DebugSnapshotSeconds`.
- Fields: `static ReflexMonitor _monitor; static readonly Dictionary<string, Reflex> LoadedReflexes = new Dictionary<string, Reflex>(); public static ReflexBook Book { get; private set; }`.
- In `Begin()`, after hello is built:

```csharp
hello.SessionTag = tag;
Book = ReflexBook.Load(projectRoot);
Book.ImportInbox();
var reflexes = Book.PrepareSession(hello, m => Debug.LogWarning("[AgentPlay] " + m));
LoadedReflexes.Clear();
foreach (var r in reflexes)
{
    LoadedReflexes[r.Id] = r;
    hello.Reflexes.Add(ReflexInfo.From(r));
}
_monitor = new ReflexMonitor(Book.File.Policy);
if (reflexes.Count > 0) Debug.Log("[AgentPlay] " + reflexes.Count + " reflex(es) active");
_trace.WriteHeader(hello.Game, hello.Version, Who, tag, DateTime.UtcNow, hello);
...
_executorHost = AgentExecutorHost.Create(executor, Snapshot, OnFrame);
_executorHost.SetReflexes(reflexes);
```

- `OnFrame`:

```csharp
static void OnFrame(IReadOnlyList<ExecutorOutput> outputs, bool important, IReadOnlyList<string> names)
{
    _core?.OnFrameEnd(outputs, important, names);
    if (_monitor == null) return;
    foreach (var o in outputs)
    {
        if (o.Kind == ExecutorOutputKinds.ReflexFired && LoadedReflexes.TryGetValue(o.ReflexId, out var r))
        {
            ReflexLifecycle.RecordFired(r);
            _monitor.OnFired(o.ReflexId, Time.timeAsDouble);
        }
        else if (o.Kind == ExecutorOutputKinds.ReflexEnded && o.Reason != null && o.Reason.StartsWith("failed", StringComparison.Ordinal))
        {
            _monitor.OnFailed(o.ReflexId);
        }
    }
}
```

- `OnEvent`: `if (record.Outcome == AgentOutcome.Bad) _monitor?.OnBadEvent();` before forwarding.
- `Tick()`: when playing, `ApplyOutcomes(_monitor.Tick(Time.timeAsDouble))`:

```csharp
static void ApplyOutcomes(List<ReflexOutcome> outcomes)
{
    foreach (var o in outcomes)
    {
        if (!LoadedReflexes.TryGetValue(o.Id, out var r)) continue;
        if (!ReflexLifecycle.RecordOutcome(r, o.Bad, Book.File.Policy)) continue;
        Debug.LogWarning("[AgentPlay] Reflex " + r.Id + " suspended: " + r.Note);
        _executorHost?.RemoveReflex(r.Id);
        LoadedReflexes.Remove(r.Id);
        Book.Save();
    }
}
```

- `End()`: `Book?.Save();` (stats of this session), then null the monitor and clear `LoadedReflexes`. Reflex objects in `LoadedReflexes` are the same instances as in `Book.File`, so stats land in the file.

- [ ] **Step 4:** `bash Tools/unity-tests.sh` pass; `bash Tools/e2e-m2a.sh` still passes (no reflexes file → behaviour unchanged). Note: e2e-m2a checks `version=m2`; Heist is still m2 until Task 11.
- [ ] **Step 5: Commit** (with `.meta` of `ReflexBook.cs` and the test) `feat(editor): reflexes.json book, inbox import and reflex wiring in the session`

---

### Task 6: Runner telemetry

**Files:**
- Create: `Runner/Telemetry.cs`
- Modify: `Runner/RunnerSession.cs`, `Runner/Program.cs`, `Runner/Brains/OpusBrain.cs` (no change to API; `Called` already exposes `CallRecord`)
- Test: `Runner.Tests/TelemetryTests.cs`

**Interfaces:**
- Consumes: `ReflexMessage`, `ReflexEndMessage` (Task 2), `CallRecord` (existing: `LatencyMs`, `InputTokens`, `OutputTokens`, `CacheReadTokens`, `CacheWriteTokens`, `Valid`).
- Produces:
  - `TelemetryRecorder` with `OnHello(HelloMessage)`, `OnEvent(EventMessage)`, `OnPaused(PausedMessage)`, `OnReflex(ReflexMessage)`, `OnReflexEnd(ReflexEndMessage)`, `OnPlanSent()`, `OnPlanStatus(PlanStatusMessage)`, `OnCall(CallRecord)`, `TelemetryReport Build(string result)`, `static string Write(string projectRoot, DateTime localNow, TelemetryReport report)` (returns path; file `AgentPlay/Telemetry/yyyyMMdd-HHmmss.json`, `-2` suffix when taken).
  - `TelemetryReport` JSON fields: `game, version, session_tag, result, game_seconds, model_calls, calls_per_game_minute, wait_ms {median, p90}, tokens {input, output, cache_read, cache_write}, plans_sent, plans_failed, important_events, important_events_handled_by_reflex, pauses_by_reason {reason: n}, reflex_fires {id: n}, reflex_failures`.
  - `RunnerSession(RunnerOptions, Action<string>, TelemetryRecorder telemetry = null)` calls the recorder from `Dispatch` and `OnPlan`.

- [ ] **Step 1: Failing test**

```csharp
// Runner.Tests/TelemetryTests.cs
using AgentPlay.Contract;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class TelemetryTests
    {
        [Fact]
        public void CountsReflexHandledEventsCallsAndPauses()
        {
            var t = new TelemetryRecorder();
            t.OnHello(new HelloMessage { Game = "G", Version = "v", SessionTag = "play" });
            foreach (var ms in new long[] { 1000, 3000, 2000, 10000 }) t.OnCall(new CallRecord { LatencyMs = ms, InputTokens = 2, OutputTokens = 100, CacheReadTokens = 50 });
            t.OnEvent(new EventMessage { Name = "guard.spotted", Important = true, T = 10 });
            t.OnEvent(new EventMessage { Name = "item.picked", T = 20 });
            t.OnEvent(new EventMessage { Name = "guard.spotted", Important = true, T = 30 });
            t.OnReflex(new ReflexMessage { Id = "R-L1", T = 30, HandledEvents = { "guard.spotted" } });
            t.OnReflexEnd(new ReflexEndMessage { Id = "R-L1", T = 31, Status = "failed:timeout" });
            t.OnPaused(new PausedMessage { Reason = "event", T = 10 });
            t.OnPaused(new PausedMessage { Reason = "plan", T = 60 });
            t.OnPlanSent(); t.OnPlanSent();
            t.OnPlanStatus(new PlanStatusMessage { Status = PlanStatuses.PlanFailed });

            var r = JObject.FromObject(t.Build("won"));
            Assert.Equal(4, (int)r["model_calls"]);
            Assert.Equal(4.0, (double)r["calls_per_game_minute"], 3);   // 4 calls over 60 game seconds
            Assert.Equal(2500, (double)r["wait_ms"]["median"]);
            Assert.Equal(10000, (double)r["wait_ms"]["p90"]);
            Assert.Equal(2, (int)r["important_events"]);
            Assert.Equal(1, (int)r["important_events_handled_by_reflex"]);
            Assert.Equal(1, (int)r["reflex_fires"]["R-L1"]);
            Assert.Equal(1, (int)r["reflex_failures"]);
            Assert.Equal(1, (int)r["pauses_by_reason"]["event"]);
            Assert.Equal(2, (int)r["plans_sent"]);
            Assert.Equal(1, (int)r["plans_failed"]);
            Assert.Equal(400, (long)r["tokens"]["output"]);
            Assert.Equal("play", (string)r["session_tag"]);
        }

        [Fact]
        public void WriteCreatesUniqueFiles()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agentplay-tel-" + System.Guid.NewGuid().ToString("N"));
            var now = new System.DateTime(2026, 10, 13, 9, 0, 0);
            var a = TelemetryRecorder.Write(root, now, new TelemetryRecorder().Build("lost"));
            var b = TelemetryRecorder.Write(root, now, new TelemetryRecorder().Build("lost"));
            Assert.EndsWith("20261013-090000.json", a);
            Assert.EndsWith("20261013-090000-2.json", b);
        }
    }
}
```

- [ ] **Step 2:** `dotnet test AgentPlay.sln --filter TelemetryTests` → build errors.
- [ ] **Step 3: Implement** `Runner/Telemetry.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AgentPlay.Contract;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json;

namespace AgentPlay.Runner
{
    public sealed class TelemetryReport
    {
        [JsonProperty("game")] public string Game;
        [JsonProperty("version")] public string Version;
        [JsonProperty("session_tag")] public string SessionTag;
        [JsonProperty("result")] public string Result;
        [JsonProperty("game_seconds")] public double GameSeconds;
        [JsonProperty("model_calls")] public int ModelCalls;
        [JsonProperty("calls_per_game_minute")] public double CallsPerGameMinute;
        [JsonProperty("wait_ms")] public Dictionary<string, double> WaitMs = new Dictionary<string, double>();
        [JsonProperty("tokens")] public Dictionary<string, long> Tokens = new Dictionary<string, long>();
        [JsonProperty("plans_sent")] public int PlansSent;
        [JsonProperty("plans_failed")] public int PlansFailed;
        [JsonProperty("important_events")] public int ImportantEvents;
        [JsonProperty("important_events_handled_by_reflex")] public int HandledByReflex;
        [JsonProperty("pauses_by_reason")] public Dictionary<string, int> PausesByReason = new Dictionary<string, int>();
        [JsonProperty("reflex_fires")] public Dictionary<string, int> ReflexFires = new Dictionary<string, int>();
        [JsonProperty("reflex_failures")] public int ReflexFailures;
    }

    /// <summary>Counts what happened in one runner session (spec M3 section 8). Not thread-safe; the runner loop is sequential.</summary>
    public sealed class TelemetryRecorder
    {
        readonly TelemetryReport _r = new TelemetryReport();
        readonly List<long> _latencies = new List<long>();
        long _in, _out, _cacheRead, _cacheWrite;
        double _lastT;

        public void OnHello(HelloMessage h)
        {
            _r.Game = h.Game;
            _r.Version = h.Version;
            _r.SessionTag = h.SessionTag;
        }

        public void OnEvent(EventMessage e)
        {
            Seen(e.T);
            if (e.Important) _r.ImportantEvents++;
        }

        public void OnPaused(PausedMessage p)
        {
            Seen(p.T);
            string reason = p.Reason ?? "unknown";
            _r.PausesByReason[reason] = _r.PausesByReason.TryGetValue(reason, out var n) ? n + 1 : 1;
        }

        public void OnReflex(ReflexMessage m)
        {
            Seen(m.T);
            _r.ReflexFires[m.Id] = _r.ReflexFires.TryGetValue(m.Id, out var n) ? n + 1 : 1;
            _r.HandledByReflex += m.HandledEvents?.Count ?? 0;
        }

        public void OnReflexEnd(ReflexEndMessage m)
        {
            Seen(m.T);
            if (m.Status != null && m.Status.StartsWith("failed", StringComparison.Ordinal)) _r.ReflexFailures++;
        }

        public void OnPlanSent() => _r.PlansSent++;

        public void OnPlanStatus(PlanStatusMessage s)
        {
            if (s.Status == PlanStatuses.PlanFailed) _r.PlansFailed++;
        }

        public void OnCall(CallRecord c)
        {
            _latencies.Add(c.LatencyMs);
            _in += c.InputTokens; _out += c.OutputTokens; _cacheRead += c.CacheReadTokens; _cacheWrite += c.CacheWriteTokens;
        }

        public TelemetryReport Build(string result)
        {
            _r.Result = result;
            _r.GameSeconds = _lastT;
            _r.ModelCalls = _latencies.Count;
            _r.CallsPerGameMinute = _lastT > 0 ? _latencies.Count / (_lastT / 60.0) : 0;
            var sorted = _latencies.OrderBy(x => x).ToList();
            _r.WaitMs["median"] = Percentile(sorted, 0.5);
            _r.WaitMs["p90"] = Percentile(sorted, 0.9);
            _r.Tokens["input"] = _in; _r.Tokens["output"] = _out; _r.Tokens["cache_read"] = _cacheRead; _r.Tokens["cache_write"] = _cacheWrite;
            return _r;
        }

        public static string Write(string projectRoot, DateTime localNow, TelemetryReport report)
        {
            string dir = Path.Combine(projectRoot, "AgentPlay", "Telemetry");
            Directory.CreateDirectory(dir);
            string stem = localNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string path = Path.Combine(dir, stem + ".json");
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(dir, stem + "-" + n + ".json");
            AtomicFile.WriteAllText(path, JsonConvert.SerializeObject(report, Formatting.Indented));
            return path;
        }

        void Seen(double t)
        {
            if (t > _lastT) _lastT = t;
        }

        /// <summary>Median uses the mean of the two middle values; other percentiles take the nearest rank.</summary>
        static double Percentile(List<long> sorted, double p)
        {
            if (sorted.Count == 0) return 0;
            if (p == 0.5 && sorted.Count % 2 == 0) return (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0;
            int rank = (int)Math.Ceiling(p * sorted.Count) - 1;
            return sorted[Math.Max(0, Math.Min(sorted.Count - 1, rank))];
        }
    }
}
```

`RunnerSession`: add optional constructor parameter `TelemetryRecorder telemetry = null` stored as `_telemetry`, and call `_telemetry?.OnHello(Hello)`, `OnEvent(e)`, `OnPaused(p)`, `OnReflex(rx)`, `OnReflexEnd(rxe)`, `OnPlanStatus(s)` in the matching `Dispatch` cases, and `_telemetry?.OnPlanSent()` in `OnPlan` after validation.

`Program`: create `var telemetry = options.Brain != null ? new TelemetryRecorder() : null;`, pass it to `RunnerSession`, add `telemetry?.OnCall(record)` inside the existing `opus.Called` handler (before the run log), and after the loop, when `telemetry != null && session.Hello?.ProjectRoot != null`:

```csharp
var report = telemetry.Build(session.GameResult ?? (session.Finished ? "stopped" : "unfinished"));
string telemetryPath = TelemetryRecorder.Write(session.Hello.ProjectRoot, DateTime.Now, report);
Log("telemetry: " + telemetryPath + " (model calls " + report.ModelCalls + ", important events " + report.ImportantEvents
    + ", handled by reflex " + report.HandledByReflex + ")");
```

`RunnerSession.Reset()` is called per connection; the recorder is not reset (one file per runner run).

- [ ] **Step 4:** `dotnet test AgentPlay.sln` pass.
- [ ] **Step 5: Commit** `feat(runner): telemetry file per brain run (calls, waits, reflex-handled events)`

---

### Task 7: Playbook store and brain prompt sections

**Files:**
- Create: `Runner/Learning/Playbook.cs`
- Modify: `Runner/Brains/PromptBuilder.cs`, `Runner/Brains/BrainContext.cs`, `Runner/Brains/OpusBrain.cs`, `Runner/RunnerSession.cs`, `Runner/Program.cs`
- Test: `Runner.Tests/PlaybookTests.cs`, `Runner.Tests/PromptBuilderTests.cs` (add cases)

**Interfaces:**
- Produces (`namespace AgentPlay.Runner.Learning`):
  - `ReflexCandidate { string When, Do; JObject Args }` (`when, do, args`)
  - `Lesson { string Id, Text, GameVersion; int Confirmations, Contradictions; List<string> Sources; ReflexCandidate Reflex; bool ReflexProposed; string Updated }` (`id, text, game_version, confirmations, contradictions, sources, reflex, reflex_proposed, updated`)
  - `Playbook { int Version = 1; List<Lesson> Lessons; int NextId = 1 }` (`version, lessons, next_id`); `int Score(Lesson l) => l.Confirmations - l.Contradictions` as a static on `Playbook`.
  - `PlaybookStore.Load(string projectRoot) : Playbook` (missing → empty; corrupt → throws `InvalidDataException` with the path), `PlaybookStore.Save(string projectRoot, Playbook)` (writes `playbook.json` and `playbook.md` atomically), `PlaybookStore.RenderMarkdown(Playbook) : string`, `PlaybookStore.ForPrompt(Playbook, int max = 20) : List<Lesson>`.
  - `ReflectedLedger.Load(string projectRoot) : HashSet<string>`, `ReflectedLedger.Save(string projectRoot, HashSet<string>)` (`reflected.json` = sorted JSON array of trace file names).
  - `PromptBuilder.BuildGameSection(HelloMessage) : string` (game name/version, brief, observes, actions, events, condition grammar paragraph) and `PromptBuilder.BuildSystem(HelloMessage hello, Playbook playbook = null)`.
  - `BrainContext.Playbook` (Playbook, may be null). `RunnerSession(RunnerOptions, Action<string>, TelemetryRecorder telemetry = null, Func<HelloMessage, Playbook> loadPlaybook = null)` sets `_context.Playbook` on hello.

Prompt additions (appended after the events section of `BuildSystem`):

```
## Playbook (lessons from earlier sessions of this game)
- [L2, confirmed 4x, contradicted 0x] When a guard spots you, hide behind the nearest cover.
(none yet)   ← when there are no lessons

## Reflexes the game runs by itself
- R-L2: when event:guard.spotted do hide_nearest() — When a guard spots you, hide behind the nearest cover
When a reflex handles an important event the game does not pause, so your plan does not need a rule for it.
(none)   ← when hello.reflexes is empty
```

- [ ] **Step 1: Failing tests**

```csharp
// Runner.Tests/PlaybookTests.cs
using System;
using System.IO;
using AgentPlay.Runner.Learning;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class PlaybookTests
    {
        static string Root()
        {
            var r = Path.Combine(Path.GetTempPath(), "agentplay-pb-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(r);
            return r;
        }

        [Fact]
        public void MissingPlaybookIsEmpty() => Assert.Empty(PlaybookStore.Load(Root()).Lessons);

        [Fact]
        public void SaveWritesJsonAndMarkdown()
        {
            var root = Root();
            var pb = new Playbook();
            pb.Lessons.Add(new Lesson { Id = "L1", Text = "Hide when spotted", Confirmations = 3, Sources = { "a.ndjson" }, GameVersion = "m3" });
            PlaybookStore.Save(root, pb);
            Assert.Equal("L1", PlaybookStore.Load(root).Lessons[0].Id);
            var md = File.ReadAllText(Path.Combine(root, "AgentPlay", "playbook.md"));
            Assert.Contains("## L1", md);
            Assert.Contains("Hide when spotted", md);
            Assert.Contains("confirmed 3", md);
        }

        [Fact]
        public void CorruptPlaybookThrows()
        {
            var root = Root();
            Directory.CreateDirectory(Path.Combine(root, "AgentPlay"));
            File.WriteAllText(Path.Combine(root, "AgentPlay", "playbook.json"), "{ nope");
            Assert.Throws<InvalidDataException>(() => PlaybookStore.Load(root));
        }

        [Fact]
        public void ForPromptOrdersByScoreAndCaps()
        {
            var pb = new Playbook();
            for (int i = 0; i < 25; i++) pb.Lessons.Add(new Lesson { Id = "L" + i, Text = "t", Confirmations = i, Contradictions = i % 2 });
            var top = PlaybookStore.ForPrompt(pb);
            Assert.Equal(20, top.Count);
            Assert.Equal("L24", top[0].Id);
        }

        [Fact]
        public void LedgerRoundTrips()
        {
            var root = Root();
            ReflectedLedger.Save(root, new System.Collections.Generic.HashSet<string> { "b.ndjson", "a.ndjson" });
            Assert.Contains("a.ndjson", ReflectedLedger.Load(root));
        }
    }
}
```

PromptBuilderTests additions:

```csharp
[Fact]
public void SystemPromptListsPlaybookAndReflexes()
{
    var hello = Hello();   // existing helper in this test class
    hello.Reflexes.Add(new ReflexInfo { Id = "R-L2", When = "event:guard.spotted", Do = "hide_nearest", Args = new JObject(), Text = "Hide when spotted" });
    var pb = new Playbook();
    pb.Lessons.Add(new Lesson { Id = "L2", Text = "Hide when spotted", Confirmations = 4 });
    string system = PromptBuilder.BuildSystem(hello, pb);
    Assert.Contains("- [L2, confirmed 4x, contradicted 0x] Hide when spotted", system);
    Assert.Contains("- R-L2: when event:guard.spotted do hide_nearest()", system);
    Assert.Equal(system, PromptBuilder.BuildSystem(hello, pb));
}

[Fact]
public void SystemPromptWithoutLearningSaysNone()
{
    string system = PromptBuilder.BuildSystem(Hello());
    Assert.Contains("## Playbook", system);
    Assert.Contains("(none yet)", system);
}
```

(If `PromptBuilderTests` has no `Hello()` helper, build a minimal `HelloMessage` inline as in `OpusBrainTests.Context()`.)

- [ ] **Step 2:** `dotnet test` → build errors.
- [ ] **Step 3: Implement** `Runner/Learning/Playbook.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AgentPlay.Contract;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner.Learning
{
    public sealed class ReflexCandidate
    {
        [JsonProperty("when")] public string When;
        [JsonProperty("do")] public string Do;
        [JsonProperty("args")] public JObject Args = new JObject();
    }

    public sealed class Lesson
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("text")] public string Text;
        [JsonProperty("game_version")] public string GameVersion;
        [JsonProperty("confirmations")] public int Confirmations;
        [JsonProperty("contradictions")] public int Contradictions;
        [JsonProperty("sources")] public List<string> Sources = new List<string>();
        [JsonProperty("reflex")] public ReflexCandidate Reflex;
        [JsonProperty("reflex_proposed")] public bool ReflexProposed;
        [JsonProperty("updated")] public string Updated;
    }

    public sealed class Playbook
    {
        public const int MaxLessons = 50;
        [JsonProperty("version")] public int Version = 1;
        [JsonProperty("next_id")] public int NextId = 1;
        [JsonProperty("lessons")] public List<Lesson> Lessons = new List<Lesson>();

        public static int Score(Lesson l) => l.Confirmations - l.Contradictions;
    }

    /// <summary>AgentPlay/playbook.json (source of truth) and playbook.md (rendered). The runner is the only writer.</summary>
    public static class PlaybookStore
    {
        public static string JsonPath(string root) => Path.Combine(root, "AgentPlay", "playbook.json");
        public static string MarkdownPath(string root) => Path.Combine(root, "AgentPlay", "playbook.md");

        public static Playbook Load(string root)
        {
            string path = JsonPath(root);
            if (!File.Exists(path)) return new Playbook();
            try
            {
                var pb = JsonConvert.DeserializeObject<Playbook>(File.ReadAllText(path)) ?? new Playbook();
                if (pb.Lessons == null) pb.Lessons = new List<Lesson>();
                return pb;
            }
            catch (JsonException e)
            {
                throw new InvalidDataException("Cannot read " + path + ": " + e.Message + ". Fix or delete the file.", e);
            }
        }

        public static void Save(string root, Playbook pb)
        {
            AtomicFile.WriteAllText(JsonPath(root), JsonConvert.SerializeObject(pb, Formatting.Indented));
            AtomicFile.WriteAllText(MarkdownPath(root), RenderMarkdown(pb));
        }

        public static List<Lesson> ForPrompt(Playbook pb, int max = 20) =>
            pb.Lessons.OrderByDescending(Playbook.Score).ThenBy(l => l.Id, StringComparer.Ordinal).Take(max).ToList();

        public static string RenderMarkdown(Playbook pb)
        {
            var sb = new StringBuilder("# Playbook\n\nGenerated from playbook.json by AgentPlay; edits here are overwritten.\n");
            foreach (var l in ForPrompt(pb, int.MaxValue))
            {
                sb.Append("\n## ").Append(l.Id).Append('\n');
                sb.Append(l.Text).Append("\n\n");
                sb.Append("- confirmed ").Append(l.Confirmations).Append(", contradicted ").Append(l.Contradictions)
                  .Append(", game version ").Append(l.GameVersion ?? "-").Append('\n');
                if (l.Reflex != null)
                    sb.Append("- reflex: when ").Append(l.Reflex.When).Append(" do ").Append(l.Reflex.Do)
                      .Append(l.Reflex.Args == null ? "()" : "(" + l.Reflex.Args.ToString(Formatting.None) + ")")
                      .Append(l.ReflexProposed ? " (proposed)" : "").Append('\n');
                sb.Append("- sources: ").Append(string.Join(", ", l.Sources)).Append('\n');
            }
            return sb.ToString();
        }
    }

    public static class ReflectedLedger
    {
        static string PathOf(string root) => Path.Combine(root, "AgentPlay", "reflected.json");

        public static HashSet<string> Load(string root)
        {
            string path = PathOf(root);
            if (!File.Exists(path)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(path)) ?? new List<string>();
            return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        }

        public static void Save(string root, HashSet<string> names) =>
            AtomicFile.WriteAllText(PathOf(root), JsonConvert.SerializeObject(names.OrderBy(n => n, StringComparer.Ordinal).ToList(), Formatting.Indented));
    }
}
```

`PromptBuilder`: move the game part (from `"# Game: "` to the "Important events pause..." line) into `public static string BuildGameSection(HelloMessage hello)`. Move the condition-language paragraph from `Instructions` into a `public const string ConditionGrammar` included in `BuildGameSection` (the Reflector needs it). Then:

```csharp
public static string BuildSystem(HelloMessage hello, Playbook playbook = null)
{
    var sb = new StringBuilder();
    sb.Append(Instructions).Append("\n\n").Append(BuildGameSection(hello));
    sb.Append("\n## Playbook (lessons from earlier sessions of this game)\n");
    var lessons = playbook == null ? new List<Lesson>() : PlaybookStore.ForPrompt(playbook);
    if (lessons.Count == 0) sb.Append("(none yet)\n");
    foreach (var l in lessons)
        sb.Append("- [").Append(l.Id).Append(", confirmed ").Append(l.Confirmations).Append("x, contradicted ")
          .Append(l.Contradictions).Append("x] ").Append(l.Text).Append('\n');
    sb.Append("\n## Reflexes the game runs by itself\n");
    if (hello.Reflexes == null || hello.Reflexes.Count == 0) sb.Append("(none)\n");
    else
    {
        foreach (var r in hello.Reflexes)
            sb.Append("- ").Append(r.Id).Append(": when ").Append(r.When).Append(" do ").Append(r.Do)
              .Append('(').Append(FormatArgs(r.Args)).Append(')')
              .Append(string.IsNullOrWhiteSpace(r.Text) ? "" : " — " + r.Text.Trim()).Append('\n');
        sb.Append("When a reflex handles an important event the game does not pause, so your plan does not need a rule for it.\n");
    }
    return sb.ToString();
}

static string FormatArgs(JObject args) => args == null || !args.HasValues ? "" : string.Join(", ", args.Properties().Select(p => p.Name + ": " + p.Value.ToString(Formatting.None)));
```

The existing `Instructions` text keeps everything except the "Conditions compare..." paragraph, which now lives in `ConditionGrammar` (inside the game section, so the brain still sees it). Keep existing PromptBuilder tests passing; update any that matched exact positions.

`OpusBrain`: where it rebuilds the system prompt on a new hello, call `PromptBuilder.BuildSystem(context.Hello, context.Playbook)`.

`RunnerSession`: constructor parameter `Func<HelloMessage, Playbook> loadPlaybook = null`; in the hello case: `_context.Playbook = loadPlaybook?.Invoke(Hello);`.

`Program`: pass `hello => LoadPlaybookSafe(hello)` where

```csharp
static Playbook LoadPlaybookSafe(HelloMessage hello)
{
    if (string.IsNullOrEmpty(hello.ProjectRoot)) return null;
    try
    {
        var pb = PlaybookStore.Load(hello.ProjectRoot);
        Log("playbook: " + pb.Lessons.Count + " lesson(s); reflexes active in game: " + hello.Reflexes.Count);
        return pb;
    }
    catch (InvalidDataException e)
    {
        Log("WARNING: " + e.Message + " Playing without the playbook.");
        return null;
    }
}
```

- [ ] **Step 4:** `dotnet test` pass.
- [ ] **Step 5: Commit** `feat(runner): playbook store and playbook/reflex sections in the brain prompt`

---

### Task 8: TraceDigest

**Files:**
- Create: `Runner/Learning/TraceDigest.cs`
- Test: `Runner.Tests/TraceDigestTests.cs`

**Interfaces:**
- Produces: `TraceDigest.Build(IEnumerable<string> lines, int maxChars) : TraceDigestResult` with `HelloMessage Hello; string Tag; string Who; bool AgentControlled; string Text; string Error` (Error set and Text null when the header is missing or has no `hello`); `TraceDigest.ReadHeader(string path) : JObject` (first line parsed, null if unreadable).

Format of `Text` (one line per item):

```
Game: AgentPlay Heist (version m3). Player: human. Session tag: play. Length: 62.4 game seconds.
t=0.00 state player.zone='start' player.pos=(-8.0,1.0,0.0) items.left=3 ...      ← first snapshot: all keys
t=0.50 state player.pos=(-6.5,1.0,0.0)                                            ← later: changed keys only
t=3.49 event item.picked {"item":"item_a","carrying":1}
t=4.10 agent takes control
t=4.10 plan p1 "Take item_b": goto(zone:'item_b'); pick(item:'item_b') | rules: event:guard.spotted -> hide_nearest() then replan
t=6.77 plan p1 rule_fired step=0 replan
t=6.80 reflex R-L2 fired (handled guard.spotted)
t=8.10 reflex R-L2 done
```

Rules: floats rounded to 1 decimal ("F1", invariant), vectors `(x,y,z)` each rounded; lists (`guards`) rendered as `[{distance:5.2,sees_player:true,...}, ...]` with the same rounding; a key whose rounded value equals the previous rendered value is omitted; a state line with no changed key is omitted. Event lines also include changed state keys from the event's `state` in the same diff stream (render the event line, then a `state` line for changes, with the same `t`). If the text exceeds `maxChars`, drop every 2nd `state` line (except the first), then every 2nd again, until it fits; if only non-state lines remain and it still does not fit, cut at `maxChars` and append `\n(truncated)`.

- [ ] **Step 1: Failing tests**

```csharp
// Runner.Tests/TraceDigestTests.cs
using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using AgentPlay.Runner.Learning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class TraceDigestTests
    {
        static string Header(bool withHello = true)
        {
            var o = new JObject { ["type"] = "header", ["game"] = "G", ["version"] = "v1", ["who"] = "human", ["tag"] = "play" };
            if (withHello) o["hello"] = JObject.FromObject(new HelloMessage { Game = "G", Version = "v1", SessionTag = "play" }, ContractJson.Serializer);
            return o.ToString(Formatting.None);
        }

        static string Snap(double t, double x, bool hidden) => new JObject
        {
            ["type"] = "snapshot", ["t"] = t, ["frame"] = 1,
            ["state"] = new JObject { ["player.pos"] = new JObject { ["x"] = x, ["y"] = 1.0, ["z"] = 0.0 }, ["player.hidden"] = hidden },
        }.ToString(Formatting.None);

        [Fact]
        public void MissingHelloIsAnError()
        {
            var d = TraceDigest.Build(new[] { Header(withHello: false) }, 1000);
            Assert.NotNull(d.Error);
            Assert.Null(d.Text);
        }

        [Fact]
        public void DiffsSnapshotsAndRoundsNumbers()
        {
            var lines = new[] { Header(), Snap(0, -8.0, false), Snap(0.5, -8.02, false), Snap(1.0, -6.5, true) };
            var d = TraceDigest.Build(lines, 10000);
            Assert.Null(d.Error);
            Assert.Equal("play", d.Tag);
            Assert.False(d.AgentControlled);
            var text = d.Text.Split('\n').ToList();
            Assert.StartsWith("Game: G (version v1). Player: human. Session tag: play.", text[0]);
            Assert.Contains("t=0.00 state player.pos=(-8.0,1.0,0.0) player.hidden=false", text);
            Assert.DoesNotContain(text, l => l.StartsWith("t=0.50"));        // -8.02 rounds to -8.0: no change
            Assert.Contains("t=1.00 state player.pos=(-6.5,1.0,0.0) player.hidden=true", text);
        }

        [Fact]
        public void EventsPlansAndReflexesAreKept()
        {
            var lines = new List<string>
            {
                Header(),
                new JObject { ["type"] = "event", ["t"] = 2.0, ["name"] = "guard.spotted", ["payload"] = new JObject { ["distance"] = 6.99 }, ["state"] = new JObject() }.ToString(Formatting.None),
                new JObject { ["type"] = "control", ["t"] = 2.5, ["who"] = "agent" }.ToString(Formatting.None),
                new JObject { ["type"] = "plan", ["t"] = 2.5, ["plan"] = JObject.Parse("{\"id\":\"p1\",\"intent\":\"Hide\",\"steps\":[{\"do\":\"hide\",\"args\":{\"zone\":\"cover_1\"},\"until\":\"\"}],\"rules\":[],\"timeout_s\":30}") }.ToString(Formatting.None),
                new JObject { ["type"] = "reflex", ["t"] = 3.0, ["id"] = "R-L2", ["handled_events"] = new JArray("guard.spotted") }.ToString(Formatting.None),
                new JObject { ["type"] = "reflex_end", ["t"] = 3.4, ["id"] = "R-L2", ["status"] = "done" }.ToString(Formatting.None),
            };
            var d = TraceDigest.Build(lines, 10000);
            Assert.True(d.AgentControlled);
            Assert.Contains("t=2.00 event guard.spotted {\"distance\":7.0}", d.Text);
            Assert.Contains("t=2.50 agent takes control", d.Text);
            Assert.Contains("t=2.50 plan p1 \"Hide\": hide(zone:'cover_1')", d.Text);
            Assert.Contains("t=3.00 reflex R-L2 fired (handled guard.spotted)", d.Text);
            Assert.Contains("t=3.40 reflex R-L2 done", d.Text);
        }

        [Fact]
        public void LongTracesDropStateLinesButKeepEvents()
        {
            var lines = new List<string> { Header() };
            for (int i = 0; i < 400; i++) lines.Add(Snap(i * 0.5, i, i % 2 == 0));
            lines.Add(new JObject { ["type"] = "event", ["t"] = 999.0, ["name"] = "game.won", ["payload"] = new JObject(), ["state"] = new JObject() }.ToString(Formatting.None));
            var d = TraceDigest.Build(lines, 4000);
            Assert.True(d.Text.Length <= 4000);
            Assert.Contains("event game.won", d.Text);
        }
    }
}
```

- [ ] **Step 2:** fail.
- [ ] **Step 3: Implement** `TraceDigest` with the format above. Key helpers:

```csharp
static string Render(JToken v)
{
    switch (v?.Type)
    {
        case null: case JTokenType.Null: return "null";
        case JTokenType.Float: case JTokenType.Integer:
            return ((double)v).ToString("0.0", CultureInfo.InvariantCulture);
        case JTokenType.Boolean: return (bool)v ? "true" : "false";
        case JTokenType.String: return "'" + (string)v + "'";
        case JTokenType.Object:
            var o = (JObject)v;
            if (o.Count == 3 && o["x"] != null && o["y"] != null && o["z"] != null)
                return "(" + Render(o["x"]) + "," + Render(o["y"]) + "," + Render(o["z"]) + ")";
            return "{" + string.Join(",", o.Properties().Select(p => p.Name + ":" + Render(p.Value))) + "}";
        case JTokenType.Array:
            return "[" + string.Join(", ", v.Select(Render)) + "]";
        default: return v.ToString(Formatting.None);
    }
}
```

Integers render as `"0.0"` too? No: integers keep their exact value — use `((long)v).ToString(CultureInfo.InvariantCulture)` for `Integer` and the one-decimal format only for `Float`. Payloads use the same rounding but JSON-ish output: render payload with a helper that rounds floats in a deep clone (`RoundFloats(JToken)`) then `ToString(Formatting.None)` (so `6.99` becomes `7.0`).

Plan line: `plan <id> "<intent>": do(arg:'v'); do2() | rules: <when> -> do(args) then <then>; ...` (omit `| rules:` part when there are none; append ` until <cond>` after a step when its until is non-empty).
Plan status line: `plan <id> <status> step=<n>[ <reason>]`.

Length: game seconds = largest `t` seen.

- [ ] **Step 4:** pass. **Step 5: Commit** `feat(runner): trace digest for the reflector`

---

### Task 9: Reflector, schema, merger and the `reflect` command

**Files:**
- Create: `Runner/Learning/ReflectorSchema.cs`, `Runner/Learning/PlaybookMerger.cs`, `Runner/Learning/Reflector.cs`
- Modify: `Runner/RunnerOptions.cs`, `Runner/Program.cs`, `Runner/RunnerConfig.cs`, `Runner/runner.json`, `Runner/Brains/PlanSchema.cs` (make `Args(ActionInfo)` and `Closed(...)` `internal static`)
- Test: `Runner.Tests/ReflectorSchemaTests.cs`, `Runner.Tests/PlaybookMergerTests.cs`, `Runner.Tests/ReflectorTests.cs`, `Runner.Tests/RunnerOptionsTests.cs` (add cases)

**Interfaces:**
- Consumes: `IModelClient`, `ModelRequest { System, User, Schema }`, `ModelReply`, `ModelCallException` (existing); `TraceDigest` (Task 8); `Playbook`, `PlaybookStore`, `ReflectedLedger`, `ReflexCandidate` (Task 7); `ReflexValidator`, `Reflex`, `AtomicFile` (Task 1); `PromptBuilder.BuildGameSection` (Task 7).
- Produces:
  - `RunnerConfig.Reflector` (`reflector`): `ReflectorConfig { string Effort = "high"; int MinConfirmations = 3; int MaxDigestChars = 60000; int MaxTracesPerRun = 5 }` (`effort, min_confirmations, max_digest_chars, max_traces_per_run`), validated in `RunnerConfig.Validate`.
  - `ReflectorSchema.Build(HelloMessage) : JObject`.
  - `ReflectorOutput { string Summary; List<ReflectorLesson> Lessons }`, `ReflectorLesson { string Id, Verdict, Text, Evidence; ReflexCandidate Reflex }` (`summary, lessons, id, verdict, text, evidence, reflex`); `ReflectorOutput.Validate() : List<string>` (verdict in new/confirmed/contradicted, text non-empty and ≤ 300 chars).
  - `PlaybookMerger.Merge(Playbook pb, ReflectorOutput output, string traceName, HelloMessage hello, int minConfirmations, DateTime now) : MergeResult` with `int New, Confirmed, Contradicted; List<Reflex> Proposals; List<string> Notes`.
  - `Reflector(IModelClient client, RunnerConfig config, Action<string> log)`, `Task<int> RunAsync(string projectRoot, IReadOnlyList<string> traces, bool force, CancellationToken ct)` returning the exit code (0, 2, 4); `int RetryDelayMs` property (tests set 0).
  - `RunnerOptions.Command` (`null` or `"reflect"`), `ProjectDir`, `Traces` (`List<string>`), `Force`, `NoReflect`.

Merge rules (spec 6.4):
- For each output lesson: find by id (case-insensitive) when verdict is `confirmed`/`contradicted`; if not found, or verdict `new`, create `L<NextId++>` with `Confirmations = 1` (a contradicted unknown lesson is ignored with a note instead).
- A lesson touched twice by the same trace counts once (keep a `HashSet<string>` of ids touched this merge; also skip if `Sources` already contains `traceName`).
- `confirmed`: `Confirmations++`, replace `Text` with the new text when non-empty.
- `contradicted`: `Contradictions++`.
- Always: `GameVersion = hello.Version`, add `traceName` to `Sources`, `Updated = now ISO`.
- Reflex candidate: when provided, convert to `Reflex { Id = "R-" + lesson.Id, When, Do, Args }` and run `ReflexValidator.Validate(.., hello)`; valid → `lesson.Reflex = candidate`; invalid → note `"L3: reflex dropped: <errors>"`.
- After all: for lessons with `Confirmations >= minConfirmations && Contradictions == 0 && Reflex != null && !ReflexProposed` → proposal `Reflex { Id = "R-" + Id, LessonId = Id, Text = Text, When, Do, Args, CooldownSeconds = 1, Status = proposed, GameVersion = hello.Version }`, set `ReflexProposed = true`.
- Cap: while `Lessons.Count > 50`, remove the lesson with the lowest score (ties: oldest `Updated`).

Reflector flow:
1. `ProjectDir` must contain `AgentPlay/` (else exit 2, message).
2. Load playbook (corrupt → log message, exit 2) and ledger.
3. Candidate traces: `--trace` files, else `AgentPlay/Traces/*.ndjson` sorted by name. For each: read header via `TraceDigest.ReadHeader`; skip tag ≠ `play` (log "skipped (tag debug)"), skip names in the ledger unless `force`; take at most `MaxTracesPerRun`.
4. For each trace: `TraceDigest.Build(File.ReadLines(path), MaxDigestChars)`; on `Error` log and add to ledger (an unreadable/old trace is not retried forever).
5. Request: `System = ReflectorInstructions + "\n\n" + PromptBuilder.BuildGameSection(digest.Hello)`; `User` = current lessons (one line each: `- L2 [confirmed 2x, contradicted 0x] text | reflex: when ... do ...(args)` or `(no lessons yet)`) + `"\n\n# Session\n" + digest.Text + "\n\nAnswer with the lessons this session supports or contradicts."`; `Schema = ReflectorSchema.Build(digest.Hello)`. Uses a `RunnerConfig` copy with `Effort = config.Reflector.Effort`.
6. Call: transient `ModelCallException` → retry up to 2 more times with `RetryDelayMs` (default 5000); non-retryable → log, exit 4 (stop processing; earlier traces stay saved).
7. Parse `reply.Text` into `ReflectorOutput` (JsonException or `Validate()` errors → one retry with `"Your previous answer was rejected: ...\nAnswer again."` appended to the user message; second failure → log, exit 4).
8. Merge, write each proposal to `AgentPlay/Inbox/reflex-<lessonId>-<yyyyMMdd-HHmmss>.json` (atomic; `ContractJson.Settings`), `PlaybookStore.Save`, add to ledger, `ReflectedLedger.Save`. Log `"<trace>: <summary first 200 chars>"` and `"lessons: N new, N confirmed, N contradicted; proposals: ids"`.
9. End: log `"reflect done: X trace(s), Y proposal(s)"`, return 0.

Reflector instructions (constant `Reflector.Instructions`):

```
You review one recorded session of a video game and keep the game's playbook of lessons up to date. The playbook helps an AI agent play this game better next time.

You get the game description, the current lessons, and the session as a timeline: state changes, events, and (when the agent played) its plans and reflexes. When the player is a human, infer what they did from how the state changed.

For each lesson the session gives real evidence for, answer one entry:
- confirmed: the session shows an existing lesson working again. Use its id.
- contradicted: the session shows an existing lesson failing or being wrong. Use its id.
- new: a useful lesson not in the playbook. Leave id empty. Do not create a new lesson that says the same as an existing one; confirm the existing one instead.
A lesson says what to do in which situation, in one or two sentences, at most 300 characters. Only write lessons that would change how a player acts. Do not write lessons about the timeline format.

reflex: when a lesson is a simple, immediate reaction that is safe every time its condition is true, also write it as a reflex: a condition in the game's condition language and one action with its arguments. Prefer conditions on events (event:name) or simple observe comparisons. Otherwise set reflex to null.

Return an empty lessons list if the session teaches nothing new.
```

Schema (risk 2): `reflex` is `{"anyOf": [{"type": "null"}, variant per action {when: string, do: const, args: Args(action)}]}`. `ReflectorSchema` reuses `PlanSchema.Args` and `PlanSchema.Closed`.

- [ ] **Step 1 (risk 2 probe, real model call, do first):** write the schema class and a throwaway check: build the schema for a 2-action hello, then run

```bash
cd /e/Unity-WorkSpace/AgentPlay && dotnet run --project Runner -- --reflector-smoke
```

Implement `--reflector-smoke` in `Program` (like `--smoke`): one `ReflectorSchema` call with a fake two-line digest; print the structured output. Expected: JSON with `lessons[*].reflex` either `null` or an object. If the CLI rejects the schema (error mentioning anyOf/null), switch the schema to `has_reflex` (boolean) + `reflex` (always an object; `when` empty string when none) and make `ReflectorOutput` map `has_reflex == false` to `Reflex = null`; record the ruling in the plan ledger. Keep `--reflector-smoke` (cheap check like `--smoke`).

- [ ] **Step 2: Failing tests**

```csharp
// Runner.Tests/ReflectorSchemaTests.cs
[Fact]
public void ReflexIsNullOrAnActionVariant()
{
    var hello = new HelloMessage { Game = "G" };
    hello.Actions.Add(new ActionInfo { Name = "hide_nearest" });
    hello.Actions.Add(new ActionInfo { Name = "goto", Params = { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } } });
    var schema = ReflectorSchema.Build(hello);
    var reflex = schema["properties"]["lessons"]["items"]["properties"]["reflex"];
    var variants = (JArray)reflex["anyOf"];
    Assert.Equal("null", (string)variants[0]["type"]);
    Assert.Equal(3, variants.Count);
    Assert.Equal("goto", (string)variants[2]["properties"]["do"]["const"]);
    Assert.False((bool)schema["additionalProperties"]);
    Assert.False((bool)schema["properties"]["lessons"]["items"]["additionalProperties"]);
    Assert.Equal(new[] { "new", "confirmed", "contradicted" }, schema["properties"]["lessons"]["items"]["properties"]["verdict"]["enum"].Values<string>());
}
```

```csharp
// Runner.Tests/PlaybookMergerTests.cs
using System;
using System.Collections.Generic;
using AgentPlay.Contract;
using AgentPlay.Runner.Learning;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class PlaybookMergerTests
    {
        static readonly DateTime Now = new DateTime(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);

        static HelloMessage Hello()
        {
            var h = new HelloMessage { Game = "G", Version = "m3" };
            h.Events.Add(new EventInfo { Name = "guard.spotted" });
            h.Actions.Add(new ActionInfo { Name = "hide_nearest" });
            h.Actions.Add(new ActionInfo { Name = "stop", Safe = true });
            return h;
        }

        static ReflectorLesson L(string id, string verdict, string text = "Hide when spotted", ReflexCandidate reflex = null) =>
            new ReflectorLesson { Id = id, Verdict = verdict, Text = text, Evidence = "t=3", Reflex = reflex };

        static ReflexCandidate Hide() => new ReflexCandidate { When = "event:guard.spotted", Do = "hide_nearest", Args = new JObject() };

        static ReflectorOutput Out(params ReflectorLesson[] lessons) => new ReflectorOutput { Summary = "s", Lessons = new List<ReflectorLesson>(lessons) };

        [Fact]
        public void NewLessonGetsAnIdAndOneConfirmation()
        {
            var pb = new Playbook();
            var r = PlaybookMerger.Merge(pb, Out(L("", "new")), "t1.ndjson", Hello(), 3, Now);
            Assert.Equal(1, r.New);
            Assert.Equal("L1", pb.Lessons[0].Id);
            Assert.Equal(1, pb.Lessons[0].Confirmations);
            Assert.Equal(new[] { "t1.ndjson" }, pb.Lessons[0].Sources);
            Assert.Equal(2, pb.NextId);
        }

        [Fact]
        public void UnknownConfirmedIdBecomesNewLesson()
        {
            var pb = new Playbook();
            var r = PlaybookMerger.Merge(pb, Out(L("L9", "confirmed")), "t1.ndjson", Hello(), 3, Now);
            Assert.Equal(1, r.New);
            Assert.Equal("L1", pb.Lessons[0].Id);
        }

        [Fact]
        public void ThirdConfirmationProposesTheReflexOnce()
        {
            var pb = new Playbook();
            PlaybookMerger.Merge(pb, Out(L("", "new", reflex: Hide())), "t1.ndjson", Hello(), 3, Now);
            Assert.Empty(PlaybookMerger.Merge(pb, Out(L("L1", "confirmed")), "t2.ndjson", Hello(), 3, Now).Proposals);
            var third = PlaybookMerger.Merge(pb, Out(L("L1", "confirmed")), "t3.ndjson", Hello(), 3, Now);
            Assert.Single(third.Proposals);
            Assert.Equal("R-L1", third.Proposals[0].Id);
            Assert.Equal(ReflexStatuses.Proposed, third.Proposals[0].Status);
            Assert.Equal("m3", third.Proposals[0].GameVersion);
            Assert.Empty(PlaybookMerger.Merge(pb, Out(L("L1", "confirmed")), "t4.ndjson", Hello(), 3, Now).Proposals);
        }

        [Fact]
        public void SameTraceCountsOnce()
        {
            var pb = new Playbook();
            PlaybookMerger.Merge(pb, Out(L("", "new")), "t1.ndjson", Hello(), 3, Now);
            PlaybookMerger.Merge(pb, Out(L("L1", "confirmed"), L("L1", "confirmed")), "t2.ndjson", Hello(), 3, Now);
            PlaybookMerger.Merge(pb, Out(L("L1", "confirmed")), "t2.ndjson", Hello(), 3, Now);
            Assert.Equal(2, pb.Lessons[0].Confirmations);
        }

        [Fact]
        public void ContradictionBlocksProposal()
        {
            var pb = new Playbook();
            PlaybookMerger.Merge(pb, Out(L("", "new", reflex: Hide())), "t1.ndjson", Hello(), 3, Now);
            PlaybookMerger.Merge(pb, Out(L("L1", "contradicted")), "t2.ndjson", Hello(), 3, Now);
            PlaybookMerger.Merge(pb, Out(L("L1", "confirmed")), "t3.ndjson", Hello(), 3, Now);
            Assert.Empty(PlaybookMerger.Merge(pb, Out(L("L1", "confirmed")), "t4.ndjson", Hello(), 3, Now).Proposals);
            Assert.Equal(1, pb.Lessons[0].Contradictions);
        }

        [Fact]
        public void InvalidReflexCandidateIsDroppedWithANote()
        {
            var pb = new Playbook();
            var bad = new ReflexCandidate { When = "event:guard.spotted", Do = "stop", Args = new JObject() };
            var r = PlaybookMerger.Merge(pb, Out(L("", "new", reflex: bad)), "t1.ndjson", Hello(), 3, Now);
            Assert.Null(pb.Lessons[0].Reflex);
            Assert.Contains(r.Notes, n => n.StartsWith("L1: reflex dropped"));
        }

        [Fact]
        public void PlaybookIsCappedAtFiftyLessons()
        {
            var pb = new Playbook();
            for (int i = 0; i < 50; i++) pb.Lessons.Add(new Lesson { Id = "L" + (i + 1), Text = "t", Confirmations = 2, Updated = "2026-01-01" });
            pb.NextId = 51;
            pb.Lessons[7].Confirmations = 0;
            PlaybookMerger.Merge(pb, Out(L("", "new")), "t.ndjson", Hello(), 3, Now);
            Assert.Equal(50, pb.Lessons.Count);
            Assert.DoesNotContain(pb.Lessons, l => l.Id == "L8");
        }
    }
}
```

Note on the cap test: the new lesson has score 1 and `L8` has score 0, so `L8` is the one removed.

```csharp
// Runner.Tests/ReflectorTests.cs  (FakeClient like OpusBrainTests)
// Builds a temp project with AgentPlay/Traces/<name>-human-play.ndjson (header with hello + two snapshots + one event)
// and one -human-debug.ndjson file, then:
[Fact]
public async Task ReflectsPlayTracesOnlyAndWritesPlaybookLedgerAndInbox()
{
    // fake reply: {"summary":"s","lessons":[{"id":"","verdict":"new","text":"Hide when spotted","evidence":"t=2","reflex":{"when":"event:guard.spotted","do":"hide_nearest","args":{}}}]}
    // config.Reflector.MinConfirmations = 1 so the first trace already proposes.
    int code = await reflector.RunAsync(root, null, false, CancellationToken.None);
    Assert.Equal(0, code);
    Assert.Single(client.Requests);                                   // debug trace skipped
    Assert.Contains("# Session", client.Requests[0].User);
    Assert.Contains("Game: G", client.Requests[0].User);
    Assert.Equal("L1", PlaybookStore.Load(root).Lessons[0].Id);
    Assert.Contains("x-human-play.ndjson", ReflectedLedger.Load(root));
    Assert.Single(Directory.GetFiles(Path.Combine(root, "AgentPlay", "Inbox"), "reflex-L1-*.json"));
    Assert.Equal(0, await reflector.RunAsync(root, null, false, CancellationToken.None));
    Assert.Single(client.Requests);                                   // already reflected
}

[Fact]
public async Task InvalidAnswerIsRetriedOnceThenFails()
{
    // two replies "not json" → exit 4, ledger untouched, second request contains "Your previous answer was rejected"
}

[Fact]
public async Task NonRetryableErrorStops()
{
    // client throws ModelCallException("limit", null, retryable: false) → exit 4, one request
}

[Fact]
public async Task CorruptPlaybookExitsTwo() { /* write "{" to playbook.json → exit 2, no request */ }
```

Write these three bodies fully when implementing (same fixture helper), following the first test.

RunnerOptions tests:

```csharp
[Fact]
public void ReflectCommandParses()
{
    var o = RunnerOptions.Parse(new[] { "reflect", "--project", "C:/g", "--trace", "a.ndjson", "--trace", "b.ndjson", "--force" });
    Assert.Equal("reflect", o.Command);
    Assert.Equal("C:/g", o.ProjectDir);
    Assert.Equal(new[] { "a.ndjson", "b.ndjson" }, o.Traces);
    Assert.True(o.Force);
}

[Fact]
public void ReflectNeedsProject() => Assert.Throws<ArgumentException>(() => RunnerOptions.Parse(new[] { "reflect" }));

[Fact]
public void NoReflectFlag() => Assert.True(RunnerOptions.Parse(new[] { "--brain", "opus", "--no-reflect" }).NoReflect);
```

- [ ] **Step 3:** run, fail.
- [ ] **Step 4: Implement** the classes per the interfaces and rules above. `RunnerOptions.Parse`: if `args.Length > 0 && args[0] == "reflect"`, set `Command` and parse `--project`, `--trace` (repeatable), `--force` from index 1; require `--project`; reject `--brain`/`--mode` with reflect. Update `Usage` with a second line `AgentPlay.Runner reflect --project <dir> [--trace <file>]... [--force]`.

`Program.Main` (top, after parsing):

```csharp
if (options.Command == "reflect")
{
    var config = LoadConfig(options);   // extract the existing runner.json load + overrides + Validate into a helper
    if (config == null) return 2;
    return await new Reflector(MakeClient(config), config, Log).RunAsync(options.ProjectDir, options.Traces, options.Force, CancellationToken.None);
}
```

`MakeClient(config)` is the existing backend selection extracted into a helper. After a brain run (end of `Main`, before returning):

```csharp
if (brain != null && !options.NoReflect && session.Hello?.SessionTag == "play" && session.Hello.ProjectRoot != null)
{
    Log("session tag is play: learning from it");
    var config = LoadConfig(options);
    if (config != null)
        await new Reflector(MakeClient(config), config, Log).RunAsync(session.Hello.ProjectRoot, null, false, CancellationToken.None);
}
```

The Reflector reads the trace Unity has been writing; Unity flushes every line (`AutoFlush`), so the finished part is on disk when the runner gets `game over`. If Unity is still in Play mode the trace grows later; the ledger then holds a partial trace (acceptable; the session already ended).

`runner.json`: add `"reflector": { "effort": "high", "min_confirmations": 3, "max_digest_chars": 60000, "max_traces_per_run": 5 }`.

- [ ] **Step 5:** `dotnet test` pass; `--reflector-smoke` prints valid output.
- [ ] **Step 6: Commit** `feat(runner): reflect command (trace digest, Opus reflector, playbook merge, reflex proposals)`

---

### Task 10: AgentPlay window: Learning and Reflexes

**Files:**
- Create: `Packages/com.agentplay.core/Editor/ReflectLauncher.cs`
- Modify: `Editor/AgentPlayWindow.cs`
- Test: `Tests/EditMode/ReflectLauncherTests.cs`

**Interfaces:**
- Consumes: `ReflexBook` (Task 5), `AgentPlaySettings.RunnerPath`.
- Produces: `ReflectLauncher.BuildStartInfo(string runnerPath, string projectRoot) : ProcessStartInfo` (static, testable: `dotnet` + `"<dll>" reflect --project "<root>"`; when `runnerPath` is a folder, use `<folder>/AgentPlay.Runner.dll`), `ReflectLauncher.Start(string runnerPath, string projectRoot) : string` (returns an error string or null), `ReflectLauncher.IsRunning`, `LastExitCode` (int?), `LastSummary` (last log line containing "reflect done" or the last line), `LogPath`; `ReflectLauncher.CountPendingPlayTraces(string projectRoot) : int` (play-tagged trace files not in `reflected.json`; tag read from the file name suffix `-play.ndjson` or `-play-<n>.ndjson`).

- [ ] **Step 1: Failing tests**

```csharp
[Test]
public void StartInfoRunsReflectForTheProject()
{
    var info = ReflectLauncher.BuildStartInfo("C:/r/AgentPlay.Runner.dll", "C:/game");
    Assert.AreEqual("dotnet", info.FileName);
    StringAssert.Contains("\"C:/r/AgentPlay.Runner.dll\" reflect --project \"C:/game\"", info.Arguments);
}

[Test]
public void FolderPathFindsTheDll()
{
    var dir = Path.Combine(Path.GetTempPath(), "agentplay-runner-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "AgentPlay.Runner.dll"), "");
    StringAssert.Contains(Path.Combine(dir, "AgentPlay.Runner.dll"), ReflectLauncher.BuildStartInfo(dir, "C:/game").Arguments);
}

[Test]
public void StartWithoutRunnerExplainsWhereToSetIt()
{
    StringAssert.Contains("Runner path", ReflectLauncher.Start("", "C:/game"));
}

[Test]
public void CountsPendingPlayTraces()
{
    var root = Path.Combine(Path.GetTempPath(), "agentplay-pending-" + Guid.NewGuid().ToString("N"));
    var traces = Path.Combine(root, "AgentPlay", "Traces");
    Directory.CreateDirectory(traces);
    File.WriteAllText(Path.Combine(traces, "20261013-100000-human-play.ndjson"), "");
    File.WriteAllText(Path.Combine(traces, "20261013-100100-human-play-2.ndjson"), "");
    File.WriteAllText(Path.Combine(traces, "20261013-100200-human-debug.ndjson"), "");
    File.WriteAllText(Path.Combine(root, "AgentPlay", "reflected.json"), "[\"20261013-100000-human-play.ndjson\"]");
    Assert.AreEqual(1, ReflectLauncher.CountPendingPlayTraces(root));
}
```

- [ ] **Step 2:** fail. **Step 3: Implement.** `Start` uses `Process` with redirected stdout/stderr appended to `AgentPlay/Runs/reflect-<yyyyMMdd-HHmmss>.log` via `OutputDataReceived`/`ErrorDataReceived` (thread-safe append with a lock), `EnableRaisingEvents`, `Exited` sets `LastExitCode` and `IsRunning = false` (read on the main thread by the window's `OnInspectorUpdate`). `Start` returns `"Set the Runner path (AgentPlay.Runner.dll) first"` when the path is empty or the dll does not exist, `"Already running"` when running.

Window additions after the existing status block (keep `DisabledScope(playing)` around every button):

```csharp
EditorGUILayout.Space();
EditorGUILayout.LabelField("Learning", EditorStyles.boldLabel);
AgentPlaySettings.RunnerPath = EditorGUILayout.TextField(new GUIContent("Runner path", "AgentPlay.Runner.dll or its folder"), AgentPlaySettings.RunnerPath);
EditorGUILayout.LabelField("Play sessions to learn", ReflectLauncher.CountPendingPlayTraces(ProjectRoot).ToString());
using (new EditorGUI.DisabledScope(playing || ReflectLauncher.IsRunning))
{
    if (GUILayout.Button(ReflectLauncher.IsRunning ? "Learning..." : "Learn from play sessions"))
        _launchError = ReflectLauncher.Start(AgentPlaySettings.RunnerPath, ProjectRoot);
    if (GUILayout.Button("Open playbook")) EditorUtility.OpenWithDefaultApp(Path.Combine(ProjectRoot, "AgentPlay", "playbook.md"));
}
if (_launchError != null) EditorGUILayout.HelpBox(_launchError, MessageType.Error);
if (ReflectLauncher.LastExitCode.HasValue)
    EditorGUILayout.LabelField("Last run", "exit " + ReflectLauncher.LastExitCode + ": " + ReflectLauncher.LastSummary);

EditorGUILayout.Space();
EditorGUILayout.LabelField("Reflexes", EditorStyles.boldLabel);
var book = _book;
if (book.LoadError != null) EditorGUILayout.HelpBox(book.LoadError, MessageType.Error);
foreach (var status in new[] { ReflexStatuses.Proposed, ReflexStatuses.NeedsReview, ReflexStatuses.Active, ReflexStatuses.Suspended, ReflexStatuses.Rejected })
    foreach (var r in book.Reflexes.Where(x => x.Status == status).ToList())
        DrawReflex(book, r, playing);
```

```csharp
void DrawReflex(ReflexBook book, Reflex r, bool playing)
{
    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
    {
        EditorGUILayout.LabelField(r.Id + "  [" + r.Status + "]", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(r.Text ?? "", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("when " + r.When + " do " + r.Do + "(" + (r.Args == null ? "" : r.Args.ToString(Newtonsoft.Json.Formatting.None)) + ")", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField("fired " + r.Stats.Fired + ", good " + r.Stats.Good + ", bad " + r.Stats.Bad + (string.IsNullOrEmpty(r.Note) ? "" : " — " + r.Note), EditorStyles.miniLabel);
        using (new EditorGUI.DisabledScope(playing))
        using (new EditorGUILayout.HorizontalScope())
        {
            string version = CurrentVersion();
            if ((r.Status == ReflexStatuses.Proposed || r.Status == ReflexStatuses.NeedsReview) && GUILayout.Button("Approve")) book.Approve(r.Id, version);
            if (r.Status == ReflexStatuses.Active && GUILayout.Button("Disable")) book.Disable(r.Id);
            if (r.Status == ReflexStatuses.Suspended && GUILayout.Button("Re-enable")) book.Reenable(r.Id, version);
            if (r.Status != ReflexStatuses.Rejected && r.Status != ReflexStatuses.Active && GUILayout.Button("Reject")) book.Reject(r.Id);
        }
    }
}
```

`CurrentVersion()`: `AgentRegistry.ToHello(AgentRegistry.Build(TypeScanner.FindAnnotatedTypes()), Application.productName).Version`, cached per window enable (registry build is ~10 ms). `_book` is `AgentPlaySessionController.Book` while playing, else a `ReflexBook.Load(ProjectRoot)` reloaded in `OnFocus()` (also calling `ImportInbox()`) and when `ReflectLauncher` finishes (detected in `OnInspectorUpdate` by `LastExitCode` changing). `ProjectRoot` = `Directory.GetParent(Application.dataPath).FullName`.

- [ ] **Step 4:** `bash Tools/unity-tests.sh` pass (window compiles; logic tested).
- [ ] **Step 5: Commit** `feat(editor): learning and reflex review sections in the AgentPlay window`

---

### Task 11: Heist demo for M3

**Files:**
- Modify: `Demos/Heist/Assets/Heist/HeistGame.cs` (version `m3`, add keyboard + pilot components), `HeistPlayer.cs` (manual control methods, `hide_nearest`), `Demos/Heist/Assets/Editor/HeistAutoPlay.cs` (env hooks), `Demos/Heist/AgentPlay/brief.md` (hide_nearest), `Tools/verify_m2a.py` (version m3, 6 actions), `Tools/run-heist-opus.sh` (unchanged)
- Create: `Demos/Heist/Assets/Heist/HeistKeyboard.cs`, `Demos/Heist/Assets/Heist/HeistPilot.cs`

**Interfaces:**
- Produces:
  - `HeistPlayer.ManualMove(Vector3 direction)` (moves at `Speed * Time.deltaTime` along the flat direction, clamps to the room x∈[-9.5,9.5], z∈[-5.5,5.5], stands up), `HeistPlayer.SetCrouched(bool)`, `HeistPlayer.PickNearest() : bool`, `HeistLevel.NearestCover(Vector3) : string`.
  - Action `hide_nearest` ("Walk to the nearest cover spot and crouch behind its box").
  - `HeistPilot` (MonoBehaviour) created by `HeistGame.BuildLevel` when `Environment.GetEnvironmentVariable("AGENTPLAY_HEIST_PILOT") == "1"`.
  - `HeistAutoPlay.Run` always sets `AgentPlay.Editor.AgentPlaySettings.SessionTag` from `AGENTPLAY_SESSION_TAG` (`play` or `debug`), defaulting to `debug` when the variable is missing, because the tag is stored in EditorPrefs and would otherwise leak `play` into later E2E runs (which would then call the real Reflector) and `AGENTPLAY_APPROVE_PROPOSED=1` (`var book = ReflexBook.Load(root); book.ImportInbox(); foreach proposed: book.Approve(id, "m3")`, logging `[Heist] approved reflex <id>`).

- [ ] **Step 1:** Implement player methods:

```csharp
[AgentAction("hide_nearest", "Walk to the nearest cover spot and crouch behind its box")]
public AgentActionStatus HideNearest(AgentActionContext ctx)
{
    if (HeistGame.Result != null) return AgentActionStatus.Done;
    if (ctx.IsFirstFrame) { Crouched = false; _hideTarget = HeistLevel.NearestCover(transform.position); }
    if (!MoveTowards(HeistLevel.Zones[_hideTarget])) return AgentActionStatus.Running;
    Crouched = true;
    return AgentActionStatus.Done;
}

/// <summary>Human control (keyboard or the E2E pilot): one frame of walking.</summary>
public void ManualMove(Vector3 direction)
{
    direction.y = 0f;
    if (direction.sqrMagnitude < 1e-6f) return;
    Crouched = false;
    var p = transform.position + direction.normalized * Speed * Time.deltaTime;
    transform.position = new Vector3(Mathf.Clamp(p.x, -9.5f, 9.5f), p.y, Mathf.Clamp(p.z, -5.5f, 5.5f));
}

public void SetCrouched(bool crouched) => Crouched = crouched;

public bool PickNearest()
{
    foreach (var item in HeistGame.Instance.Items)
        if (item.Value.activeSelf && HeistLevel.FlatDistance(transform.position, item.Value.transform.position) <= PickRange)
            return Pick(item.Key) == AgentActionStatus.Done;
    return false;
}
```

`HeistLevel.NearestCover`: the `cover_*` zone with the smallest flat distance.

`HeistKeyboard` (added to the thief in `BuildLevel`): in `Update`, when `HeistGame.Result == null`: direction from `Input.GetKey` W/A/S/D and arrows (x = right − left, z = up − down) → `ManualMove`; `Input.GetKeyDown(KeyCode.C)` → `SetCrouched(!Crouched)`; `Input.GetKeyDown(KeyCode.E)` → `PickNearest()`. No allocations.

`HeistPilot` (state machine, runs in `Update`, uses only `ManualMove`, `SetCrouched`, `PickNearest`, and reads guards):

```csharp
public sealed class HeistPilot : MonoBehaviour
{
    static readonly string[] Route = { "item_a", "item_b", "item_c", "exit" };
    int _goal;
    bool _hiding;
    string _cover;
    float _hiddenFor;

    void Update()
    {
        var game = HeistGame.Instance;
        if (game == null || HeistGame.Result != null || _goal >= Route.Length) return;
        var player = game.Player;
        var guard = game.GuardList[0];

        if (!_hiding && guard.SeesPlayer)
        {
            _hiding = true;
            _hiddenFor = 0f;
            _cover = HeistLevel.NearestCover(player.transform.position);
        }
        if (_hiding)
        {
            var c = HeistLevel.Zones[_cover];
            if (HeistLevel.FlatDistance(player.transform.position, c) > 0.1f) { player.ManualMove(c - player.transform.position); return; }
            player.SetCrouched(true);
            bool walkingAway = (guard.MovingNorth && guard.Z > player.transform.position.z) || (!guard.MovingNorth && guard.Z < player.transform.position.z);
            _hiddenFor = !guard.SeesPlayer && !guard.Chasing && walkingAway && guard.Distance > 5f ? _hiddenFor + Time.deltaTime : 0f;
            if (_hiddenFor < 1f) return;
            _hiding = false;
            player.SetCrouched(false);
        }

        string goal = Route[_goal];
        var target = HeistLevel.Zones[goal];
        if (HeistLevel.FlatDistance(player.transform.position, target) > (goal == "exit" ? 0.3f : 1.0f))
        {
            player.ManualMove(target - player.transform.position);
            return;
        }
        if (goal == "exit" || player.PickNearest()) _goal++;
    }
}
```

`brief.md`: add a line "- hide_nearest walks to the closest cover spot and crouches."

`verify_m2a.py`: `version=m2` → `version=m3`, `7 observes, 5 actions` → `7 observes, 6 actions`.

- [ ] **Step 2:** `bash Tools/e2e-m2a.sh` → ALL CHECKS PASSED (script brain still wins with version m3).
- [ ] **Step 3:** Pilot smoke: run the Heist AutoPlay with `AGENTPLAY_HEIST_PILOT=1 AGENTPLAY_SESSION_TAG=play` and no runner (`AGENTPLAY_AUTOPLAY_SECONDS=180`); expect `[Heist] result=won` (or `lost`, both acceptable for learning) and a trace `AgentPlay/Traces/*-human-play.ndjson` whose header has `hello` and whose snapshots are 0.5 s apart. If the pilot never ends (stuck), fix the state machine until it finishes in under 120 s game time in 3 consecutive runs.
- [ ] **Step 4: Commit** (with Unity-generated `.meta` for the two new scripts) `feat(heist): m3 demo: hide_nearest, keyboard control, E2E pilot and AutoPlay hooks`

---

### Task 12: Deterministic reflex E2E

**Files:**
- Create: `Demos/Heist/AgentPlay/fixtures/reflexes-active.json`, `Demos/Heist/AgentPlay/scripts/reflex.json`, `Tools/e2e-m3-reflex.sh`, `Tools/verify_m3_reflex.py`

- [ ] **Step 1:** Fixture:

```json
{
  "version": 1,
  "policy": { "eval_window_s": 10, "min_fires": 5, "max_fail_rate": 0.3 },
  "reflexes": [
    {
      "id": "R-fixture",
      "lesson_id": "",
      "text": "When a guard spots you, hide behind the nearest cover",
      "when": "event:guard.spotted",
      "do": "hide_nearest",
      "args": {},
      "cooldown_s": 1,
      "status": "active",
      "game_version": "m3",
      "stats": { "fired": 0, "good": 0, "bad": 0 },
      "note": ""
    }
  ]
}
```

Script `reflex.json`: the four plans of `win.json` with every `event:guard.spotted` rule removed, and plan 2 changed so the thief walks to `item_b` while the guard comes north (exposure). Start from:

```json
{ "id": "x2", "intent": "Walk to item_b while the guard comes north, so it spots the thief",
  "steps": [
    { "do": "wait", "args": { "seconds": 30 }, "until": "any(guards.moving_north) && min(guards.z) > -3 && min(guards.z) < -1" },
    { "do": "goto", "args": { "zone": "item_b" } },
    { "do": "pick", "args": { "item": "item_b" } },
    { "do": "hide", "args": { "zone": "cover_1" } }
  ], "rules": [], "timeout_s": 90 }
```

then keep `s3`/`s4` of `win.json` (without spotted rules) to finish. Tune the wait window during Step 3 if the guard does not spot the thief or the thief gets caught; the requirement is: at least one `guard.spotted` handled by the reflex, and the run is won.

- [ ] **Step 2:** `Tools/e2e-m3-reflex.sh` (`REFLEXES` env var overrides the fixture path; used by Task 13):

```bash
#!/usr/bin/env bash
# M3: an active reflex handles guard.spotted in-game; no model calls. Demos/Heist must not be open in another Editor.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
PROJECT="$ROOT/Demos/Heist"
LOGS="$ROOT/.e2e/heist-reflex"
FIXTURE="${REFLEXES:-$PROJECT/AgentPlay/fixtures/reflexes-active.json}"
mkdir -p "$LOGS"
rm -rf "$PROJECT/AgentPlay/Traces" "$PROJECT/AgentPlay/Telemetry" "$LOGS"/*.log
cp "$PROJECT/AgentPlay/reflexes.json" "$LOGS/reflexes.backup.json" 2>/dev/null || true
cp "$FIXTURE" "$PROJECT/AgentPlay/reflexes.json"

dotnet build "$ROOT/Runner/AgentPlay.Runner.csproj" -c Debug -v q || exit 1
dotnet "$ROOT/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll" \
  --brain script "$(cygpath -w "$PROJECT/AgentPlay/scripts/reflex.json")" --idle-exit 20 --no-reflect \
  > "$LOGS/runner.log" 2>&1 < /dev/null &
RUNNER_PID=$!
AGENTPLAY_SESSION_TAG=debug AGENTPLAY_AUTOPLAY_SECONDS=240 "$UNITY_EXE" -projectPath "$(cygpath -m "$PROJECT")" \
  -executeMethod HeistDemo.HeistAutoPlay.Run -logFile "$(cygpath -m "$LOGS/autoplay.log")"
UNITY_CODE=$?
wait "$RUNNER_PID"
RUNNER_CODE=$?
cp "$PROJECT/AgentPlay/reflexes.json" "$LOGS/reflexes.after.json"
python -I "$ROOT/Tools/verify_m3_reflex.py" "$PROJECT" "$LOGS" "$RUNNER_CODE" "$UNITY_CODE"
CODE=$?
if [ -f "$LOGS/reflexes.backup.json" ]; then mv "$LOGS/reflexes.backup.json" "$PROJECT/AgentPlay/reflexes.json"; else rm -f "$PROJECT/AgentPlay/reflexes.json"; fi
exit $CODE
```

Note: the runner log of Heist E2E normally goes to `Demos/Heist/Logs`; M3 E2E logs go to `$ROOT/.e2e/` (git-ignored) so the Bash deny rule on `Logs/` paths does not block reading them.

`Tools/verify_m3_reflex.py` (args PROJECT LOGS RUNNER_EXIT UNITY_EXIT) checks:
- runner exit 0 and Unity exit 0 (won);
- runner log has a line matching `reflex R-\S+ t=\S+ handled .*guard\.spotted`;
- runner log has `reflex_end R-` with `done`;
- for each such reflex line's `t`, there is no `paused t=<same t> reason=event` line;
- telemetry JSON in `PROJECT/AgentPlay/Telemetry/` has `important_events_handled_by_reflex >= 1` and `model_calls == 0`;
- `LOGS/reflexes.after.json` (copied before the original file is restored) has `stats.fired >= 1` for the reflex;
- trace has a `reflex` line and a header with `hello`;
- Unity log has `reflex(es) active` and no `NullReferenceException`.
Print PASS/FAIL lines and `ALL CHECKS PASSED` like `verify_m2a.py`.

- [ ] **Step 3:** run `bash Tools/e2e-m3-reflex.sh` until ALL CHECKS PASSED (tune only the script's wait window, never the checks).
- [ ] **Step 4: Commit** `test(e2e): deterministic reflex run on Heist (no model calls)`

---

### Task 13: Learning E2E with the real model, verification notes, review, merge

**Files:**
- Create: `Tools/e2e-m3-learn.sh`, `Tools/verify_m3_learn.py`, `docs/notes/m3-verification.md`
- Modify: spec status line, memory file

- [ ] **Step 1:** `Tools/e2e-m3-learn.sh` (costs subscription usage: 3 reflector calls + up to 1 Opus game):

```bash
#!/usr/bin/env bash
# M3: three pilot (human-path) play sessions → reflect with the real model → lesson and reflex proposal →
# approve → the learned reflex handles guard.spotted in the deterministic scenario → one Opus game for telemetry.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
PROJECT="$ROOT/Demos/Heist"
AP="$PROJECT/AgentPlay"
LOGS="$ROOT/.e2e/heist-learn"
mkdir -p "$LOGS"
rm -rf "$AP/Traces" "$AP/Inbox" "$AP/Telemetry" "$AP/playbook.json" "$AP/playbook.md" "$AP/reflected.json" "$AP/reflexes.json" "$LOGS"/*
dotnet build "$ROOT/Runner/AgentPlay.Runner.csproj" -c Debug -v q || exit 1
RUNNER="$ROOT/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll"

for i in 1 2 3; do
  AGENTPLAY_HEIST_PILOT=1 AGENTPLAY_SESSION_TAG=play AGENTPLAY_AUTOPLAY_SECONDS=180 "$UNITY_EXE" -projectPath "$(cygpath -m "$PROJECT")" \
    -executeMethod HeistDemo.HeistAutoPlay.Run -logFile "$(cygpath -m "$LOGS/pilot-$i.log")"
  echo "pilot $i: Unity exit $?"
done

dotnet "$RUNNER" reflect --project "$(cygpath -w "$PROJECT")" > "$LOGS/reflect.log" 2>&1
echo "reflect exit $?"
python -I "$ROOT/Tools/verify_m3_learn.py" "$PROJECT" "$LOGS" || exit 1

# Approve the learned reflex (AutoPlay hook) and rerun the deterministic scenario with it.
LEARNED="$LOGS/reflexes-learned.json"
AGENTPLAY_APPROVE_PROPOSED=1 AGENTPLAY_AUTOPLAY_SECONDS=1 "$UNITY_EXE" -batchmode -projectPath "$(cygpath -m "$PROJECT")" \
  -executeMethod HeistDemo.HeistAutoPlay.ApproveOnly -logFile "$(cygpath -m "$LOGS/approve.log")"
cp "$AP/reflexes.json" "$LEARNED"
REFLEXES="$LEARNED" bash "$ROOT/Tools/e2e-m3-reflex.sh" || exit 1

# One Opus game with the learned reflex active, for the telemetry in the notes.
dotnet "$RUNNER" --brain opus --idle-exit 20 --no-reflect > "$LOGS/opus-runner.log" 2>&1 < /dev/null &
RP=$!
cp "$LEARNED" "$AP/reflexes.json"
AGENTPLAY_SESSION_TAG=debug AGENTPLAY_AUTOPLAY_SECONDS=900 "$UNITY_EXE" -projectPath "$(cygpath -m "$PROJECT")" \
  -executeMethod HeistDemo.HeistAutoPlay.Run -logFile "$(cygpath -m "$LOGS/opus-autoplay.log")"
wait $RP
echo "opus runner exit $?"
ls -1 "$AP/Telemetry" | tail -1
```

Add `HeistAutoPlay.ApproveOnly()` (approve hook without entering Play mode; exits 0 after approving, 1 if nothing was approved). The deterministic scenario run inside this script uses the learned reflex file; `e2e-m3-reflex.sh` verifies `R-\S+` ids, so a learned id like `R-L1` matches.

`Tools/verify_m3_learn.py` (args PROJECT LOGS) checks:
- three traces `*-human-play*.ndjson` exist, each with header `who == human`, `tag == play`, a `hello`, and no `control` line with `who == agent`;
- `reflect.log` has `reflect done: 3 trace(s)`;
- `playbook.json` has at least one lesson whose `sources` contains one of those trace names (criterion 1);
- `playbook.md` exists;
- `reflected.json` lists the three traces;
- at least one `Inbox/reflex-*.json` (criterion 3, first half) that parses and has `status == proposed`.
Print the lessons (id, confirmations, text) for the notes.

- [ ] **Step 2:** Run `bash Tools/e2e-m3-learn.sh`. If no reflex is proposed because lessons did not reach 3 confirmations (risk 1): read `reflect.log` and the playbook; tune `Reflector.Instructions` (generic wording only), record the change as a ruling in the ledger, clear and rerun. At most 3 tuning rounds; then report the result honestly.
- [ ] **Step 3:** Full regression: `dotnet test AgentPlay.sln`, `bash Tools/unity-tests.sh`, `bash Tools/check-generic.sh`, `bash Tools/e2e-m2a.sh`, `bash Tools/e2e-m2c.sh script` (TileThieves must be closed; the junction is still linked there), `bash Tools/e2e-m3-reflex.sh`.
- [ ] **Step 4:** Write `docs/notes/m3-verification.md` (Vietnamese, same structure as m2b): tests counts, each "xong" criterion with evidence (playbook lessons with sources, proposal id, reflex E2E checks, Opus telemetry numbers: calls, handled_by_reflex, wait median/p90), rulings, known limits. Mark the spec status "Đã triển khai". Commit.
- [ ] **Step 5:** Fresh review: dispatch one reviewer agent on `git diff main...feat/m3-learning` (focus: executor reflex semantics, file ownership/atomic writes, reflector merge, window state). Fix confirmed findings with tests; rerun affected suites; commit.
- [ ] **Step 6:** Merge `feat/m3-learning` into `main` locally (`git merge --no-ff`), run `dotnet test` on main, update memory (`agentplay-project.md`: M3 status, new commands).
