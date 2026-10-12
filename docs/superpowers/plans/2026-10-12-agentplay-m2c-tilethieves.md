# AgentPlay M2c (Adapter TileThieves) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agent chơi được TileThieves (scene `Prototype_Pry`) qua package AgentPlay mà không sửa file game nào: đi tới tranh, nhắm, cạy đủ 6 viên vào bao mà mức ồn không lên `Lockdown`.

**Architecture:** Package có thêm asmdef `AgentPlay.InputSystem` chứa `VirtualGamepad` (chỉ biên dịch khi project có Input System). Repo TileThieves có thư mục `Assets/_Project/Scripts/AgentAdapter/` với asmdef `TileThieves.AgentAdapter`, chỉ biên dịch khi có package `com.agentplay.core`. Thư mục này chứa `TileThievesAgent`: một MonoBehaviour đọc trạng thái qua API public, phát sự kiện khi thấy trạng thái đổi, và lái nhân vật bằng tay cầm ảo. Package được gắn vào TileThieves bằng junction (`Tools/link-agentplay.ps1`), nên `manifest.json` không đổi.

**Tech Stack:** Unity 6000.0.77f1, Input System 1.19.0 (TileThieves), package AgentPlay (M2a/M2b), runner `--brain script` để kiểm không tốn tiền, `--brain opus` để chạy thật.

**Spec:** `docs/superpowers/specs/2026-10-12-agentplay-m2-design.md` (mục 9, 11; tiêu chí 3; rủi ro 3, 4, 5).

### Ghi chú so với spec M2 (đồng bộ ở Task 5)

- **Mục tiêu 6 viên là hằng số của adapter** (`TileThievesAgent.GoalTiles`), không phải số cân bằng của GDD. `brief.md` ghi cùng con số.
- **Tay cầm ảo chỉ được tạo khi agent gọi action đầu tiên.** Người chơi bằng bàn phím hay tay cầm thật không bị ảnh hưởng khi agent chưa điều khiển.
- **Trong lúc tay cầm ảo tồn tại, `InputSystem.settings.backgroundBehavior` được đặt `IgnoreFocus`**, trả lại giá trị cũ khi hủy, để game vẫn nhận input khi Editor mất focus (rủi ro 4).
- **Số đếm gạch rơi dùng `LooseTilePool.Instance.ActiveTiles`** (observe `loose.count`), không đếm theo khoảng cách. Đếm theo khoảng cách sẽ phải tìm object mỗi frame.
- **Không có unit test cho `VirtualGamepad`:** Testbed không có Input System. Lớp này được kiểm bằng E2E trên TileThieves.
- **Thay đổi `Packages/packages-lock.json` do Unity sinh ra vì package nhúng không được commit vào TileThieves**, vì máy đồng đội không có junction.

## Global Constraints

- Không sửa file nào có sẵn của TileThieves ngoài `.gitignore`. Mọi code mới nằm trong `Assets/_Project/Scripts/AgentAdapter/`, cộng `AgentPlay/brief.md` và `AgentPlay/scripts/`.
- Asmdef của adapter: `versionDefines` `com.agentplay.core` → `TT_AGENTPLAY`; `defineConstraints` `TT_AGENTPLAY`. Không có package thì asmdef không biên dịch.
- Theo `CLAUDE.md` của TileThieves: namespace `TileThieves.AgentAdapter`, code và comment tiếng Anh, commit `.meta` do Unity sinh, nhánh `feat/agentplay-adapter` tách từ `origin/main`, mở PR để người dùng xem.
- Không bao giờ dùng `rm -rf` lên junction `Packages/com.agentplay.core`; gỡ bằng `cmd //c rmdir`.
- Không mở TileThieves trong Editor khác khi chạy script; người dùng đã đóng Editor của họ.
- Package AgentPlay vẫn tổng quát: `Tools/check-generic.sh` quét thêm thư mục `InputSystem`.
- Commit trong cả hai repo kết thúc bằng hai dòng trailer như M2a và M2b.

## Review Focus

1. **Người chơi thật trong lúc adapter có mặt nhưng agent chưa điều khiển:** không có tay cầm ảo, input thật hoạt động như cũ. Kiểm bằng log `virtual gamepad created` chỉ xuất hiện sau Plan đầu tiên (Task 3).
2. **Editor mất focus trong lúc agent chơi:** game vẫn nhận input từ tay cầm ảo. E2E chạy khi cửa sổ Unity không được bấm vào (Task 3).
3. **Tranh hết gạch hoặc không có tranh trong scene:** `goto_painting` và `aim_next_tile` trả `Failed` với Plan hỏng, không treo. Kiểm khi kịch bản chạy (Task 3: log `plan_failed` nếu có).
4. **Thoát Play mode giữa chừng:** tay cầm ảo bị gỡ và `backgroundBehavior` được trả lại. Kiểm log `virtual gamepad removed` (Task 3).
5. **Project không có package AgentPlay** (máy đồng đội, CI): TileThieves vẫn biên dịch. Kiểm bằng `Tools/LogicTests` của TileThieves và việc asmdef bị loại (Task 4).

---

### Task 1: VirtualGamepad trong package

