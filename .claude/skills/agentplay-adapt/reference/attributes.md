# AgentPlay adapter API

All types are in namespace `AgentPlay` (assembly `AgentPlay.Runtime`), except the AutoPlay hook in `AgentPlay.Editor`. Examples are taken from the Heist demo (`$AGENTPLAY_HOME/Demos/Heist/Assets/Heist/`).

## `[AgentGame]`: name the game (once, on the adapter class)

```csharp
[AgentGame("AgentPlay Heist", Version = "m3")]
public sealed class HeistGame : MonoBehaviour { ... }
```

`VersionMember = nameof(SomeStaticStringProperty)` reads the version at runtime instead (for example a property returning `Application.version`). Reflexes learned for one version are re-checked when the version changes.

## `[AgentObserve]`: a value the agent sees

```csharp
[AgentObserve("player.pos")] public Vector3 Pos => transform.position;

[AgentObserve("player.zone", "Named zone the thief stands in, or empty")]
public string Zone => HeistLevel.ZoneAt(transform.position);

[AgentObserve("player.carrying", "Number of items carried")] public int Carrying => Carried.Count;
```

- On a property or field of a `MonoBehaviour` that exists in the scene (the registry scans live objects when Play mode starts).
- Types: `float`, `int`, `bool`, `string`, enums (sent as their name), `Vector3`.
- Paths are dotted and lowercase; the description is what the model reads, so say units and meaning.
- Read on every snapshot: keep it O(1) and allocation-free. Return a safe default (`0`, `""`, `Vector3.zero`) when the source object is missing, never throw.

## `[AgentObserveList]`: several similar objects

```csharp
[AgentObserveList("guards", "Guards in the room")]
public IEnumerable<HeistGuard> Guards => GuardList;

// In HeistGuard: these become the fields of each list element.
[AgentObserve("distance", "Metres from this guard to the thief")]
public float Distance => ...;
[AgentObserve("moving_north", "True while walking towards +z")] public bool MovingNorth => _forward.z > 0f;
```

The element type contributes no top-level observes. It may be a `MonoBehaviour` or a plain C# class whose members carry `[AgentObserve]`. Plans can test list fields with `any(guards.moving_north)`, `max(guards.z)`.

## `[AgentAction]`: something the agent can do

```csharp
[AgentAction("goto", "Walk straight to a zone: start, exit, item_a, item_b, item_c, cover_1, cover_2")]
public AgentActionStatus GoTo(AgentActionContext ctx, string zone)
{
    if (HeistGame.Result != null) return AgentActionStatus.Done;   // round over: nothing left to do
    if (!HeistLevel.Zones.TryGetValue(zone, out var target)) return AgentActionStatus.Failed;
    if (ctx.IsFirstFrame) Crouched = false;
    if (!MoveTowards(target)) return AgentActionStatus.Running;
    HeistGame.Instance.CheckWin();
    return AgentActionStatus.Done;
}

[AgentAction("pick", "Pick up an item within 1.5 m: item_a, item_b or item_c")]
public AgentActionStatus Pick(string item) { ... }
```

- Called **every frame** while it runs, after the game's own `Update` (the executor runs at execution order 10000); return `Running`, `Done` or `Failed`.
- Input staged on `VirtualGamepad` lasts one frame: the gamepad flushes in `LateUpdate` and then resets its stage, so set stick and button values again every frame the action runs.
- Optional first parameter `AgentActionContext`: `IsFirstFrame` (set up on the first call) and `Elapsed` (game seconds since the action started).
- Other parameters: `float`, `int`, `bool`, `string` only. Name the allowed string values in the description.
- Exactly one action has `Safe = true` and no parameters (for example `[AgentAction("stop", "Stand still", Safe = true)]`). It runs while no plan is available.
- Every action needs an end: a distance reached, a timeout from `ctx.Elapsed`, or `Failed` when it cannot progress (target gone, path blocked).

## Events: `[AgentEventInfo]` + `AgentEvents.Emit`

```csharp
[AgentEventInfo("guard.spotted", "A guard started seeing the thief", Important = true, Outcome = AgentOutcome.Bad)]
[AgentEventInfo("game.won", "The thief left through the exit with all three items", Important = true, Outcome = AgentOutcome.Good)]
public sealed class HeistGame : MonoBehaviour { ... }

AgentEvents.Emit("game.won", new { items = Player.Carried.Count }, important: true, outcome: AgentOutcome.Good);
AgentEvents.Emit("guard.spotted", new { guard = name, distance = Distance }, important: true, outcome: AgentOutcome.Bad);
```

- Declare every event on the adapter class; emit from the main thread.
- `Important = true` interrupts the current plan (turn-based: pauses the game) unless a plan rule or an approved reflex handles it. Use it for surprises the agent must react to.
- `Outcome` scores reflexes; set it on every important event.
- `game.won` and `game.lost` are reserved: they end the round, and the generic AutoPlay quits on them. For a goal like "finish 3 missions", the adapter emits `game.won` itself when the count reaches 3.
- For an adapter that watches the game rather than living in it, turn state changes into events in `Update` (compare with the value from the previous frame).

## Creating the adapter object

The adapter is a `MonoBehaviour` that must exist in the scene before Play mode settles. Create it yourself; never edit a scene or prefab of the game:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
static void Create()
{
#if UNITY_EDITOR
    if (FindAnyObjectByType<MyGameAgent>() != null) return;
    var go = new GameObject("AgentPlayAdapter");
    DontDestroyOnLoad(go);
    go.AddComponent<MyGameAgent>();
#endif
}
```

If the game loads its gameplay scene after a loading scene, keep the adapter alive with `DontDestroyOnLoad` and look up game objects lazily (cache them the first time they are found, retry while they are null).

## AutoPlay hooks (`AgentPlay.Editor`)

```csharp
public interface IAgentAutoPlayHook
{
    void BeforePlay();   // before Play mode; throw to abort the run (AutoPlay exits 2)
    void AfterPlay();    // after Play mode ended: also after a deadline, a failed hook, or this hook's own BeforePlay throwing
}
```

Editor-only, created with a public parameterless constructor (AutoPlay logs a warning and skips a hook without one); keep state on disk (a domain reload separates the two calls). Make `AfterPlay` safe to call when `BeforePlay` stopped halfway. An exception in `AfterPlay` makes AutoPlay exit 2 whatever the round's result. Template: `adapter-shapes.md`.
