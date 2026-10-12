# AgentPlay M2: Opus tự chơi theo lượt

**Ngày:** 2026-10-12
**Trạng thái:** Bản nháp, chờ người dùng duyệt
**Spec gốc:** `docs/superpowers/specs/2026-10-11-agentplay-design.md` (mục 6, 7.1, 12). M2 kế thừa M1 đã merge (`docs/notes/m1-verification.md`).

---

## 1. Mục tiêu

Opus 5.5 tự chơi hai game Unity khác nhau ở chế độ theo lượt, qua cùng một package và cùng một runner, không sửa code chung cho từng game.

### Đã chốt với người dùng

- Phạm vi: một game demo "trộm nhỏ" trong Testbed của AgentPlay chứng minh vòng chơi đầy đủ (bảo vệ đi tuần, lấy đồ, thoát ra). TileThieves chỉ dùng phần game đang có: cạy gạch, nhặt gạch, giữ ồn thấp. TileThieves chưa có bảo vệ trong scene và chưa có VanZone; khi có thì chỉ cần mở rộng adapter.
- Executor chạy trong Unity; Opus trả kế hoạch JSON có luật ứng phó (cách A).
- Adapter TileThieves là asmdef riêng, đọc trạng thái qua API public, lái nhân vật bằng tay cầm ảo, chỉ biên dịch khi có package AgentPlay. Package được gắn qua junction git-ignore nên `manifest.json` của nhóm không đổi.
- Agent phải chạy được ở mọi project Unity, không riêng TileThieves (mục 2).

### Định nghĩa "xong"

1. **Testbed, model giả theo kịch bản:** E2E tự động, không gọi API, agent lấy đủ 3 món và thoát ra. Chạy được bất kỳ lúc nào, không tốn tiền.
2. **Testbed, Opus thật:** trong 5 lần chạy theo lượt, ít nhất 3 lần Opus lấy đủ 3 món và thoát mà không bị bắt. Mỗi lần chạy giới hạn 40 lượt gọi model.
3. **TileThieves, Opus thật:** trong scene `Prototype_Pry`, Opus tự cạy và nhặt đủ số viên ghi trong `brief.md` (mặc định 6) mà mức ồn không lên mức cao nhất, trong 3 lần chạy liên tiếp.
4. **Tổng quát:** không file nào trong `Packages/com.agentplay.core` và `Runner/` nhắc tới Testbed hay TileThieves; script `Tools/check-generic.sh` kiểm điều này và nằm trong bộ kiểm tra cuối.
5. Trace của phiên agent ghi đủ từng Plan và kết quả của nó.

### Ngoài phạm vi M2

- Playbook, phản xạ, học từ trace (M3).
- Chế độ thời gian thực, Haiku, xin trước kế hoạch (M4).
- Mức 0 "chơi mù" bằng ảnh chụp (M4).
- Chọn game khi có hai Editor cùng chạy AgentPlay (hoãn từ M1, xem ghi chú M1).
- Viết GuardAI hay VanZone cho TileThieves (việc của backlog TileThieves).

---

## 2. Tính tổng quát

Đây là ràng buộc thiết kế, không phải mong muốn:

- **Package và runner không chứa kiến thức game.** Mọi thứ runner đưa vào prompt đến từ `hello`: tên game, `brief`, observe, action, sự kiện đã khai báo. Không có nhánh code nào theo tên game.
- **Mỗi game chỉ cung cấp ba thứ:**
  - attribute `[AgentObserve]`, `[AgentObserveList]`, `[AgentAction]`, `[AgentEventInfo]` (trực tiếp trên code game, hoặc trên một lớp adapter riêng như TileThieves);
  - lời gọi `AgentEvents.Emit` (trong code game hoặc adapter);
  - file `AgentPlay/brief.md` ở gốc project: mục tiêu, luật thắng thua, mẹo. Unity đọc file này lúc bắt đầu phiên và gửi trong `hello.brief`. Thiếu file thì `brief` rỗng và runner cảnh báo.
