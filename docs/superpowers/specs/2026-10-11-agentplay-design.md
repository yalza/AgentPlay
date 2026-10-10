# AgentPlay: agent LLM tự chơi game Unity qua Editor

**Ngày:** 2026-10-11
**Trạng thái:** Bản nháp, chờ người dùng duyệt
**Repo:** `E:\Unity-WorkSpace\AgentPlay` (repo riêng, chưa có remote)
**Game thử đầu tiên:** TileThieves (`E:\Unity-WorkSpace\TileThieves`, Unity 6000.0.77f1, Input System mới)

---

## 1. Mục tiêu

Một agent dùng model Claude tự chơi được các game Unity do người dùng tự làm. Agent chạy qua Editor ở Play mode, chơi càng lâu càng nhanh, và học được từ cả lượt tự chơi lẫn lượt người dùng tự tay test.

Ba nguyên tắc thiết kế, rút ra từ phần thảo luận trước khi viết spec:

1. **Gọi model ít nhất có thể.** Model trả về một *kế hoạch có điều kiện* gồm nhiều bước kèm luật ứng phó. Game tự chạy kế hoạch đó từng frame, và chỉ gọi lại model khi gặp tình huống kế hoạch không lường trước.
2. **Độ trễ không được làm hỏng gameplay.** Ở chế độ theo lượt, Editor dừng thời gian trong lúc chờ model. Ở chế độ thời gian thực, nhân vật làm *hành vi an toàn* trong lúc chờ.
3. **Kinh nghiệm được biên dịch thành phản xạ.** Bài học đủ chắc từ playbook trở thành luật chạy trong game. Lần sau gặp lại, tình huống đó không cần gọi model nữa.

### Định nghĩa "xong" cho toàn hệ thống

1. Cài package vào TileThieves, gắn attribute adapter, bấm Play. Runner kết nối vào và agent tự chơi trọn một vụ trộm: vào bảo tàng, cạy gạch, mang về, không ai điều khiển.
2. Ở chế độ theo lượt, kết quả chơi không phụ thuộc thời gian model suy nghĩ.
3. Người dùng tự chơi một phiên được gắn nhãn "chơi thật". Sau phiên, playbook có bài học mới rút ra từ phiên đó.
4. Một tình huống đã thành phản xạ (đã duyệt) được xử lý mà không gọi model. Telemetry chứng minh điều này.
5. Telemetry cho thấy số lượt gọi model mỗi phút giảm dần qua các phiên trên cùng một màn chơi.

### Ngoài phạm vi

- Game không phải Unity, game không có source, hoặc game không mở trong Editor.
- Chơi cùng người thật qua mạng ở chế độ thời gian thực. Co-op có người thật vẫn chạy được, nhưng không có bảo đảm về độ trễ.
- Behavior cloning hay huấn luyện mạng nơ-ron. Có thể thêm sau nếu tầng phản xạ dạng luật không đủ.
- Chạy trong bản build. Package chỉ hoạt động trong Editor; phần runtime cho build để dành về sau.
- MCP server để Claude Code điều khiển agent. Có thể thêm sau trên cùng giao thức.
- Input Manager cũ. Game của người dùng thống nhất dùng Input System mới.

---

## 2. Bối cảnh

- **TileThieves** dùng Input System 1.19 (`activeInputHandler: 1`). Input đọc qua `ThiefInput` (`Move`, `LookDelta`, `PryHeld`, `InteractPressed`...). Logic thuần đã tách khỏi MonoBehaviour: `NoiseMeter`, `SackInventory`, `PrySession`, `GuardBrain`. Các component `PlayerController`, `PlayerSack`, `NoiseSystem`, `PryTool` có property public phù hợp để quan sát.
- **BotPlayGame** là repo riêng làm record và replay input cho game mobile, mới có spec và plan, chưa có code. Người dùng đã chọn để AgentPlay **độc lập hoàn toàn**: không phụ thuộc và không dùng chung code với BotPlay.
- **unity-mcp và UnitySkills** dùng để sửa project, không dùng để chơi. AgentPlay không phụ thuộc vào chúng.

---

## 3. Kiến trúc