**Files (repo AgentPlay):**
- Create: `Packages/com.agentplay.core/InputSystem/AgentPlay.InputSystem.asmdef`
- Create: `Packages/com.agentplay.core/InputSystem/VirtualGamepad.cs`
- Modify: `Tools/check-generic.sh` (quét thêm `InputSystem`)

**Interfaces:**
- Produces: `AgentPlay.InputDriving.VirtualGamepad`: `static Create()`, `SetLeftStick(Vector2)`, `SetRightStick(Vector2)`, `SetRightTrigger(bool)`, `SetButton(GamepadButton, bool)`, `Flush()` (gọi một lần mỗi frame trong `LateUpdate`), `Dispose()`, `Device : Gamepad`.

- [ ] **Step 1: Viết code**

`Packages/com.agentplay.core/InputSystem/AgentPlay.InputSystem.asmdef`:

```json
{
  "name": "AgentPlay.InputSystem",
  "rootNamespace": "AgentPlay.InputDriving",
  "references": ["Unity.InputSystem"],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": ["AGENTPLAY_INPUT_SYSTEM"],
  "versionDefines": [
    { "name": "com.unity.inputsystem", "expression": "1.0.0", "define": "AGENTPLAY_INPUT_SYSTEM" }
  ],
  "noEngineReferences": false
}
```

`Packages/com.agentplay.core/InputSystem/VirtualGamepad.cs`:

```csharp
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace AgentPlay.InputDriving
{
    /// <summary>
    /// A virtual gamepad for game adapters. Actions stage stick and button values during the frame;
    /// Flush() (once per frame, in LateUpdate) queues them for the next input update and resets the stage,
    /// so anything not set again returns to rest. While it exists, input keeps flowing when the Editor is unfocused.
    /// </summary>
    public sealed class VirtualGamepad : IDisposable
    {
        readonly InputSettings.BackgroundBehavior _previousBackground;
        GamepadState _staged;
        bool _disposed;

        public Gamepad Device { get; }

        VirtualGamepad()
        {
            Device = InputSystem.AddDevice<Gamepad>("AgentPlayVirtualGamepad");
            _previousBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _staged = new GamepadState();
            Debug.Log("[AgentPlay] virtual gamepad created");
        }

        public static VirtualGamepad Create() => new VirtualGamepad();

        public void SetLeftStick(Vector2 value) => _staged.leftStick = Vector2.ClampMagnitude(value, 1f);

        public void SetRightStick(Vector2 value) => _staged.rightStick = Vector2.ClampMagnitude(value, 1f);

        public void SetRightTrigger(bool pressed) => _staged.rightTrigger = pressed ? 1f : 0f;

        public void SetButton(GamepadButton button, bool pressed) => _staged = _staged.WithButton(button, pressed);

        public void Flush()
        {
            if (_disposed || !Device.added) return;
            InputSystem.QueueStateEvent(Device, _staged);
            _staged = new GamepadState();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Device.added) InputSystem.RemoveDevice(Device);
            InputSystem.settings.backgroundBehavior = _previousBackground;
            Debug.Log("[AgentPlay] virtual gamepad removed");
        }
    }
}
```

