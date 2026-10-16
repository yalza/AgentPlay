# Hướng dẫn sử dụng AgentPlay

Tài liệu này dành cho người muốn cho một agent Claude tự chơi game Unity của mình: dùng để làm gì, cài thế nào, dùng ra sao, và bên trong hoạt động thế nào. Bản tóm tắt tiếng Anh nằm ở [README](../README.md).

## Mục lục

1. [AgentPlay là gì, dùng để làm gì](#1-agentplay-là-gì-dùng-để-làm-gì)
2. [Yêu cầu](#2-yêu-cầu)
3. [Cài đặt](#3-cài-đặt)
4. [Cách 1: để Claude Code tự viết adapter (skill `agentplay-adapt`)](#4-cách-1-để-claude-code-tự-viết-adapter)
5. [Cách 2: tự viết adapter](#5-cách-2-tự-viết-adapter)
6. [Cho agent chơi](#6-cho-agent-chơi)
7. [Cho agent học](#7-cho-agent-học)
8. [Cơ chế hoạt động](#8-cơ-chế-hoạt-động)
9. [Tham chiếu lệnh, biến môi trường, mã thoát](#9-tham-chiếu)
10. [Xử lý sự cố](#10-xử-lý-sự-cố)
11. [Giới hạn](#11-giới-hạn)

---

## 1. AgentPlay là gì, dùng để làm gì

AgentPlay gồm hai phần:

- **Package Unity** `com.agentplay.core`: chạy trong Editor, cho agent "nhìn" trạng thái game và "bấm" các hành động mà game cho phép, ghi lại mọi phiên chơi.
- **Runner** (.NET 9): chương trình console nói chuyện với game qua TCP, hỏi Claude nên làm gì tiếp, gửi kế hoạch cho game chạy, và rút kinh nghiệm sau mỗi phiên.

Kèm theo là skill **`agentplay-adapt`** cho Claude Code: tự đọc code game của bạn và viết phần nối (adapter) giữa game và AgentPlay.

Dùng để:

- **Playtest tự động:** đặt mục tiêu ("chơi hết 3 nhiệm vụ đầu từ save mới", "lấy đủ 3 món đồ rồi thoát"), agent tự chơi và báo kết quả: tới được hay không, mất bao lâu, kẹt ở đâu.
- **Kiểm hồi quy gameplay:** sau mỗi lần sửa game, chạy lại cùng mục tiêu và so số ván thắng, số lượt gọi model, thời gian.
- **Học từ người chơi:** phiên bạn tự chơi (gắn nhãn `play`) trở thành bài học trong playbook. Bài học đúng nhiều lần thành **phản xạ**: phản ứng tức thì chạy ngay trong game, không cần gọi model.
- **Cầu nối cho công cụ khác:** giao thức TCP (JSON từng dòng) được mô tả ở mục 8.6; công cụ nào nói được giao thức này đều quan sát và điều khiển được game.

Không dùng được cho: bản build đã phát hành (chỉ chạy trong Editor), game cần phản xạ tính bằng mili giây (một lượt gọi model mất 3–15 giây), game không phải Unity hoặc Unity cũ hơn 6000.0.

## 2. Yêu cầu

| Thành phần | Phiên bản |
| --- | --- |
| Hệ điều hành | Windows 10/11 (đã kiểm) |
| Unity | 6000.0 trở lên; project mở được trong Editor |
| .NET SDK | 9 |
| Claude Code CLI | đã đăng nhập (`claude -p` chạy được). Hoặc đặt `ANTHROPIC_API_KEY` và dùng `--backend api` |
| Git Bash, Python 3 | cho các script trong `Tools/` |

## 3. Cài đặt

### 3.1 Lấy mã nguồn và build runner

```powershell
git clone https://github.com/yalza/AgentPlay.git E:\AgentPlay
cd E:\AgentPlay
dotnet build Runner/AgentPlay.Runner.csproj
setx AGENTPLAY_HOME E:\AgentPlay
```

Kiểm nhanh: `dotnet test AgentPlay.sln` phải báo `Passed!`.

### 3.2 Gắn package vào game

Chọn một trong ba cách:

**a. Chỉ trên máy (khuyên dùng cho repo của nhóm)**

```powershell
powershell -File E:\AgentPlay\Tools\link-agentplay.ps1 -ProjectPath D:\MyGame -LocalOnly
```

- Tạo junction `D:\MyGame\Packages\com.agentplay.core` trỏ tới package, nên không phải sửa `manifest.json`.
- Loại các thứ AgentPlay thêm vào khỏi git qua `.git/info/exclude`: junction, thư mục `AgentPlay/`, `Assets/AgentPlayAdapter/`, và `Assets/csc.rsp` do AgentPlay tạo. Vì vậy `git status` của game không đổi, đồng đội không thấy gì.
- Gỡ: `link-agentplay.ps1 -ProjectPath D:\MyGame -Unlink`. Lệnh này xóa junction, adapter, `csc.rsp` của AgentPlay, khối loại trừ, và hoàn tác `Packages/packages-lock.json`. Thư mục `AgentPlay/` (trace, playbook) được giữ lại; thêm `-Purge` để xóa luôn.

**b. Project của riêng bạn:** cùng lệnh nhưng bỏ `-LocalOnly`; junction được thêm vào `.gitignore`.

**c. Khai báo trong manifest:** thêm `"com.agentplay.core": "file:E:/AgentPlay/Packages/com.agentplay.core"` vào `Packages/manifest.json`.

Package cần `com.unity.nuget.newtonsoft-json` 3.2.1 (Unity tự cài). Phần tay cầm ảo chỉ biên dịch khi game có Input System.

### 3.3 Cài skill cho Claude Code

```powershell
New-Item -ItemType Junction -Path "$env:USERPROFILE\.claude\skills\agentplay-adapt" -Target "E:\AgentPlay\.claude\skills\agentplay-adapt"
```

Mở lại Claude Code để nó nhận skill.

## 4. Cách 1: để Claude Code tự viết adapter

Mở Claude Code ngay trong thư mục project game và nói mục tiêu, ví dụ:

> Dùng agentplay-adapt để agent chơi hết 3 nhiệm vụ đầu từ save mới.

Skill đi qua 5 bước:

1. **Khảo sát (chỉ đọc code):** tìm nhân vật và cách input đi tới nó (Input System, joystick UI tự viết, bàn phím), các manager giữ trạng thái liên quan tới mục tiêu, cách game lưu tiến trình, code nằm trong asmdef hay `Assembly-CSharp`, scene khởi đầu, và những thứ có thể chặn khi chơi trong Editor (popup đồng ý, quảng cáo, đăng nhập).
2. **Đề xuất, rồi dừng chờ bạn duyệt:** bảng observe (agent thấy gì), action (agent làm được gì, đi qua lối input nào), event (sự kiện nào quan trọng, tốt hay xấu), điều kiện thắng/thua, cách bắt đầu và kết thúc một lượt chạy. Bạn sửa hoặc đồng ý.
3. **Gắn vào project:**
   - liên kết package ở chế độ chỉ trên máy;
   - viết adapter trong `Assets/AgentPlayAdapter/`;
   - viết `AgentPlay/brief.md` (mô tả game cho model);
   - viết một plan kịch bản để kiểm mà không cần model;
   - viết hook save: trước mỗi lượt chạy, save của bạn được **đổi tên cất sang bên cạnh** (không chép, không xóa); lượt chạy bắt đầu từ save mới; sau lượt chạy, save của lượt chạy được giữ ở `<thư mục save>.agentplay-last-run` và save của bạn được trả lại đúng chỗ. Nếu Unity bị tắt ngang, lần chạy kế tiếp trả save của bạn về trước khi làm gì khác.
4. **Tự kiểm theo bậc** (hỏng bậc nào thì sửa adapter rồi chạy lại từ bậc đó):
   1. biên dịch sạch;
   2. `AgentPlay.Runner check` đạt (mục 6.3);
   3. plan kịch bản chạy hết, chỉ số tiến độ có thay đổi;
   4. Opus chơi tới mục tiêu.
5. **Báo cáo:** các file đã thêm (đều nằm ngoài git), cách gỡ, kết quả từng bậc kèm đường dẫn log, những gì adapter chưa phủ.

Những điều skill luôn giữ:

- Không sửa file nào git đang theo dõi, không sửa code game.
- Không bao giờ xóa save của bạn.
- Action đi qua đúng lối input của người chơi (đặt giá trị joystick, nút tay cầm ảo, hàm mà nút UI gọi). Không dịch chuyển tức thời, không sửa thẳng tiền hay tiến độ.
- Không nới tiêu chí kiểm để cho qua.

Hai dạng adapter skill có thể viết:

| | Game dùng asmdef | Game không có asmdef (`Assembly-CSharp`) |
| --- | --- | --- |
| Vị trí | `Assets/AgentPlayAdapter/` có asmdef riêng | `Assets/AgentPlayAdapter/`, không asmdef |
| Chỉ biên dịch khi có package | `versionDefines` trên `com.agentplay.core` | mọi file bọc `#if AGENTPLAY`, symbol đặt trong `Assets/csc.rsp` (chỉ trên máy) |

## 5. Cách 2: tự viết adapter

```csharp
using AgentPlay;
using UnityEngine;

[AgentGame("My Game", Version = "1")]
[AgentEventInfo("game.won", "Tới lối ra", Important = true, Outcome = AgentOutcome.Good)]
[AgentEventInfo("enemy.spotted", "Bị kẻ địch nhìn thấy", Important = true, Outcome = AgentOutcome.Bad)]
public class PlayerAgent : MonoBehaviour
{
    [AgentObserve("player.pos")] public Vector3 Pos => transform.position;
    [AgentObserve("player.hp", "Máu hiện tại, 0-100")] public int Hp => _hp;

    [AgentAction("goto", "Đi tới một vùng: start, exit, shop")]
    public AgentActionStatus GoTo(AgentActionContext ctx, string zone)
    {
        if (GameOver) return AgentActionStatus.Done;
        if (ctx.Elapsed > 20) return AgentActionStatus.Failed;            // không bao giờ chạy mãi
        return MoveTowards(zone) ? AgentActionStatus.Done : AgentActionStatus.Running;
    }

    [AgentAction("stop", "Đứng yên", Safe = true)]
    public AgentActionStatus Stop() => AgentActionStatus.Done;

    void OnReachedExit() => AgentEvents.Emit("game.won", new { }, important: true, outcome: AgentOutcome.Good);
}
```

Quy tắc chính:

- `[AgentObserve("đường.dẫn", "mô tả")]` trên property/field: kiểu `float`, `int`, `bool`, `string`, enum, `Vector3`. Đọc mỗi lần chụp trạng thái nên phải rẻ, không cấp phát.
- `[AgentObserveList("guards")]` trên `IEnumerable<T>`: mỗi phần tử có các field `[AgentObserve]` của `T`.
- `[AgentAction]` trên hàm trả `AgentActionStatus` (`Running`, `Done`, `Failed`), được gọi **mỗi frame** cho tới khi xong. Tham số đầu tùy chọn `AgentActionContext` (`IsFirstFrame`, `Elapsed`), sau đó chỉ `float/int/bool/string`. Đúng một action có `Safe = true`, không tham số.
- `[AgentEventInfo]` khai báo sự kiện; `AgentEvents.Emit(...)` phát sự kiện. `Important = true` làm agent phải quyết định lại; `Outcome` dùng để chấm điểm phản xạ. `game.won` và `game.lost` kết thúc ván.
- Viết `AgentPlay/brief.md`: mục tiêu, cách thắng, cách thua, những gì observe không thể hiện.

Tài liệu API đầy đủ kèm ví dụ thật: [`.claude/skills/agentplay-adapt/reference/attributes.md`](../.claude/skills/agentplay-adapt/reference/attributes.md). Những lỗi hay gặp: [`pitfalls.md`](../.claude/skills/agentplay-adapt/reference/pitfalls.md).

## 6. Cho agent chơi

### 6.1 Chơi tương tác

Chạy runner, rồi bấm Play trong Editor:

```bash
dotnet Runner/bin/Debug/net9.0/AgentPlay.Runner.dll --brain opus                    # theo lượt (mặc định)
dotnet Runner/bin/Debug/net9.0/AgentPlay.Runner.dll --brain opus --mode realtime    # thời gian thực
```

Runner in ra `hello` (game khai báo gì), từng sự kiện, từng Plan được gửi, trạng thái từng bước, và kết quả cuối. Trong lúc chạy, gõ `s` để xem trạng thái, `r` để chạy tiếp (chế độ theo lượt), `q` để thoát.

### 6.2 Chạy không người trông (nhiều ván liền)

```bash
bash Tools/run-adapter-batch.sh D:/MyGame 3              # 3 ván với Opus
bash Tools/run-adapter-batch.sh D:/MyGame 1 plan.json    # 1 ván theo plan kịch bản, không gọi model
```

Script mở Unity, vào Play mode, cho agent chơi, rồi thoát với mã 0 (thắng), 3 (thua), 1 (hết giờ) hoặc 2 (hook lỗi). Mỗi ván in một dòng: mã của runner và Unity, số lượt gọi model, và giá trị của observe tiến độ nếu đặt `AGENTPLAY_PROGRESS_OBSERVE`. Log nằm ở `.e2e/adapter-batch/<tên project>/`.

Lưu ý: Unity không mở được một project đã mở sẵn trong Editor khác. Đóng Editor của game trước khi chạy batch.

### 6.3 Kiểm adapter mà không gọi model

```bash
dotnet Runner/bin/Debug/net9.0/AgentPlay.Runner.dll check --idle-exit 120 &
"$UNITY_EXE" -projectPath D:/MyGame -executeMethod AgentPlay.Editor.AutoPlay.Run
```

`check` in mỗi mục một dòng `PASS`/`FAIL`: có observe, có action, đúng một action an toàn, có khai báo `game.won`, có `brief.md`, kiểu tham số action hợp lệ, nhận đủ hai snapshot, từng observe có giá trị (không null ở cả hai snapshot), Unity không báo lỗi. Thoát 0 nếu tất cả đạt.

### 6.4 Hook trước và sau lượt chạy

Game nào cũng có thể thêm hook Editor để dựng bối cảnh test và dọn sau đó:

```csharp
public sealed class MyHook : AgentPlay.Editor.IAgentAutoPlayHook
{
    public void BeforePlay() { /* cất save, tắt popup, ... ; ném exception để hủy lượt chạy */ }
    public void AfterPlay()  { /* trả lại mọi thứ; luôn được gọi, kể cả khi hết giờ hoặc hook khác lỗi */ }
}
```

`BeforePlay` chạy theo thứ tự tên, `AfterPlay` theo thứ tự ngược lại, sau khi đã thoát Play mode. Giữa hai lần gọi có domain reload, nên lưu trạng thái ra đĩa thay vì vào field.

## 7. Cho agent học

1. Mở **Window > AgentPlay**, chọn *Session tag* là `play`, rồi tự chơi (hoặc cho agent chơi với `AGENTPLAY_SESSION_TAG=play`). Nhãn mặc định là `debug` để không học nhầm từ những lần cố tình phá game.
2. Sau phiên, runner tự chạy Reflector. Bạn cũng có thể bấm *Learn from play sessions*, hoặc chạy `AgentPlay.Runner reflect --project D:/MyGame`.
3. Bấm *Open playbook* để đọc bài học. Mỗi bài có số lần được xác nhận và số lần bị phản bác.
4. Bài học được xác nhận 3 lần và chưa bị phản bác lần nào có thể được đề xuất thành phản xạ. Phản xạ đề xuất hiện trong cửa sổ, với các nút *Approve*, *Reject*, *Disable*, *Re-enable*.
5. Phản xạ đã duyệt chạy ngay trong frame sự kiện xảy ra, không gọi model. Nó tự tắt (`suspended`) nếu sau ít nhất 5 lần kích hoạt mà quá 30% lần dẫn tới kết quả xấu trong 10 giây. Khi version game đổi, phản xạ chuyển sang `needs_review` và chờ bạn duyệt lại.

Playbook được đưa vào prompt của mọi lượt gọi chiến thuật, nên agent chơi tốt dần lên qua các phiên.

## 8. Cơ chế hoạt động

### 8.1 Kiến trúc

```
Unity Editor (Play mode)                          Runner (.NET 9)
  Adapter: observe / action / event                 Vòng lặp phiên
  Registry: quét attribute khi vào Play   <--TCP-->  Bộ não: Opus / Haiku qua claude -p
  Executor: chạy Plan từng frame                     Router: phản ứng nhanh hay chiến thuật
  Phản xạ: chạy trong frame                          Xin trước Plan tiếp theo
  Trace: ghi mọi phiên                               Reflector: trace -> playbook -> phản xạ
  Cửa sổ AgentPlay                                   Telemetry, check, reflect
```

Hai bên dùng chung một mã nguồn **Contract** (kiểu message, Plan, điều kiện), nên luật chạy trong game và luật runner kiểm tra luôn cho cùng kết quả.

### 8.2 Một phiên chơi diễn ra thế nào

1. Bấm Play (hoặc AutoPlay vào Play). Registry quét mọi `[AgentObserve]`, `[AgentAction]`, `[AgentEventInfo]` trên các object trong scene.
2. Bridge mở cổng TCP `127.0.0.1:47800` (bận thì thử tới 47809). Runner kết nối và nhận `hello`.
3. Runner gửi `set_mode`. Game xin quyết định đầu tiên (`paused`/`attention` với lý do `mode`).
4. Runner dựng prompt: brief, danh sách observe/action/event, playbook, trạng thái hiện tại, sự kiện gần đây, lịch sử các Plan. Claude trả về một Plan (JSON theo schema).
5. `PlanValidator` kiểm Plan: tên action có thật, kiểu tham số đúng, observe trong điều kiện có thật, cú pháp điều kiện đúng. Sai thì gửi lỗi cho model và xin lại **một lần**; sai lần nữa thì runner dừng với mã 4.
6. Executor chạy Plan từng frame. Mỗi frame theo thứ tự ưu tiên: phản xạ, rồi luật của Plan, rồi bước hiện tại. Gửi `plan_status` cho mỗi bước xong.
7. Khi Plan xong, hỏng (hết giờ, action báo `Failed`, luật `replan`), hoặc có sự kiện quan trọng mà không luật hay phản xạ nào xử lý, game xin quyết định mới. Quay lại bước 4.
8. Khi có `game.won` hoặc `game.lost`, phiên kết thúc. Runner ghi telemetry, rồi nếu nhãn là `play` thì chạy Reflector trên trace.

### 8.3 Plan DSL

```json
{
  "id": "p3",
  "intent": "Lấy item_b khi lính gác đi về phía nam, rồi nấp",
  "steps": [
    { "do": "wait", "args": { "seconds": 30 }, "until": "!any(guards.moving_north) && max(guards.z) < 1" },
    { "do": "goto", "args": { "zone": "item_b" } },
    { "do": "pick", "args": { "item": "item_b" } },
    { "do": "hide", "args": { "zone": "cover_1" } }
  ],
  "rules": [
    { "when": "event:guard.spotted", "do": "hide", "args": { "zone": "cover_1" }, "then": "replan" }
  ],
  "timeout_s": 60
}
```

- **Bước** (`steps`): làm một action với tham số; `until` là điều kiện dừng sớm.
- **Điều kiện:** so sánh (`< <= > >= == !=`), `&& || !`, ngoặc, hằng số, đường dẫn observe, `event:<tên>` (đúng trong frame sự kiện vừa xảy ra), và hàm trên danh sách như `any(...)`, `max(...)`.
- **Luật** (`rules`): khi điều kiện đúng thì làm action, rồi `then`: `resume` (quay lại bước đang dở), `next` (sang bước kế), `replan` (dừng Plan, hỏi model), `abort` (hủy Plan, chạy hành vi an toàn).
- **`timeout_s`:** Plan chạy quá thì hỏng với lý do `timeout`.

### 8.4 Hai chế độ chạy

- **Theo lượt (mặc định):** khi cần quyết định, Editor tạm dừng game (`EditorApplication.isPaused`). Có Plan mới thì chạy tiếp. Kết quả không phụ thuộc thời gian model suy nghĩ.
- **Thời gian thực** (`--mode realtime`): game không dừng; trong lúc chờ, action an toàn chạy. Để giảm thời gian chờ:
  - **Định tuyến:** sự kiện bất ngờ khi đang có Plan, hoặc Plan hỏng vì luật `replan`, đi sang model nhanh (Haiku, effort thấp, Plan tối đa 3 bước). Còn lại, và khi đã có 2 lượt nhanh liên tiếp trong 30 giây, đi sang model chiến thuật (Opus).
  - **Xin trước:** ngay khi gửi một Plan, runner xin luôn Plan tiếp theo với giả định Plan hiện tại thành công. Có sự kiện bất ngờ thì Plan xin trước bị bỏ.
  - **Phản xạ:** xử lý trong frame, không chờ ai.

### 8.5 Học

- **Trace:** mọi phiên (người hay agent) được ghi vào `AgentPlay/Traces/<thời điểm>-<human|agent>-<nhãn>.ndjson`: header, snapshot định kỳ, sự kiện, Plan, trạng thái Plan, phản xạ.
- **Reflector:** sau phiên `play`, Opus đọc bản tóm tắt trace cùng playbook hiện tại, trả về bài học mới, bài học được xác nhận, bài học bị phản bác. Bài học giữ ID ổn định qua các phiên.
- **Phản xạ:** bài học đủ tin cậy được viết thành luật `when <sự kiện> do <action>`, đặt vào `AgentPlay/Inbox/` để bạn duyệt. Vòng đời: `proposed` → `active` → (`suspended` khi hay thất bại | `needs_review` khi đổi version) hoặc `rejected`.

### 8.6 Giao thức

TCP trên `127.0.0.1`, mỗi dòng là một JSON có `type` và `seq`. Unity là server.

| Chiều | `type` | Nội dung |
| --- | --- | --- |
| Unity → runner | `hello` | Game, version, brief, observe, action, event, chế độ, phản xạ đang bật, thư mục project |
| Unity → runner | `state` | Snapshot mọi observe và thời gian game (trả lời `get_state`) |
| Unity → runner | `event` | Sự kiện game kèm snapshot |
| Unity → runner | `plan_status` | `step_done`, `plan_done`, `plan_failed` (kèm lý do), `rule_fired` |
| Unity → runner | `paused` / `attention` | Cần quyết định (theo lượt / thời gian thực), kèm lý do `mode`, `plan`, `event`, `budget` |
| Unity → runner | `reflex`, `reflex_end` | Phản xạ bắt đầu / kết thúc |
| Unity → runner | `waiting`, `error` | Hết Plan, đang chạy hành vi an toàn / message không xử lý được |
| Runner → Unity | `set_mode` | `turn_based` hoặc `realtime` |
| Runner → Unity | `plan`, `stop_plan` | Thay Plan hiện tại / dừng Plan |
| Runner → Unity | `resume` | Theo lượt: chạy tiếp tối đa `max_game_seconds` |
| Runner → Unity | `get_state` | Xin snapshot ngay |

### 8.7 Telemetry

Mỗi phiên ghi `AgentPlay/Telemetry/<phiên>.json`:
- số lượt gọi model mỗi phút game, tách theo model và vai trò (`fast`, `strategy`, `prefetch`);
- thời gian chờ quyết định (trung vị, p90);
- số Plan đã gửi và số Plan hỏng;
- số sự kiện quan trọng, và bao nhiêu sự kiện được phản xạ xử lý;
- số Plan xin trước đã xin, đã dùng và đã bỏ;
- tỉ lệ thời gian game chạy bằng hành vi an toàn, bằng Plan và bằng phản xạ;
- số token.

`python -I Tools/telemetry_trend.py <thư mục Telemetry>` in xu hướng số lượt gọi mỗi phút qua các phiên (chỉ tính phiên thắng hoặc thua).

## 9. Tham chiếu

### Lệnh runner

```
AgentPlay.Runner [--brain script <file> | --brain opus [--backend claude-code|api] [--model M] [--effort E] [--max-calls N]] [--smoke]
                 [--mode turn_based|realtime] [--router on|off] [--no-prefetch] [--auto-resume] [--resume-seconds N] [--think-ms N]
                 [--ports A-B] [--idle-exit SECONDS] [--no-reflect] [--reflector-smoke]
AgentPlay.Runner reflect --project <thư mục project game> [--trace <file>]... [--force]
AgentPlay.Runner check [--ports A-B] [--idle-exit SECONDS]
```

Cấu hình mặc định ở `Runner/runner.json`: model chiến thuật, effort, giới hạn 40 lượt gọi mỗi phiên, ngưỡng của Reflector, và router (`fast_model`, `fast_effort`, `fast_max_steps`, `escalate_after`, `escalate_window_s`).

### Biến môi trường

| Biến | Ý nghĩa |
| --- | --- |
| `AGENTPLAY_HOME` | Thư mục repo AgentPlay (skill dùng) |
| `UNITY_EXE` | Đường dẫn Unity.exe cho các script (mặc định `/e/6000.0.77f1/Editor/Unity.exe`) |
| `AGENTPLAY_AUTOPLAY_SCENE` | Scene AutoPlay mở trước khi vào Play |
| `AGENTPLAY_AUTOPLAY_SECONDS` | Hạn chót của AutoPlay (giây thực, mặc định 240; batch mặc định 900) |
| `AGENTPLAY_SESSION_TAG` | `play` (học từ phiên) hoặc `debug` |
| `AGENTPLAY_PROGRESS_OBSERVE` | Observe in ra sau mỗi ván của batch |

### Mã thoát

| Chương trình | Mã |
| --- | --- |
| Runner (chơi) | 0 thắng, 3 thua, 4 bộ não dừng (hết lượt gọi, lỗi model, hai Plan sai liên tiếp), 5 lệch giao thức, 6 phiên chưa kết thúc |
| AutoPlay | 0 thắng, 1 hết giờ, 2 hook lỗi, 3 thua |
| `check` | 0 đạt, 1 có mục hỏng, 6 không có game kết nối |
| `reflect` | 0 xong, 2 dữ liệu vào hỏng (playbook hoặc ledger), 4 lỗi model, 5 đang có lần `reflect` khác chạy |

### File trong project game

| Đường dẫn | Nội dung |
| --- | --- |
| `AgentPlay/brief.md` | Mô tả game cho model (bạn viết) |
| `AgentPlay/Traces/` | Trace từng phiên |
| `AgentPlay/playbook.json`, `playbook.md` | Bài học |
| `AgentPlay/Inbox/`, `reflexes.json` | Phản xạ đề xuất và vòng đời |
| `AgentPlay/Telemetry/` | Số liệu từng phiên |
| `AgentPlay/Runs/` | Nhật ký lượt gọi model |
| `Assets/AgentPlayAdapter/` | Adapter do skill viết (chỉ trên máy) |

## 10. Xử lý sự cố

| Triệu chứng | Nguyên nhân thường gặp | Cách xử lý |
| --- | --- | --- |
| Runner báo `no Unity connection` | Game chưa vào Play mode, hoặc cổng 47800–47809 bị chiếm | Bấm Play; tắt runner/Unity cũ còn sót; `--ports` |
| `connected but no hello` | Một runner khác đang giữ kết nối | Tắt runner cũ |
| `model call failed ... (429)` | Hết quota Claude (giới hạn theo tuần) | Chờ quota phục hồi; thử trước bằng `echo OK \| claude -p --model claude-haiku-5-5` |
| Batch báo Unity thoát ngay | Project đang mở trong Editor khác | Đóng Editor của project |
| `check` báo `FAIL observe ...` | Observe trả null ở cả hai snapshot | Sửa nguồn của observe (object chưa tìm thấy, chưa khởi tạo) |
| Agent đi vòng vòng, hết giờ | Action không bao giờ kết thúc, hoặc mô tả action không khớp việc nó làm | Thêm giới hạn thời gian/khoảng cách cho action; sửa mô tả; bổ sung brief |
| Unity chậm, treo, log báo hết chỗ | Ổ C: đầy (Temp, cache Unity) | Giải phóng ổ C: trước khi chạy batch dài |
| `dotnet test` lỗi MSB3027 | Runner cũ còn chạy, giữ file dll | Tắt tiến trình `AgentPlay.Runner.dll` |

## 11. Giới hạn

- Chỉ Unity 6000.0 trở lên, chỉ trong Editor, mới kiểm trên Windows.
- Một lượt gọi model mất khoảng 3–15 giây; game cần phản xạ nhanh chỉ chơi được nhờ phản xạ đã học, hoặc nằm ngoài phạm vi.
- Mỗi ván chơi bằng model tốn quota Claude.
- Chơi mạng với người thật không có bảo đảm về độ trễ.
- Chơi bằng ảnh chụp màn hình khi chưa có adapter (mức 0) chưa được làm.
- Skill cần bạn duyệt bảng đề xuất một lần cho mỗi game, và game lớn có thể cần vài vòng sửa adapter.