```
┌──────────────────────── Unity Editor (Play mode) ─────────────────────────┐
│  Game code  ──[AgentObserve]/[AgentAction]/AgentEvents.Emit──┐            │
│                                                              ▼            │
│  AgentPlay.Runtime: Registry · Executor · Reflexes · SafeBehavior ·       │
│                     TraceRecorder · InputDriver                           │
│  AgentPlay.Editor:  BridgeServer (TCP 127.0.0.1) · StepController         │
└──────────────────────────────────────┬────────────────────────────────────┘
                                       │ NDJSON qua TCP
┌──────────────────────────────────────┴────────────────────────────────────┐
│  Runner (.NET console): SessionLoop · ModelRouter · PlanValidator ·       │
│                         Prefetcher · Reflector · Telemetry                │
└──────────────────────────────────────┬────────────────────────────────────┘
                                       │ HTTPS
                                Anthropic API (Opus 5.5 / Haiku 5.5)
```

### 3.1 Cấu trúc repo

```
AgentPlay/
  Packages/com.agentplay.core/        package UPM, các game tham chiếu qua git URL
    Runtime/                          asmdef AgentPlay.Runtime
    Editor/                           asmdef AgentPlay.Editor
    Tests/EditMode/
  Contract/                           kiểu dữ liệu dùng chung (C#, netstandard2.1, không dùng UnityEngine)
  Runner/                             .NET console, tham chiếu Contract
  Runner.Tests/
  .claude/skills/agentplay-adapt/     skill sinh adapter cho game mới (M4)
  docs/
```

`Contract/` chứa định nghĩa message, Plan DSL và bộ đánh giá điều kiện. Unity package nhúng cùng các file nguồn này: `Contract/` là thư mục nguồn duy nhất, và package tham chiếu tới nó bằng một asmdef riêng `AgentPlay.Contract` đặt trong package. Cách nối cụ thể (copy lúc build hay symlink) sẽ chốt trong plan M1, sau khi thử trong Editor. Điều bắt buộc: hai bên luôn dùng **cùng một mã nguồn** cho parser điều kiện, để luật chạy trong game và luật runner kiểm tra cho cùng một kết quả.

### 3.2 Đơn vị và trách nhiệm

| Đơn vị | Nằm ở | Việc làm | Phụ thuộc |
| --- | --- | --- | --- |
| `AgentRegistry` | Runtime | Quét attribute khi vào Play mode, lập danh sách observe/action/event, xuất mô tả cho runner | Reflection |
| `AgentEvents` | Runtime | API tĩnh `Emit(name, payload)` cho game gọi, đưa vào hàng đợi sự kiện | Không |
| `Executor` | Runtime | Chạy Plan từng frame: bước hiện tại, điều kiện `until`, luật `rules` | Registry, Contract |
| `ReflexSet` | Runtime | Luật đã duyệt, chạy trước Plan, có ưu tiên cao nhất | Contract |
| `SafeBehavior` | Runtime | Hành động mặc định khi không có Plan (game khai báo qua `[AgentAction(safe = true)]`) | Registry |
| `InputDriver` | Runtime | Bơm input qua Input System cho game chưa có action cấp cao (mức 0) | Input System |
| `TraceRecorder` | Runtime | Ghi sự kiện, ảnh chụp trạng thái và kết quả hành động ra file NDJSON | Registry |
| `BridgeServer` | Editor | TCP server, nhận và gửi message, chuyển việc về main thread qua `EditorApplication.update` | Contract |
| `StepController` | Editor | Dừng và chạy tiếp Play mode cho chế độ theo lượt | `EditorApplication` |
| `SessionLoop` | Runner | Vòng chính: nhận sự kiện, quyết định có gọi model không, gửi Plan | Tất cả bên dưới |
| `ModelRouter` | Runner | Chọn Haiku hoặc Opus và mức effort, quản lý prompt caching | Anthropic SDK |
| `PlanValidator` | Runner | Kiểm Plan model trả về theo danh sách action và observe | Contract |
| `Prefetcher` | Runner | Xin trước Plan tiếp theo khi Plan hiện tại sắp xong | ModelRouter |
| `Reflector` | Runner | Sau phiên: đọc trace, cập nhật playbook, đề xuất phản xạ | ModelRouter |
| `Telemetry` | Runner | Đếm lượt gọi, thời gian chờ, tỉ lệ thời gian chạy bằng phản xạ | Không |

Bridge nằm ở Editor vì `Update` không chạy khi Editor đang dừng, còn `EditorApplication.update` thì vẫn chạy.

---

