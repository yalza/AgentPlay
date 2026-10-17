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

`Assets/csc.rsp`, exactly this one line (Unity's `csc.rsp` has no comment syntax: any other text is read as a source file name and breaks the build; `link-agentplay.ps1 -Unlink` recognises the file by this exact content):

```
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

A run must start where the goal starts (usually a new player) without losing the user's own progress. The hook **moves** the user's save aside (a rename: nothing is copied or deleted), lets the run create a fresh save, then moves the run's save to `<save folder>.agentplay-last-run` and the user's save back. Everything stays next to the save, so the moves never cross drives. Only the save of an earlier agent run is ever deleted.

Rules the template keeps:
- **Crash recovery on Editor load.** If Unity is killed mid-run, the user's save stays parked. `RecoverParkedSave` gives it back as soon as an Editor opens the project again (when no AutoPlay run is in progress: `AgentPlay.Editor.AutoPlay.RunActive`), before the user can play on the agent's save.
- **No user save yet.** If the user had no save, a marker file makes sure the run's save is moved away afterwards instead of becoming the user's save.
- **Idempotent.** `BeforePlay` gives back a stale park first; `AfterPlay` and the load-time recovery do nothing when there is nothing parked. AutoPlay also calls `AfterPlay` on a hook whose `BeforePlay` threw, so a half-finished park is undone.
- **PlayerPrefs only when they exist.** In the Editor on Windows they live in the registry under `HKCU\Software\Unity\UnityEditor\<companyName>\<productName>`; `reg.exe copy` keeps value types. Leave the PlayerPrefs part out if the survey found no progress-relevant PlayerPrefs.

```csharp
#if AGENTPLAY
using System.Diagnostics;
using System.IO;
using AgentPlay.Editor;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AgentPlayAdapter.Editor
{
    /// <summary>
    /// Starts every AutoPlay run as a new player and gives the user's state back afterwards. The user's save folder
    /// and PlayerPrefs are parked by rename/copy, never deleted; the run's save is kept as *.agentplay-last-run.
    /// </summary>
    public sealed class FreshSaveHook : IAgentAutoPlayHook
    {
        // The folder the game saves into; adjust from the survey.
        static string Save => Path.Combine(Application.persistentDataPath, "<save folder>");
        static string Parked => Save + ".agentplay-user";
        static string NoUserSave => Save + ".agentplay-none";
        static string LastRun => Save + ".agentplay-last-run";
        static string Key => @"HKCU\Software\Unity\UnityEditor\" + Application.companyName + @"\" + Application.productName;
        static string ParkedKey => Key + ".agentplay-user";

        public void BeforePlay()
        {
            GiveBack();   // a park left by a crashed run is the user's real state: restore it before anything else
            if (Directory.Exists(Save)) Directory.Move(Save, Parked);
            else File.WriteAllText(NoUserSave, "");
            if (RegExists(Key))
            {
                Reg("copy \"" + Key + "\" \"" + ParkedKey + "\" /s /f");
                Reg("delete \"" + Key + "\" /f");
            }
            Debug.Log("[AgentPlayAdapter] user save and PlayerPrefs parked; the run starts as a new player");
        }

        public void AfterPlay()
        {
            GiveBack();
            Debug.Log("[AgentPlayAdapter] user save and PlayerPrefs restored");
        }

        /// <summary>Gives back what a run parked. Does nothing when nothing is parked.</summary>
        public static void GiveBack()
        {
            if (Directory.Exists(Parked) || File.Exists(NoUserSave))
            {
                if (Directory.Exists(Save))
                {
                    if (Directory.Exists(LastRun)) Directory.Delete(LastRun, true);   // an earlier run's save, never the user's
                    Directory.Move(Save, LastRun);
                }
                if (Directory.Exists(Parked)) Directory.Move(Parked, Save);
                if (File.Exists(NoUserSave)) File.Delete(NoUserSave);
            }
            if (RegExists(ParkedKey))
            {
                if (RegExists(Key)) Reg("delete \"" + Key + "\" /f");   // the run's PlayerPrefs
                Reg("copy \"" + ParkedKey + "\" \"" + Key + "\" /s /f");
                Reg("delete \"" + ParkedKey + "\" /f");
            }
        }

        static bool RegExists(string key) => Run("query \"" + key + "\"") == 0;

        static void Reg(string args)
        {
            int code = Run(args);
            if (code != 0) throw new IOException("reg " + args + " failed with exit code " + code);
        }

        static int Run(string args)
        {
            var info = new ProcessStartInfo("reg.exe", args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(info))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode;
            }
        }
    }

    /// <summary>Gives a crashed run's parked save back as soon as an Editor opens the project, outside AutoPlay runs.</summary>
    [InitializeOnLoad]
    static class RecoverParkedSave
    {
        static RecoverParkedSave()
        {
            EditorApplication.delayCall += () =>
            {
                if (AutoPlay.RunActive) return;
                try
                {
                    FreshSaveHook.GiveBack();
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[AgentPlayAdapter] could not give a parked save back: " + e);
                }
            };
        }
    }
}
#endif
```

Tell the user in the step-5 report where a parked save would be (`<save folder>.agentplay-user`) and where the last run's save is kept (`<save folder>.agentplay-last-run`).
