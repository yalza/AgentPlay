# AgentPlay M4b: `agentplay-adapt` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Claude Code skill that writes a working AgentPlay adapter for an existing Unity game, plus the generic tools it needs (AutoPlay, `runner check`, local-only linking), proven on PetRPG (Farm Pal): Opus completes the first 3 missions from a fresh save.

**Architecture:** Three generic pieces land in the package and tools: `AgentPlay.Editor.AutoPlay` (enter Play mode, quit on `game.won`/`game.lost`/deadline, run game-supplied `IAgentAutoPlayHook`s), `AgentPlay.Runner check` (pure `AdapterCheck` over `hello` plus two snapshots), and `link-agentplay.ps1 -LocalOnly/-Unlink/-Purge` (everything excluded through `.git/info/exclude`). The skill in `.claude/skills/agentplay-adapt/` drives a 5-step workflow that uses them. A dry run on a TileThieves `main` worktree precedes the PetRPG run.

**Tech Stack:** C# (.NET 9 runner, xUnit; Unity 6000.0.77f1 Editor code, NUnit EditMode), PowerShell 5.1, Git Bash, Python 3 (`python -I`).

**Spec:** `docs/superpowers/specs/2026-10-17-agentplay-m4b-adapt-design.md`

## Global Constraints

- Unity `6000.0.77f1` at `/e/6000.0.77f1/Editor/Unity.exe` (`UNITY_EXE` overrides). .NET SDK 9.
- Adapter folder is always `Assets/AgentPlayAdapter/` (both shapes). Assembly-CSharp shape guards every file with `#if AGENTPLAY`; the symbol comes from `Assets/csc.rsp` whose whole content is exactly `-define:AGENTPLAY` (ruling in Task 8: csc.rsp has no comment syntax).
- Nothing the skill adds may change `git status` of the game repo. Use `.git/info/exclude`, never the game's `.gitignore`, in `-LocalOnly` mode. Exclude block markers: `# AgentPlay begin` / `# AgentPlay end`.
- AutoPlay exit codes: 0 won, 1 deadline, 2 hook failed, 3 lost. `check` exit codes: 0 all pass, 1 a check failed, 6 no connection/hello within `--idle-exit` (default 30 s for `check`).
- Action rule for adapters: drive the player's own input path (joystick value, virtual gamepad, the method a UI button calls); no teleports, no direct currency/progress edits, except test setup inside a hook.
- PetRPG (`E:\PetRPG`) is a team repo on branch `ToanNN/Working`: never commit, never edit tracked files, never touch `.gitignore`. Its save lives in `C:\Users\Admin\AppData\LocalLow\3HP\Farm Pal\` (folders `LocalStorage`, `Saves`, `Unity`) and possibly Editor PlayerPrefs under `HKCU\Software\Unity\UnityEditor\3HP\Farm Pal`; both must be byte-identical after every run.
- Notes in `docs/notes/` are Vietnamese; code, comments and skill files are English.
- Before any real-model batch: check C: free space, probe quota with `echo OK | claude -p --model claude-haiku-5-5`, and kill leftover `Unity.exe`/`AgentPlay.Runner.dll` processes (TaskStop does not stop the bash loop).

## Review Focus

1. Unity crashes or is killed between `BeforePlay` and `AfterPlay`: the next run must restore the user's save before doing anything else (stale backup present means restore first). Pinned by the skill's hook template (Task 6) and exercised in Task 8 step 3.
2. `game.won` arrives twice, or `game.lost` after `game.won`: AutoPlay keeps the first result. Pinned by `AutoPlayRulesTests.FirstResultWins` (Task 3).
3. A project that already has its own `Assets/csc.rsp`: `-LocalOnly` and `-Unlink` must never overwrite or delete it, and the skill must stop and ask. Pinned by the `test-link-agentplay.sh` case "foreign csc.rsp kept" (Task 4) and `SKILL.md` step 3.
4. An observe that is legitimately null at the start (no active mission yet) but valid later: `check` passes it if any snapshot has a value. Pinned by `AdapterCheckTests.ObserveNullOnlyInFirstSnapshotPasses` (Task 2).
5. `-Unlink` run twice, or on a project never linked: succeeds and changes nothing the second time. Pinned by the `test-link-agentplay.sh` case "unlink is idempotent" (Task 4).

---

### Task 1: PetRPG feasibility probe (no code)

Answers spec risks 1, 4 and 5 before anything is built. Uses the PetRPG Editor that is already open (MCP for Unity is installed in PetRPG: `Assets/MCPForUnity`), never batchmode on a team project unless step 5 says so.

**Files:**
- Modify: `.superpowers/sdd/2026-10-17-agentplay-m4b-adapt/progress.md` (create; findings ledger)

- [ ] **Step 1: Snapshot the user's save and git state**

```powershell
$save = "C:\Users\Admin\AppData\LocalLow\3HP\Farm Pal"
$probe = "E:\Unity-WorkSpace\AgentPlay\.e2e\petrpg-probe"
New-Item -ItemType Directory -Force $probe | Out-Null
Get-ChildItem $save -Recurse -File | Get-FileHash | Select-Object Hash, Path | Export-Csv "$probe\save-before.csv" -NoTypeInformation
Copy-Item $save "$probe\save-backup" -Recurse -Force
reg export "HKCU\Software\Unity\UnityEditor\3HP\Farm Pal" "$probe\prefs-backup.reg" /y
git -C E:\PetRPG status --porcelain > "$probe\git-before.txt"
```

Expected: the CSV lists the save files; `reg export` either succeeds or reports the key is missing (record which).

- [ ] **Step 2: Check the define symbol is free**

Run: `grep -rn "AGENTPLAY" /e/PetRPG/Assets /e/PetRPG/ProjectSettings --include=*.cs --include=*.asset --include=*.rsp | head`
Expected: no output. If anything matches, record it and use `AGENTPLAY_ADAPTER` everywhere instead of `AGENTPLAY` (update Global Constraints in this plan before Task 6).

- [ ] **Step 3: Enter Play mode from a fresh save**

Move the save folders aside (do not delete): `Rename-Item "$save\LocalStorage" LocalStorage.agentplay-probe` and the same for `Saves`. Through the Unity MCP tools on the open PetRPG Editor: open the first scene in Build Settings, enter Play mode, wait 30 s, read the console (errors and warnings), take a Game view screenshot, exit Play mode.

Record in the ledger: the start scene path; whether the game reached a playable state (player visible, joystick, first mission arrow) or stopped on a consent/ad/login screen; every exception in the console with its source.

- [ ] **Step 4: Restore and verify the save**

```powershell
Remove-Item "$save\LocalStorage","$save\Saves" -Recurse -Force -ErrorAction SilentlyContinue
Rename-Item "$save\LocalStorage.agentplay-probe" LocalStorage; Rename-Item "$save\Saves.agentplay-probe" Saves
if (Test-Path "$probe\prefs-backup.reg") { reg import "$probe\prefs-backup.reg" }
Get-ChildItem $save -Recurse -File | Get-FileHash | Select-Object Hash, Path | Export-Csv "$probe\save-after.csv" -NoTypeInformation
Compare-Object (Import-Csv "$probe\save-before.csv") (Import-Csv "$probe\save-after.csv") -Property Hash, Path
git -C E:\PetRPG status --porcelain | Out-File "$probe\git-after.txt"
Compare-Object (Get-Content "$probe\git-before.txt") (Get-Content "$probe\git-after.txt")
```

Expected: both `Compare-Object` calls print nothing. Note: the Unity folder under the save path holds Unity's own analytics/cache; if only files in `Unity\` differ, record that and exclude `Unity\` from the hash comparison in Task 8.

- [ ] **Step 5: Measure batchmode cost**

Time a batchmode compile of a throwaway worktree is NOT possible for PetRPG (team repo, huge Library). Instead record the open Editor's last import time from `%LOCALAPPDATA%\Unity\Editor\Editor.log` (`Asset Pipeline Refresh` lines). Decision rule for Task 8: if a refresh after a script change takes over 3 minutes, the skill's step-4 checks run through the open Editor (MCP: `refresh`, read console, play) instead of batchmode.

- [ ] **Step 6: Record the findings**

Write `.superpowers/sdd/2026-10-17-agentplay-m4b-adapt/progress.md` with: start scene, blockers found (and how a hook could clear them without touching game code), save locations and whether PlayerPrefs are used, the define symbol chosen, batchmode or Editor route. If the game cannot reach a playable state from a fresh save without editing game code, STOP and report to the user before Task 2.

---

### Task 2: `AgentPlay.Runner check`

**Files:**
- Create: `Runner/AdapterCheck.cs`
- Create: `Runner/CheckCommand.cs`
- Modify: `Runner/RunnerOptions.cs` (Usage string; `Parse` dispatch; new `ParseCheck`)
- Modify: `Runner/Program.cs:39` (dispatch `check` next to `reflect`)
- Test: `Runner.Tests/AdapterCheckTests.cs` (create), `Runner.Tests/RunnerOptionsTests.cs`

**Interfaces:**
- Produces: `AdapterCheck.Run(HelloMessage hello, IReadOnlyList<JObject> snapshots, IReadOnlyList<string> unityErrors) -> List<CheckItem>`; `CheckItem { string Name; bool Passed; string Detail; }`; `CheckCommand.RunAsync(RunnerOptions options, Action<string> log) -> Task<int>`; `RunnerOptions.Command == "check"`.

- [ ] **Step 1: Write the failing tests**

`Runner.Tests/AdapterCheckTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using AgentPlay.Runner;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class AdapterCheckTests
    {
        static HelloMessage GoodHello()
        {
            var h = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G", Version = "1", Brief = "# Brief\nWin by X." };
            h.Observes.Add(new ObserveInfo { Path = "player.pos", ValueType = "vector3" });
            h.Observes.Add(new ObserveInfo { Path = "mission.id", ValueType = "string" });
            h.Actions.Add(new ActionInfo { Name = "idle", Safe = true });
            h.Actions.Add(new ActionInfo { Name = "goto", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "target", ValueType = "string" } } });
            h.Events.Add(new EventInfo { Name = "game.won", Important = true, Outcome = "good" });
            return h;
        }

        static JObject Snap(object pos, object mission) => new JObject
        {
            ["player.pos"] = pos == null ? JValue.CreateNull() : JToken.FromObject(pos),
            ["mission.id"] = mission == null ? JValue.CreateNull() : JToken.FromObject(mission),
        };

        static List<JObject> TwoGoodSnaps() => new List<JObject> { Snap(new { x = 0, y = 0, z = 0 }, "m1"), Snap(new { x = 1, y = 0, z = 0 }, "m1") };

        static CheckItem Item(List<CheckItem> items, string name) => items.Single(i => i.Name == name);

        [Fact]
        public void GoodAdapterPassesEveryCheck()
        {
            var items = AdapterCheck.Run(GoodHello(), TwoGoodSnaps(), new string[0]);
            Assert.All(items, i => Assert.True(i.Passed, i.Name + ": " + i.Detail));
            Assert.Contains(items, i => i.Name == "observe mission.id has a value");
        }

        [Fact]
        public void ObserveNullInEverySnapshotFails()
        {
            var snaps = new List<JObject> { Snap(new { x = 0 }, null), Snap(new { x = 1 }, null) };
            Assert.False(Item(AdapterCheck.Run(GoodHello(), snaps, new string[0]), "observe mission.id has a value").Passed);
        }

        [Fact]
        public void ObserveNullOnlyInFirstSnapshotPasses()
        {
            var snaps = new List<JObject> { Snap(new { x = 0 }, null), Snap(new { x = 1 }, "m1") };
            Assert.True(Item(AdapterCheck.Run(GoodHello(), snaps, new string[0]), "observe mission.id has a value").Passed);
        }

        [Fact]
        public void ObserveMissingFromSnapshotsFails()
        {
            var snaps = new List<JObject> { new JObject { ["player.pos"] = 1 }, new JObject { ["player.pos"] = 2 } };
            Assert.False(Item(AdapterCheck.Run(GoodHello(), snaps, new string[0]), "observe mission.id has a value").Passed);
        }

        [Fact]
        public void NoSafeActionFails()
        {
            var h = GoodHello();
            h.Actions[0].Safe = false;
            Assert.False(Item(AdapterCheck.Run(h, TwoGoodSnaps(), new string[0]), "exactly one safe action").Passed);
        }

        [Fact]
        public void TwoSafeActionsFail()
        {
            var h = GoodHello();
            h.Actions[1].Safe = true;
            Assert.False(Item(AdapterCheck.Run(h, TwoGoodSnaps(), new string[0]), "exactly one safe action").Passed);
        }

        [Fact]
        public void MissingGameWonFails()
        {
            var h = GoodHello();
            h.Events.Clear();
            Assert.False(Item(AdapterCheck.Run(h, TwoGoodSnaps(), new string[0]), "declares game.won").Passed);
        }

        [Fact]
        public void EmptyBriefFails()
        {
            var h = GoodHello();
            h.Brief = "  \n";
            Assert.False(Item(AdapterCheck.Run(h, TwoGoodSnaps(), new string[0]), "brief is not empty").Passed);
        }

        [Fact]
        public void NoObservesOrNoActionsFail()
        {
            var h = GoodHello();
            h.Observes.Clear();
            h.Actions.Clear();
            var items = AdapterCheck.Run(h, TwoGoodSnaps(), new string[0]);
            Assert.False(Item(items, "has observes").Passed);
            Assert.False(Item(items, "has actions").Passed);
        }

        [Fact]
        public void UnsupportedParameterTypeFails()
        {
            var h = GoodHello();
            h.Actions[1].Params[0].ValueType = "vector3";
            Assert.False(Item(AdapterCheck.Run(h, TwoGoodSnaps(), new string[0]), "action parameter types are supported").Passed);
        }

        [Fact]
        public void UnityErrorFails()
        {
            var item = Item(AdapterCheck.Run(GoodHello(), TwoGoodSnaps(), new[] { "boom" }), "no error from Unity");
            Assert.False(item.Passed);
            Assert.Contains("boom", item.Detail);
        }

        [Fact]
        public void FewerThanTwoSnapshotsFails()
        {
            var items = AdapterCheck.Run(GoodHello(), new List<JObject> { Snap(new { x = 0 }, "m1") }, new string[0]);
            Assert.False(Item(items, "two state snapshots received").Passed);
        }
    }
}
```

Add to `Runner.Tests/RunnerOptionsTests.cs`:

```csharp
        [Fact]
        public void ParsesCheck()
        {
            var o = RunnerOptions.Parse(new[] { "check", "--ports", "47850-47851", "--idle-exit", "12" });
            Assert.Equal("check", o.Command);
            Assert.Equal(47850, o.FirstPort);
            Assert.Equal(47851, o.LastPort);
            Assert.Equal(12.0, o.IdleExitSeconds);
        }

        [Fact]
        public void CheckDefaultsToThirtySecondsAndRejectsBrain()
        {
            Assert.Equal(30.0, RunnerOptions.Parse(new[] { "check" }).IdleExitSeconds);
            Assert.Throws<ArgumentException>(() => RunnerOptions.Parse(new[] { "check", "--brain", "opus" }));
        }
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test AgentPlay.sln 2>&1 | grep -E " error |Passed!|Failed!" | sort -u | head`
Expected: compile errors `AdapterCheck` / `CheckItem` not found.

- [ ] **Step 3: Implement `AdapterCheck`**

`Runner/AdapterCheck.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner
{
    public sealed class CheckItem
    {
        public string Name;
        public bool Passed;
        public string Detail = "";
    }

    /// <summary>
    /// Checks a game's adapter from what the game says about itself (spec M4b section 3.2): the hello message, two
    /// state snapshots taken a moment apart, and any error message Unity sent. Pure; CheckCommand does the I/O.
    /// </summary>
    public static class AdapterCheck
    {
        static readonly string[] ParamTypes = { ValueKinds.Float, ValueKinds.Int, ValueKinds.Bool, ValueKinds.String };

        public static List<CheckItem> Run(HelloMessage hello, IReadOnlyList<JObject> snapshots, IReadOnlyList<string> unityErrors)
        {
            var items = new List<CheckItem>
            {
                Item("has observes", hello.Observes.Count > 0, hello.Observes.Count + " observe(s)"),
                Item("has actions", hello.Actions.Count > 0, hello.Actions.Count + " action(s)"),
            };
            var safe = hello.Actions.Where(a => a.Safe).Select(a => a.Name).ToList();
            items.Add(Item("exactly one safe action", safe.Count == 1, safe.Count == 0 ? "none" : string.Join(", ", safe)));
            items.Add(Item("declares game.won", hello.Events.Any(e => e.Name == ReservedEvents.GameWon), ""));
            items.Add(Item("brief is not empty", !string.IsNullOrWhiteSpace(hello.Brief), "write AgentPlay/brief.md"));
            var badParams = hello.Actions.SelectMany(a => a.Params.Where(p => !ParamTypes.Contains(p.ValueType)).Select(p => a.Name + "(" + p.Name + ": " + p.ValueType + ")")).ToList();
            items.Add(Item("action parameter types are supported", badParams.Count == 0, string.Join(", ", badParams)));
            items.Add(Item("two state snapshots received", snapshots.Count >= 2, snapshots.Count + " received"));
            foreach (var o in hello.Observes)
            {
                bool hasValue = snapshots.Any(s => s.TryGetValue(o.Path, out var v) && v.Type != JTokenType.Null);
                items.Add(Item("observe " + o.Path + " has a value", hasValue, hasValue ? "" : "null or missing in every snapshot"));
            }
            items.Add(Item("no error from Unity", unityErrors.Count == 0, string.Join(" | ", unityErrors)));
            return items;
        }

        static CheckItem Item(string name, bool passed, string detail) => new CheckItem { Name = name, Passed = passed, Detail = detail ?? "" };
    }
}
```

`ValueKinds` and `ReservedEvents` are declared in `Packages/com.agentplay.core/Contract/Protocol/MessageTypes.cs` (namespace `AgentPlay.Contract`).

- [ ] **Step 4: Implement option parsing**

In `Runner/RunnerOptions.cs`:
- Append to `Usage`: `"\n       AgentPlay.Runner check [--ports A-B] [--idle-exit SECONDS]"`.
- In `Parse`, after the `reflect` dispatch: `if (args.Length > 0 && args[0] == "check") return ParseCheck(args);`
- Add:

```csharp
        static RunnerOptions ParseCheck(string[] args)
        {
            var o = new RunnerOptions { Command = "check", IdleExitSeconds = 30 };
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
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
                        throw new ArgumentException("Unknown argument for check: " + a);
                }
            }
            return o;
        }
