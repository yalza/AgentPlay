# Pitfalls seen in real adapters

**Stick deadzone (M2c, TileThieves).** The Input System applies a deadzone (default 0.125) to gamepad sticks, so a small virtual stick value does nothing and fine aiming stalls. Pass stick values through `AgentPlay.InputDriving.VirtualGamepad.PastDeadzone(output)` (`$AGENTPLAY_HOME/Packages/com.agentplay.core/InputSystem/VirtualGamepad.cs`), which returns the raw value that reads back as `output` after the deadzone.

**No allocation per frame (M2c review).** Actions run every frame and observes are read on every snapshot. No `FindObjectsByType`, LINQ, `new List<>` or string building in them. Cache references on first use; use `Physics.OverlapSphereNonAlloc` and similar `NonAlloc` queries.

**Actions must notice that the round is over.** After `game.won` or `game.lost` the executor still calls the running action until it returns. Start every action with the equivalent of `if (roundOver) return AgentActionStatus.Done;` (see `HeistPlayer.GoTo`).

**Every action must end.** An action that returns `Running` forever blocks the plan until its timeout. Give movement an arrival radius and a stuck detector (no progress for N seconds: `Failed`), and give waits a limit from `ctx.Elapsed`.

**Important events need an `Outcome`.** Reflexes are scored by the outcome of the events that follow them. An important event without one cannot teach anything.

**Observes are cheap reads.** Expose numbers the game already holds. If a value needs a search (nearest target), compute it at most a few times per second and cache it.

**UI joysticks that only read touch events.** Many mobile games read a custom joystick component that is fed by `IPointerDown/Drag` handlers. Find the value the movement code actually reads (often a `Vector2` property or field on the joystick, or a `Move(Vector3)` method on the controller) and set it at that point, the lowest level the joystick itself calls. Say in the step-2 table which entry point you drive and why it is the player's input path.

**UI-only steps.** When a goal needs a button (buy, upgrade, claim), call the same method the button's `onClick` calls, after checking the same conditions the UI checks (enough currency, unlocked). Do not edit the UI.

**Third-party SDK popups in the Editor.** Consent forms, ads and login screens may appear or may be stubbed. If one blocks play, dismiss it from an action or the hook by calling the method its close button calls. Never edit SDK or game code.

**Saves outlive Play mode.** Games save on pause, on quit and on timers. The AutoPlay hook runs `AfterPlay` only after Play mode has ended, so the game's own final save lands in the run's folder, not in the user's.

**Scene order.** If the game expects a loading scene to run first (it initializes services, addressables or the save), set `AGENTPLAY_AUTOPLAY_SCENE` to that scene; the adapter must then survive scene changes (`DontDestroyOnLoad`, lazy lookups).

**`game.won` for progress goals.** Games rarely have a "win" for "finish 3 missions". The adapter counts the goal itself (for example completed missions since the run started) and emits `game.won` once when it is reached; emit it only once.
