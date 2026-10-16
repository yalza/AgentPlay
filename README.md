# AgentPlay

AgentPlay lets a Claude agent play your Unity game from the Editor, learn from the sessions it and you play, and react in real time.

You mark what the agent may see and do with a few attributes. A .NET runner connects to the Editor over TCP, asks Claude for a short **plan** (steps, conditions and reaction rules), and the game runs that plan frame by frame. After each session a **reflector** turns the trace into lessons in a playbook. Lessons that hold up become **reflexes**: instant reactions that run inside the game without a model call.

> Status: pre-release `v0.4.0-m4a`. Milestones M1–M4a are done (see [Roadmap](#roadmap)). Results are recorded in [`docs/notes/m4a-verification.md`](docs/notes/m4a-verification.md).

## How it works

```
Unity Editor (Play mode)                         Runner (.NET 9)
┌──────────────────────────────┐   TCP 47800+   ┌─────────────────────────────┐
│ Registry   [AgentObserve]    │ ─ hello ─────▶ │ Brain: Opus / Haiku via     │
│            [AgentAction]     │ ─ state/events▶│        `claude -p`          │
│ Executor   runs plan steps,  │ ◀──── plan ──  │ Router: fast vs strategy    │
│            rules, reflexes   │ ─ attention ─▶ │ Prefetch: next plan ahead   │
│ Trace      every session     │                │ Telemetry, reflector        │
└──────────────────────────────┘                └─────────────────────────────┘
```

- **Turn-based mode** (default): the game pauses while the agent decides.
- **Realtime mode**: the game keeps running. Unity sends `attention` when a decision is needed. Quick reactions (an important event while a plan runs, a rule asking to replan) go to a fast model (Haiku), and everything else goes to the strategy model (Opus). The next plan is requested ahead of time so the agent rarely waits.

## Requirements

- Unity 6000.0 or later
- .NET 9 SDK
- [Claude Code](https://claude.com/claude-code) CLI, signed in (the default backend calls `claude -p`). The `api` backend reads `ANTHROPIC_API_KEY` instead.
- Windows is the tested platform. The tool scripts need Git Bash and Python 3.

## Quick start

1. **Add the package to your game.** Either reference `Packages/com.agentplay.core` in your project's `Packages/manifest.json` (`"com.agentplay.core": "file:<path>/Packages/com.agentplay.core"`), or link it without editing the manifest:

   ```powershell
   powershell -File Tools/link-agentplay.ps1 -ProjectPath D:\MyGame
   ```

2. **Describe the game** for the agent in `<your project>/AgentPlay/brief.md`: the goal, how you win or lose, and anything not visible in the observed state.

3. **Expose state, actions and events:**

   ```csharp
   using AgentPlay;

   [AgentGame("My Game", Version = "1")]
   [AgentEventInfo("game.won", "Reached the exit", Important = true, Outcome = AgentOutcome.Good)]
   public class Player : MonoBehaviour
   {
       [AgentObserve("player.pos")] public Vector3 Pos => transform.position;

       [AgentAction("goto", "Walk to a zone: start, exit")]
       public AgentActionStatus GoTo(AgentActionContext ctx, string zone)
       {
           // Called every frame until it returns Done or Failed.
           return MoveTowards(zone) ? AgentActionStatus.Done : AgentActionStatus.Running;
       }

       void Win() => AgentEvents.Emit("game.won", new { }, important: true, outcome: AgentOutcome.Good);
   }
   ```

   `[AgentObserveList]` exposes a collection (for example, every guard), and `[AgentAction(..., Safe = true)]` marks the default action used while no plan is available.

4. **Build and start the runner,** then enter Play mode:

   ```bash
   dotnet build Runner/AgentPlay.Runner.csproj
   dotnet Runner/bin/Debug/net9.0/AgentPlay.Runner.dll --brain opus --mode realtime
   ```

   Open **Window > AgentPlay** to see the connection, tag a session `play` (learn from it) or `debug`, and review proposed reflexes.

## Adapting your game with Claude Code

The `agentplay-adapt` skill writes the adapter for you. Make it available to Claude Code in your game project:

```powershell
setx AGENTPLAY_HOME E:\path\to\AgentPlay
New-Item -ItemType Junction -Path "$env:USERPROFILE\.claude\skills\agentplay-adapt" -Target "E:\path\to\AgentPlay\.claude\skills\agentplay-adapt"
```

Then, in your game project, ask Claude Code something like "use agentplay-adapt so the agent can finish the first 3 missions". It surveys the code, shows you what the agent will see and do, and after you approve writes a local-only adapter (excluded from git) and verifies it. Remove everything with `Tools/link-agentplay.ps1 -ProjectPath <game> -Unlink`.

### Runner options

```
AgentPlay.Runner [--brain script <file> | --brain opus [--backend claude-code|api] [--model M] [--effort E] [--max-calls N]]
                 [--mode turn_based|realtime] [--router on|off] [--no-prefetch] [--ports A-B] [--idle-exit SECONDS] [--no-reflect]
AgentPlay.Runner reflect --project <game project folder> [--trace <file>]... [--force]
```

Defaults (model, effort, call budget, router, reflector) live in [`Runner/runner.json`](Runner/runner.json). `--brain script` plays plans from a JSON file with no model calls; see [`Demos/Heist/AgentPlay/scripts/win.json`](Demos/Heist/AgentPlay/scripts/win.json) for the plan format.

## What a game project gets

Everything AgentPlay writes lives in `<your project>/AgentPlay/`:

| Path | Contents |
| --- | --- |
| `brief.md` | Your description of the game (you write it) |
| `Traces/` | One NDJSON trace per Play session |
| `playbook.json`, `playbook.md` | Lessons learned from `play` sessions |
| `Inbox/`, `reflexes.json` | Proposed reflexes and the ones you approved |
| `Telemetry/` | Per-session numbers: calls per game minute, waits, routing, prefetch |
| `Runs/` | Model call log of each run |

## Repository layout

| Folder | What it is |
| --- | --- |
| `Packages/com.agentplay.core` | The Unity package: contract, registry, executor, bridge, traces, editor window |
| `Runner/` | The .NET runner: session loop, brains, router, prefetch, reflector, telemetry |
| `Runner.Tests/` | xUnit tests for the runner and the shared contract |
| `Demos/Heist` | A stealth demo: steal three items without being caught by a guard |
| `Testbed/` | A minimal Unity project used by the package tests and E2E runs |
| `Tools/` | E2E scripts, verifiers, real-model batch scripts, the telemetry trend tool |
| `docs/` | Design specs, implementation plans and verification notes (in Vietnamese) |

## Tests

```bash
dotnet test AgentPlay.sln                 # runner and contract
bash Tools/unity-tests.sh                 # package EditMode tests, run inside Testbed
bash Tools/e2e-m4a-realtime.sh            # realtime E2E with the script brain, no model calls
python -I Tools/test_telemetry_trend.py
```

`Tools/run-heist-realtime.sh` and `Tools/run-heist-learning.sh` play Heist with real models and use your Claude quota. Set `UNITY_EXE` if Unity is not at the default path the scripts assume.

## Roadmap

| Milestone | Status |
| --- | --- |
| M1 Foundation: contract, bridge, step control, traces, editor window | done |
| M2 Turn-based play: plan DSL, executor, validator, Opus brain | done |
| M3 Learning: reflector, playbook, reflexes and their review | done |
| M4a Realtime: attention, model router, plan prefetch, trend | done |
| M4b Reach: `agentplay-adapt` skill that writes adapters for new games, screenshot-level input | planned |

## License

[MIT](LICENSE)
