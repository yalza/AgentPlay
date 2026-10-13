# AgentPlay M4a: Realtime, Router, Prefetch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the brain play without pausing the game (attention signals instead of pauses), route quick reactions to Haiku and strategy to Opus, prefetch the next plan, and measure the calls-per-minute trend across learning sessions.

**Architecture:** Unity sends `attention` where turn-based mode would pause, and reports a time split (safe/plan/reflex). The runner replaces its sequential read-then-think loop with an async loop driven by a pure `DecisionCoordinator` state machine (one decision call at a time, coalescing, optional prefetch). A pure `ModelRouter` picks model, effort and step limit per call; `OpusBrain` applies them through per-request overrides.

**Tech Stack:** Unity 6000.0.77f1 (C# 9, NUnit), .NET 9 (xUnit), Newtonsoft.Json, `claude -p`.

**Spec:** `docs/superpowers/specs/2026-10-13-agentplay-m4a-realtime-design.md`

## Global Constraints

- Protocol version 4 (`ProtocolVersion.Current = 4`). Turn-based stays the default mode.
- `Packages/com.agentplay.core` and `Runner/` stay generic (`Tools/check-generic.sh`).
- Router defaults: `fast_model` `claude-haiku-5-5`, `fast_effort` `low`, `fast_max_steps` 3, `escalate_after` 2, `escalate_window_s` 30; model ids only in `runner.json`.
- One decision call at a time; at most one prefetch call; prefetch only in realtime with the Opus brain.
- Prefetch prompt sentence: `Plan <id> is on its last step (<do>). Plan what to do after it finishes, assuming it succeeds.`
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. New Unity files get Unity-generated `.meta` committed. Large code edits go through scratchpad Python scripts (Bash heredocs mangle `\n`).

## Review Focus

1. Unity leaves Play mode while a realtime decision call is in flight: the call is cancelled (CLI process killed), no plan is sent to the dead connection, the next connection starts clean (Program loop cancels all calls on disconnect; checked in Task 6/7 runs: no surviving `claude.exe`).
2. The brain returns null (call budget, model failure) in realtime: the run finishes with exit code 4, no hang (Task 4 test `NullPlanFinishesTheRun`).
3. `attention` arrives after `game.won`/`game.lost`: no new call (Task 2 test `AttentionAfterGameOverFinishes`).
4. A prefetched plan completes after an important event invalidated it: it is never sent (Task 4 test `InvalidatedPrefetchIsDropped`).
5. Script brain with `--mode realtime`: prefetch is off, so scripted plans are consumed in order (Task 2 test `ScriptBrainDoesNotPrefetch`).

---

### Task 1: Unity attention and time split (protocol 4)

**Files:**
- Modify: `Packages/com.agentplay.core/Contract/Protocol/MessageTypes.cs`, `Messages.cs`
- Modify: `Packages/com.agentplay.core/Runtime/Execution/ExecutorCore.cs`, `AgentExecutorHost.cs`
- Modify: `Packages/com.agentplay.core/Runtime/Session/SessionCore.cs`, `StepPolicy.cs`
- Test: `Runner.Tests/Contract/ProtocolV4Tests.cs`, `Tests/EditMode/SessionCoreRealtimeTests.cs`, `Tests/EditMode/ExecutorReflexTests.cs` (time split cases), fix `SessionCorePlanTests.FakeExecutor`

**Interfaces:**
- Produces:
  - `TimeSplit { double Safe, Plan, Reflex }` (`safe, plan, reflex`), `AttentionMessage { string Reason; double T; JObject State; TimeSplit TimeSplit }` (`reason, t, state, time_split`), `PausedMessage.TimeSplit`, `MessageTypes.Attention = "attention"`, `ProtocolVersion.Current = 4`.
  - `ExecutorCore.TimeSplit` (live object), `IPlanExecutor.TimeSplit`.
  - `StepPolicy.OnImportantEvent()/OnPlanEnded()` return the reason in both modes; `OnTick` budget stays turn-based only. `SessionCore.Pause(reason)` in realtime sends `attention` (no pause, `AwaitingResume` untouched); `set_mode realtime` sends `attention` reason `mode`.

- [ ] **Step 1: Failing tests.** Contract:

```csharp
public class ProtocolV4Tests
{
    [Fact]
    public void AttentionRoundTrips()
    {
        var m = new AttentionMessage { Reason = "event", T = 3, State = new JObject { ["x"] = 1 }, TimeSplit = new TimeSplit { Safe = 1, Plan = 2, Reflex = 0.5 } };
        var back = MessageCodec.Decode(MessageCodec.Encode(MessageTypes.Attention, 1, m)).As<AttentionMessage>();
        Assert.Equal("event", back.Reason);
        Assert.Equal(0.5, back.TimeSplit.Reflex);
        Assert.Equal(4, ProtocolVersion.Current);
    }
}
```

Unity (`SessionCoreRealtimeTests`, same fixture style as `SessionCorePlanTests`: FakeHost, FakeExecutor with a `TimeSplit` property):
- `SetModeRealtimeSendsAttentionMode`: after `set_mode realtime`, one `attention` with reason `mode`, host not paused.
- `PlanEndInRealtimeSendsAttentionPlan`: `OnFrameEnd([plan_done])` → `attention` reason `plan` with state and `time_split`, not paused.
- `UnhandledImportantEventInRealtimeSendsAttention`: `OnFrameEnd([], true, ["guard.spotted"])` → `attention` reason `event`.
- `HandledEventInRealtimeSendsNothing`: `OnFrameEnd([rule_fired], true, ...)` → no `attention`.
- `TurnBasedStillPauses`: existing behaviour: `paused` with `time_split`.

Executor (`ExecutorReflexTests`): `TimeSplitCountsSafePlanAndReflex`: before control nothing counted; plan running 0.2 s → Plan 0.2; reflex 0.3 s → Reflex 0.3; no plan → Safe.

- [ ] **Step 2:** run `dotnet test` and `bash Tools/unity-tests.sh` → compile errors.
- [ ] **Step 3: Implement.** In `ExecutorCore.Tick`, after `_time += deltaTime`, accumulate per branch actually executed: in the reflex branch `TimeSplit.Reflex += deltaTime`; in plan branches `TimeSplit.Plan += deltaTime`; in `RunSafe` when `ControlTaken` `TimeSplit.Safe += deltaTime`. `SessionCore`:

```csharp
void Pause(PauseReason reason)
{
    if (_step.Mode == Modes.Realtime)
    {
        Send(MessageTypes.Attention, new AttentionMessage { Reason = reason.ToString().ToLowerInvariant(), T = _host.GameTime, State = _snapshot(), TimeSplit = Split() });
        return;
    }
    ... existing ...
}
TimeSplit Split() => _executor?.TimeSplit == null ? new TimeSplit() : new TimeSplit { Safe = _executor.TimeSplit.Safe, Plan = _executor.TimeSplit.Plan, Reflex = _executor.TimeSplit.Reflex };
```

`AnnouncePause` adds `TimeSplit = Split()`. In `Dispatch(SetMode)` realtime branch, after unpausing: `Pause(PauseReason.Mode)`. `HandlePlan` invalid-plan path: in realtime `_step.OnPlanEnded()` now returns `Plan` → `Pause` sends attention (fine). `StepPolicy.Stop`: drop the `Mode != TurnBased` early return (budget reset stays). Update any StepPolicy test that expected `None` in realtime (record as ruling).

- [ ] **Step 4:** both suites green.
- [ ] **Step 5: Commit** `feat(session): realtime attention signal and executor time split (protocol 4)` (with `.meta` of new test file).

---

### Task 2: Runner session in realtime

**Files:**
- Modify: `Runner/RunnerSession.cs`, `Runner/RunnerOptions.cs`, `Runner/Brains/BrainContext.cs`
- Test: `Runner.Tests/RunnerSessionTests.cs`, `Runner.Tests/RunnerOptionsTests.cs`

**Interfaces:**
- Produces:
  - `RunnerOptions`: `--mode realtime` allowed with `--brain`; `Router` (`--router on|off`, default on), `Prefetch` (bool; default true only when `--brain opus`; `--no-prefetch` turns off).
  - `RunnerSession.Realtime` (bool: options.Mode == realtime); `attention` handled like `paused` in brain mode (sets `DecisionPending`, `PauseReason`, `State`, `PauseT`; game over → finish); `OnPlan` in realtime returns only the `plan` message.
  - Prefetch signals: `RunnerSession.PrefetchOpportunityPlanId` (string, set when `plan_status step_done` moves `LastPlan` onto its last step, i.e. `step == LastPlan.Steps.Count - 2`; cleared by `TakePrefetchOpportunity()`), `RunnerSession.Invalidated` (bool, set by `attention` reason `event` or `plan_status plan_failed`; cleared by `TakeInvalidated()`).
  - `RunnerSession.LastEndedPlanId` and `LastEndedDone` (set by `plan_done`/`plan_failed` statuses).
  - `BrainContext.Clone()` (copies lists), `BrainContext.Route` (`RouteDecision`, Task 3), `BrainContext.PrefetchNote` (string).

- [ ] **Step 1: Failing tests** (RunnerSessionTests):

```csharp
[Fact]
public void AttentionOpensADecisionWithoutResume()
{
    var s = Session("--brain", "script", "x.json", "--mode", "realtime");
    s.Handle(Env(MessageTypes.Hello, Hello()));
    s.Handle(Env(MessageTypes.Attention, new AttentionMessage { Reason = "mode", T = 1, State = new JObject() }));
    Assert.True(s.DecisionPending);
    var ctx = s.TakeDecisionContext();
    Assert.Equal("mode", ctx.PauseReason);
    var outgoing = s.OnPlan(new Plan { Id = "p1", Steps = { new PlanStep { Do = "wait" } }, TimeoutSeconds = 10 });
    Assert.Equal(new[] { MessageTypes.Plan }, outgoing.Select(o => o.Type));
}

[Fact]
public void AttentionAfterGameOverFinishes()
{
    var s = Session("--brain", "script", "x.json", "--mode", "realtime");
    s.Handle(Env(MessageTypes.Hello, Hello()));
    s.Handle(Env(MessageTypes.Event, new EventMessage { Name = "game.won", Important = true }));
    s.Handle(Env(MessageTypes.Attention, new AttentionMessage { Reason = "event", T = 5 }));
    Assert.True(s.Finished);
    Assert.False(s.DecisionPending);
    Assert.Equal(0, s.ExitCode);
}

[Fact]
public void LastStepOpensAPrefetchOpportunity()
{
    var s = Session("--brain", "script", "x.json", "--mode", "realtime");
    s.Handle(Env(MessageTypes.Hello, Hello()));
    s.Handle(Env(MessageTypes.Attention, new AttentionMessage { Reason = "mode", T = 1 }));
    s.TakeDecisionContext();
    s.OnPlan(new Plan { Id = "p1", Steps = { new PlanStep { Do = "wait" }, new PlanStep { Do = "wait" } }, TimeoutSeconds = 10 });
    s.Handle(Env(MessageTypes.PlanStatus, new PlanStatusMessage { PlanId = "p1", Status = PlanStatuses.StepDone, Step = 0 }));
    Assert.Equal("p1", s.TakePrefetchOpportunity());
    Assert.Null(s.TakePrefetchOpportunity());
    s.Handle(Env(MessageTypes.Attention, new AttentionMessage { Reason = "event", T = 2 }));
    Assert.True(s.TakeInvalidated());
}
```

Options: `RealtimeWithBrainIsAllowed` (`--brain opus --mode realtime` → Mode realtime, Prefetch true, Router true); `NoPrefetchAndRouterOff`; `ScriptBrainDoesNotPrefetch` (`--brain script x --mode realtime` → Prefetch false).

- [ ] **Step 2:** fail. **Step 3:** implement (remove the `--brain plays in turn_based mode only` throw; set `o.Mode ??= TurnBased`). In `Dispatch`, route `MessageTypes.Attention` through the same code as `Paused` (extract `OnDecisionMessage(string reason, double t, JObject state)`), print `attention t=.. reason=..`. Track statuses in the `PlanStatus` case. **Step 4:** pass. **Step 5: Commit** `feat(runner): realtime session handling (attention, no resume, prefetch signals)`.

---

### Task 3: Model router and per-call overrides

**Files:**
- Create: `Runner/Brains/ModelRouter.cs`
- Modify: `Runner/RunnerConfig.cs`, `Runner/runner.json`, `Runner/Brains/ModelClient.cs` (`ModelRequest.Model`, `CallRecord.Role`), `ClaudeCodeModelClient.cs`, `AnthropicModelClient.cs`, `OpusBrain.cs`, `PromptBuilder.cs`
- Test: `Runner.Tests/ModelRouterTests.cs`, `Runner.Tests/OpusBrainTests.cs`, `Runner.Tests/ClaudeCodeModelClientTests.cs`, `Runner.Tests/PromptBuilderTests.cs`

**Interfaces:**
- Produces:
  - `RouterConfig { bool Enabled = true; string FastModel = "claude-haiku-5-5"; string FastEffort = "low"; int FastMaxSteps = 3; int EscalateAfter = 2; double EscalateWindowSeconds = 30; }` (`enabled, fast_model, fast_effort, fast_max_steps, escalate_after, escalate_window_s`) at `RunnerConfig.Router`.
  - `RouteDecision { string Role; string Model; string Effort; int MaxSteps; }` with roles `fast`, `strategy`, `prefetch`.
  - `ModelRouter(RunnerConfig config)`; `RouteDecision Route(BrainContext ctx)` (uses `ctx.PauseReason`, `ctx.PlanStatuses`, `ctx.PauseT`); `RouteDecision Prefetch()`.
  - `ModelRequest.Model` (override); `CallRecord.Role`.
  - `OpusBrain` reads `context.Route`: sets `request.Model/Effort`; when `Route.MaxSteps > 0` and the plan has more steps → rejected with `steps: at most N steps for a quick plan` and retried once.
  - `PromptBuilder.BuildTurn`: when `ctx.Route?.MaxSteps > 0` append `Answer with a short plan of at most N steps.`; when `ctx.PrefetchNote` is set, append it under `# Look ahead`.

Routing rules (spec section 4):

```csharp
public RouteDecision Route(BrainContext ctx)
{
    if (!_config.Router.Enabled) return Strategy();
    bool replanRule = ctx.PlanStatuses.Any(s => s.Status == PlanStatuses.PlanFailed && (s.Reason ?? "").StartsWith("replan:", StringComparison.Ordinal));
    bool fast = ctx.PauseReason == "event" || (ctx.PauseReason == "plan" && replanRule);
    if (fast)
    {
        _recentFast.RemoveAll(t => ctx.PauseT - t > _config.Router.EscalateWindowSeconds);
        if (_recentFast.Count >= _config.Router.EscalateAfter)
        {
            _recentFast.Clear();
            return Strategy();
        }
        _recentFast.Add(ctx.PauseT);
        return new RouteDecision { Role = "fast", Model = _config.Router.FastModel, Effort = _config.Router.FastEffort, MaxSteps = _config.Router.FastMaxSteps };
    }
    _recentFast.Clear();
    return Strategy();
}
RouteDecision Strategy() => new RouteDecision { Role = "strategy", Model = _config.Model, Effort = _config.Effort };
public RouteDecision Prefetch() => new RouteDecision { Role = "prefetch", Model = _config.Model, Effort = _config.Effort };
```

- [ ] **Step 1: Failing tests** (ModelRouterTests): event → fast Haiku low 3; plan done → strategy; plan failed `replan:0` → fast; plan failed `timeout` → strategy; mode → strategy; third fast within 30 s → strategy (and counter reset); fast decisions 40 s apart stay fast; `enabled:false` → always strategy. OpusBrainTests: `QuickPlanTooLongIsRetried` (route MaxSteps 3, first reply 4 steps, second 2 → plan ok, 2 requests, second contains `at most 3 steps`), `RouteSetsModelAndEffortAndRole` (request.Model == "claude-haiku-5-5", Effort "low", record.Role "fast"). ClaudeCode: `RequestModelOverridesConfig`. PromptBuilder: `TurnMentionsStepLimitAndLookAhead`.
- [ ] **Step 2:** fail. **Step 3:** implement; `runner.json` gains the `router` block. **Step 4:** pass. **Step 5: Commit** `feat(runner): model router (Haiku for quick reactions, Opus for strategy)`.

---

### Task 4: Decision coordinator, async loop, prefetch

**Files:**
- Create: `Runner/DecisionCoordinator.cs`
- Modify: `Runner/Program.cs` (loop), `Runner/Brains/ClaudeCodeModelClient.cs` (verify kill on cancel)
- Test: `Runner.Tests/DecisionCoordinatorTests.cs`, `Runner.Tests/ClaudeCodeModelClientTests.cs`

**Interfaces:**
- Consumes: Task 2 session signals, Task 3 router.
- Produces:

```csharp
public enum CallKind { Decision, Prefetch }
public sealed class CoordinatorCommand
{
    public string Kind;        // "start", "send", "cancel_prefetch"
    public CallKind Call;      // for start
    public BrainContext Context; // for start
    public Plan Plan;          // for send (may be null = brain stopped)
}
public sealed class DecisionCoordinator
{
    public DecisionCoordinator(bool prefetchEnabled);
    public bool DecisionInFlight { get; }
    public bool PrefetchInFlight { get; }
    public List<CoordinatorCommand> OnDecisionNeeded(BrainContext ctx, string endedPlanId, bool endedDone);
    public List<CoordinatorCommand> OnPrefetchOpportunity(BrainContext ctx, string planId);
    public List<CoordinatorCommand> OnInvalidated();
    public List<CoordinatorCommand> OnCallCompleted(CallKind kind, Plan plan);
    public int PrefetchRequested, PrefetchUsed, PrefetchDiscarded;
}
```

Rules:
- `OnDecisionNeeded`: if a prefetch exists (in flight or done), is valid, `ctx.PauseReason == "plan"`, `endedDone` and `endedPlanId == prefetchFor` → use it: done → `send(result)`; in flight → remember the decision context and wait. Otherwise discard any prefetch (`cancel_prefetch` if in flight, `PrefetchDiscarded++`), then: decision in flight → keep `ctx` as the pending one (latest wins); else `start(Decision, ctx)`.
- `OnPrefetchOpportunity`: only if enabled, no decision in flight, no prefetch → `start(Prefetch, ctx)` (`PrefetchRequested++`).
- `OnInvalidated`: prefetch exists → discard (`cancel_prefetch` if in flight). If a decision was waiting for that prefetch → `start(Decision, waitingCtx)`.
- `OnCallCompleted(Decision, plan)`: `send(plan)`; then if a pending ctx exists → `start(Decision, pending)`.
- `OnCallCompleted(Prefetch, plan)`: invalid/discarded → nothing; `plan == null` → drop, and if a decision waits → `start(Decision, waitingCtx)`; waiting → `send(plan)` (`PrefetchUsed++`); else store.

Program loop (both modes):

```csharp
var coordinator = new DecisionCoordinator(options.Prefetch && opus != null && session.Realtime);
var router = opus != null ? new ModelRouter(config) : null;
var calls = new Dictionary<Task<Plan>, (CallKind kind, CancellationTokenSource cts)>();
var lines = client.ReadLinesAsync(cts.Token).GetAsyncEnumerator();
Task<bool> read = lines.MoveNextAsync().AsTask();
while (!session.Finished)
{
    var done = await Task.WhenAny(calls.Keys.Cast<Task>().Append(read));
    if (done == read)
    {
        if (!await read) break;                       // Unity closed
        ...decode, session.Handle → send outgoing...
        if (session.TakeInvalidated()) Execute(coordinator.OnInvalidated());
        if (session.DecisionPending && brain != null)
        {
            var ctx = session.TakeDecisionContext().Clone();
            if (router != null) ctx.Route = router.Route(ctx);
            telemetry?.OnDecisionStarted();
            Execute(coordinator.OnDecisionNeeded(ctx, session.LastEndedPlanId, session.LastEndedDone));
        }
        var opportunity = session.TakePrefetchOpportunity();
        if (opportunity != null) { var p = session.PeekContext().Clone(); p.Route = router?.Prefetch(); p.PauseReason = "prefetch"; p.PrefetchNote = ...; Execute(coordinator.OnPrefetchOpportunity(p, opportunity)); }
        read = lines.MoveNextAsync().AsTask();
    }
    else
    {
        var task = (Task<Plan>)done; var (kind, callCts) = calls[task]; calls.Remove(task);
        Plan plan = task.IsCanceled ? null : await task;   // exceptions surface
        if (task.IsCanceled) continue;
        Execute(coordinator.OnCallCompleted(kind, plan));
    }
}
foreach (var c in calls.Values) c.cts.Cancel();         // disconnect or game over
```

`Execute`: `start` → `calls[brain.NextPlanAsync(ctx, linkedToken)] = (kind, cts)`; `send` → `foreach (var m in session.OnPlan(plan)) await Send(...)` and `telemetry?.OnPlanSent(prefetchUsed)`; `cancel_prefetch` → cancel that call's cts. `session.PeekContext()` returns the live context without clearing `DecisionPending`.

- [ ] **Step 1: Failing tests** (DecisionCoordinatorTests, synchronous, `Ctx(reason)` helper builds a `BrainContext`):
  - `FirstDecisionStartsACall`
  - `DecisionWhileBusyIsCoalescedAndStartedAfter` (two needs during a call → one more start, with the latest ctx)
  - `PrefetchUsedWhenPlanDoneWithoutSurprise` (opportunity → start prefetch; prefetch completes; decision for plan done of same id → `send` of the prefetched plan, no start; PrefetchUsed 1)
  - `DecisionWaitsForPrefetchInFlight` (decision before prefetch completes → no command; prefetch completes → send)
  - `InvalidatedPrefetchIsDropped` (opportunity, invalidate → cancel_prefetch; prefetch completes later → nothing sent; PrefetchDiscarded 1)
  - `PrefetchForOtherPlanIsDiscarded` (ended plan id differs → cancel/discard + start decision)
  - `PlanFailedDoesNotUsePrefetch` (`endedDone false` → discard + start)
  - `NullPrefetchFallsBackToADecision` (waiting decision, prefetch returns null → start decision)
  - `NoPrefetchWhenDisabledOrBusy`
  - `NullPlanFinishesTheRun` (decision completes with null → `send(null)` command, which `session.OnPlan(null)` turns into exit 4)
  Not unit-tested, by ruling: killing the CLI on cancel (`ClaudeCodeModelClient` already kills on `OperationCanceledException`) and cancelling calls on disconnect (Program loop) are checked in Task 6/7 runs by confirming no `claude.exe` survives a run and the runner exits cleanly after Play mode ends mid-call.
- [ ] **Step 2:** fail. **Step 3:** implement coordinator + Program loop. **Step 4:** `dotnet test` green; `bash Tools/e2e-m2a.sh` (turn-based through the new loop) passes. **Step 5: Commit** `feat(runner): async decision loop with coalescing and plan prefetch`.

---

### Task 5: Telemetry additions and trend tool

**Files:**
- Modify: `Runner/Telemetry.cs`, `Runner/Program.cs`
- Create: `Tools/telemetry_trend.py`, `Tools/test_telemetry_trend.py`
- Test: `Runner.Tests/TelemetryTests.cs`

**Interfaces:**
- Produces: `TelemetryRecorder.Mode` (set from options), `OnDecisionStarted()`, `OnPlanSent(bool fromPrefetch)` (wall-clock wait from the last decision start; prefetch sends also go to `prefetch.used_wait_ms`), `SetPrefetchCounts(int requested, int used, int discarded)`, `OnAttention(AttentionMessage)` (time split, pauses_by_reason counts under the reason), `OnPaused` takes `TimeSplit`; `CallRecord.Role` feeds `calls_by_model[model].role[role]` and per-model `wait_ms`. Report fields: `mode`, `calls_by_model`, `prefetch`, `time_split {safe, plan, reflex, safe_ratio, plan_ratio, reflex_ratio}`, `decision_wait_ms {median, p90}`.
- `Tools/telemetry_trend.py DIR [--last N]`: prints one row per file (sorted by name) and the slope; exit 0 when last < first and slope < 0, 1 otherwise, 2 when fewer than 2 files.

- [ ] **Step 1: Failing tests:** TelemetryTests `CountsByModelRoleAndPrefetch`, `TimeSplitFromLastAttention`, `DecisionWaitIsMeasured` (use an injectable clock: `TelemetryRecorder(Func<DateTime> now = null)`). Python test (`python -I Tools/test_telemetry_trend.py`): builds a temp dir with 5 JSON files (calls_per_game_minute 10, 9, 9.5, 7, 6) → exit 0; reversed → exit 1.
- [ ] **Step 2:** fail. **Step 3:** implement. **Step 4:** pass. **Step 5: Commit** `feat(telemetry): per-model calls, prefetch, time split, decision wait; trend tool`.

---

### Task 6: Realtime E2E without a model

**Files:**
- Create: `Tools/e2e-m4a-realtime.sh`, `Tools/verify_m4a_realtime.py`

- [ ] **Step 1:** script like `e2e-m3-reflex.sh` but: no fixture reflexes (remove `reflexes.json` for the run, restore after), `--brain script win.json --mode realtime --no-reflect`, logs in `.e2e/heist-realtime`.
- [ ] **Step 2:** verifier checks: runner and Unity exit 0; no `paused t=` line after `hello`; at least 4 `attention t=` lines; `game over: won`; telemetry `mode == realtime`, `time_split.plan > 0`, `model_calls == 0`; no `unity error:`.
- [ ] **Step 3:** run until ALL CHECKS PASSED (tune nothing in the checks; if `win.json` timing fails in realtime, add `scripts/win-realtime.json` and record a ruling). Also confirm no stray `claude.exe` processes after any Task 7 run (`tasklist | grep -i claude`).
- [ ] **Step 4: Commit** `test(e2e): realtime Heist run with the script brain`.

---

### Task 7: Real-model runs, notes, review, merge

**Files:**
- Create: `Tools/run-heist-realtime.sh`, `Tools/run-heist-learning.sh`, `docs/notes/m4a-verification.md`

- [ ] **Step 1:** `run-heist-realtime.sh N`: like `run-heist-opus.sh` with `--mode realtime`, logs `.e2e/heist-realtime-opus/run-$i.log`, copies each telemetry file, prints wins, per-run calls by role, prefetch used, decision wait median. Playbook/reflexes from M3 learn E2E stay in place (approved `R-L2` copied into `reflexes.json` before the batch).
- [ ] **Step 2:** run 5; check "xong" items 2–4. If wins < 3: tune generic runner instructions (ruling), rerun once, report both.
- [ ] **Step 3:** `run-heist-learning.sh 5`: clears playbook/inbox/reflexes/telemetry; for i in 1..5: `HeistAutoPlay.ApproveOnly` (ignore exit 1 when nothing to approve), realtime Opus run with `AGENTPLAY_SESSION_TAG=play` (runner auto-reflects); then `python -I Tools/telemetry_trend.py Demos/Heist/AgentPlay/Telemetry`. Check item 5; if it fails, rerun once and report both.
- [ ] **Step 4:** full regression (dotnet, unity-tests, check-generic, e2e-m2a, e2e-m3-reflex, e2e-m2c script, e2e-m4a-realtime).
- [ ] **Step 5:** notes `docs/notes/m4a-verification.md` (Vietnamese), fresh whole-branch review (opus), fix pass, merge to main, memory update.