- **Gắn package vào project bất kỳ:** script `Tools/link-agentplay.ps1 <đường dẫn project>` tạo junction `Packages/com.agentplay.core` trỏ về package và thêm dòng git-ignore. Unity coi đây là embedded package, không cần sửa `manifest.json`. Adapter đặt trong asmdef có `defineConstraints` theo `versionDefines` của package, nên project vẫn biên dịch khi không có AgentPlay.
- **Kết thúc ván:** hai tên sự kiện dành riêng `game.won` và `game.lost` (kèm `outcome`). Runner dừng gọi model khi nhận một trong hai và ghi kết quả.
- **Kiểm tra:** `Tools/check-generic.sh` tìm chuỗi `TileThieves` và `Testbed` (không phân biệt hoa thường) trong `Packages/com.agentplay.core/{Contract,Runtime,Editor}` và `Runner/`; có kết quả là fail.

---

## 3. Thành phần mới

| Đơn vị | Nằm ở | Việc làm |
| --- | --- | --- |
| `ConditionParser`, `ConditionEvaluator` | Contract | Parse và đánh giá biểu thức điều kiện (mục 5) trên snapshot và tập sự kiện của frame |
| `PlanModel`, `PlanValidator` | Contract | Kiểu dữ liệu Plan và kiểm Plan theo danh sách action, observe, sự kiện trong `hello` |
| `AgentObserveListAttribute` | Runtime | Danh sách đối tượng cùng loại, ví dụ bảo vệ (mục 6) |
| `AgentEventInfoAttribute` | Runtime | Khai báo tên sự kiện, mô tả, `Important`, `Outcome` để đưa vào `hello` |
| `AgentActionContext` | Runtime | Tham số đầu tùy chọn của action: `IsFirstFrame`, `Elapsed` |
| `ActionInvoker` | Runtime | Gọi method action bằng reflection với tham số từ JSON |
| `ExecutorCore` | Runtime | Chạy Plan từng frame, lõi thuần nhận snapshot, sự kiện và `deltaTime` để test không cần Play mode |
| `AgentExecutorHost` | Runtime | MonoBehaviour ẩn tạo khi có Plan, gọi `ExecutorCore` trong `Update` |
| `VirtualGamepad` | asmdef `AgentPlay.InputSystem` | Tay cầm ảo cho adapter (mục 9), chỉ biên dịch khi project có Input System |
| `SessionLoop` | Runner | Vòng chính: nhận sự kiện, quyết định gọi model, gửi Plan, dừng khi `game.won`/`game.lost` |
| `ModelClient` (`IModelClient`) | Runner | Gọi Opus qua SDK chính thức `Anthropic` trên NuGet; có bản giả `ScriptedModelClient` |
| `PromptBuilder` | Runner | Dựng phần prompt cố định (cache) và phần mỗi lượt |
| `RunLog` | Runner | Ghi mỗi lượt gọi model: thời gian, token, Plan, kết quả |

`SessionCore` (M1) được mở rộng để nhận `plan`, chuyển cho executor và dừng game theo luật mục 7.

---

## 4. Giao thức: thay đổi so với M1

| Chiều | `type` | Nội dung |
| --- | --- | --- |
| Unity → Runner | `hello` | Thêm `brief` (string), `events` (danh sách `{name, description, important, outcome}`), `project_root` (đường dẫn tuyệt đối, để runner ghi `RunLog` cạnh trace) |
| Runner → Unity | `plan` | Plan mới (mục 5), thay Plan đang chạy |
| Unity → Runner | `plan_status` | `{plan_id, status, step, reason}`; `status` là `step_done`, `plan_done`, `plan_failed`, `rule_fired` |
| Unity → Runner | `waiting` | Executor hết Plan và đang chạy action `Safe` |
| Runner → Unity | `stop_plan` | Hủy Plan đang chạy, chuyển sang action `Safe` |

`ProtocolVersion.Current` tăng lên 2. Runner và Unity khác version thì runner in lỗi và không gửi Plan.