Trong `Tools/check-generic.sh`: đổi `for dir in Contract Runtime Editor; do` thành `for dir in Contract Runtime Editor InputSystem; do`, và thêm `"$ROOT/Packages/com.agentplay.core/InputSystem" \` vào danh sách thư mục của `grep`.

- [ ] **Step 2: Kiểm tra**

Run:

```bash
cd /e/Unity-WorkSpace/AgentPlay
bash Tools/check-generic.sh
bash Tools/unity-tests.sh
```

Expected: `check-generic: OK`; Unity tests vẫn pass (Testbed không có Input System nên asmdef mới bị loại, không ảnh hưởng). Việc biên dịch thật của `VirtualGamepad` được kiểm ở Task 2.

- [ ] **Step 3: Commit (repo AgentPlay, nhánh `feat/m2b-opus`)**

```bash
git add Packages Tools/check-generic.sh
git commit -m "feat(input): virtual gamepad for game adapters (Input System only)" -m "<trailers>"
```

---

### Task 2: Adapter TileThieves

**Files (repo TileThieves, nhánh mới `feat/agentplay-adapter` từ `origin/main`):**
- Create: `Assets/_Project/Scripts/AgentAdapter/TileThieves.AgentAdapter.asmdef`
- Create: `Assets/_Project/Scripts/AgentAdapter/TileThievesAgent.cs`
- Create: `Assets/_Project/Scripts/AgentAdapter/Editor/TileThieves.AgentAdapter.Editor.asmdef`
- Create: `Assets/_Project/Scripts/AgentAdapter/Editor/AgentAdapterAutoPlay.cs`
- Create: `AgentPlay/brief.md`, `AgentPlay/scripts/pry6.json`
- Modify: `.gitignore`
- Junction (không commit): `Packages/com.agentplay.core`

**Interfaces:**
- Consumes: `VirtualGamepad` (Task 1); attribute và `AgentEvents` của package; API public của TileThieves: `PlayerController.IsCrouching`, `PryTool.AimedPainting/AimedCell/AimInReach/IsPrying/TilesPried`, `PlayerSack.Inventory.Count/FreeSpace`, `NoiseSystem.Instance.DisplayValue/DisplayLevel`, `PaintingRuntime.Wall/CellCenterWorld/FrontNormal/TileRemoved`, `LooseTilePool.Instance.ActiveTiles`, `LooseTile`.
- Produces: `TileThievesAgent.Result` (`"won"`, `"lost"` hoặc null), `TileThievesAgent.GoalTiles = 6`; menu `HeistDemo`-style auto play `TileThieves.AgentAdapter.Editor.AgentAdapterAutoPlay.Run`.

- [ ] **Step 1: Nhánh và junction**

```bash
cd /e/Unity-WorkSpace/TileThieves
git checkout -b feat/agentplay-adapter origin/main
powershell -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w /e/Unity-WorkSpace/AgentPlay/Tools/link-agentplay.ps1)" -ProjectPath "$(cygpath -w /e/Unity-WorkSpace/TileThieves)"
git status --short
```

Expected: junction tạo xong; `.gitignore` có dòng `/Packages/com.agentplay.core`; `AgentPlay/brief.md` (mẫu) được tạo. File `URP.png` đang sửa dở của người dùng vẫn ở trạng thái `M`, không được commit.

Thêm vào cuối `.gitignore` của TileThieves (sau phần script link vừa thêm):

```gitignore
/AgentPlay/Traces/
/AgentPlay/Runs/
```

- [ ] **Step 2: Asmdef**

`Assets/_Project/Scripts/AgentAdapter/TileThieves.AgentAdapter.asmdef`:

```json
{
  "name": "TileThieves.AgentAdapter",
  "rootNamespace": "TileThieves.AgentAdapter",
  "references": [
    "TileThieves.Runtime",
    "TileThieves.Components",
    "AgentPlay.Runtime",
    "AgentPlay.InputSystem",
    "Unity.InputSystem"
  ],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": false,
  "defineConstraints": ["TT_AGENTPLAY"],
  "versionDefines": [
    { "name": "com.agentplay.core", "expression": "0.0.1", "define": "TT_AGENTPLAY" }
  ],
  "noEngineReferences": false
}
```

`Assets/_Project/Scripts/AgentAdapter/Editor/TileThieves.AgentAdapter.Editor.asmdef`:

```json
{
  "name": "TileThieves.AgentAdapter.Editor",
  "rootNamespace": "TileThieves.AgentAdapter.Editor",
  "references": ["TileThieves.AgentAdapter"],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": false,
  "defineConstraints": ["TT_AGENTPLAY"],
  "versionDefines": [
    { "name": "com.agentplay.core", "expression": "0.0.1", "define": "TT_AGENTPLAY" }
  ],
  "noEngineReferences": false
}
```

- [ ] **Step 3: Adapter**

`Assets/_Project/Scripts/AgentAdapter/TileThievesAgent.cs`:

```csharp
using System.Collections.Generic;
using AgentPlay;
using AgentPlay.InputDriving;
using TileThieves.Components;
using TileThieves.Gameplay.Noise;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace TileThieves.AgentAdapter
{
    /// <summary>
    /// Lets an AgentPlay agent play the prying prototype: reads game state through public APIs, turns state changes
    /// into events, and drives the thief with a virtual gamepad. No game file depends on this; it only compiles when
    /// the AgentPlay package is linked into the project.
    /// </summary>
    [AgentGame("Tile Thieves", VersionMember = nameof(GameVersion))]
    [AgentEventInfo("tile.pried", "A tile came off a painting")]
    [AgentEventInfo("tile.collected", "The sack gained tiles")]
    [AgentEventInfo("noise.level_up", "The noise meter reached a higher level", Important = true, Outcome = AgentOutcome.Bad)]
    [AgentEventInfo("game.won", "The sack holds the goal number of tiles", Important = true, Outcome = AgentOutcome.Good)]
    [AgentEventInfo("game.lost", "The noise meter reached Lockdown", Important = true, Outcome = AgentOutcome.Bad)]
    [DefaultExecutionOrder(9000)]
    public sealed class TileThievesAgent : MonoBehaviour
    {
        /// <summary>Tiles the sack must hold to win the agent's round (adapter goal, not a GDD balance value).</summary>
        public const int GoalTiles = 6;

        const float StandDistance = 1.3f;
        const float ArriveRadius = 0.25f;
        const float AimDegreesForFullStick = 25f;
        const float ChestHeight = 1.2f;

        public static string GameVersion => Application.version;
        /// <summary>"won" or "lost" once the round is over; null while playing.</summary>
        public static string Result { get; private set; }

        PlayerController _player;
        PryTool _pry;
        PlayerSack _sack;
        Camera _camera;
        VirtualGamepad _pad;
        readonly List<PaintingRuntime> _paintings = new List<PaintingRuntime>();
        NoiseLevel _lastLevel;
        int _lastSackCount;
        PaintingRuntime _targetPainting;
        int _targetCell = -1;
        int _pryStart;

        [AgentObserve("player.pos")] public Vector3 PlayerPos => _player != null ? _player.transform.position : Vector3.zero;
        [AgentObserve("player.crouched")] public bool Crouched => _player != null && _player.IsCrouching;
        [AgentObserve("noise.value", "Noise meter value")] public float NoiseValue => NoiseSystem.Instance != null ? NoiseSystem.Instance.DisplayValue : 0f;
        [AgentObserve("noise.level", "Calm, Suspicious, Alarm or Lockdown")] public NoiseLevel Level => NoiseSystem.Instance != null ? NoiseSystem.Instance.DisplayLevel : NoiseLevel.Calm;
        [AgentObserve("sack.count", "Tiles in the sack")] public int SackCount => _sack != null ? _sack.Inventory.Count : 0;
        [AgentObserve("sack.free", "Free space in the sack")] public int SackFree => _sack != null ? _sack.Inventory.FreeSpace : 0;
        [AgentObserve("goal.tiles", "Tiles the sack must hold to win")] public int Goal => GoalTiles;
        [AgentObserve("pry.aimed", "The crosshair is on a tile within reach")] public bool Aimed => _pry != null && _pry.AimedCell >= 0 && _pry.AimInReach;
        [AgentObserve("pry.prying", "A tile is being pried right now")] public bool Prying => _pry != null && _pry.IsPrying;
        [AgentObserve("pry.tiles_pried", "Tiles pried so far")] public int TilesPried => _pry != null ? _pry.TilesPried : 0;
        [AgentObserve("painting.distance", "Metres to the nearest painting that still has tiles")] public float PaintingDistance
        {
            get
            {
                var p = NearestPainting();
                return p != null ? Flat(Center(p) - PlayerPos).magnitude : -1f;
            }
        }
        [AgentObserve("painting.remaining", "Tiles left on the nearest painting")] public int PaintingRemaining
        {
            get
            {
                var p = NearestPainting();
                return p != null ? p.Wall.Remaining : 0;
            }
        }
        [AgentObserve("loose.count", "Loose tiles lying in the room")] public int LooseCount => LooseTilePool.Instance != null ? LooseTilePool.Instance.ActiveTiles : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Spawn()
        {
            Result = null;
            if (FindFirstObjectByType<TileThievesAgent>() != null) return;
            if (FindFirstObjectByType<PlayerController>() == null) return;
            new GameObject("AgentPlay TileThieves Adapter").AddComponent<TileThievesAgent>();
        }

        void Start()
        {
            _player = FindFirstObjectByType<PlayerController>();
            if (_player != null)
            {
                _pry = _player.GetComponent<PryTool>();
                _sack = _player.GetComponent<PlayerSack>();
            }
            _camera = Camera.main;
            _paintings.AddRange(FindObjectsByType<PaintingRuntime>(FindObjectsSortMode.InstanceID));
            foreach (var painting in _paintings)
            {
                var p = painting;
                p.TileRemoved += cell => AgentEvents.Emit("tile.pried", new { painting = p.PaintingId, cell });
            }
            _lastLevel = Level;
            _lastSackCount = SackCount;
        }

        void Update()
        {
            if (Result != null) return;
            var level = Level;
            if (level > _lastLevel)
            {
                AgentEvents.Emit("noise.level_up", new { level = level.ToString(), value = NoiseValue }, important: true, outcome: AgentOutcome.Bad);
                if (level == NoiseLevel.Lockdown) End("lost");
            }
            _lastLevel = level;

            int count = SackCount;
            if (count > _lastSackCount) AgentEvents.Emit("tile.collected", new { count });
            _lastSackCount = count;
            if (Result == null && count >= GoalTiles) End("won");
        }

        void LateUpdate() => _pad?.Flush();

        void OnDestroy()
        {
            _pad?.Dispose();
            _pad = null;
        }

        void End(string result)
        {
            Result = result;
            Debug.Log("[TileThieves agent] result=" + result);
            bool won = result == "won";
            AgentEvents.Emit(won ? "game.won" : "game.lost", new { sack = SackCount, noise = NoiseValue }, important: true,
                outcome: won ? AgentOutcome.Good : AgentOutcome.Bad);
        }

        [AgentAction("goto_painting", "Walk to the nearest painting that still has tiles and face it")]
        public AgentActionStatus GotoPainting(AgentActionContext ctx)
        {
            if (Result != null) return AgentActionStatus.Done;
            if (!Ready()) return AgentActionStatus.Failed;
            if (ctx.IsFirstFrame) _targetPainting = NearestPainting();
            if (_targetPainting == null || _targetPainting.Wall.Remaining == 0) return AgentActionStatus.Failed;

            Vector3 stand = Center(_targetPainting) + _targetPainting.FrontNormal * StandDistance;
            stand.y = _player.transform.position.y;
            LookAt(Center(_targetPainting));
            if (Flat(stand - _player.transform.position).magnitude <= ArriveRadius) return AgentActionStatus.Done;
            if (ctx.Elapsed > 15) return AgentActionStatus.Failed;
            MoveTowards(stand);
            return AgentActionStatus.Running;
        }

        [AgentAction("aim_next_tile", "Turn the camera until the crosshair rests on a tile within reach")]
        public AgentActionStatus AimNextTile(AgentActionContext ctx)
        {
            if (Result != null) return AgentActionStatus.Done;
            if (!Ready()) return AgentActionStatus.Failed;
            if (ctx.IsFirstFrame) ChooseTarget();
            if (_targetPainting == null || _targetCell < 0) return AgentActionStatus.Failed;
            if (Aimed) return AgentActionStatus.Done;
            if (ctx.Elapsed > 5) return AgentActionStatus.Failed;
            LookAt(_targetPainting.CellCenterWorld(_targetCell));
            return AgentActionStatus.Running;
        }

        [AgentAction("pry", "Hold the chisel until this many more tiles came off (each tile takes about 0.6 s and adds noise)")]
        public AgentActionStatus Pry(AgentActionContext ctx, int count)
        {
            if (Result != null) return AgentActionStatus.Done;
            if (!Ready() || count <= 0) return AgentActionStatus.Failed;
            if (ctx.IsFirstFrame) _pryStart = _pry.TilesPried;
            if (_pry.TilesPried - _pryStart >= count) return AgentActionStatus.Done;
            if (_sack.Inventory.FreeSpace <= 0) return AgentActionStatus.Done;
            if (ctx.Elapsed > 6 + count * 1.5) return AgentActionStatus.Failed;

            if (_pry.IsPrying || Aimed)
            {
                _pad.SetRightTrigger(true);
                return AgentActionStatus.Running;
            }
            // Lost the aim (the row ended): pick the next reachable tile and turn to it.
            if (_targetCell < 0 || _targetPainting == null || !_targetPainting.Wall.IsPresent(_targetCell)) ChooseTarget();
            if (_targetCell < 0) return AgentActionStatus.Failed;
            LookAt(_targetPainting.CellCenterWorld(_targetCell));
            return AgentActionStatus.Running;
        }

        [AgentAction("collect_loose", "Walk over loose tiles on the floor and pick them up into the sack")]
        public AgentActionStatus CollectLoose(AgentActionContext ctx)
        {
            if (Result != null) return AgentActionStatus.Done;
            if (!Ready()) return AgentActionStatus.Failed;
            if (LooseCount == 0 || _sack.Inventory.FreeSpace <= 0 || ctx.Elapsed > 10) return AgentActionStatus.Done;
            LooseTile nearest = null;
            float best = float.MaxValue;
            foreach (var tile in FindObjectsByType<LooseTile>(FindObjectsSortMode.None))
            {
                float d = Flat(tile.transform.position - _player.transform.position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = tile;
                }
            }
            if (nearest == null) return AgentActionStatus.Done;
            MoveTowards(nearest.transform.position);
            _pad.SetButton(GamepadButton.West, true);
            return AgentActionStatus.Running;
        }

        [AgentAction("crouch", "Crouch (quieter, slower) or stand up")]
        public AgentActionStatus Crouch(AgentActionContext ctx, bool on)
        {
            if (Result != null) return AgentActionStatus.Done;
            if (!Ready()) return AgentActionStatus.Failed;
            if (_player.IsCrouching == on) return AgentActionStatus.Done;
            if (ctx.IsFirstFrame) _pad.SetButton(GamepadButton.East, true);
            return ctx.Elapsed > 0.5 ? AgentActionStatus.Failed : AgentActionStatus.Running;
        }

        [AgentAction("stop", "Stand still (used whenever there is no plan)", Safe = true)]
        public AgentActionStatus Stop() => AgentActionStatus.Done;

        bool Ready()
        {
            if (_player == null || _pry == null || _sack == null || _camera == null) return false;
            if (_pad == null) _pad = VirtualGamepad.Create();
            return true;
        }

        void ChooseTarget()
        {
            _targetCell = -1;
            if (_targetPainting == null || _targetPainting.Wall.Remaining == 0) _targetPainting = NearestPainting();
            if (_targetPainting == null) return;
            Vector3 chest = _player.transform.position + Vector3.up * ChestHeight;
            float best = float.MaxValue;
            var wall = _targetPainting.Wall;
            for (int cell = 0; cell < wall.TileCount; cell++)
            {
                if (!wall.IsPresent(cell)) continue;
                float d = (_targetPainting.CellCenterWorld(cell) - chest).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    _targetCell = cell;
                }
            }
        }

        PaintingRuntime NearestPainting()
        {
            PaintingRuntime nearest = null;
            float best = float.MaxValue;
            foreach (var p in _paintings)
            {
                if (p == null || p.Wall == null || p.Wall.Remaining == 0) continue;
                float d = Flat(Center(p) - PlayerPos).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = p;
                }
            }
            return nearest;
        }

        static Vector3 Center(PaintingRuntime p) => (p.CellCenterWorld(0) + p.CellCenterWorld(p.Wall.TileCount - 1)) * 0.5f;

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void MoveTowards(Vector3 target)
        {
            Vector3 d = Flat(target - _player.transform.position);
            float yaw = _camera.transform.eulerAngles.y;
            Vector3 local = Quaternion.Euler(0f, -yaw, 0f) * d.normalized;
            float speed = Mathf.Clamp(d.magnitude / 0.8f, 0.3f, 1f);
            _pad.SetLeftStick(new Vector2(local.x, local.z) * speed);
        }

        void LookAt(Vector3 target)
        {
            Transform cam = _camera.transform;
            Vector3 desired = Quaternion.LookRotation(target - cam.position).eulerAngles;
            float yawError = Mathf.DeltaAngle(cam.eulerAngles.y, desired.y);
            float pitchError = Mathf.DeltaAngle(cam.eulerAngles.x, desired.x);
            // The camera adds stick.x to yaw and subtracts stick.y from pitch.
            _pad.SetRightStick(new Vector2(
                Mathf.Clamp(yawError / AimDegreesForFullStick, -1f, 1f),
                Mathf.Clamp(-pitchError / AimDegreesForFullStick, -1f, 1f)));
        }
    }
}
```

`Assets/_Project/Scripts/AgentAdapter/Editor/AgentAdapterAutoPlay.cs`:

```csharp
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TileThieves.AgentAdapter.Editor
{
    /// <summary>
    /// Run with: Unity.exe -projectPath TileThieves -executeMethod TileThieves.AgentAdapter.Editor.AgentAdapterAutoPlay.Run
    /// Opens Prototype_Pry, enters Play mode, and quits 2 s after the agent's round ends (exit 0 won, 3 lost)
    /// or at AGENTPLAY_AUTOPLAY_SECONDS (exit 1).
    /// </summary>
    [InitializeOnLoad]
    public static class AgentAdapterAutoPlay
    {
        const string ScenePath = "Assets/_Project/Scenes/Prototype_Pry.unity";
        const string DeadlineKey = "TileThieves.AgentAdapter.AutoPlay.Deadline";
        const string EndedKey = "TileThieves.AgentAdapter.AutoPlay.EndedAt";

        static AgentAdapterAutoPlay()
        {
            EditorApplication.update += Watch;
        }

        public static void Run()
        {
            if (!int.TryParse(Environment.GetEnvironmentVariable("AGENTPLAY_AUTOPLAY_SECONDS"), out var seconds) || seconds <= 0) seconds = 240;
            EditorSceneManager.OpenScene(ScenePath);
            SessionState.SetFloat(DeadlineKey, (float)(EditorApplication.timeSinceStartup + seconds));
            SessionState.EraseFloat(EndedKey);
            Debug.Log("[TileThieves agent] AutoPlay for at most " + seconds + " s");
            EditorApplication.EnterPlaymode();
        }

        static void Watch()
        {
            float deadline = SessionState.GetFloat(DeadlineKey, 0f);
            if (deadline <= 0f) return;
            double now = EditorApplication.timeSinceStartup;
            if (EditorApplication.isPlaying && TileThievesAgent.Result != null)
            {
                float endedAt = SessionState.GetFloat(EndedKey, 0f);
                if (endedAt <= 0f)
                {
                    SessionState.SetFloat(EndedKey, (float)now);
                    return;
                }
                if (now - endedAt >= 2.0) Quit(TileThievesAgent.Result == "won" ? 0 : 3);
                return;
            }
            if (now >= deadline)
            {
                Debug.Log("[TileThieves agent] AutoPlay deadline reached");
                Quit(1);
            }
        }

        static void Quit(int code)
        {
            SessionState.EraseFloat(DeadlineKey);
            SessionState.EraseFloat(EndedKey);
            Debug.Log("[TileThieves agent] AutoPlay quitting with " + code);
            EditorApplication.isPaused = false;
            EditorApplication.ExitPlaymode();
            EditorApplication.delayCall += () => EditorApplication.Exit(code);
        }
    }
}
```

`AgentPlay/brief.md` (ghi đè file mẫu do script link tạo):

```markdown
# Tile Thieves: prying prototype