## 4. Adapter: game khai báo cho agent

Game chỉ thêm attribute và lời gọi phát sự kiện, không kế thừa lớp nào của AgentPlay.

```csharp
public class NoiseSystem : MonoBehaviour {
    [AgentObserve("noise.value", "Noise meter 0-100")]
    public float DisplayValue => ...;
}

public class PlayerSack : MonoBehaviour {
    [AgentObserve("sack.tiles")] public int TileCount => ...;
    [AgentObserve("sack.full")]  public bool IsFull => ...;
}

public class ThiefAgentActions : MonoBehaviour {   // game tự viết, mỏng
    [AgentAction("goto", "Walk to a named zone", safe = false)]
    public AgentActionStatus GoTo(string zone) { ... }   // gọi mỗi frame tới khi trả Done/Failed

    [AgentAction("hide", "Crouch at nearest cover", safe = true)]
    public AgentActionStatus Hide() { ... }
}

// Trong logic game, bất kể người hay agent đang chơi:
AgentEvents.Emit("guard.spotted", new { guard = id, distance = d });
```

Quy tắc:

- **`[AgentObserve(path)]`** đặt trên field hoặc property kiểu số, bool, string, enum hoặc `Vector3`. `path` là tên ổn định mà Plan và luật dùng để tham chiếu. Registry gom thành một snapshot JSON phẳng.
- **`[AgentAction(name)]`** đặt trên method trả `AgentActionStatus` (`Running`, `Done`, `Failed`). Tham số chỉ dùng kiểu cơ bản. Executor gọi lại method mỗi frame cho tới khi xong. Tối đa một action được đánh dấu `safe = true`; đó là hành vi an toàn mặc định (mục 7.2).
- **`AgentEvents.Emit(name, payload, important = false, outcome = Neutral)`** phát sự kiện có nghĩa. `important` đánh dấu sự kiện cần báo runner khi không luật nào bắt (mục 6). `outcome` (`Good`, `Neutral`, `Bad`) dùng để chấm điểm phản xạ (mục 8.4). Đây vừa là điểm ngắt kế hoạch, vừa là dữ liệu để học từ phiên người chơi.
- **`[AgentGame(name, version)]`** đặt trên một lớp bất kỳ, khai báo tên game và phiên bản luật chơi. TileThieves trả version bằng hash của `BalanceConfig`. Đổi version thì playbook và phản xạ cũ bị đánh dấu cần kiểm lại (mục 8.4).
- Object có nhiều bản (nhiều bảo vệ) dùng `[AgentObserveList("guards")]` trên một `IEnumerable` trả các object có field `[AgentObserve]`.

Game chưa có adapter vẫn chơi được ở **mức 0**: action duy nhất là `input`, do `InputDriver` thực hiện (giữ hoặc nhả phím, di chuột), và quan sát gồm ảnh chụp Game view cộng cây scene rút gọn. Mức 0 là phương án dự phòng, không phải trọng tâm tối ưu.

---

## 5. Giao thức Bridge

TCP trên `127.0.0.1`, cổng mặc định 47800 (nếu bận thì thử tiếp lên 47809). Mỗi dòng là một JSON message có `type` và `seq`. Unity là server; runner tự kết nối lại khi Play mode khởi động lại.

| Chiều | `type` | Nội dung |
| --- | --- | --- |
| Unity → Runner | `hello` | Tên và version game, danh sách observe, action (kèm tham số), event đã khai báo, các mode được hỗ trợ |
| Unity → Runner | `state` | Snapshot observe và thời gian game |
| Unity → Runner | `event` | Sự kiện game, kèm snapshot tại thời điểm đó |
| Unity → Runner | `plan_status` | `step_done`, `plan_done`, `plan_failed` (kèm lý do), `rule_fired`, `reflex_fired` |
| Unity → Runner | `waiting` | Executor hết Plan, đang chạy hành vi an toàn |
| Runner → Unity | `set_mode` | `turn_based` hoặc `realtime` |
| Runner → Unity | `plan` | Plan mới, thay thế Plan cũ |
| Runner → Unity | `resume` | Chế độ theo lượt: chạy tiếp tới sự kiện ngắt tiếp theo hoặc tối đa N giây game |
| Runner → Unity | `set_reflexes` | Danh sách phản xạ đã duyệt |
| Runner → Unity | `get_state` | Xin snapshot ngay |
| Runner → Unity | `screenshot` | Chỉ ở mức 0: chụp Game view, trả PNG base64 |