---

## 5. Plan DSL

```json
{
  "id": "p-3",
  "intent": "Grab item_b while the guard patrols the far side, then hide",
  "steps": [
    { "do": "goto", "args": { "zone": "item_b" }, "until": "min(guards.distance) < 3" },
    { "do": "pick", "args": { "item": "item_b" } },
    { "do": "hide", "args": { "zone": "cover_1" } }
  ],
  "rules": [
    { "when": "event:guard.spotted", "do": "hide", "args": { "zone": "cover_2" }, "then": "replan" },
    { "when": "any(guards.chasing)", "do": "goto", "args": { "zone": "exit" }, "then": "abort" }
  ],
  "timeout_s": 60
}
```

### Ngữ nghĩa

- **Bước:** Executor gọi method action mỗi frame tới khi nó trả `Done` hoặc `Failed`. Có `until` thì điều kiện được kiểm mỗi frame sau lời gọi; đúng thì bước coi như xong dù action trả `Running`. `Failed` thì Plan hỏng với lý do `action_failed:<tên>`.
- **Luật:** mỗi frame, trước khi chạy bước, kiểm `when` của từng luật theo thứ tự. Luật chỉ bắn ở sườn lên (điều kiện từ sai thành đúng), để không bắn lặp lại mỗi frame. Khi bắn: chạy action `do` của luật tới khi `Done` hoặc `Failed`, rồi làm theo `then`: `resume` quay lại bước đang dở (gọi lại với `IsFirstFrame = true`), `next` sang bước kế, `replan` kết thúc Plan với lý do `replan:<chỉ số luật>`, `abort` kết thúc Plan và chạy `Safe`. Trong lúc luật đang chạy action, các luật khác vẫn được kiểm và có thể ngắt.
- **Hết giờ:** `timeout_s` tính bằng giây game; hết thì Plan hỏng với lý do `timeout`.
- **Hành vi an toàn:** khi không có Plan, Executor gọi action có `Safe = true` mỗi frame. Action `Safe` không được có tham số (registry báo lỗi nếu có).
- **Giới hạn:** tối đa 12 bước và 8 luật mỗi Plan.

### Ngôn ngữ điều kiện

```
expr     := or
or       := and ( "||" and )*
and      := unary ( "&&" unary )*
unary    := "!" unary | compare
compare  := term ( ( "<" | "<=" | ">" | ">=" | "==" | "!=" ) term )?
term     := number | "true" | "false" | string | event | call | path | "(" expr ")"
event    := "event:" name
call     := ( "min" | "max" | "count" | "any" ) "(" path ")"
path     := segment ( "." segment )*        segment := [a-z_][a-z0-9_]*
string   := "'" ... "'"
```

- `event:name` đúng trong frame sự kiện đó được phát.
- `min`, `max` lấy trên một trường số của danh sách (`min(guards.distance)`); `count(guards)` đếm phần tử; `any(guards.sees_player)` đúng nếu có phần tử nào đúng. Danh sách rỗng: `min`/`max` trả vô cực dương/âm, `count` trả 0, `any` trả sai.
- Path trỏ tới observe không tồn tại hoặc đang null thì so sánh trả sai, không ném lỗi.
- Sai kiểu (so sánh chuỗi với số) thì `PlanValidator` báo lỗi khi kiểm Plan; lúc chạy trả sai.

---

## 6. Observe danh sách

```csharp
[AgentObserveList("guards", "Guards in the level")]
public IEnumerable<GuardView> Guards => ...;   // GuardView has [AgentObserve("distance")], [AgentObserve("sees_player")] ...
```

- Snapshot có `"guards": [ { "distance": 6.2, "sees_player": false }, ... ]`, tối đa 16 phần tử (cắt bớt và cảnh báo một lần).
- `hello` liệt kê `guards` với kiểu `list` và các trường con.
- Trong điều kiện, trường con chỉ dùng được qua `min`, `max`, `count`, `any`.

---

## 7. Theo lượt trong M2