You are a thief in a small museum room with three pixel-art paintings made of small square tiles. Pry tiles off a painting into your sack.

- Win: the sack holds 6 tiles (event game.won). goal.tiles shows the number.
- Lose: the noise meter reaches Lockdown (event game.lost). Every pried tile adds noise; noise slowly fades over time. Levels go Calm, Suspicious, Alarm, Lockdown; noise.level_up tells you when it rises.
- Typical round: goto_painting, then aim_next_tile, then pry with the number of tiles you still need. pry keeps going along the row while it runs.
- If noise climbs fast, stop for a while (wait is not available, so use a short plan with stop) before prying more.
- Tiles that do not fit in the sack fall on the floor; collect_loose picks them up.
```

`AgentPlay/scripts/pry6.json`:

```json
{
  "plans": [
    {
      "id": "t1",
      "intent": "Walk to the nearest painting, aim at a tile and pry six tiles",
      "steps": [
        { "do": "goto_painting", "args": {} },
        { "do": "aim_next_tile", "args": {} },
        { "do": "pry", "args": { "count": 6 } }
      ],
      "rules": [],
      "timeout_s": 90
    }
  ]
}
```

- [ ] **Step 4: Biên dịch trong Unity (batch)**

Run (timeout 600000 ms; Editor TileThieves phải đóng):

```bash
cd /e/Unity-WorkSpace/TileThieves
"/e/6000.0.77f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "$(cygpath -m .)" -logFile "$(cygpath -m Logs/agentplay-compile.log)"
echo "exit=$?"
```

Expected: exit 0. Dùng Grep tìm `error CS` trong `Logs/agentplay-compile.log`; Expected: không có. Nếu `GamepadState.WithButton` hay tên khác của Input System sai thì sửa theo lỗi biên dịch và ghi ruling.

- [ ] **Step 5: Commit (repo TileThieves)**

```bash
git restore Packages/packages-lock.json 2>/dev/null || true
git add .gitignore AgentPlay/brief.md AgentPlay/scripts Assets/_Project/Scripts/AgentAdapter Assets/_Project/Scripts/AgentAdapter.meta
git status --short
git commit -m "feat: AgentPlay adapter for the prying prototype (compiles only with the AgentPlay package)" -m "<trailers>"
```

Expected trong `git status` trước khi commit: không có `Packages/com.agentplay.core`, không có `URP.png` trong vùng staged.

---

### Task 3: E2E TileThieves bằng bộ não kịch bản

**Files (repo AgentPlay):**
- Create: `Tools/e2e-m2c.sh`, `Tools/verify_m2c.py`

- [ ] **Step 1: Viết script**

`Tools/e2e-m2c.sh`:

```bash
#!/usr/bin/env bash
# M2c end-to-end: the script brain plays TileThieves' prying prototype until the sack holds the goal.
# TileThieves must not be open in another Editor and must have the AgentPlay junction and adapter.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GAME="${TILETHIEVES_DIR:-/e/Unity-WorkSpace/TileThieves}"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
BRAIN="${1:-script}"
LOGS="$GAME/Logs"
mkdir -p "$LOGS"
rm -rf "$GAME/AgentPlay/Traces" "$LOGS/agentplay-runner.log" "$LOGS/agentplay-autoplay.log"