---

## 6. Plan DSL

```json
{
  "id": "p-42",
  "intent": "Take tiles from painting A, retreat when loud",
  "steps": [
    { "do": "goto", "args": { "zone": "painting_A" } },
    { "do": "pry",  "args": {}, "until": "sack.full || noise.value > 70" },
    { "do": "goto", "args": { "zone": "van" } }
  ],
  "rules": [
    { "when": "event:guard.spotted", "do": "hide", "then": "replan" },
    { "when": "noise.value > 85",    "do": "hide", "then": "resume" }
  ],
  "timeout_s": 90
}
```

- **Điều kiện** là ngôn ngữ biểu thức nhỏ: so sánh (`< <= > >= == !=`), `&& || !`, ngoặc, hằng số, observe path và `event:<name>`. Đúng khi sự kiện đó vừa xảy ra trong frame hiện tại. Không có lời gọi hàm, không có vòng lặp. Parser nằm trong `Contract`.
- **`then`** của luật quyết định việc tiếp theo: `resume` quay lại bước đang dở, `next` sang bước kế tiếp, `replan` dừng Plan và gọi model, `abort` hủy Plan và chạy hành vi an toàn.
- **Thứ tự ưu tiên mỗi frame:** phản xạ, rồi luật của Plan, rồi bước hiện tại.
- **Sự kiện ngoài Plan:** sự kiện mà không luật hay phản xạ nào bắt sẽ được gửi về runner. Runner quyết định gọi model hay bỏ qua, dựa trên danh sách sự kiện quan trọng mà game khai báo bằng `AgentEvents.Emit(..., important: true)`.
- **Kiểm Plan:** `PlanValidator` kiểm tra tên action, kiểu tham số, observe path và cú pháp điều kiện. Plan sai thì gửi lỗi cho model và xin lại **một lần**. Lần thứ hai vẫn sai thì chạy hành vi an toàn và ghi lỗi vào telemetry.

---

## 7. Hai chế độ chạy và cách giảm độ trễ

### 7.1 Theo lượt (mặc định khi thử trong Editor)

Khi Executor cần model (Plan xong, Plan hỏng, có sự kiện quan trọng), `StepController` đặt `EditorApplication.isPaused = true`. Có Plan mới thì runner gửi `resume` và game chạy tiếp. Kết quả không phụ thuộc thời gian model suy nghĩ.

### 7.2 Thời gian thực

Không dừng thời gian. Trong lúc chờ, `SafeBehavior` chạy. Các kỹ thuật giảm độ trễ:

| Kỹ thuật | Đơn vị phụ trách |
| --- | --- |
| Kế hoạch nhiều bước có luật ứng phó, mỗi lượt gọi đổi được hàng chục giây tự chơi | Model, Executor |
| Xin trước: khi Plan đã chạy tới bước cuối, runner gửi trạng thái dự đoán để xin Plan tiếp theo. Có sự kiện ngoài dự đoán thì bỏ Plan xin trước | Prefetcher |
| Định tuyến: phản ứng nhanh với bất ngờ nhỏ dùng Haiku 5.5 effort thấp; chiến thuật dùng Opus 5.5; rút kinh nghiệm sau phiên dùng Opus effort cao | ModelRouter |
| Prompt cache cho phần cố định (luật game, mô tả action, playbook); chỉ gửi snapshot gọn và phần thay đổi | ModelRouter |
| Phản xạ chạy trong frame, không gọi model | ReflexSet |

Quy tắc định tuyến cụ thể (khởi đầu, sẽ chỉnh theo telemetry):

- `plan_failed` do một luật có `then: replan`, hoặc sự kiện quan trọng trong lúc đang có Plan: **Haiku**, yêu cầu một Plan ngắn tối đa 3 bước.
- `plan_done`, bắt đầu màn mới, hoặc Haiku replan hai lần liên tiếp trong 30 giây: **Opus**.

Model ID nằm trong file cấu hình của runner, không viết cứng trong code.

---

## 8. Học: trace, playbook, phản xạ

### 8.1 Trace

