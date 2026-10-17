# Adapter shapes

Both shapes put every adapter file under `Assets/AgentPlayAdapter/`, which `link-agentplay.ps1 -LocalOnly` excludes from git, and both compile only while the AgentPlay package is linked. Pick the shape from the survey:

| | asmdef shape | Assembly-CSharp shape |
| --- | --- | --- |
| When | Game code lives in `.asmdef` assemblies | Game code has no asmdef (it is in `Assembly-CSharp`) |
| Guard | asmdef `defineConstraints` + `versionDefines` on `com.agentplay.core` | `#if AGENTPLAY` around every file; `Assets/csc.rsp` defines `AGENTPLAY` |
| Editor code (hooks) | a second asmdef with `includePlatforms: ["Editor"]` | the folder `Assets/AgentPlayAdapter/Editor/` |

An asmdef cannot reference `Assembly-CSharp`, which is why the second shape exists.

## 1. asmdef shape

`Assets/AgentPlayAdapter/<Game>.AgentAdapter.asmdef`:

```json
{
  "name": "<Game>.AgentAdapter",
  "rootNamespace": "<Game>.AgentAdapter",
  "references": ["<game runtime asmdef names>", "AgentPlay.Runtime"],
  "includePlatforms": [],
  "excludePlatforms": [],
  "autoReferenced": false,
  "defineConstraints": ["AGENTPLAY"],
  "versionDefines": [{ "name": "com.agentplay.core", "expression": "0.0.1", "define": "AGENTPLAY" }],
  "noEngineReferences": false
}
```

Add `"AgentPlay.InputSystem"` and `"Unity.InputSystem"` to `references` when the adapter drives a virtual gamepad.

`Assets/AgentPlayAdapter/Editor/<Game>.AgentAdapter.Editor.asmdef` (hooks):

```json
{
  "name": "<Game>.AgentAdapter.Editor",
  "rootNamespace": "<Game>.AgentAdapter.Editor",
  "references": ["<Game>.AgentAdapter", "AgentPlay.Editor", "AgentPlay.Runtime"],
  "includePlatforms": ["Editor"],
  "autoReferenced": false,
  "defineConstraints": ["AGENTPLAY"],
  "versionDefines": [{ "name": "com.agentplay.core", "expression": "0.0.1", "define": "AGENTPLAY" }]
}
```

A gamepad-driven adapter reads state through the game's public APIs, turns state changes into events in `Update`, and drives the player with `AgentPlay.InputDriving.VirtualGamepad` (`$AGENTPLAY_HOME/Packages/com.agentplay.core/InputSystem/VirtualGamepad.cs`): stage stick and button values in the action, and the gamepad flushes them once per frame in `LateUpdate`.

## 2. Assembly-CSharp shape

`Assets/csc.rsp` (the first line is how `link-agentplay.ps1 -Unlink` knows the file is ours):

```
# AgentPlay: local define, created by agentplay-adapt
-define:AGENTPLAY
```

Every `.cs` file under `Assets/AgentPlayAdapter/` is wrapped:

```csharp
#if AGENTPLAY
using AgentPlay;
using UnityEngine;

namespace AgentPlayAdapter
{
    [AgentGame("<Game>", VersionMember = nameof(GameVersion))]
    [AgentEventInfo("game.won", "<goal reached>", Important = true, Outcome = AgentOutcome.Good)]
    public sealed class GameAgent : MonoBehaviour
    {
        public static string GameVersion => Application.version;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
#if UNITY_EDITOR
            if (FindAnyObjectByType<GameAgent>() != null) return;
            var go = new GameObject("AgentPlayAdapter");
            DontDestroyOnLoad(go);
            go.AddComponent<GameAgent>();
#endif
        }

        // observes, actions, Update() that emits events ...
    }
}
#endif
```

Editor hooks go in `Assets/AgentPlayAdapter/Editor/` (Unity compiles a folder named `Editor` into `Assembly-CSharp-Editor`), also wrapped in `#if AGENTPLAY`.

Note: `csc.rsp` applies to every assembly in the project. Check first that no file in the game already uses the symbol `AGENTPLAY` (`grep -rn AGENTPLAY Assets ProjectSettings`); if one does, use `AGENTPLAY_ADAPTER` everywhere instead.

## 3. Save hook

A run must start where the goal starts (usually a new player) without losing the user's own progress. The hook **moves** the user's save aside (a rename, nothing is copied or deleted), lets the run create a fresh save, then moves the run's save to `<save folder>.agentplay-last-run` and the user's save back. Everything stays next to the save, so the moves never cross drives. Only a previous run's save is ever deleted.

```csharp
#if AGENTPLAY
using System.IO;
using AgentPlay.Editor;
using UnityEngine;

namespace AgentPlayAdapter.Editor
{
    /// <summary>Starts every AutoPlay run from a fresh save and gives the user's save back afterwards.</summary>
    public sealed class FreshSaveHook : IAgentAutoPlayHook
    {
        // The folder the game saves into; adjust from the survey.
        static string Save => Path.Combine(Application.persistentDataPath, "<save folder>");
        static string Parked => Save + ".agentplay-user";
        static string LastRun => Save + ".agentplay-last-run";

        public void BeforePlay()
        {
            // A parked save left by a crashed run is the user's real save: put it back before anything else.
            if (Directory.Exists(Parked)) GiveBack();
            if (Directory.Exists(Save)) Directory.Move(Save, Parked);
            Debug.Log("[AgentPlay] user save parked; the run starts from a fresh save");
        }

        public void AfterPlay()
        {
            if (Directory.Exists(Parked)) GiveBack();
            Debug.Log("[AgentPlay] user save restored");
        }

        static void GiveBack()
        {
            if (Directory.Exists(Save))
            {
                if (Directory.Exists(LastRun)) Directory.Delete(LastRun, true);   // the previous run's save, never the user's
                Directory.Move(Save, LastRun);
            }
            Directory.Move(Parked, Save);
        }
    }
}
#endif
```

### PlayerPrefs

In the Editor on Windows, PlayerPrefs live in the registry under `HKCU\Software\Unity\UnityEditor\<companyName>\<productName>`. If the survey found progress-relevant PlayerPrefs (tutorial flags, first-launch markers), park them the same way with `reg.exe`, which also preserves value types:

```csharp
static string Key => @"HKCU\Software\Unity\UnityEditor\" + Application.companyName + @"\" + Application.productName;

static void Reg(string args)
{
    using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("reg.exe", args) { CreateNoWindow = true, UseShellExecute = false }))
    {
        p.WaitForExit();
        if (p.ExitCode != 0 && !args.StartsWith("delete")) throw new IOException("reg " + args + " failed with " + p.ExitCode);
    }
}

// BeforePlay (after giving back a stale park):  Reg("copy \"" + Key + "\" \"" + Key + ".agentplay-user\" /s /f"); Reg("delete \"" + Key + "\" /f");
// GiveBack:  Reg("delete \"" + Key + "\" /f"); Reg("copy \"" + Key + ".agentplay-user\" \"" + Key + "\" /s /f"); Reg("delete \"" + Key + ".agentplay-user\" /f");
```

Detect a stale park with `reg query "<Key>.agentplay-user"` (exit code 0 means it exists). List the exact keys you found in the step-2 table, and tell the user that their PlayerPrefs are parked during runs.