dotnet build "$ROOT/Runner/AgentPlay.Runner.csproj" -c Debug -v q || exit 1
if [ "$BRAIN" = "opus" ]; then
  BRAIN_ARGS=(--brain opus)
else
  BRAIN_ARGS=(--brain script "$(cygpath -w "$GAME/AgentPlay/scripts/pry6.json")")
fi
dotnet "$ROOT/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll" "${BRAIN_ARGS[@]}" --idle-exit 30 \
  > "$LOGS/agentplay-runner.log" 2>&1 < /dev/null &
RUNNER_PID=$!

AGENTPLAY_AUTOPLAY_SECONDS=300 "$UNITY_EXE" -projectPath "$(cygpath -m "$GAME")" \
  -executeMethod TileThieves.AgentAdapter.Editor.AgentAdapterAutoPlay.Run -logFile "$(cygpath -m "$LOGS/agentplay-autoplay.log")"
UNITY_CODE=$?
echo "Unity exited with $UNITY_CODE"
wait "$RUNNER_PID"
RUNNER_CODE=$?
echo "Runner exited with $RUNNER_CODE"
git -C "$GAME" restore Packages/packages-lock.json 2>/dev/null || true
python -I "$ROOT/Tools/verify_m2c.py" "$GAME" "$RUNNER_CODE" "$UNITY_CODE"
```

`Tools/verify_m2c.py`:

```python
"""Checks logs and traces from Tools/e2e-m2c.sh. Usage: verify_m2c.py GAME_DIR RUNNER_EXIT UNITY_EXIT"""
import json
import pathlib
import sys