`TraceRecorder` luôn ghi, dù người hay agent đang chơi. File `AgentPlay/Traces/<yyyyMMdd-HHmmss>-<who>-<tag>.ndjson` nằm trong project game. Mỗi dòng là một sự kiện kèm snapshot, một lần bắt đầu hoặc kết thúc action, hoặc một snapshot định kỳ (mặc định mỗi 2 giây). Header ghi tên game, version, người chơi (`human` hoặc `agent`) và nhãn phiên.

### 8.2 Nhãn phiên

Người dùng chọn nhãn trong cửa sổ Editor **AgentPlay** (menu `Window → AgentPlay`): `play` (chơi thật, dùng để học) hoặc `debug` (test bug, không học). Mặc định là `debug`, để tránh học nhầm hành vi cố tình phá game.

### 8.3 Reflector và playbook

Sau mỗi phiên có nhãn `play`, runner chạy `Reflector` (Opus, effort cao) trên trace.

1. Cập nhật `AgentPlay/playbook.md` trong project game. Mỗi bài học có ID, mô tả, version game, số lần chứng thực, số lần trái ngược và trace nguồn.
2. Bài học nào có thể viết thành luật DSL thì đề xuất thành **phản xạ ứng viên** trong `AgentPlay/reflexes.json`, trạng thái `proposed`.

Playbook được nạp vào phần prompt cố định (được cache) mỗi lần gọi model chiến thuật.

### 8.4 Vòng đời phản xạ

```
proposed ──(người dùng duyệt trong cửa sổ AgentPlay)──▶ active ──(tỉ lệ thất bại > ngưỡng)──▶ suspended
    │                                                     │
    └──(người dùng từ chối)──▶ rejected                   └──(đổi version game)──▶ needs_review
```

- **Đề xuất:** chỉ đề xuất khi bài học có từ 3 lần chứng thực trở lên và không có lần trái ngược nào.
- **Đánh giá:** mỗi lần phản xạ kích hoạt, Executor ghi kết quả trong cửa sổ đánh giá (mặc định 10 giây sau). Kết quả xấu nghĩa là trong cửa sổ đó xảy ra sự kiện game khai báo là thất bại (`AgentEvents.Emit(..., outcome: Bad)`), ví dụ bị bắt.
- **Tự tắt:** phản xạ chuyển sang `suspended` khi tỉ lệ thất bại vượt 30% sau ít nhất 5 lần kích hoạt.
- **Đổi version game:** phản xạ đang `active` chuyển sang `needs_review` và không chạy cho tới khi được duyệt lại.

Tất cả ngưỡng nằm trong file cấu hình của runner.

---

## 9. Xử lý lỗi

| Tình huống | Xử lý |
| --- | --- |
| Runner mất kết nối | Executor chạy nốt Plan hiện tại rồi chạy hành vi an toàn. Ở chế độ theo lượt, Editor dừng và chờ |
| Thoát Play mode | Bridge đóng, trace được flush. Runner đánh dấu phiên kết thúc và chạy Reflector nếu nhãn là `play` |
| API lỗi hoặc quá 20 giây | Thử lại một lần với Haiku, sau đó dùng hành vi an toàn, ghi telemetry |
| Plan không hợp lệ | Xin lại một lần (mục 6), sau đó dùng hành vi an toàn |
| Action ném exception | Bắt lại, coi là `Failed`, phát sự kiện `agent.action_error` kèm stack, gửi `plan_failed` |
| Action chạy quá `timeout_s` | `plan_failed`, lý do `timeout` |
| Snapshot quá lớn (hơn 4 KB) | Ghi cảnh báo trong Console và cắt bớt danh sách observe lớn nhất |
| Game không có action `safe` | Hành vi an toàn mặc định là không làm gì (không bơm input) |

---

## 10. Kiểm thử

- **`Contract`**: parser điều kiện, kiểm Plan và serialize message, test bằng xUnit chạy `dotnet test`, không cần Unity.
- **Runtime**: test EditMode cho Registry (quét attribute trên lớp giả), Executor (Plan cùng luật cùng phản xạ trên action giả) và vòng đời phản xạ. Executor tách lõi thuần nhận `deltaTime` và snapshot để test không cần Play mode.
- **Runner**: `SessionLoop`, `ModelRouter` và `Prefetcher` test với `IModelClient` giả trả Plan định sẵn, không gọi API thật.
- **Tích hợp**: một scene test tối giản trong package (khối lập phương đi tới điểm đích, có "lính gác" đi tuần) cho runner kết nối thật, chạy theo lượt với model giả. Đây là cách kiểm tra ổn định nhất cho Bridge và StepController.
- **Thực tế**: TileThieves với model thật, đo theo mục 11.

