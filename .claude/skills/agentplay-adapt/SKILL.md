---
name: agentplay-adapt
description: Use when the user wants an AgentPlay agent (Claude playing their Unity game through the AgentPlay package) to play an existing Unity game that has no adapter yet, e.g. "make the agent play my game", "write an AgentPlay adapter", "let Claude playtest the first missions". Surveys the game, proposes what the agent sees and does, writes a local-only adapter, and verifies it in four tiers.
---

# agentplay-adapt

Write an AgentPlay adapter for the Unity game in the current project so the agent can reach a goal the user names.

An **adapter** is a small set of C# files that tells AgentPlay what the agent may observe (`[AgentObserve]`), which actions it may take (`[AgentAction]`), and which game events matter (`AgentEvents.Emit`, `game.won`/`game.lost`). The AgentPlay runner then plays the game by sending plans built from those actions.

**Needs:** the AgentPlay repo (env `AGENTPLAY_HOME`; if unset, ask the user for its path once and use it for the session), Unity 6000.0+ (`UNITY_EXE`, default `/e/6000.0.77f1/Editor/Unity.exe`), .NET 9 SDK, Claude Code CLI signed in, Git Bash. Build the runner once: `dotnet build "$AGENTPLAY_HOME/Runner/AgentPlay.Runner.csproj"`.

Read `reference/attributes.md` before step 2, `reference/adapter-shapes.md` before step 3, and `reference/pitfalls.md` before writing any action.

## Hard rules

- **Change no file that git tracks in the game repo.** Everything you add is excluded through `.git/info/exclude` by `link-agentplay.ps1 -LocalOnly`. Never edit the game's `.gitignore` or any game source file. Record `git -C <game> status --porcelain` before you start and compare after every step that writes files. The one expected difference: while the package is linked, Unity adds it to the tracked `Packages/packages-lock.json`; `-Unlink` restores that file (or run `git -C <game> checkout -- Packages/packages-lock.json`).
- **Never lose the player's save.** If the game persists progress, the adapter ships an `IAgentAutoPlayHook` that moves the save aside before Play and moves it back after (template in `reference/adapter-shapes.md`). Never delete the player's own save; only the save an agent run created may be deleted.
- **Actions go through the player's own input path** (joystick value, virtual gamepad button, the exact method a UI button calls). No teleports and no direct edits to currency or progress. Test setup inside a hook (starting from a fresh save) is the only exception.
- **Write no file before the user approves the step-2 table.** If the user said up front that you may proceed without them, run step 3.1 (the link, which also excludes `AgentPlay/` from git) first, then write the table to `<game>/AgentPlay/proposal.md`, say so, and continue.
- **Never loosen a check in step 4 to make it pass.** Fix the adapter or report the blocker.

## Step 1: Survey (read only)

Find and note with `file:line`:

1. The player character and the component that moves it; how input reaches it (Input System actions, a UI joystick script, legacy `Input.GetAxis`).
2. The managers that hold the state the goal depends on (singletons, an event bus, ScriptableObject data). For a mission goal: where missions, their goals and their progress live, and which call or event marks a mission complete.
3. How progress is saved: path (`Application.persistentDataPath/...`), PlayerPrefs keys, when it writes.
4. The assembly layout: does game code sit in `.asmdef` assemblies, or in `Assembly-CSharp` (no asmdef)? This picks the adapter shape.
5. The scene the game starts in (`ProjectSettings/EditorBuildSettings.asset`) and anything that blocks play in the Editor (consent dialogs, ads, login, a tutorial that locks input).
6. The game's own Editor tools that act when the project opens: `[InitializeOnLoad]` classes and `EditorApplication` callbacks that pull or commit with git, rebuild scenes or regenerate files. Every tier below opens the project, so they will run. If one changes git or files, tell the user and agree on a way around it (its own on/off setting, or working on a detached HEAD or a separate worktree) before step 3.

Skip third-party SDK folders. Output a short survey note in the conversation.

## Step 2: Propose the adapter surface, then STOP

Show one table per kind:

- **Observes:** path (dotted, lowercase), type, source expression, why the agent needs it for the goal. Prefer few, decision-relevant values: the current objective, positions of targets, counts, progress numbers. Lists of similar objects use `[AgentObserveList]`.
- **Actions:** name, parameters (float/int/bool/string only), how it drives the player's input, when it returns `Done` or `Failed`, and the single `Safe = true` action (no parameters; usually stop/idle) the game runs while no plan is available. Every action must end: give it a time or distance limit and return `Failed` when it cannot make progress.
- **Events:** name, when emitted, `Important`, `Outcome` (good/bad/neutral). Always `game.won` (the goal is reached) and, if the goal can fail, `game.lost`.
- **Start and teardown:** how a run starts at the goal's starting point (for example a fresh save) and how the hook gives the user's state back.