game = pathlib.Path(sys.argv[1])
runner_code = int(sys.argv[2])
unity_code = int(sys.argv[3])
runner_log = (game / "Logs" / "agentplay-runner.log").read_text(encoding="utf-8", errors="replace")
unity_log = (game / "Logs" / "agentplay-autoplay.log").read_text(encoding="utf-8", errors="replace")
failures = []


def check(ok, message):
    print(("PASS " if ok else "FAIL ") + message)
    if not ok:
        failures.append(message)


check(runner_code == 0, "runner exited with 0 (won), got %d" % runner_code)
check(unity_code == 0, "Unity exited with 0 (won), got %d" % unity_code)
check("hello: game=Tile Thieves" in runner_log, "runner received hello from TileThieves")
check("WARNING: the game sent no brief" not in runner_log, "TileThieves sent its brief")
check("game over: won" in runner_log, "runner saw the win")
check("unity error:" not in runner_log, "Unity reported no protocol errors")
check("[TileThieves agent] result=won" in unity_log, "the adapter declared the win")
check("[AgentPlay] virtual gamepad created" in unity_log, "the virtual gamepad was created")
check("[AgentPlay] virtual gamepad removed" in unity_log, "the virtual gamepad was removed when Play mode ended")
check("error CS" not in unity_log, "no compile errors")
check("NullReferenceException" not in unity_log, "no NullReferenceException in the Unity log")
check("[AgentPlay] Registry:" in unity_log and "[AgentPlay] More than one" not in unity_log, "registry built without duplicate games")