---

## 11. Telemetry và tiêu chí đo

Runner ghi `AgentPlay/Telemetry/<phiên>.json` trong project game:

- Số lượt gọi model mỗi phút, tách theo Opus và Haiku.
- Thời gian chờ model: trung vị và p90.
- Tỉ lệ thời gian game chạy bằng phản xạ, bằng Plan, bằng hành vi an toàn.
- Số Plan hỏng, phân loại theo lý do.
- Token vào, token ra và token đọc từ cache.

Tiêu chí 5 của mục 1 được kiểm bằng cách so số lượt gọi mỗi phút của 5 phiên liên tiếp trên cùng màn chơi.

---

## 12. Các mốc

Mỗi mốc có một implementation plan riêng.

| Mốc | Nội dung | Xong khi |
| --- | --- | --- |
| **M1: Nền** | Contract, Registry, AgentEvents, BridgeServer, StepController, TraceRecorder, cửa sổ AgentPlay (trạng thái kết nối, nhãn phiên); runner kết nối, in `hello` và sự kiện | Scene test gửi `hello` và sự kiện tới runner; trace được ghi; theo lượt dừng và chạy tiếp được |
| **M2: Tự chơi theo lượt** | Plan DSL, Executor, SafeBehavior, PlanValidator, SessionLoop với Opus; adapter TileThieves | Agent tự chơi trọn một vụ trộm trong TileThieves ở chế độ theo lượt (tiêu chí 1 và 2) |
| **M3: Học** | Reflector, playbook, phản xạ cùng vòng đời và cửa sổ duyệt | Tiêu chí 3 và 4 |
| **M4: Nhanh và mở rộng** | Chế độ thời gian thực, Haiku routing, Prefetcher, telemetry đầy đủ; InputDriver mức 0; skill `agentplay-adapt` | Tiêu chí 5; một game thứ hai của người dùng chạy được với adapter do skill sinh |

---

## 13. Rủi ro và điều chưa kiểm chứng

Những điều dưới đây là giả định thiết kế, **chưa được thử trong phiên này**. Plan M1 phải kiểm từng điều trước khi xây lên trên.

1. `EditorApplication.isPaused` kết hợp `EditorApplication.update` đủ để giữ Bridge hoạt động trong lúc dừng, và `resume` chạy tiếp game đúng như mong đợi.
2. `TcpListener` trên thread nền trong Editor không gây treo khi domain reload. Bridge phải đóng socket trong `AssemblyReloadEvents.beforeAssemblyReload`.
3. Nhúng mã nguồn `Contract` vào package UPM qua git URL: cách nối thư mục ngoài package (mục 3.1).
4. SDK C# chính thức của Anthropic hỗ trợ prompt caching và chọn effort theo cách runner cần. Nếu không, runner gọi HTTP trực tiếp.
5. Reflection quét attribute khi vào Play mode đủ nhanh với game cỡ TileThieves (mục tiêu dưới 50 ms). Nếu chậm thì dùng `TypeCache` của Editor.
6. Chất lượng Plan do model sinh trong TileThieves chỉ biết được sau M2. Nếu Plan nhiều bước hỏng quá thường xuyên, phải rút ngắn Plan và chấp nhận gọi model nhiều hơn.

---

## 14. Quyết định đã chốt

**Người dùng chọn:**

- AgentPlay là repo riêng, độc lập với BotPlay.
- Bộ não là runner riêng gọi Anthropic API.
- Phạm vi là game Unity do người dùng tự làm.

**Tôi chọn, người dùng có thể đổi khi duyệt spec:**

- Runner viết bằng .NET (C#), để dùng chung mã nguồn `Contract` với Unity.
- Giao thức dùng TCP với JSON theo dòng thay vì WebSocket: đơn giản và chắc chắn chạy trong Editor.
- Package tên `com.agentplay.core`, cổng mặc định 47800.
- Nhãn phiên mặc định là `debug`.
- Playbook, phản xạ, trace và telemetry lưu ở thư mục `AgentPlay/` tại gốc project game (cạnh `Assets/`, không nằm trong `Assets/` để Unity không import và sinh `.meta`). Có commit vào git hay không do từng game quyết định.