Ask the user to approve or change the table. Do not continue without approval (see the hard rule for the exception).

## Step 3: Attach

1. `powershell -NoProfile -ExecutionPolicy Bypass -File "$AGENTPLAY_HOME/Tools/link-agentplay.ps1" -ProjectPath <game> -LocalOnly`. In a git worktree the exclude block lands in the repository's shared `info/exclude`, so it applies to every worktree of that repo until `-Unlink`.
2. If `Assets/csc.rsp` already exists and its whole content is not the single line `-define:AGENTPLAY`, STOP and ask the user (the Assembly-CSharp shape needs that file, and `-Unlink` only removes a `csc.rsp` with exactly that content).
3. Write the adapter in `Assets/AgentPlayAdapter/` using the shape from `reference/adapter-shapes.md`.
4. Write `<game>/AgentPlay/brief.md` from `reference/brief-template.md`. The model reads it every session; state the goal and the win condition exactly.
5. Write `<game>/AgentPlay/scripts/smoke.json`: a script plan that makes real progress toward the goal without a model. The file is `{"plans": [ <plan>, ... ]}`, played in order; each plan has `id`, `intent`, `steps` (`{do, args, until}`), optional `rules` (`{when, do, args, then}`) and `timeout_s`. Example: `$AGENTPLAY_HOME/Demos/Heist/AgentPlay/scripts/win.json`.
6. Write the save hook if step 1 found saved progress.
7. `git -C <game> status --porcelain` must be unchanged.

## Step 4: Verify in tiers

Stop at the first failing tier, fix the adapter, rerun from that tier. Batchmode cannot open a project that an Editor already has open: ask the user before closing their Editor (or, if they gave you the go-ahead, close it only when its scene is not dirty).

Set `AGENTPLAY_AUTOPLAY_SCENE=<Assets/...unity>` for tiers 2-4 when the project does not open on the start scene. Set `AGENTPLAY_AUTOPLAY_SECONDS` to the Editor's load time plus about three times the expected play time (default 900 in the batch script); a deadline exit (1) means the goal was not reached in time.

If Unity never exits, read its log: `[AgentPlay] AutoPlay could not start` or an exception from `executeMethod` means AutoPlay failed before Play mode. Stop that Unity process, fix the cause, and rerun the tier. Do not leave a second Unity running on the same project.

1. **Compiles:** `"$UNITY_EXE" -batchmode -nographics -quit -projectPath <game> -logFile <log>`; the log has no `error CS`.
2. **Adapter check:** start `dotnet "$AGENTPLAY_HOME/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll" check --idle-exit 300` in the background, then `AGENTPLAY_AUTOPLAY_SECONDS=90 "$UNITY_EXE" -projectPath <game> -executeMethod AgentPlay.Editor.AutoPlay.Run -logFile <log>`. `check` exits 0 and prints no `FAIL` line. A `FAIL observe ...` means the observe is null in both snapshots: fix its source, do not drop it unless the goal does not need it.
3. **Script plan:** `bash "$AGENTPLAY_HOME/Tools/run-adapter-batch.sh" <game> 1 <game>/AgentPlay/scripts/smoke.json` with `AGENTPLAY_PROGRESS_OBSERVE` set to the goal's progress observe. The script prints its log folder (one per call, so earlier tiers' logs stay) and `<observe>=<first>-><last>`. Pass: the run log has no `plan_failed` and the last value differs from the first in the goal's direction.
4. **Opus:** `bash "$AGENTPLAY_HOME/Tools/run-adapter-batch.sh" <game> 1`; `runner=0` (game.won). If it loses or times out, read the run log: is the agent missing an observe, does an action not do what its description says, is the brief unclear? Fix that, not the goal.

Exit codes. AutoPlay: 0 won, 1 deadline, 2 a hook failed, 3 lost. `check`: 0 all pass, 1 a check failed, 6 no game connected. Runner: 0 won, 3 lost, 4 the brain stopped (budget or model failure).

## Step 5: Report

List: every file added (all excluded from git); how to remove everything (`link-agentplay.ps1 -ProjectPath <game> -Unlink`, add `-Purge` to also delete `AgentPlay/`); the result of each tier with its log path; what the adapter does not cover; anything about the game that blocked or slowed the agent.