records = []
for path in sorted((game / "AgentPlay" / "Traces").glob("*.ndjson")):
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            records.append(json.loads(line))
check(sum(1 for r in records if r.get("type") == "event" and r.get("name") == "tile.pried") >= 6, "trace has at least six tile.pried events")
won = [r for r in records if r.get("type") == "event" and r.get("name") == "game.won"]
check(len(won) == 1 and (won[0].get("state") or {}).get("sack.count", 0) >= 6, "game.won with sack.count >= 6")
check(not any(r.get("type") == "event" and r.get("name") == "game.lost" for r in records), "no game.lost")

print("%d check(s) failed" % len(failures) if failures else "ALL CHECKS PASSED")
sys.exit(1 if failures else 0)
```

- [ ] **Step 2: Chạy E2E và tinh chỉnh**

Run (timeout 600000 ms; một cửa sổ Unity sẽ mở): `cd /e/Unity-WorkSpace/AgentPlay && bash Tools/e2e-m2c.sh`
Expected: `ALL CHECKS PASSED`.

Nếu fail vì nhân vật không tới được tranh, nhắm không trúng, hoặc tay cầm ảo không được nhận khi Editor mất focus: đọc `Logs/agentplay-runner.log` (lý do `plan_failed`), `Logs/agentplay-autoplay.log`, và trace. Chỉ chỉnh hằng số và logic điều khiển trong `TileThievesAgent.cs` (khoảng đứng, độ lợi nhắm, điểm chọn ô) hoặc `VirtualGamepad`; mỗi thay đổi một ruling. Không nới điều kiện trong `verify_m2c.py`.

- [ ] **Step 3: Commit**

Repo AgentPlay:

```bash
git add Tools/e2e-m2c.sh Tools/verify_m2c.py Packages
git commit -m "test: TileThieves prying prototype won by the script brain through the adapter" -m "<trailers>"
```

Repo TileThieves (nếu Task 3 có chỉnh adapter):

```bash
git add Assets/_Project/Scripts/AgentAdapter
git commit -m "fix: tune the AgentPlay adapter steering from the end-to-end run" -m "<trailers>"
```

---

### Task 4: Kiểm TileThieves không có package

**Files:** không có file mới.

- [ ] **Step 1: Gỡ junction tạm thời và kiểm biên dịch**

```bash
cd /e/Unity-WorkSpace/TileThieves
cmd //c rmdir "$(cygpath -w Packages/com.agentplay.core)"
"/e/6000.0.77f1/Editor/Unity.exe" -batchmode -nographics -quit -projectPath "$(cygpath -m .)" -logFile "$(cygpath -m Logs/no-agentplay.log)"
echo "exit=$?"
(cd Tools/LogicTests && dotnet run)
powershell -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w /e/Unity-WorkSpace/AgentPlay/Tools/link-agentplay.ps1)" -ProjectPath "$(cygpath -w .)"
git restore Packages/packages-lock.json 2>/dev/null || true
```

Expected: Unity exit 0 và không có `error CS` trong `Logs/no-agentplay.log` (adapter bị loại vì thiếu define); `Tools/LogicTests` pass; junction được gắn lại; `git status` không có thay đổi ngoài `URP.png` của người dùng.

---

### Task 5: Chạy Opus thật, đồng bộ spec, PR

**Cần:** khóa API của người dùng (`ANTHROPIC_API_KEY` ở biến môi trường user). Người dùng đã đồng ý chi phí khoảng $5–15 cho M2b Task 6 cộng phần này.

- [ ] **Step 1: M2b Task 6** (smoke test, năm ván Heist) theo plan M2b.

- [ ] **Step 2: Ba lần TileThieves với Opus**

Run (mỗi lần timeout 600000 ms): `cd /e/Unity-WorkSpace/AgentPlay && bash Tools/e2e-m2c.sh opus` ba lần.
Expected: cả ba lần `ALL CHECKS PASSED` (tiêu chí 3 của spec M2). Ghi số lượt gọi và token từ dòng `model calls ...` của `Logs/agentplay-runner.log` sau mỗi lần.

- [ ] **Step 3: Đồng bộ spec M2**

Trong `docs/superpowers/specs/2026-10-12-agentplay-m2-design.md`, mục 11:
- Danh sách action thành: `goto_painting()`, `aim_next_tile()`, `pry(count)`, `collect_loose()`, `crouch(on)`, `stop()` (an toàn), đúng như code.
- Thêm: "Tay cầm ảo chỉ được tạo khi agent gọi action đầu tiên; trong lúc nó tồn tại, `InputSystem.settings.backgroundBehavior` là `IgnoreFocus`."
- Thêm: "Observe `loose.count` dùng `LooseTilePool.ActiveTiles`. Mục tiêu 6 viên là hằng số `TileThievesAgent.GoalTiles`."
- Thêm: "Thay đổi `Packages/packages-lock.json` do package nhúng không được commit vào TileThieves."
Mục 16: ghi kết quả thật của rủi ro 3, 4, 5.

- [ ] **Step 4: Ghi kết quả**

Tạo `docs/notes/m2c-verification.md` (số liệu thật: E2E kịch bản, Task 4, ba lần Opus, các ruling khi tinh chỉnh).

- [ ] **Step 5: PR TileThieves**

```bash
cd /e/Unity-WorkSpace/TileThieves
git push -u origin feat/agentplay-adapter
gh pr create --base main --title "AgentPlay adapter for the prying prototype" --body-file <file soạn sẵn mô tả adapter, cách gắn package, cách chạy, và kết quả kiểm chứng; kết thúc bằng dòng attribution PR>
```

Người dùng đã đồng ý mở PR để xem (câu trả lời "Tôi sẽ đóng Editor" kèm mô tả mở PR).