Editor dừng và gửi `paused` khi: Plan xong (`plan_done`), Plan hỏng (`plan_failed`, gồm `replan` và `timeout`), có sự kiện `important` mà không luật nào của Plan bắn trong frame đó, hoặc hết ngân sách `resume`. Gửi Plan mới thì runner gửi `plan` rồi `resume`. Thứ tự message `plan_status` luôn đi trước `paused` của cùng một lần dừng.

---

## 8. Runner gọi Opus

- **SDK:** package NuGet `Anthropic` (SDK C# chính thức, bản 10 trở lên). Bước đầu tiên của plan kiểm tra SDK hỗ trợ `cache_control` và tool use với `tool_choice` bắt buộc. Nếu thiếu, `ModelClient` gọi HTTP trực tiếp với cùng interface.
- **Đầu ra có cấu trúc:** model phải gọi tool `submit_plan`, input schema là JSON schema của Plan. Không parse văn bản tự do.
- **Prompt:**
  - Phần cố định, đánh dấu cache: hướng dẫn chung về DSL và cách chơi theo lượt; `brief`; danh sách observe, action (tham số, mô tả, action an toàn), sự kiện đã khai báo.
  - Phần mỗi lượt: snapshot hiện tại; tối đa 20 sự kiện kể từ Plan trước; Plan trước và kết quả (`plan_done`, lý do hỏng, luật đã bắn); tóm tắt 5 Plan gần nhất (intent và kết quả).
- **Kiểm Plan:** `PlanValidator` trả danh sách lỗi; có lỗi thì gửi lại cho model một lần kèm lỗi; vẫn sai thì gửi `stop_plan`, ghi `RunLog` và thử lại ở lượt sau.
- **Lỗi API:** quá 60 giây hoặc lỗi mạng thì thử lại một lần sau 2 giây; vẫn lỗi thì để game ở hành vi an toàn, chờ 10 giây rồi thử tiếp. Ở chế độ theo lượt, game đang dừng nên không có hậu quả gameplay.
- **Giới hạn chi phí:** `--max-calls` (mặc định 40) mỗi phiên; đạt giới hạn thì runner dừng gọi model, in tổng token và thoát.
- **Cấu hình:** `runner.json` cạnh file chạy chứa model id (mặc định `claude-opus-5-5`), `max_tokens`, giới hạn. Khóa API đọc từ biến môi trường `ANTHROPIC_API_KEY`, không ghi vào file.
- **Chế độ bộ não:** `--brain opus` (mặc định), `--brain script <file>` (đọc chuỗi Plan định sẵn, dùng cho E2E không tốn tiền), `--brain manual` (in trạng thái, chờ dán Plan JSON; để debug).
- **RunLog:** `<project_root>/AgentPlay/Runs/<thời gian>.ndjson`, mỗi dòng một lượt: thời điểm, model, độ trễ, token vào, ra, đọc từ cache, Plan, kết quả. Cuối phiên ghi tổng.

---

## 9. Tay cầm ảo

- `VirtualGamepad` thêm một thiết bị `Gamepad` ảo vào Input System khi adapter yêu cầu và gỡ khi phiên kết thúc.
- Adapter đặt giá trị mỗi frame: `leftStick`, `rightStick`, các nút. Giá trị không được đặt trong frame thì về 0, để action ngừng được gọi là nhân vật dừng.
- Nằm trong asmdef riêng `AgentPlay.InputSystem` với `versionDefines` theo `com.unity.inputsystem`, nên package vẫn dùng được cho project không có Input System.

---

## 10. Game demo trong Testbed: "trộm nhỏ"

Toàn bộ code demo nằm trong `Testbed/Assets/Demo/`, không nằm trong package.

- **Bản đồ:** sàn 20 × 12 m, 3 món đồ (`item_a`, `item_b`, `item_c`), 2 vật che (`cover_1`, `cover_2`), cửa thoát `exit`, điểm xuất phát `start`. Các vùng có tên được nối bằng một đồ thị điểm đi cố định; `goto` đi theo đường ngắn nhất trên đồ thị.
- **Bảo vệ:** đi vòng qua 4 điểm tuần tra, tầm nhìn hình nón 70° và 7 m, bị vật che chặn (raycast). Nhân vật đang `hide` sau vật che thì không bị nhìn thấy từ phía bên kia.
- **Luật:** bị nhìn thấy liên tục 1,5 giây thì bảo vệ đuổi; bảo vệ tới cách 1 m thì bị bắt (`game.lost`). Vào `exit` khi đã cầm đủ 3 món thì thắng (`game.won`).
- **Observe:** `player.pos`, `player.zone`, `player.carrying`, `player.hidden`; danh sách `guards` với `distance`, `sees_player`, `chasing`, `zone`.
- **Action:** `goto(zone)`, `pick(item)` (phải đứng trong 1,5 m), `hide(zone)`, `wait(seconds)`, `stop()` (an toàn).
- **Sự kiện đã khai báo:** `item.picked`, `guard.spotted` (quan trọng, xấu), `guard.lost_sight`, `game.won` (tốt), `game.lost` (xấu).
- **`AgentPlay/brief.md`** của Testbed mô tả mục tiêu và luật trên.
- **Kịch bản cho model giả:** `Testbed/AgentPlay/scripts/win.json`, chuỗi Plan đủ để thắng khi bảo vệ chạy theo lộ trình xác định. Bảo vệ không có yếu tố ngẫu nhiên.

---

## 11. Adapter TileThieves

Code adapter nằm trong repo TileThieves, thư mục `Assets/_Project/Scripts/AgentAdapter/`, asmdef `TileThieves.AgentAdapter`:

- `versionDefines`: `com.agentplay.core` → `TT_AGENTPLAY`; `defineConstraints`: `TT_AGENTPLAY`. Không có package thì asmdef không biên dịch, máy đồng đội và CI không bị ảnh hưởng.
- Không sửa file game nào. Adapter đọc qua API public đã có: `NoiseSystem.Instance` (`DisplayValue`, `DisplayLevel`), `PryTool` (`AimedCell`, `AimInReach`, `IsPrying`, `TilesPried`), `PlayerSack.Inventory`, `PaintingRuntime` (`TileRemoved`, `CellCenterWorld`, `WorldToCell`), `PlayerController`.
- Sự kiện phát từ adapter: đăng ký `PaintingRuntime.TileRemoved` (`tile.pried`), theo dõi đổi mức ồn (`noise.level_up`, quan trọng khi lên mức cao), số gạch trong bao (`tile.collected`), đạt mục tiêu (`game.won`), ồn lên mức cao nhất (`game.lost`).
- Action lái nhân vật bằng `VirtualGamepad`: `goto_painting()`, `aim_next_tile()`, `pry(count)`, `collect_loose()`, `crouch(on)`, `stop()` (an toàn). Cách nhắm dùng vòng điều khiển kín: xoay `rightStick` tới khi `PryTool.AimedCell` trùng ô mục tiêu.
- `TileThieves/AgentPlay/brief.md` mô tả mục tiêu N viên và giới hạn ồn.
- Danh sách thành viên chính xác của các lớp trên sẽ được kiểm lại trong bước đầu của plan TileThieves, vì code TileThieves có thể đã đổi.

---

## 12. Trace

- Header vẫn ghi `who = human`. Khi runner gửi Plan đầu tiên, trace ghi dòng `{"type":"control","who":"agent"}`.
- Mỗi Plan nhận được ghi dòng `{"type":"plan", ...}`; mỗi `plan_status` ghi dòng `{"type":"plan_status", ...}`.

---

## 13. Xử lý lỗi bổ sung

| Tình huống | Xử lý |
| --- | --- |
| Action ném exception | Bắt lại, coi là `Failed`, phát `agent.action_error` kèm thông điệp, Plan hỏng với lý do `action_error:<tên>` |
| Plan tham chiếu action, observe, sự kiện không có | Unity cũng chạy `PlanValidator`; sai thì trả `plan_status plan_failed` với lý do `invalid:<chi tiết>`, không chạy |
| Tham số action sai kiểu khi chạy | Như action ném exception |
| Runner mất kết nối giữa Plan | Executor chạy nốt Plan, rồi hành vi an toàn; theo lượt thì dừng ở lần dừng kế tiếp |
| Không có action `Safe` | Không có hành vi an toàn; `waiting` vẫn được gửi; registry cảnh báo một lần |
| Thiếu `brief.md` | `hello.brief` rỗng, runner in cảnh báo, vẫn chạy |
| Thiếu `ANTHROPIC_API_KEY` với `--brain opus` | Runner thoát ngay với thông báo rõ ràng |

---

## 14. Kiểm thử

- **Contract (xUnit):** parser và evaluator điều kiện (ưu tiên toán tử, hàm danh sách, null, sai kiểu), `PlanValidator` (mọi loại lỗi), schema Plan khớp kiểu C#.
- **Runtime (EditMode):** `ExecutorCore` với action giả: bước, `until`, luật sườn lên, bốn kiểu `then`, timeout, action ném lỗi, hành vi an toàn; `ActionInvoker`; `[AgentObserveList]`; `[AgentEventInfo]`; `SessionCore` với `plan` và các lần dừng mục 7.
- **Runner (xUnit):** `SessionLoop` với `ScriptedModelClient` và `IModelClient` giả trả Plan sai, lỗi mạng, hết giới hạn lượt; `PromptBuilder` (phần cache không đổi giữa các lượt).
- **E2E tự động:** Testbed với `--brain script` (tiêu chí 1), kiểm bằng script Python như M1.
- **E2E có tốn tiền, chạy tay:** Testbed với Opus (tiêu chí 2), TileThieves với Opus (tiêu chí 3). Kết quả ghi vào `docs/notes/m2-verification.md` kèm tổng token và chi phí ước tính.
- **Tổng quát:** `Tools/check-generic.sh` (tiêu chí 4).

---

## 15. Thứ tự triển khai

M2 chia làm ba plan, mỗi plan tự kiểm được:

| Plan | Nội dung | Xong khi |
| --- | --- | --- |
| **M2a: Động cơ** | Điều kiện, Plan, validator, executor, observe danh sách, event info, giao thức v2, `brief`, game demo Testbed, `--brain script`, `check-generic.sh`, `link-agentplay.ps1` | Tiêu chí 1 và 4 |
| **M2b: Bộ não Opus** | Kiểm tra SDK, `ModelClient`, `PromptBuilder`, `SessionLoop`, giới hạn chi phí, `RunLog` | Tiêu chí 2 và 5 |
| **M2c: TileThieves** | `VirtualGamepad`, adapter TileThieves, `brief.md`, junction | Tiêu chí 3 |

---

## 16. Rủi ro và điều chưa kiểm chứng

1. SDK `Anthropic` có hỗ trợ `cache_control` và `tool_choice` bắt buộc không: kiểm ở bước đầu M2b; dự phòng là HTTP trực tiếp.
2. Opus có viết được Plan nhiều bước đủ tốt để thắng 3/5 ván Testbed không: chỉ biết sau M2b. Nếu không đạt, giảm độ dài Plan và thêm ví dụ Plan mẫu vào phần cache.
3. Junction trong `Packages/` được Unity nhận là embedded package và `versionDefines` hoạt động với nó: kiểm ở đầu M2c.
4. Tay cầm ảo được `ThiefInput` của TileThieves nhận khi Editor không có focus: kiểm ở đầu M2c.
5. Nhắm ô gạch bằng vòng điều khiển kín có hội tụ không, với độ nhạy tay cầm 180°/giây của TileThieves: kiểm ở M2c; dự phòng là giảm biên độ stick khi gần mục tiêu.
6. Chi phí Opus cho tiêu chí 2 và 3: ước tính trước khi chạy, dừng nếu vượt giới hạn `--max-calls`.
