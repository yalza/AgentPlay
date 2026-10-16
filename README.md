# AgentPlay

AgentPlay lets a Claude agent play your Unity game inside the Editor, learn from the sessions it and you play, and react in real time. A Claude Code skill (`agentplay-adapt`) connects it to an existing game for you.

Hướng dẫn tiếng Việt đầy đủ: [docs/HUONG-DAN.md](docs/HUONG-DAN.md).

> Status: `v0.5.0-m4b`. Milestones M1–M4b are done (see [Roadmap](#roadmap)). Each milestone's evidence is in [`docs/notes/`](docs/notes/).

## What it is for

- **Automated playtesting.** Point the agent at a goal ("finish the first 3 missions", "steal the three items") and let it play from a fresh save. It reports whether it got there, how long it took and where it got stuck, without anyone touching the controller.
- **Regression play.** Re-run the same goal after every change to the game and compare wins, model calls and time.
- **Learning from players.** Sessions you play yourself and tag `play` become lessons in a playbook; lessons that keep holding become reflexes, instant reactions that run inside the game without a model call.
- **A bridge for your own tools.** The TCP protocol (newline-delimited JSON) is documented below; anything that speaks it can observe and drive the game.

It is not a bot framework for shipped builds (it only runs in the Editor) and not a twitch-reflex player: one model call takes 3–15 s, so it suits games where plans of several steps make sense (strategy, management, stealth, progression, mission flows).

## How it works

```
Unity Editor (Play mode)                                 Runner (.NET 9 console)
┌──────────────────────────────────────┐   TCP 47800+   ┌──────────────────────────────────┐
│ Adapter: [AgentObserve] [AgentAction]│ ─── hello ───▶ │ Session loop                     │
│          AgentEvents.Emit(...)       │ ─ state/event ▶│ Brain: Opus / Haiku (claude -p)  │
│ Registry: scans attributes on Play   │ ◀─── plan ──── │ Router: quick reaction vs.       │
│ Executor: runs plan steps per frame, │ ─ plan_status ▶│         strategy                 │
│           rules, reflexes            │ ─ attention ──▶│ Prefetch: next plan ahead        │
│ Trace recorder: every session        │                │ Reflector: trace -> playbook     │
│ AgentPlay window: tag, reflexes      │                │ Telemetry, check, reflect        │
└──────────────────────────────────────┘                └──────────────────────────────────┘
```

1. **Adapter.** The game marks what the agent may see (`[AgentObserve]`, `[AgentObserveList]`), what it may do (`[AgentAction]`, one of them `Safe`), and which events matter (`AgentEvents.Emit`, with `Important` and `Outcome`). `game.won` and `game.lost` end a round. The adapter lives in the game's code, or in a separate local-only folder written by the `agentplay-adapt` skill.
2. **Session.** When Play mode starts, the package scans the attributes, opens a TCP server on `127.0.0.1:47800` (up to 47809) and sends `hello`: game name, version, `AgentPlay/brief.md`, every observe, action and event.
3. **Plans.** The runner asks Claude for a **plan**: a few steps (`do` an action with `args`, optionally `until` a condition), reaction `rules` (`when` a condition or `event:<name>`, `do` an action, `then` `resume`/`next`/`replan`/`abort`) and a `timeout_s`. The plan is validated against `hello` before it is sent; an invalid plan is sent back to the model once with the errors.
4. **Execution.** The Unity-side executor runs the plan frame by frame. Priority each frame: reflexes, then the plan's rules, then the current step. While no plan is available the `Safe` action runs.
5. **When the agent needs to decide again** (plan done, plan failed, an important event no rule or reflex handled):
   - *turn-based mode* (default): the Editor pauses, so the result does not depend on how long the model thinks;
   - *realtime mode*: the game keeps running and Unity sends `attention`; quick reactions go to a fast model (Haiku), everything else to the strategy model (Opus); the next plan is requested ahead of time while the current one still runs.
6. **Learning.** Every session is recorded to `AgentPlay/Traces/`. After a session tagged `play`, the **reflector** (Opus) reads the trace and updates `AgentPlay/playbook.json`/`.md`: lessons with confirmations and contradictions. A lesson confirmed 3 times with no contradiction can be proposed as a **reflex** (`when <event> do <action>`); you approve it in the AgentPlay window. Approved reflexes run in the frame the event happens, without a model call, and are suspended automatically if they keep failing (over 30 % bad outcomes after 5 fires).
7. **Telemetry.** Each run writes `AgentPlay/Telemetry/<session>.json`: model calls per game minute by model and role, wait times, plan failures, prefetch use, reflex fires, the split of game time between safe behaviour, plans and reflexes, tokens.

### Protocol

Newline-delimited JSON over TCP; every message has `type` and `seq`. Unity is the server.

| Direction | `type` | Meaning |
| --- | --- | --- |
| Unity → runner | `hello` | Game, version, brief, observes, actions, events, modes, active reflexes, project root |
| Unity → runner | `state` | Snapshot of every observe and the game time (answer to `get_state`) |
| Unity → runner | `event` | A game event with a snapshot |
| Unity → runner | `plan_status` | `step_done`, `plan_done`, `plan_failed` (with reason), `rule_fired` |
| Unity → runner | `paused` / `attention` | A decision is needed (turn-based / realtime), with the reason (`mode`, `plan`, `event`, `budget`) |
| Unity → runner | `reflex`, `reflex_end` | A reflex fired / finished |
| Unity → runner | `waiting`, `error` | No plan, safe behaviour running / a message could not be handled |
| Runner → Unity | `set_mode` | `turn_based` or `realtime` |
| Runner → Unity | `plan`, `stop_plan` | Replace the current plan / stop it |
| Runner → Unity | `resume` | Turn-based: run up to `max_game_seconds` |
| Runner → Unity | `get_state` | Ask for a snapshot now |

## Requirements

- Windows 10/11 (tested), Unity **6000.0** or later, the project opened in the Editor (source needed).
- .NET 9 SDK (runner and tests).
- [Claude Code](https://claude.com/claude-code) CLI signed in: the default backend runs `claude -p`. The `api` backend uses `ANTHROPIC_API_KEY` instead.
- Git Bash and Python 3 for the tool scripts.

## Install

```powershell
git clone https://github.com/yalza/AgentPlay.git E:\AgentPlay
cd E:\AgentPlay
dotnet build Runner/AgentPlay.Runner.csproj
setx AGENTPLAY_HOME E:\AgentPlay
```

### Add the package to a game

Pick one:

- **Local-only link (recommended for team repos):** nothing tracked by git changes.

  ```powershell
  powershell -File Tools/link-agentplay.ps1 -ProjectPath D:\MyGame -LocalOnly
  ```

  The package is linked through a junction at `Packages/com.agentplay.core`; the junction, `AgentPlay/`, `Assets/AgentPlayAdapter/` and an AgentPlay-made `Assets/csc.rsp` are excluded through `.git/info/exclude`. Remove everything with `-Unlink` (add `-Purge` to also delete `AgentPlay/` with traces and playbook).
- **Link for your own project:** same command without `-LocalOnly`; the junction goes into `.gitignore`.
- **manifest reference:** `"com.agentplay.core": "file:E:/AgentPlay/Packages/com.agentplay.core"` in `Packages/manifest.json`.

The package depends on `com.unity.nuget.newtonsoft-json` 3.2.1. The virtual gamepad helper compiles only when the Input System package is present.

### Install the `agentplay-adapt` skill for Claude Code

```powershell
New-Item -ItemType Junction -Path "$env:USERPROFILE\.claude\skills\agentplay-adapt" -Target "$env:AGENTPLAY_HOME\.claude\skills\agentplay-adapt"
```

## Use

### A. Let Claude Code write the adapter (any existing game)

Open Claude Code in your game project and ask, for example:

> Use agentplay-adapt so the agent can finish the first 3 missions from a fresh save.

The skill works in five steps:

1. **Survey** (read only): player controller and input path, the managers behind the goal, how the game saves, asmdef layout, start scene, Editor blockers.
2. **Proposal:** tables of observes, actions, events, the win condition and how a run starts and ends. **It stops here for your approval.**
3. **Attach:** local-only link, adapter in `Assets/AgentPlayAdapter/`, `AgentPlay/brief.md`, a no-model smoke plan, and a save hook that parks your own save during runs and gives it back afterwards.
4. **Verify in tiers:** compiles; `AgentPlay.Runner check` passes; the smoke plan runs; Opus reaches the goal.
5. **Report:** files added, how to remove them, each tier's result.

Rules the skill keeps: no tracked file changes, your save is never deleted, actions go through the player's own input path (no teleports, no currency edits).

### B. Write an adapter by hand

```csharp
using AgentPlay;
using UnityEngine;

[AgentGame("My Game", Version = "1")]
[AgentEventInfo("game.won", "Reached the exit", Important = true, Outcome = AgentOutcome.Good)]
public class PlayerAgent : MonoBehaviour
{
    [AgentObserve("player.pos")] public Vector3 Pos => transform.position;

    [AgentAction("goto", "Walk to a zone: start, exit")]
    public AgentActionStatus GoTo(AgentActionContext ctx, string zone)
    {
        // Called every frame until it returns Done or Failed.
        return MoveTowards(zone) ? AgentActionStatus.Done : AgentActionStatus.Running;
    }

    [AgentAction("stop", "Stand still", Safe = true)]
    public AgentActionStatus Stop() => AgentActionStatus.Done;

    void OnReachedExit() => AgentEvents.Emit("game.won", new { }, important: true, outcome: AgentOutcome.Good);
}
```

Write `AgentPlay/brief.md` (goal, how to win, how to lose, what the observes do not show). Full API with examples: [`.claude/skills/agentplay-adapt/reference/attributes.md`](.claude/skills/agentplay-adapt/reference/attributes.md).

### C. Play

Interactive: start the runner, then press Play in the Editor.

```bash
dotnet Runner/bin/Debug/net9.0/AgentPlay.Runner.dll --brain opus                    # turn-based (default)
dotnet Runner/bin/Debug/net9.0/AgentPlay.Runner.dll --brain opus --mode realtime    # game keeps running
```

Unattended (the Editor enters Play mode, plays, and quits with 0 won / 3 lost / 1 deadline / 2 hook failed):

```bash
bash Tools/run-adapter-batch.sh D:/MyGame 3              # 3 Opus runs
bash Tools/run-adapter-batch.sh D:/MyGame 1 plan.json    # a script plan, no model calls
```

Environment: `AGENTPLAY_AUTOPLAY_SCENE` (scene to open), `AGENTPLAY_AUTOPLAY_SECONDS` (deadline), `AGENTPLAY_SESSION_TAG` (`play` to learn from the run), `AGENTPLAY_PROGRESS_OBSERVE` (an observe printed after each run). Any game can add Editor hooks (`AgentPlay.Editor.IAgentAutoPlayHook`) that run before and after an unattended run.

Check an adapter without a model:

```bash
dotnet Runner/bin/Debug/net9.0/AgentPlay.Runner.dll check --idle-exit 120 &
"$UNITY_EXE" -projectPath D:/MyGame -executeMethod AgentPlay.Editor.AutoPlay.Run
```

### D. Learn

1. Open **Window > AgentPlay**, set *Session tag* to `play`, and play yourself (or let the agent play with `AGENTPLAY_SESSION_TAG=play`).
2. After the session the runner runs the reflector (or press *Learn from play sessions*; or `AgentPlay.Runner reflect --project D:/MyGame`).
3. Read the lessons with *Open playbook*. Proposed reflexes appear in the window: *Approve*, *Reject*, *Disable*, *Re-enable*.
4. The playbook is part of every strategy prompt; approved reflexes run in the game.

### Runner reference

```
AgentPlay.Runner [--brain script <file> | --brain opus [--backend claude-code|api] [--model M] [--effort E] [--max-calls N]] [--smoke]
                 [--mode turn_based|realtime] [--router on|off] [--no-prefetch] [--auto-resume] [--resume-seconds N] [--think-ms N]
                 [--ports A-B] [--idle-exit SECONDS] [--no-reflect] [--reflector-smoke]
AgentPlay.Runner reflect --project <game project folder> [--trace <file>]... [--force]
AgentPlay.Runner check [--ports A-B] [--idle-exit SECONDS]
```

Runner exit codes: 0 won, 3 lost, 4 the brain stopped (call budget, model failure, two invalid plans), 5 protocol mismatch with the package, 6 the session never finished. Defaults live in [`Runner/runner.json`](Runner/runner.json): model, effort, call budget (40), reflector thresholds, router (`fast_model`, `fast_max_steps`, `escalate_after`, `escalate_window_s`).

## What a game project gets

| Path | Contents |
| --- | --- |
| `AgentPlay/brief.md` | Your description of the game for the model |
| `AgentPlay/Traces/` | One NDJSON trace per Play session (`<time>-<human|agent>-<tag>.ndjson`) |
| `AgentPlay/playbook.json`, `playbook.md` | Lessons learned from `play` sessions |
| `AgentPlay/Inbox/`, `reflexes.json` | Proposed reflexes and their lifecycle (`proposed`, `active`, `suspended`, `needs_review`, `rejected`) |
| `AgentPlay/Telemetry/` | Per-session numbers |
| `AgentPlay/Runs/` | Model call log of each run |
| `Assets/AgentPlayAdapter/` | The adapter written by `agentplay-adapt` (local-only) |

## Repository layout

| Folder | What it is |
| --- | --- |
| `Packages/com.agentplay.core` | Unity package: contract, registry, executor, bridge, traces, AutoPlay, editor window |
| `Runner/` | .NET runner: session loop, brains, router, prefetch, reflector, telemetry, `check` |
| `Runner.Tests/` | xUnit tests for the runner and the shared contract |
| `.claude/skills/agentplay-adapt/` | The Claude Code skill and its reference files |
| `Demos/Heist` | Stealth demo: steal three items without being caught by the guard |
| `Testbed/` | Minimal Unity project for the package tests |
| `Tools/` | Link script, E2E scripts and verifiers, batch scripts, telemetry trend tool |
| `docs/` | Design specs, implementation plans, verification notes (Vietnamese) |

## Tests

```bash
dotnet test AgentPlay.sln                 # runner and contract
bash Tools/unity-tests.sh                 # package EditMode tests, run inside Testbed
bash Tools/test-link-agentplay.sh         # link script, including -LocalOnly/-Unlink
bash Tools/e2e-m4b-check.sh               # adapter check on Heist, no model calls
bash Tools/e2e-m4a-realtime.sh            # realtime Heist with a script plan, no model calls
python -I Tools/test_telemetry_trend.py
```

`Tools/run-heist-*.sh` and `Tools/run-adapter-batch.sh` without a script use real model calls and your Claude quota. Set `UNITY_EXE` if Unity is not at `/e/6000.0.77f1/Editor/Unity.exe`.

## Limits

- Unity 6000.0+ only, Editor only, Windows tested.
- One model call takes about 3–15 s; fast-reflex games need learned reflexes or are out of scope.
- Real model runs cost Claude quota; the weekly limit can stop a batch (runner exit 4).
- Multiplayer with real people over the network has no latency guarantee.
- Playing from screenshots without an adapter ("level 0") is not built.

## Roadmap

| Milestone | Status |
| --- | --- |
| M1 Foundation: contract, bridge, step control, traces, editor window | done |
| M2 Turn-based play: plan DSL, executor, validator, Opus brain | done |
| M3 Learning: reflector, playbook, reflexes and their review | done |
| M4a Realtime: attention, model router, plan prefetch, trend | done |
| M4b Reach: `agentplay-adapt` skill, generic AutoPlay, `runner check`, local-only linking | done |
| Level 0: play from screenshots without an adapter | not started |

## License

[MIT](LICENSE)