```

- [ ] **Step 5: Implement `CheckCommand`**

`Runner/CheckCommand.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner
{
    /// <summary>
    /// `AgentPlay.Runner check`: connects to the game, reads hello, asks for the state, lets the game run about two
    /// seconds, asks again, then prints AdapterCheck's verdict. Sends no plan and calls no model.
    /// </summary>
    public static class CheckCommand
    {
        public const int ExitPassed = 0, ExitFailed = 1, ExitNoGame = 6;

        public static async Task<int> RunAsync(RunnerOptions options, Action<string> log)
        {
            var deadline = Stopwatch.StartNew();
            var limit = TimeSpan.FromSeconds(options.IdleExitSeconds);
            BridgeClient client = null;
            while (client == null)
            {
                if (deadline.Elapsed > limit)
                {
                    log("check: no Unity connection within " + options.IdleExitSeconds + " s");
                    return ExitNoGame;
                }
                client = await BridgeClient.TryConnectAsync(options.Host, options.FirstPort, options.LastPort, CancellationToken.None);
                if (client == null) await Task.Delay(500);
            }
            using (client)
            using (var cts = new CancellationTokenSource(limit))
            {
                HelloMessage hello = null;
                var snapshots = new List<JObject>();
                var errors = new List<string>();
                try
                {
                    await foreach (var line in client.ReadLinesAsync(cts.Token))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var env = MessageCodec.Decode(line);
                        if (env.Type == MessageTypes.Hello)
                        {
                            hello = env.As<HelloMessage>();
                            log("check: hello from " + hello.Game + " " + hello.Version);
                            await Send(client, MessageTypes.GetState, null, cts.Token);
                        }
                        else if (env.Type == MessageTypes.Error)
                        {
                            errors.Add(env.As<ErrorMessage>().Message);
                        }
                        else if (env.Type == MessageTypes.State && hello != null)
                        {
                            snapshots.Add(env.As<StateMessage>().State ?? new JObject());
                            if (snapshots.Count == 1)
                            {
                                // Let the game run: resume is harmless when it is not paused.
                                await Send(client, MessageTypes.Resume, new ResumeMessage { MaxGameSeconds = 2 }, cts.Token);
                                await Task.Delay(2500, cts.Token);
                                await Send(client, MessageTypes.GetState, null, cts.Token);
                            }
                            else break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                if (hello == null)
                {
                    log("check: connected but no hello within " + options.IdleExitSeconds + " s");
                    return ExitNoGame;
                }
                var items = AdapterCheck.Run(hello, snapshots, errors);
                foreach (var i in items) log((i.Passed ? "PASS " : "FAIL ") + i.Name + (i.Detail.Length > 0 ? " (" + i.Detail + ")" : ""));
                bool ok = items.All(i => i.Passed);
                log(ok ? "check: adapter OK" : "check: adapter has problems");
                return ok ? ExitPassed : ExitFailed;
            }
        }

        static readonly SequenceCounter Seq = new SequenceCounter();

        static Task Send(BridgeClient client, string type, object body, CancellationToken ct) =>
            client.SendAsync(MessageCodec.Encode(type, Seq.Next(), body), ct);
    }
}
```

`SequenceCounter` comes from the shared Contract (`Packages/com.agentplay.core/Contract/Protocol/SequenceCounter.cs`), the same type `Program` uses. Check `MessageCodec.Decode` throws `ProtocolException` on bad lines; wrap the decode in the same `try/catch (ProtocolException)` + `continue` that `Program.RunConnectionAsync` uses.

In `Runner/Program.cs`, right after the `reflect` dispatch block:

```csharp
            if (options.Command == "check") return await CheckCommand.RunAsync(options, Log);
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test AgentPlay.sln 2>&1 | grep -E " error |Passed!|Failed!" | sort -u`
Expected: `Passed!`, 0 failed (343 existing + 14 new).

- [ ] **Step 7: Commit**

```bash
git add Runner/AdapterCheck.cs Runner/CheckCommand.cs Runner/RunnerOptions.cs Runner/Program.cs Runner.Tests/AdapterCheckTests.cs Runner.Tests/RunnerOptionsTests.cs
git commit -m "feat(runner): check command that verifies a game's adapter"
```

---

### Task 3: Generic AutoPlay with hooks; Heist uses it

**Files:**
- Create: `Packages/com.agentplay.core/Editor/AutoPlay.cs`, `Packages/com.agentplay.core/Editor/AutoPlayRules.cs`, `Packages/com.agentplay.core/Editor/AutoPlayHooks.cs`, `Packages/com.agentplay.core/Editor/IAgentAutoPlayHook.cs` (each with a `.meta` that Unity generates on first import; commit the generated `.meta` files)
- Modify: `Demos/Heist/Assets/Editor/HeistAutoPlay.cs` (Run delegates; Watch/Quit removed)
- Test: `Packages/com.agentplay.core/Tests/EditMode/AutoPlayRulesTests.cs`, `Packages/com.agentplay.core/Tests/EditMode/AutoPlayHooksTests.cs` (create)

**Interfaces:**
- Produces: `AgentPlay.Editor.AutoPlay.Run()` (static, for `-executeMethod`); `AgentPlay.Editor.IAgentAutoPlayHook { void BeforePlay(); void AfterPlay(); }`; `AutoPlayRules.Decide(double now, double deadline, string result, double endedAt) -> int?`; `AutoPlayRules.ResultFor(string eventName, string current) -> string`; constants `AutoPlayRules.ExitWon = 0, ExitDeadline = 1, ExitHookFailed = 2, ExitLost = 3`, `AutoPlayRules.EndDelaySeconds = 2.0`; `AutoPlayHooks.Before(IReadOnlyList<IAgentAutoPlayHook> hooks, Action<string> logError, out List<IAgentAutoPlayHook> started) -> bool` (false when a hook threw; `started` lists the hooks whose BeforePlay succeeded, in order); `AutoPlayHooks.After(IReadOnlyList<IAgentAutoPlayHook> started, Action<string> logError)` (AfterPlay in reverse order; logs and continues on exceptions).

- [ ] **Step 1: Write the failing tests**

`Packages/com.agentplay.core/Tests/EditMode/AutoPlayRulesTests.cs`:

```csharp
using AgentPlay.Editor;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class AutoPlayRulesTests
    {
        [Test]
        public void KeepsPlayingBeforeTheDeadlineWithNoResult() => Assert.IsNull(AutoPlayRules.Decide(10, 100, null, 0));

        [Test]
        public void DeadlineWithNoResultExitsOne() => Assert.AreEqual(AutoPlayRules.ExitDeadline, AutoPlayRules.Decide(100, 100, null, 0));

        [Test]
        public void WaitsTwoSecondsAfterTheResult()
        {
            Assert.IsNull(AutoPlayRules.Decide(51.9, 100, "won", 50));
            Assert.AreEqual(AutoPlayRules.ExitWon, AutoPlayRules.Decide(52, 100, "won", 50));
            Assert.AreEqual(AutoPlayRules.ExitLost, AutoPlayRules.Decide(52, 100, "lost", 50));
        }

        [Test]
        public void ResultBeatsTheDeadline() => Assert.AreEqual(AutoPlayRules.ExitWon, AutoPlayRules.Decide(200, 100, "won", 50));

        [Test]
        public void FirstResultWins()
        {
            Assert.AreEqual("won", AutoPlayRules.ResultFor("game.won", null));
            Assert.AreEqual("won", AutoPlayRules.ResultFor("game.lost", "won"));
            Assert.AreEqual("lost", AutoPlayRules.ResultFor("game.won", "lost"));
            Assert.IsNull(AutoPlayRules.ResultFor("item.picked", null));
        }
    }
}
```

`Packages/com.agentplay.core/Tests/EditMode/AutoPlayHooksTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using AgentPlay.Editor;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class AutoPlayHooksTests
    {
        sealed class Hook : IAgentAutoPlayHook
        {
            readonly string _name;
            readonly List<string> _calls;
            public bool FailBefore, FailAfter;
            public Hook(string name, List<string> calls) { _name = name; _calls = calls; }
            public void BeforePlay() { _calls.Add(_name + ".before"); if (FailBefore) throw new InvalidOperationException("x"); }
            public void AfterPlay() { _calls.Add(_name + ".after"); if (FailAfter) throw new InvalidOperationException("y"); }
        }

        [Test]
        public void RunsBeforeInOrderAndAfterInReverse()
        {
            var calls = new List<string>();
            var hooks = new List<IAgentAutoPlayHook> { new Hook("a", calls), new Hook("b", calls) };
            Assert.IsTrue(AutoPlayHooks.Before(hooks, _ => { }, out var started));
            AutoPlayHooks.After(started, _ => { });
            CollectionAssert.AreEqual(new[] { "a.before", "b.before", "b.after", "a.after" }, calls);
        }

        [Test]
        public void FailingBeforeStopsAndOnlyStartedHooksGetAfter()
        {
            var calls = new List<string>();
            var errors = new List<string>();
            var hooks = new List<IAgentAutoPlayHook> { new Hook("a", calls), new Hook("b", calls) { FailBefore = true }, new Hook("c", calls) };
            Assert.IsFalse(AutoPlayHooks.Before(hooks, errors.Add, out var started));
            AutoPlayHooks.After(started, errors.Add);
            CollectionAssert.AreEqual(new[] { "a.before", "b.before", "a.after" }, calls);
            Assert.AreEqual(1, errors.Count);
        }

        [Test]
        public void FailingAfterIsLoggedAndTheRestStillRun()
        {
            var calls = new List<string>();
            var errors = new List<string>();
            var started = new List<IAgentAutoPlayHook> { new Hook("a", calls), new Hook("b", calls) { FailAfter = true } };
            AutoPlayHooks.After(started, errors.Add);
            CollectionAssert.AreEqual(new[] { "b.after", "a.after" }, calls);
            Assert.AreEqual(1, errors.Count);
        }
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `bash Tools/unity-tests.sh`
Expected: compile failure, `AutoPlayRules` / `AutoPlayHooks` not found (Unity exit code non-zero, the log names the missing type).

- [ ] **Step 3: Implement the rules and the hook interface**

`Packages/com.agentplay.core/Editor/IAgentAutoPlayHook.cs`:

```csharp
namespace AgentPlay.Editor
{
    /// <summary>
    /// A game's setup and teardown around an AutoPlay run, e.g. backing up and restoring the player's save.
    /// Created with its parameterless constructor; keep state on disk, not in fields (a domain reload sits between
    /// the two calls). AfterPlay runs after Play mode has ended, also after a deadline or a failed hook.
    /// </summary>
    public interface IAgentAutoPlayHook
    {
        void BeforePlay();
        void AfterPlay();
    }
}
```

`Packages/com.agentplay.core/Editor/AutoPlayRules.cs`:

```csharp
using AgentPlay.Contract;

namespace AgentPlay.Editor
{
    /// <summary>When AutoPlay stops and with which exit code (spec M4b section 3.1). Pure, so EditMode tests cover it.</summary>
    public static class AutoPlayRules
    {
        public const int ExitWon = 0, ExitDeadline = 1, ExitHookFailed = 2, ExitLost = 3;
        public const double EndDelaySeconds = 2.0;

        /// <summary>The exit code once the run should stop, or null to keep playing.</summary>
        public static int? Decide(double now, double deadline, string result, double endedAt)
        {
            if (result != null) return now - endedAt >= EndDelaySeconds ? (result == "won" ? ExitWon : ExitLost) : (int?)null;
            return now >= deadline ? ExitDeadline : (int?)null;
        }

        /// <summary>The round's result after an event; the first game.won or game.lost wins.</summary>
        public static string ResultFor(string eventName, string current)
        {
            if (current != null) return current;
            if (eventName == ReservedEvents.GameWon) return "won";
            if (eventName == ReservedEvents.GameLost) return "lost";
            return null;
        }
    }
}
```

`Packages/com.agentplay.core/Editor/AutoPlayHooks.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace AgentPlay.Editor
{
    /// <summary>Calls hooks around an AutoPlay run: BeforePlay in order until one fails, AfterPlay on the started ones in reverse.</summary>
    public static class AutoPlayHooks
    {
        public static bool Before(IReadOnlyList<IAgentAutoPlayHook> hooks, Action<string> logError, out List<IAgentAutoPlayHook> started)
        {
            started = new List<IAgentAutoPlayHook>();
            foreach (var hook in hooks)
            {
                try
                {
                    hook.BeforePlay();
                    started.Add(hook);
                }
                catch (Exception e)
                {
                    logError("[AgentPlay] AutoPlay hook " + hook.GetType().FullName + " failed in BeforePlay: " + e);
                    return false;
                }
            }
            return true;
        }

        public static void After(IReadOnlyList<IAgentAutoPlayHook> started, Action<string> logError)
        {
            for (int i = started.Count - 1; i >= 0; i--)
            {
                try
                {
                    started[i].AfterPlay();
                }
                catch (Exception e)
                {
                    logError("[AgentPlay] AutoPlay hook " + started[i].GetType().FullName + " failed in AfterPlay: " + e);
                }
            }
        }
    }
}
```

- [ ] **Step 4: Implement `AutoPlay`**

`Packages/com.agentplay.core/Editor/AutoPlay.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AgentPlay.Editor
{
    /// <summary>
    /// Run with: Unity.exe -projectPath &lt;game&gt; -executeMethod AgentPlay.Editor.AutoPlay.Run
    /// Runs every IAgentAutoPlayHook.BeforePlay, opens AGENTPLAY_AUTOPLAY_SCENE (or keeps the open scene), enters
    /// Play mode and quits 2 s after game.won (exit 0) or game.lost (3), or at AGENTPLAY_AUTOPLAY_SECONDS (1, default
    /// 240). A failing hook exits 2. AfterPlay runs after Play mode ends. AGENTPLAY_SESSION_TAG (play|debug) sets the
    /// session tag. State lives in SessionState so it survives the domain reloads around Play mode.
    /// </summary>
    [InitializeOnLoad]
    public static class AutoPlay
    {
        const string DeadlineKey = "AgentPlay.AutoPlay.Deadline";
        const string ResultKey = "AgentPlay.AutoPlay.Result";
        const string EndedKey = "AgentPlay.AutoPlay.EndedAt";
        const string HooksKey = "AgentPlay.AutoPlay.Hooks";
        const string QuitKey = "AgentPlay.AutoPlay.QuitCode";

        static AutoPlay()
        {
            EditorApplication.update += Watch;
            AgentEvents.Emitted += OnEvent;
        }

        public static void Run()
        {
            if (!int.TryParse(Environment.GetEnvironmentVariable("AGENTPLAY_AUTOPLAY_SECONDS"), out var seconds) || seconds <= 0) seconds = 240;
            SessionState.EraseString(ResultKey);
            SessionState.EraseFloat(EndedKey);
            SessionState.EraseInt(QuitKey);
            AgentPlaySettings.SessionTag = Environment.GetEnvironmentVariable("AGENTPLAY_SESSION_TAG") == "play" ? "play" : "debug";

            var hooks = TypeCache.GetTypesDerivedFrom<IAgentAutoPlayHook>()
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .Select(t => (IAgentAutoPlayHook)Activator.CreateInstance(t))
                .ToList();
            bool ok = AutoPlayHooks.Before(hooks, Debug.LogError, out var started);
            // Type names survive the domain reloads around Play mode; AfterPlay recreates the hooks from them.
            SessionState.SetString(HooksKey, string.Join("\n", started.Select(h => h.GetType().AssemblyQualifiedName)));
            foreach (var h in started) Debug.Log("[AgentPlay] AutoPlay hook ready: " + h.GetType().FullName);
            if (!ok)
            {
                Finish(AutoPlayRules.ExitHookFailed);
                return;
            }

            string scene = Environment.GetEnvironmentVariable("AGENTPLAY_AUTOPLAY_SCENE");
            if (!string.IsNullOrEmpty(scene)) EditorSceneManager.OpenScene(scene);
            SessionState.SetFloat(DeadlineKey, (float)(EditorApplication.timeSinceStartup + seconds));
            Debug.Log("[AgentPlay] AutoPlay for at most " + seconds + " s, session tag " + AgentPlaySettings.SessionTag);
            EditorApplication.EnterPlaymode();
        }

        static void OnEvent(AgentEventRecord e)
        {
            if (SessionState.GetFloat(DeadlineKey, 0f) <= 0f) return;
            string current = SessionState.GetString(ResultKey, "");
            string next = AutoPlayRules.ResultFor(e.Name, current.Length == 0 ? null : current);
            if (next == null || next == current) return;
            SessionState.SetString(ResultKey, next);
            SessionState.SetFloat(EndedKey, (float)EditorApplication.timeSinceStartup);
        }

        static void Watch()
        {
            int quit = SessionState.GetInt(QuitKey, -1);
            if (quit >= 0)
            {
                // Leave Play mode first; hooks and exit run once the editor is back in Edit mode.
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                SessionState.EraseInt(QuitKey);
                RunAfterHooks();
                Debug.Log("[AgentPlay] AutoPlay quitting with " + quit);
                EditorApplication.Exit(quit);
                return;
            }
            float deadline = SessionState.GetFloat(DeadlineKey, 0f);
            if (deadline <= 0f || !EditorApplication.isPlaying) return;
            string result = SessionState.GetString(ResultKey, "");
            int? code = AutoPlayRules.Decide(EditorApplication.timeSinceStartup, deadline, result.Length == 0 ? null : result, SessionState.GetFloat(EndedKey, 0f));
            if (code == null) return;
            if (code == AutoPlayRules.ExitDeadline) Debug.Log("[AgentPlay] AutoPlay deadline reached");
            Finish(code.Value);
        }

        static void Finish(int code)
        {
            SessionState.EraseFloat(DeadlineKey);
            SessionState.SetInt(QuitKey, code);
            EditorApplication.isPaused = false;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }

        static void RunAfterHooks()
        {
            string names = SessionState.GetString(HooksKey, "");
            SessionState.EraseString(HooksKey);
            var started = names.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Type.GetType)
                .Where(t => t != null)
                .Select(t => (IAgentAutoPlayHook)Activator.CreateInstance(t))
                .ToList();
            AutoPlayHooks.After(started, Debug.LogError);
        }
    }
}
```

`AgentEventRecord` and `AgentEvents` are in namespace `AgentPlay` (runtime). If `AgentPlaySettings` lives in another namespace, add its `using`.

- [ ] **Step 5: Heist delegates to AutoPlay**

Replace `Demos/Heist/Assets/Editor/HeistAutoPlay.cs` with: keep `ApproveOnly()` and `ApproveProposed()` exactly as they are; `Run()` becomes

```csharp
        public static void Run()
        {
            if (Environment.GetEnvironmentVariable("AGENTPLAY_APPROVE_PROPOSED") == "1") ApproveProposed();
            AgentPlay.Editor.AutoPlay.Run();
        }
```

Delete `Watch`, `Quit`, the static constructor, `[InitializeOnLoad]`, `DeadlineKey` and `EndedKey`. Update the class summary to say Run delegates to `AgentPlay.Editor.AutoPlay` (exit codes unchanged: 0 won, 3 lost, 1 deadline).

- [ ] **Step 6: Run the Unity tests and the Heist E2E runs**

Run: `bash Tools/unity-tests.sh` — Expected: `result="Passed"`, previous 146 + 8 new.
Run: `bash Tools/e2e-m4a-realtime.sh` — Expected: `ALL CHECKS PASSED` (Unity exit code 0 through the generic AutoPlay).
Run: `bash Tools/e2e-m3-reflex.sh` — Expected: `ALL CHECKS PASSED`.

- [ ] **Step 7: Commit**

```bash
git add Packages/com.agentplay.core/Editor/AutoPlay*.cs* Packages/com.agentplay.core/Editor/IAgentAutoPlayHook.cs* Packages/com.agentplay.core/Tests/EditMode/AutoPlay*Tests.cs* Demos/Heist/Assets/Editor/HeistAutoPlay.cs
git commit -m "feat(editor): generic AutoPlay with setup/teardown hooks; Heist uses it"
```

---

### Task 4: `link-agentplay.ps1 -LocalOnly`, `-Unlink`, `-Purge`

**Files:**
- Modify: `Tools/link-agentplay.ps1`
- Test: `Tools/test-link-agentplay.sh`

**Interfaces:**
- Produces: `powershell -File Tools/link-agentplay.ps1 -ProjectPath <p> [-LocalOnly] [-Unlink [-Purge]]`. The exclude block in `<p>/.git/info/exclude` is exactly:

```
# AgentPlay begin
/Packages/com.agentplay.core
/AgentPlay/
/Assets/AgentPlayAdapter/
/Assets/AgentPlayAdapter.meta
/Assets/csc.rsp
/Assets/csc.rsp.meta
# AgentPlay end
```

- [ ] **Step 1: Write the failing tests**

Append to `Tools/test-link-agentplay.sh`, before the final `rm -rf "$TMP"` (keep the existing default-mode cases unchanged):

```bash
# --- LocalOnly / Unlink on a git repo ---
REPO="$TMP/TeamGame"
mkdir -p "$REPO/Packages" "$REPO/Assets"
echo '{"dependencies":{}}' > "$REPO/Packages/manifest.json"
printf 'Library/\n' > "$REPO/.gitignore"
git -C "$REPO" init -q && git -C "$REPO" add -A && git -C "$REPO" -c user.email=t@t -c user.name=t commit -q -m init
REPO_W="$(cygpath -w "$REPO")"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$REPO_W" -LocalOnly
check '[ $? -eq 0 ]' "local-only link succeeds"
mkdir -p "$REPO/Assets/AgentPlayAdapter" && echo "// adapter" > "$REPO/Assets/AgentPlayAdapter/A.cs" && touch "$REPO/Assets/AgentPlayAdapter.meta"
printf '# AgentPlay: local define, created by agentplay-adapt\n-define:AGENTPLAY\n' > "$REPO/Assets/csc.rsp"
check '[ -z "$(git -C "$REPO" status --porcelain)" ]' "local-only leaves git status clean"
check 'grep -q "^Library/$" "$REPO/.gitignore" && [ "$(wc -l < "$REPO/.gitignore")" -eq 1 ]' "local-only does not touch .gitignore"
check '[ "$(grep -c "^# AgentPlay begin$" "$REPO/.git/info/exclude")" -eq 1 ]' "exclude block written once"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$REPO_W" -LocalOnly
check '[ "$(grep -c "^# AgentPlay begin$" "$REPO/.git/info/exclude")" -eq 1 ]' "second local-only link keeps one block"

powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$REPO_W" -Unlink
check '[ $? -eq 0 ]' "unlink succeeds"
check '[ ! -e "$REPO/Packages/com.agentplay.core" ]' "unlink removes the junction"
check '[ ! -e "$REPO/Assets/AgentPlayAdapter" ] && [ ! -e "$REPO/Assets/AgentPlayAdapter.meta" ]' "unlink removes the adapter"
check '[ ! -e "$REPO/Assets/csc.rsp" ]' "unlink removes our csc.rsp"
check '! grep -q "AgentPlay" "$REPO/.git/info/exclude"' "unlink removes the exclude block"
check '[ -d "$REPO/AgentPlay" ]' "unlink keeps AgentPlay/ without -Purge"
check '[ -z "$(git -C "$REPO" status --porcelain)" ]' "git status clean after unlink"
check '[ -f "$ROOT/Packages/com.agentplay.core/package.json" ]' "real package untouched after -Unlink"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$REPO_W" -Unlink
check '[ $? -eq 0 ] && [ -z "$(git -C "$REPO" status --porcelain)" ]' "unlink is idempotent"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$REPO_W" -Unlink -Purge
check '[ ! -e "$REPO/AgentPlay" ]' "purge removes AgentPlay/"

# A csc.rsp that is not ours is never touched.
printf -- '-nowarn:0618\n' > "$REPO/Assets/csc.rsp"
git -C "$REPO" add -A && git -C "$REPO" -c user.email=t@t -c user.name=t commit -q -m rsp
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$REPO_W" -LocalOnly
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$REPO_W" -Unlink
check 'grep -q "nowarn" "$REPO/Assets/csc.rsp"' "foreign csc.rsp kept"
```

- [ ] **Step 2: Run to see them fail**

Run: `bash Tools/test-link-agentplay.sh`
Expected: `FAIL local-only link succeeds` (unknown parameter) and the cases after it fail.

- [ ] **Step 3: Implement**

In `Tools/link-agentplay.ps1`:
- `param([Parameter(Mandatory = $true)][string]$ProjectPath, [switch]$LocalOnly, [switch]$Unlink, [switch]$Purge)`; update the comment-based help with the three switches.
- Add helpers at the top:

```powershell
$Marker = '# AgentPlay: local define, created by agentplay-adapt'
$Block = @('# AgentPlay begin', '/Packages/com.agentplay.core', '/AgentPlay/', '/Assets/AgentPlayAdapter/', '/Assets/AgentPlayAdapter.meta', '/Assets/csc.rsp', '/Assets/csc.rsp.meta', '# AgentPlay end')

function Get-ExcludePath($project) {
    $gitDir = (& git -C $project rev-parse --git-dir 2>$null)
    if (-not $gitDir) { throw "$project is not a git repository; -LocalOnly and -Unlink need git" }
    if (-not [System.IO.Path]::IsPathRooted($gitDir)) { $gitDir = Join-Path $project $gitDir }
    $info = Join-Path $gitDir 'info'
    New-Item -ItemType Directory -Force $info | Out-Null
    return (Join-Path $info 'exclude')
}

function Remove-ExcludeBlock($path) {
    if (-not (Test-Path $path)) { return }
    $lines = Get-Content $path
    $out = New-Object System.Collections.Generic.List[string]
    $inside = $false
    foreach ($l in $lines) {
        if ($l -eq '# AgentPlay begin') { $inside = $true; continue }
        if ($l -eq '# AgentPlay end') { $inside = $false; continue }
        if (-not $inside) { $out.Add($l) }
    }
    [System.IO.File]::WriteAllLines($path, $out, (New-Object System.Text.UTF8Encoding($false)))
}
```

- After resolving `$project` (keep the manifest check):

```powershell
if ($Unlink) {
    $exclude = Get-ExcludePath $project
    $link = Join-Path $project 'Packages\com.agentplay.core'
    if (Test-Path $link) {
        $item = Get-Item $link -Force
        if ($item.LinkType -eq 'Junction') { cmd /c rmdir "$link" | Out-Null; Write-Host "Removed junction $link" }
        else { throw "$link is not a junction; not removing it" }
    }
    foreach ($p in @('Assets\AgentPlayAdapter', 'Assets\AgentPlayAdapter.meta')) {
        $full = Join-Path $project $p
        if (Test-Path $full) { Remove-Item $full -Recurse -Force; Write-Host "Removed $full" }
    }
    $rsp = Join-Path $project 'Assets\csc.rsp'
    if ((Test-Path $rsp) -and ((Get-Content $rsp -TotalCount 1) -eq $Marker)) {
        Remove-Item $rsp -Force; Remove-Item "$rsp.meta" -Force -ErrorAction SilentlyContinue; Write-Host "Removed $rsp"
    }
    Remove-ExcludeBlock $exclude
    & git -C $project diff --quiet -- Packages/packages-lock.json 2>$null
    if ($LASTEXITCODE -eq 1) { & git -C $project checkout -- Packages/packages-lock.json; Write-Host 'Restored Packages/packages-lock.json' }
    if ($Purge) { $ap = Join-Path $project 'AgentPlay'; if (Test-Path $ap) { Remove-Item $ap -Recurse -Force; Write-Host "Removed $ap" } }
    exit 0
}
```

- Junction creation stays as it is. Then replace the `.gitignore` block with:

```powershell
if ($LocalOnly) {
    $exclude = Get-ExcludePath $project
    Remove-ExcludeBlock $exclude
    $text = ($Block -join "`n") + "`n"
    [System.IO.File]::AppendAllText($exclude, $text, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Wrote the AgentPlay block to $exclude (nothing added to .gitignore)"
    Write-Host 'Unity may edit Packages/packages-lock.json; -Unlink restores it, or run: git checkout -- Packages/packages-lock.json'
} else {
    # existing .gitignore code, unchanged
}
```

- The `brief.md` template creation stays for both modes.

- [ ] **Step 4: Run the tests**

Run: `bash Tools/test-link-agentplay.sh`
Expected: every line `PASS`, exit 0.

- [ ] **Step 5: Commit**

```bash
git add Tools/link-agentplay.ps1 Tools/test-link-agentplay.sh
git commit -m "feat(tools): local-only linking through .git/info/exclude, and -Unlink/-Purge"
```

---

### Task 5: `e2e-m4b-check.sh` and `run-adapter-batch.sh`

**Files:**
- Create: `Tools/e2e-m4b-check.sh`, `Tools/run-adapter-batch.sh`

**Interfaces:**
- Consumes: `AgentPlay.Runner check` (Task 2), `AgentPlay.Editor.AutoPlay.Run` (Task 3).
- Produces: `bash Tools/run-adapter-batch.sh <game project> <N> [script.json]` — env: `AGENTPLAY_AUTOPLAY_SCENE`, `AGENTPLAY_AUTOPLAY_SECONDS` (default 900), `AGENTPLAY_PROGRESS_OBSERVE` (an observe path printed from the last trace snapshot). Logs in `.e2e/adapter-batch/<project folder name>/`.

- [ ] **Step 1: Write `e2e-m4b-check.sh`**

```bash
#!/usr/bin/env bash
# M4b: `AgentPlay.Runner check` passes on Heist, driven by the generic AutoPlay; no model calls.
# Demos/Heist must not be open in another Editor.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
PROJECT="$ROOT/Demos/Heist"
LOGS="$ROOT/.e2e/m4b-check"
mkdir -p "$LOGS" && rm -f "$LOGS"/*.log
dotnet build "$ROOT/Runner/AgentPlay.Runner.csproj" -c Debug -v q || exit 1
dotnet "$ROOT/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll" check --idle-exit 120 > "$LOGS/check.log" 2>&1 < /dev/null &
CHECK_PID=$!
AGENTPLAY_AUTOPLAY_SECONDS=40 "$UNITY_EXE" -projectPath "$(cygpath -m "$PROJECT")" \
  -executeMethod AgentPlay.Editor.AutoPlay.Run -logFile "$(cygpath -m "$LOGS/autoplay.log")"
UNITY_CODE=$?
wait "$CHECK_PID"
CHECK_CODE=$?
cat "$LOGS/check.log"
fail=0
if [ "$CHECK_CODE" -eq 0 ]; then echo "PASS check exited 0"; else echo "FAIL check exited $CHECK_CODE"; fail=1; fi
if grep -q "^.*FAIL " "$LOGS/check.log"; then echo "FAIL a check line failed"; fail=1; else echo "PASS no FAIL line"; fi
# No brain is attached, so the round never ends: the deadline exit (1) is the expected Unity exit here.
if [ "$UNITY_CODE" -eq 1 ]; then echo "PASS AutoPlay ended at its deadline"; else echo "FAIL AutoPlay exit $UNITY_CODE (expected 1)"; fail=1; fi
[ $fail -eq 0 ] && echo "ALL CHECKS PASSED"
exit $fail
```

- [ ] **Step 2: Run it**

Run: `bash Tools/e2e-m4b-check.sh`
Expected: `PASS check exited 0`, every check line `PASS`, `ALL CHECKS PASSED`. If an observe line fails because Heist legitimately has a null observe in both snapshots, that is a real adapter finding: fix the Heist observe (not the check) and record a ruling.

- [ ] **Step 3: Write `run-adapter-batch.sh`**

```bash
#!/usr/bin/env bash
# Plays a linked game N times with the Opus brain (turn-based) through the generic AutoPlay. Real model calls.
# Usage: bash Tools/run-adapter-batch.sh <game project> <N> [script.json]   (a script plan replaces Opus)
# Env: AGENTPLAY_AUTOPLAY_SCENE, AGENTPLAY_AUTOPLAY_SECONDS (default 900), AGENTPLAY_PROGRESS_OBSERVE.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
GAME="$(cd "$1" && pwd)"
RUNS="${2:-3}"
SCRIPT="${3:-}"
LOGS="$ROOT/.e2e/adapter-batch/$(basename "$GAME")"
mkdir -p "$LOGS" && rm -f "$LOGS"/*
dotnet build "$ROOT/Runner/AgentPlay.Runner.csproj" -c Debug -v q || exit 1
if [ -n "$SCRIPT" ]; then BRAIN=(--brain script "$(cygpath -w "$SCRIPT")"); else BRAIN=(--brain opus); fi
wins=0
for i in $(seq 1 "$RUNS"); do
  dotnet "$ROOT/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll" "${BRAIN[@]}" --idle-exit 60 --no-reflect \
    > "$LOGS/run-$i.log" 2>&1 < /dev/null &
  RUNNER_PID=$!
  AGENTPLAY_SESSION_TAG=debug AGENTPLAY_AUTOPLAY_SECONDS="${AGENTPLAY_AUTOPLAY_SECONDS:-900}" "$UNITY_EXE" -projectPath "$(cygpath -m "$GAME")" \
    -executeMethod AgentPlay.Editor.AutoPlay.Run -logFile "$(cygpath -m "$LOGS/autoplay-$i.log")"
  UNITY_CODE=$?
  wait "$RUNNER_PID"
  RUNNER_CODE=$?
  [ "$RUNNER_CODE" -eq 0 ] && wins=$((wins + 1))
  CALLS="$(grep -h -o 'model calls [0-9]*' "$LOGS/run-$i.log" | tail -1)"
  PROGRESS=""
  if [ -n "${AGENTPLAY_PROGRESS_OBSERVE:-}" ]; then
    TRACE="$(ls -t "$GAME/AgentPlay/Traces/"*.ndjson 2>/dev/null | head -1)"
    [ -n "$TRACE" ] && PROGRESS="$(python -I - "$TRACE" "$AGENTPLAY_PROGRESS_OBSERVE" <<'PY'
import json, sys
last = None
for line in open(sys.argv[1], encoding="utf-8"):
    try:
        r = json.loads(line)
    except ValueError:
        continue
    if isinstance(r, dict) and isinstance(r.get("state"), dict) and sys.argv[2] in r["state"]:
        last = r["state"][sys.argv[2]]
print("%s=%s" % (sys.argv[2], last))
PY
)"
  fi
  echo "run $i: runner=$RUNNER_CODE unity=$UNITY_CODE ${CALLS:-model calls 0} $PROGRESS"
  if [ "$RUNNER_CODE" != 0 ] && [ "$RUNNER_CODE" != 3 ] && grep -q "model call failed" "$LOGS/run-$i.log"; then
    echo "run $i: model failure, batch stopped"; exit 4
  fi
done
echo "wins: $wins/$RUNS"
```

- [ ] **Step 4: Smoke-test `run-adapter-batch.sh` on Heist with the script brain**

Run: `AGENTPLAY_PROGRESS_OBSERVE=items.left bash Tools/run-adapter-batch.sh Demos/Heist 1 Demos/Heist/AgentPlay/scripts/win.json`
Expected: `run 1: runner=0 unity=0 model calls 0 items.left=0`, `wins: 1/1`. (If `reflexes.json` makes the script brain diverge, move it aside as `e2e-m4a-realtime.sh` does and restore after.)

- [ ] **Step 5: Commit**

```bash
git add Tools/e2e-m4b-check.sh Tools/run-adapter-batch.sh
git commit -m "test(e2e): adapter check on Heist; generic adapter batch script"
```

---

### Task 6: The `agentplay-adapt` skill

**Files:**
- Create: `.claude/skills/agentplay-adapt/SKILL.md`
- Create: `.claude/skills/agentplay-adapt/reference/attributes.md`
- Create: `.claude/skills/agentplay-adapt/reference/adapter-shapes.md`
- Create: `.claude/skills/agentplay-adapt/reference/pitfalls.md`
- Create: `.claude/skills/agentplay-adapt/reference/brief-template.md`
- Modify: `README.md` (section "Adapting your game with Claude Code")

**Interfaces:**
- Consumes: `link-agentplay.ps1 -LocalOnly/-Unlink` (Task 4), `AgentPlay.Runner check` (Task 2), `AgentPlay.Editor.AutoPlay.Run` + `IAgentAutoPlayHook` (Task 3), `run-adapter-batch.sh` (Task 5).

- [ ] **Step 1: Write `SKILL.md`**

```markdown
---
name: agentplay-adapt
description: Use when the user wants an AgentPlay agent (Claude playing their Unity game through the AgentPlay package) to play an existing Unity game that has no adapter yet, e.g. "make the agent play my game", "write an AgentPlay adapter", "let Claude playtest the first missions". Surveys the game, proposes what the agent sees and does, writes a local-only adapter, and verifies it in four tiers.
---

# agentplay-adapt

Write an AgentPlay adapter for the Unity game in the current project so the agent can reach a goal the user names.

**Needs:** the AgentPlay repo (env `AGENTPLAY_HOME`; if unset, ask the user for its path once and use it for the session), Unity 6000.0+, .NET 9 SDK, Claude Code CLI signed in. Read `reference/attributes.md` before step 2 and `reference/adapter-shapes.md` before step 3.

**Hard rules**
- Change no file that git tracks in the game repo. Everything you add is excluded through `.git/info/exclude` by `link-agentplay.ps1 -LocalOnly`. Never edit the game's `.gitignore`. Check with `git status --porcelain` after every step that writes files: it must print nothing new.
- Never lose the player's save. If the game persists progress, the adapter ships an `IAgentAutoPlayHook` that backs it up before Play and restores it after (template in `reference/adapter-shapes.md`).
- Actions go through the player's own input path (joystick value, virtual gamepad button, the exact method a UI button calls). No teleports and no direct edits to currency or progress. Test setup inside a hook (e.g. starting from a fresh save) is the only exception.
- Write no file before the user approves the step-2 table.
- Never loosen a check in step 4 to make it pass. Fix the adapter or report the blocker.

## Step 1: Survey (read only)

Find and note with `file:line`:
1. The player character and the component that moves it; how input reaches it (Input System actions, a UI joystick script, legacy `Input.GetAxis`).
2. Managers that hold the state the goal depends on (singletons, an event bus, ScriptableObject data). For a mission goal: where missions, their goals and progress live, and what event or call marks a mission complete.
3. How progress is saved: path (`Application.persistentDataPath/...`), PlayerPrefs keys, when it writes.
4. Assembly layout: does game code sit in `.asmdef` assemblies, or in `Assembly-CSharp`? This picks the adapter shape.
5. The scene the game starts in (`EditorBuildSettings` first enabled scene) and anything that blocks play in the Editor (consent dialogs, ads, login). Use `Grep` widely; skip third-party SDK folders.

Output a short survey note in the conversation.

## Step 2: Propose the adapter surface, then STOP

Show one table per kind:
- **Observes:** path (dotted, lowercase), type, source expression, why the agent needs it for the goal. Prefer few, decision-relevant values: positions of targets, counts, the current objective, progress numbers. Lists of similar objects use `[AgentObserveList]`.
- **Actions:** name, parameters (float/int/bool/string only), how it drives the player's input, when it returns `Done` or `Failed`, and the single `Safe = true` action (usually idle/stop) the game runs while no plan is available.
- **Events:** name, when emitted, `Important`, `Outcome` (good/bad/neutral). Always `game.won` (goal reached) and, if the goal can fail, `game.lost`.
- **Start and teardown:** how a run starts at the goal's starting point (e.g. fresh save) and how the hook restores the user's state.

Ask the user to approve or change the table. Do not continue without approval.

## Step 3: Attach

1. `powershell -File "$AGENTPLAY_HOME/Tools/link-agentplay.ps1" -ProjectPath <game> -LocalOnly`
2. If `Assets/csc.rsp` already exists and its first line is not `# AgentPlay: local define, created by agentplay-adapt`, STOP and ask the user (the Assembly-CSharp shape needs it).
3. Write the adapter in `Assets/AgentPlayAdapter/` using the shape from `reference/adapter-shapes.md`.
4. Write `AgentPlay/brief.md` from `reference/brief-template.md`.
5. Write `AgentPlay/scripts/smoke.json`: a script plan that makes real progress toward the goal without a model (plan format: `$AGENTPLAY_HOME/Demos/Heist/AgentPlay/scripts/win.json`).
6. Write the save hook if step 1 found saved progress.
7. `git -C <game> status --porcelain` must show nothing new.

## Step 4: Verify in tiers

Stop at the first failing tier, fix the adapter, rerun from that tier. Close the game's Editor first if it is open on the same project (batchmode cannot open a project twice); if the project is too large for batchmode (a refresh takes over 3 minutes), run the same tiers through the open Editor with the Unity MCP tools instead and say so in the report.

1. **Compiles:** `"$UNITY_EXE" -batchmode -nographics -quit -projectPath <game> -logFile compile.log`; `compile.log` has no `error CS`.
2. **Adapter check:** start `dotnet "$AGENTPLAY_HOME/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll" check --idle-exit 180` in the background, then `AGENTPLAY_AUTOPLAY_SECONDS=60 "$UNITY_EXE" -projectPath <game> -executeMethod AgentPlay.Editor.AutoPlay.Run`. `check` exits 0 and prints no `FAIL` line.
3. **Script plan:** `bash "$AGENTPLAY_HOME/Tools/run-adapter-batch.sh" <game> 1 <game>/AgentPlay/scripts/smoke.json`; the run log has no `plan_failed` and the progress observe moved.
4. **Opus:** `bash "$AGENTPLAY_HOME/Tools/run-adapter-batch.sh" <game> 1` (set `AGENTPLAY_PROGRESS_OBSERVE` to the goal's progress observe); `runner=0` (game.won).

Set `AGENTPLAY_AUTOPLAY_SCENE` to the start scene for tiers 2-4 when the project does not open on it.

## Step 5: Report

List: every file added (all excluded from git), how to remove everything (`link-agentplay.ps1 -ProjectPath <game> -Unlink`, add `-Purge` to also delete `AgentPlay/`), the result of each tier with the log path, what the adapter does not cover, and anything about the game that blocked or slowed the agent.
```

- [ ] **Step 2: Write `reference/attributes.md`**

Content (English), each item with a short C# example taken from `Demos/Heist/Assets/Heist/*.cs` (copy the real lines):
- `[AgentGame("Name", Version = "1")]` or `VersionMember = nameof(StaticProp)`; one per game, on the adapter class.
- `[AgentObserve("path", "description")]` on a property or field; supported types: float, int, bool, string, enum, Vector3; must be cheap and allocation-free.
- `[AgentObserveList("path", "description")]` on an `IEnumerable<T>`; T's `[AgentObserve]` members become element fields (copy `HeistGame.Guards` and `HeistGuard` members).
- `[AgentAction("name", "description")]` on a method returning `AgentActionStatus` (`Running`, `Done`, `Failed`), called every frame; optional first parameter `AgentActionContext` (`IsFirstFrame`, elapsed game seconds); then float/int/bool/string parameters; `Safe = true` for the single default action with no parameters. Copy `HeistPlayer.GoTo` and `Pick`.
- `[AgentEventInfo("name", "description", Important = true, Outcome = AgentOutcome.Bad)]` on the adapter class; emit with `AgentEvents.Emit("name", new { ... }, important: true, outcome: AgentOutcome.Bad)` (copy `HeistGame` win/lose lines). `game.won` / `game.lost` are reserved and end the run.
- Adapter self-creation: a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]` static method that adds the adapter `MonoBehaviour` to a new `DontDestroyOnLoad` GameObject, guarded by `#if UNITY_EDITOR`.

- [ ] **Step 3: Write `reference/adapter-shapes.md`**

Two complete file skeletons:

1. **asmdef shape** (game code in asmdefs): `Assets/AgentPlayAdapter/<Game>.AgentAdapter.asmdef`:

```json
{
  "name": "<Game>.AgentAdapter",
  "rootNamespace": "<Game>.AgentAdapter",
  "references": ["<game runtime asmdef names>", "AgentPlay.Runtime"],
  "autoReferenced": false,
  "defineConstraints": ["AGENTPLAY"],
  "versionDefines": [{ "name": "com.agentplay.core", "expression": "0.0.1", "define": "AGENTPLAY" }]
}
```

plus `Assets/AgentPlayAdapter/Editor/<Game>.AgentAdapter.Editor.asmdef` (same constraints, `includePlatforms: ["Editor"]`, references `AgentPlay.Editor` and the adapter asmdef) for hooks. Cite TileThieves branch `feat/agentplay-adapter`, file `Assets/_Project/Scripts/AgentAdapter/TileThievesAgent.cs`, as the worked example (fetch with `git -C <TileThieves> show feat/agentplay-adapter:<path>` if available).

2. **Assembly-CSharp shape** (no game asmdefs): `Assets/csc.rsp`:

```
# AgentPlay: local define, created by agentplay-adapt
-define:AGENTPLAY
```

Every `.cs` under `Assets/AgentPlayAdapter/` starts with `#if AGENTPLAY` and ends with `#endif`. Hooks go in `Assets/AgentPlayAdapter/Editor/` (Editor-only by folder name).

3. **Save hook template** (both shapes):

```csharp
#if AGENTPLAY
using System.IO;
using AgentPlay.Editor;
using UnityEngine;

namespace AgentPlayAdapter.Editor
{
    /// <summary>Backs up the player's save before an AutoPlay run, starts from a fresh save, restores it after.</summary>
    public sealed class SaveHook : IAgentAutoPlayHook
    {
        static string Save => Path.Combine(Application.persistentDataPath, "<save folder>");
        static string Backup => Path.Combine(Path.GetDirectoryName(Application.dataPath), "AgentPlay", "save-backup");

        public void BeforePlay()
        {
            // A backup left by a crashed run is the user's real save: restore it before anything else.
            if (Directory.Exists(Backup)) Restore();
            if (Directory.Exists(Save)) Copy(Save, Backup);
            if (Directory.Exists(Save)) Directory.Delete(Save, true);   // fresh save for the run
        }

        public void AfterPlay()
        {
            if (Directory.Exists(Backup)) Restore();
        }

        static void Restore()
        {
            if (Directory.Exists(Save)) Directory.Delete(Save, true);
            Copy(Backup, Save);
            Directory.Delete(Backup, true);
        }

        static void Copy(string from, string to)
        {
            foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(from, to));
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(from, to), true);
        }
    }
}
#endif
```

Add: if the game also stores progress in PlayerPrefs, export/import the Editor PlayerPrefs registry key in the same hook (`reg export`/`reg import` through `System.Diagnostics.Process`), and list the exact keys in the step-2 table. `AgentPlay/` is excluded from git, so the backup never reaches the repo.

- [ ] **Step 4: Write `reference/pitfalls.md` and `reference/brief-template.md`**

`pitfalls.md` items (one paragraph each, with the source):
- Stick deadzone: virtual stick values under the Input System deadzone (default 0.125) do nothing; use `VirtualGamepad.PastDeadzone` (`Packages/com.agentplay.core/InputSystem/VirtualGamepad.cs`). From M2c.
- No allocation in `Update` or in observes (no `FindObjectsByType`, LINQ or `new` lists per frame); cache references, use `NonAlloc` physics queries. From M2c review.
- Actions must return `Done` when the round is over, or the executor waits on them forever (`HeistPlayer.GoTo` first line).
- Important events need an `Outcome`, or reflexes cannot be scored.
- Observes are read every snapshot; keep them O(1).
- A UI joystick that only reads touch events: drive the movement component at the lowest level the joystick itself calls, and say so in the step-2 table.
- Third-party SDK popups (consent, ads) in the Editor: dismiss them from the save hook or an adapter action that calls the same method the dismiss button calls; never edit SDK or game code.

`brief-template.md`:

```markdown
# <Game>

<One paragraph: what the player is and what the world looks like, in terms of the observes.>

- Goal for this run: <the user's goal, e.g. "complete the first 3 missions">.
- Win: <exact condition behind game.won>.
- Lose: <condition behind game.lost, or "no lose condition; the run ends at the deadline">.
- How things work that the observes do not show: <timers, costs, what an action does in the world>.
- Tips: <known safe order of actions, if the survey found one>.
```

- [ ] **Step 5: README section**

Add to `README.md` after "Quick start":

```markdown
## Adapting your game with Claude Code

The `agentplay-adapt` skill writes the adapter for you. Make it available to Claude Code in your game project:

```powershell
setx AGENTPLAY_HOME E:\path\to\AgentPlay
New-Item -ItemType Junction -Path "$env:USERPROFILE\.claude\skills\agentplay-adapt" -Target "E:\path\to\AgentPlay\.claude\skills\agentplay-adapt"
```

Then, in your game project, ask Claude Code something like "use agentplay-adapt so the agent can finish the first 3 missions". It surveys the code, shows you what the agent will see and do, and after you approve writes a local-only adapter (excluded from git) and verifies it. Remove everything with `Tools/link-agentplay.ps1 -ProjectPath <game> -Unlink`.
```

- [ ] **Step 6: Commit**

```bash
git add .claude/skills/agentplay-adapt README.md
git commit -m "feat(skill): agentplay-adapt writes and verifies an adapter for an existing game"
```

---

### Task 7: Dry run on a TileThieves `main` worktree

**Files:**
- Modify: `.superpowers/sdd/2026-10-17-agentplay-m4b-adapt/progress.md` (findings)
- Modify: `.claude/skills/agentplay-adapt/**` (fixes the dry run shows)

- [ ] **Step 1: Make an isolated worktree**

```bash
git -C /e/Unity-WorkSpace/TileThieves worktree add /e/Unity-WorkSpace/TileThieves-adapt-dryrun main
cp -r /e/Unity-WorkSpace/TileThieves/Library /e/Unity-WorkSpace/TileThieves-adapt-dryrun/ 2>/dev/null || true
```

The copied `Library` only speeds the first import; it is untracked.

- [ ] **Step 2: Run the skill in a fresh subagent**

Dispatch a general-purpose subagent (fresh context) with: "Working directory `E:\Unity-WorkSpace\TileThieves-adapt-dryrun`. `AGENTPLAY_HOME=E:\Unity-WorkSpace\AgentPlay`. Follow `E:\Unity-WorkSpace\AgentPlay\.claude\skills\agentplay-adapt\SKILL.md` exactly for the goal 'pry 6 tiles into the sack in scene Assets/_Project/Scenes/Prototype_Pry.unity without reaching Lockdown'. At step 2, do not wait for a human: write the table to `AgentPlay/proposal.md` and continue. Do not read the TileThieves branch `feat/agentplay-adapter` until your adapter has passed tier 2 (it is the answer key). Report each tier's result and every place SKILL.md was unclear or wrong."

- [ ] **Step 3: Judge the result**

Pass: tiers 1-3 pass, tier 4 wins at least once in 2 attempts, `git -C /e/Unity-WorkSpace/TileThieves-adapt-dryrun status --porcelain` prints nothing. For every unclear or wrong instruction the subagent reported, fix the skill file and record the fix in the ledger.

- [ ] **Step 4: Remove the worktree**

```bash
powershell -NoProfile -File Tools/link-agentplay.ps1 -ProjectPath 'E:\Unity-WorkSpace\TileThieves-adapt-dryrun' -Unlink -Purge
git -C /e/Unity-WorkSpace/TileThieves worktree remove --force /e/Unity-WorkSpace/TileThieves-adapt-dryrun
git -C /e/Unity-WorkSpace/TileThieves worktree list
```

Expected: only the user's TileThieves worktree remains; `git -C /e/Unity-WorkSpace/TileThieves status --porcelain` is unchanged from before step 1.

- [ ] **Step 5: Commit skill fixes**

```bash
git add .claude/skills/agentplay-adapt
git commit -m "fix(skill): agentplay-adapt instructions after the TileThieves dry run"
```

---

### Task 8: PetRPG run

Runs in the main session (the user approves the step-2 table). Uses the route Task 1 chose (batchmode or open Editor).

- [ ] **Step 1: Baseline** — repeat Task 1 step 1 into `.e2e/petrpg-m4b/` (save hash CSV, save copy, PlayerPrefs export, `git status --porcelain`).
- [ ] **Step 2: Skill steps 1-3** — follow `SKILL.md` in `E:\PetRPG` with goal "complete the first 3 missions from a fresh save" (if one of them is `MissionType.Ad`, the first 3 that are not; record which). Stop at step 2 for the user's approval.
- [ ] **Step 3: Crash-safety check (Review Focus 1)** — before tier 2: start AutoPlay, and once the log shows `AutoPlay hook ready`, kill Unity (`Stop-Process`). Start AutoPlay again with a 30 s deadline and let it end. Compare the save hash with the baseline: identical.
- [ ] **Step 4: Skill step 4** — tiers 1-4.
- [ ] **Step 5: Batch** — `AGENTPLAY_PROGRESS_OBSERVE=<missions completed observe> bash Tools/run-adapter-batch.sh /e/PetRPG 3`. Pass: `wins` at least 2/3.
- [ ] **Step 6: Safety** — save hash and PlayerPrefs export identical to the baseline (excluding `Unity\` only if Task 1 recorded it as Unity's own cache); `git -C /e/PetRPG status --porcelain` identical to the baseline.
- [ ] **Step 7: Unlink** — `Tools/link-agentplay.ps1 -ProjectPath E:\PetRPG -Unlink` (keep `AgentPlay/` for the user unless they ask for `-Purge`); refresh the Editor (or batchmode compile) and confirm no `error CS`; `git status --porcelain` identical to the baseline.

---

### Task 9: Notes, regression, review, merge

- [ ] **Step 1: Regression** — `dotnet test AgentPlay.sln`, `bash Tools/unity-tests.sh`, `bash Tools/check-generic.sh`, `bash Tools/test-link-agentplay.sh`, `bash Tools/e2e-m2a.sh`, `bash Tools/e2e-m3-reflex.sh`, `bash Tools/e2e-m2c.sh script`, `bash Tools/e2e-m4a-realtime.sh`, `bash Tools/e2e-m4b-check.sh`, `python -I Tools/test_telemetry_trend.py`. All pass.
- [ ] **Step 2: Notes** — `docs/notes/m4b-verification.md` (Vietnamese): each "xong" item of the spec with evidence (tier logs, batch table, save hash comparison, git status before/after), dry-run findings and skill fixes, rulings, known limits.
- [ ] **Step 3: Review** — fresh whole-branch reviewer (opus) with the spec, this plan, the ledger and the notes; Review Focus lines above must be answered explicitly. Fix pass with a failing test first for each accepted finding; rerun the affected E2E.
- [ ] **Step 4: Merge** — `git checkout main && git merge --no-ff feat/m4b-adapt`; update memory (`agentplay-status`).
- [ ] **Step 5: Publish (ask the user first)** — push `main`, tag `v0.5.0-m4b`, GitHub release. Only after the user says yes in this session.
