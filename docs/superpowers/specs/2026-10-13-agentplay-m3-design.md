# AgentPlay M3: Học từ phiên chơi, playbook và phản xạ

**Ngày:** 2026-10-13
**Trạng thái:** Người dùng đã duyệt hướng thiết kế và cho phép đi một mạch từ spec, plan đến thực thi
**Spec gốc:** `docs/superpowers/specs/2026-10-11-agentplay-design.md` (mục 1, 5, 6, 8, 11, 12). M3 kế thừa M1 và M2 đã merge (`docs/notes/m2b-verification.md`, `docs/notes/m2c-verification.md`).

---

## 1. Mục tiêu

Agent rút được kinh nghiệm từ phiên chơi được gắn nhãn `play`, kể cả phiên người dùng tự tay chơi. Kinh nghiệm được ghi vào playbook. Bài học đủ chắc được đề xuất thành phản xạ, và phản xạ đã duyệt chạy trong game mà không cần gọi model.

### Đã chốt với người dùng

- Reflector được kích hoạt bằng nút trong cửa sổ AgentPlay hoặc bằng lệnh `AgentPlay.Runner reflect`. Runner cũng tự chạy Reflector sau một phiên agent có nhãn `play`. Người dùng quyết định lúc nào tốn hạn mức gói.
- Phản xạ là một tầng riêng trong Executor của Unity (cách A). Unity tự đọc phản xạ, tự chạy, tự chấm kết quả và tự tắt phản xạ hỏng. Runner chỉ được báo để ghi telemetry và để đưa phản xạ vào prompt.
- Mỗi file chỉ có một bên ghi: Unity ghi `reflexes.json`; runner ghi playbook và thả đề xuất vào `AgentPlay/Inbox/`.
- Người dùng cho phép đi một mạch từ spec đến thực thi mà không dừng chờ duyệt từng bước.

### Định nghĩa "xong"

1. **Học từ người chơi (tiêu chí 3 của spec gốc):** một phiên Heist có nhãn `play`, do người chơi điều khiển (không có Plan nào), được Reflector thật xử lý. Sau đó `AgentPlay/playbook.json` có ít nhất một bài học mà danh sách nguồn chứa trace của phiên đó. E2E dùng một "người chơi giả" chạy bằng đường điều khiển của người (mục 10.3) vì không có người thật ngồi chơi.
2. **Phản xạ thay cho lượt gọi model (tiêu chí 4):** E2E không gọi model. Một phản xạ `active` bắt sự kiện quan trọng `guard.spotted` trong lúc Plan đang chạy. Game không dừng để hỏi runner, và telemetry ghi sự kiện đó là "do phản xạ xử lý".
3. **Cả chuỗi từ người chơi tới phản xạ:**
   - Sau ba phiên người chơi giả có nhãn `play`, Reflector thật đề xuất ít nhất một phản xạ hợp lệ.
   - Khi phản xạ học được đó được duyệt, nó thay phản xạ đặt sẵn trong kịch bản tất định của tiêu chí 2, và vẫn xử lý được `guard.spotted` mà game không dừng.
   - Thêm một ván Opus có phản xạ đó, ghi telemetry để tham khảo. Ván này không bắt buộc có sự kiện do phản xạ xử lý, vì Opus có thể không bị lính gác thấy lần nào (ở M2 chỉ 1/5 ván bị thấy).
4. **Vòng đời:** test đơn vị phủ các chuyển trạng thái `proposed → active`, `proposed → rejected`, `active → suspended` (thất bại vượt ngưỡng), `active → needs_review` (đổi version game) và `suspended → active` (bật lại).
5. **Không hồi quy:** `dotnet test`, `Tools/unity-tests.sh`, `Tools/check-generic.sh`, `Tools/e2e-m2a.sh` và `Tools/e2e-m2c.sh script` vẫn đạt.

### Ngoài phạm vi M3

- Chế độ thời gian thực, Haiku, xin trước kế hoạch, telemetry đầy đủ cho tiêu chí 5 (M4).
- Ghi lại phím người chơi bấm. Reflector suy ra hành động của người từ trạng thái game và sự kiện.
- Học trên TileThieves. Prototype hiện chỉ có cạy gạch và độ ồn; cơ chế chạy chung, nhưng kiểm chứng làm trên Heist.
- Kiểm tra trực quan giao diện cửa sổ AgentPlay. Các E2E chạy ở batch mode; logic của cửa sổ nằm trong lớp có test, phần vẽ chỉ được biên dịch.

---

## 2. Kiến trúc và quyền ghi file

```
Unity Editor (package)                         Runner (.NET)
├─ ExecutorCore: phản xạ > luật > bước          ├─ OpusBrain: prompt có playbook + phản xạ
├─ ReflexMonitor: chấm kết quả, tự tắt          ├─ Telemetry: ghi AgentPlay/Telemetry/*.json
├─ ReflexBook (Editor): đọc/ghi reflexes.json   └─ Reflector (lệnh reflect):
│    nhập AgentPlay/Inbox/*.json                     trace → TraceDigest → Opus → PlaybookMerger
└─ AgentPlayWindow: duyệt phản xạ, nút học           ghi playbook.json/.md, reflected.json, Inbox/
```

Thư mục `AgentPlay/` ở gốc project game (cạnh `Assets/`, Unity không import):

| File | Bên ghi | Nội dung |
| --- | --- | --- |
| `reflexes.json` | Unity (Editor) | Phản xạ, trạng thái, thống kê, ngưỡng |
| `Inbox/*.json` | Runner tạo, Unity chuyển vào `Inbox/imported/` sau khi nhập | Phản xạ được đề xuất |
| `playbook.json` | Runner | Bài học có cấu trúc (nguồn sự thật) |
| `playbook.md` | Runner | Bản đọc được của `playbook.json`, sinh lại mỗi lần ghi |
| `reflected.json` | Runner | Danh sách trace đã được Reflector xử lý |
| `Telemetry/*.json` | Runner | Một file cho mỗi lần runner chơi |
| `Traces/*.ndjson` | Unity | Như M1/M2, thêm vài loại dòng (mục 5) |

Spec gốc mục 8.3 nói playbook nằm trong `playbook.md`. M3 dùng `playbook.json` làm nguồn sự thật vì cần đếm và đối chiếu ID chính xác; `playbook.md` vẫn có và luôn được sinh lại từ `playbook.json`. Spec gốc mục 8.4 nói ngưỡng nằm trong cấu hình runner; vì Unity mới là bên chấm kết quả, ngưỡng nằm trong mục `policy` của `reflexes.json`.

Mọi file JSON được ghi bằng cách ghi ra file tạm cùng thư mục rồi đổi tên, để một tiến trình đọc giữa chừng không thấy file dở.

---

## 3. Phản xạ

### 3.1 Dữ liệu

```json
{
  "version": 1,
  "policy": { "eval_window_s": 10, "min_fires": 5, "max_fail_rate": 0.3 },
  "reflexes": [
    {
      "id": "R-L2",
      "lesson_id": "L2",
      "text": "When a guard spots you, hide behind the nearest cover",
      "when": "event:guard.spotted",
      "do": "hide_nearest",
      "args": {},
      "cooldown_s": 1,
      "status": "active",
      "game_version": "m3",
      "stats": { "fired": 3, "good": 3, "bad": 0 },
      "note": ""
    }
  ]
}
```

- `status`: `proposed`, `active`, `rejected`, `suspended`, `needs_review`.
- `cooldown_s` trong khoảng [0, 60], mặc định 1: sau khi phản xạ chạy xong, nó không kích hoạt lại trong khoảng này.
- `note` ghi lý do của lần chuyển trạng thái gần nhất (ví dụ "fail rate 0.40 after 5 fires").
- Lớp DTO và lớp kiểm tra nằm trong Contract (dùng chung Unity và .NET).

### 3.2 Kiểm tra hợp lệ (`ReflexValidator`)

Dùng lại kiểm tra điều kiện và action của `PlanValidator`: `when` phải parse được và đúng kiểu theo `ConditionSchema` của game; `do` là action có thật và không phải action `Safe`; `args` đúng tên và kiểu tham số. Thêm: `id` không rỗng, `cooldown_s` trong khoảng cho phép. Tối đa 16 phản xạ `active` được nạp; phản xạ thừa bị bỏ qua kèm cảnh báo trong Console.

### 3.3 Cách chạy trong Executor

- Phản xạ chỉ chạy khi agent đang điều khiển (`ControlTaken`: runner đã gửi ít nhất một Plan). Phiên người chơi không bao giờ bị phản xạ can thiệp.
- Mỗi frame, Executor đánh giá `when` của mọi phản xạ đang nạp, theo cạnh lên như luật của Plan: phản xạ thành "chờ chạy" khi điều kiện chuyển từ sai sang đúng và đã qua cooldown.
- **Thứ tự ưu tiên:** phản xạ, rồi luật của Plan, rồi bước hiện tại (spec gốc mục 6). Nếu có phản xạ chờ chạy và chưa phản xạ nào đang chạy, phản xạ có thứ tự nhỏ nhất trong file được chạy. Nó ngắt action đang chạy, dù đó là bước, action của luật hay action an toàn.
- Trong lúc phản xạ chạy: không đánh giá luật của Plan và `until` của bước; đồng hồ timeout của Plan vẫn chạy; phản xạ khác chờ.
- Action của phản xạ chạy tới khi trả `Done` hoặc `Failed`, ném lỗi, hoặc quá 15 giây game (tính là thất bại). Khi xong, thứ bị ngắt chạy lại action của nó từ đầu (context được reset); chỉ số bước và luật đang chạy giữ nguyên. Phản xạ hỏng không làm hỏng Plan.
- Executor xuất `reflex_fired` (kèm id) và `reflex_ended` (kèm `done` hoặc `failed:<lý do>`).

### 3.4 Chấm kết quả và vòng đời (`ReflexMonitor`, logic thuần trong Contract)

- Mỗi lần phản xạ kích hoạt mở một cửa sổ đánh giá dài `eval_window_s` giây game. Nếu trong cửa sổ có sự kiện game khai báo `outcome: Bad` (kể cả `game.lost`), lần kích hoạt đó tính là `bad`. Hết cửa sổ mà không có sự kiện xấu thì tính là `good`. Phản xạ tự kết thúc bằng `failed` cũng tính là `bad`. Cửa sổ còn mở khi Play mode kết thúc thì bỏ, không tính.
- Sau mỗi kết quả: nếu `fired >= min_fires` và `bad / fired > max_fail_rate`, phản xạ chuyển sang `suspended` và bị gỡ khỏi Executor ngay trong phiên.
- Khi Play mode bắt đầu, phản xạ `active` có `game_version` khác version game hiện tại chuyển sang `needs_review` và không được nạp.
- Duyệt (`proposed` hoặc `needs_review` → `active`) đặt `game_version` bằng version hiện tại. Bật lại (`suspended` → `active`) xóa thống kê. Từ chối đưa về `rejected`. Tắt tay (`active` → `suspended`) ghi `note` là "disabled by user".
- Thống kê và trạng thái được ghi vào `reflexes.json` khi Play mode kết thúc và ngay khi có phản xạ bị tự tắt. Trong Play mode, nút duyệt trên cửa sổ bị khóa, nên Unity không ghi đè thao tác của người dùng.

---

## 4. Giao thức v3

`ProtocolVersion.Current` lên 3. Runner và Unity cùng repo nên đổi cùng lúc; lệch version thì runner dừng như M2.

- `hello` thêm `session_tag` (`play` hoặc `debug`) và `reflexes`: danh sách phản xạ đang nạp (`id`, `text`, `when`, `do`, `args`).
- Unity → runner `reflex`: `{ id, t, frame, handled_events }`. `handled_events` là tên các sự kiện quan trọng có trong frame phản xạ kích hoạt.
- Unity → runner `reflex_end`: `{ id, t, status }`, `status` là `done` hoặc `failed:<lý do>`.
- Ở chế độ theo lượt, frame có phản xạ kích hoạt được xử lý như frame có luật của Plan kích hoạt: sự kiện quan trọng trong frame đó không làm game dừng.

---

## 5. Trace

- Dòng `header` thêm `hello`: toàn bộ hello của phiên (observe, action, sự kiện, brief). Reflector cần nó để hiểu trace và để kiểm phản xạ đề xuất. Trace cũ không có `hello` bị Reflector bỏ qua kèm thông báo.
- Snapshot định kỳ: 0,5 giây khi nhãn là `play`, 2 giây khi là `debug`. Phiên `play` cần độ phân giải cao hơn để suy ra người chơi đã làm gì.
- Dòng mới `reflex` (`t`, `id`, `handled_events`) và `reflex_end` (`t`, `id`, `status`).

---

## 6. Reflector

### 6.1 Lệnh

```
AgentPlay.Runner reflect --project <thư mục project game> [--trace <file>]... [--force]
```

- Không có `--trace`: xử lý các trace nhãn `play` trong `AgentPlay/Traces/` chưa có trong `reflected.json`, cũ trước, tối đa `reflector.max_traces_per_run` (mặc định 5) mỗi lần chạy. `--force` xử lý cả trace đã xử lý (dùng khi debug).
- Trace nhãn `debug` không bao giờ được học, kể cả khi truyền bằng `--trace` (in cảnh báo).
- Mã thoát: 0 thành công (kể cả không có gì để học), 2 tham số sai, 4 lỗi gọi model.
- Sau một lần chơi bằng bộ não `opus` hoặc `script` có `hello.session_tag == "play"`, runner tự chạy cùng lệnh cho project đó, trừ khi có `--no-reflect`.

### 6.2 TraceDigest

Biến trace thành văn bản gọn cho model:

- Phần đầu: tên game, version, người chơi và thời lượng phiên.
- Dòng thời gian, mỗi dòng một mốc:
  - Sự kiện kèm payload.
  - Thay đổi trạng thái giữa hai snapshot liên tiếp: chỉ các khóa đổi giá trị. Số làm tròn 1 chữ số thập phân, vector làm tròn từng thành phần; một thay đổi biến mất sau khi làm tròn thì bỏ.
  - Lúc agent nhận quyền điều khiển, Plan (intent và các bước), kết quả Plan, phản xạ kích hoạt và kết thúc.
- Nếu văn bản dài hơn `reflector.max_digest_chars` (mặc định 60.000 ký tự), giữ mọi sự kiện, Plan và phản xạ, rồi thưa dần các dòng thay đổi trạng thái cho tới khi vừa.

### 6.3 Lượt gọi model

- Cùng backend và model với bộ não Opus (`runner.json`), effort lấy từ `reflector.effort` (mặc định `high`). Mỗi trace một lượt gọi.
- **System:** hướng dẫn cho Reflector, cộng mô tả game giống phần game trong prompt của bộ não (brief, observe, action, sự kiện, ngữ pháp điều kiện).
- **User:** các bài học hiện có trong playbook (ID, nội dung, số lần chứng thực và trái ngược, dạng phản xạ nếu có), rồi TraceDigest.
- **Đầu ra có cấu trúc:**

```json
{
  "summary": "one paragraph",
  "lessons": [
    {
      "id": "L2 or empty for a new lesson",
      "verdict": "new | confirmed | contradicted",
      "text": "what to do in which situation, at most 300 characters",
      "evidence": "times and events in this trace",
      "reflex": null
    }
  ]
}
```

  `reflex` là `null` hoặc một biến thể theo từng action (`when`, `do`, `args` đúng tham số), sinh từ hello giống `PlanSchema`.
- Hướng dẫn yêu cầu: chỉ ghi bài học có bằng chứng trong trace; ưu tiên xác nhận bài học sẵn có bằng ID của nó thay vì tạo bài trùng; chỉ viết `reflex` khi phản ứng đơn giản, an toàn trong mọi tình huống khớp điều kiện, và dùng được ngay bằng một action.
- Đầu ra không hợp lệ (JSON sai, verdict lạ, `confirmed` cho ID không có thì vẫn nhận nhưng xử lý như `new`) được gửi lại một lần kèm lỗi, giống bộ não. Lần hai vẫn hỏng thì trace không được đánh dấu đã xử lý và lệnh thoát mã 4.

### 6.4 Gộp vào playbook (`PlaybookMerger`, logic thuần)

- `new`, hoặc ID không có trong playbook: bài học mới, ID `L<n>` tăng dần, `confirmations = 1`.
- `confirmed`: `confirmations + 1`; `contradicted`: `contradictions + 1`. Một bài học chỉ được tính một lần cho mỗi trace.
- Mỗi bài học giữ `text` mới nhất, `game_version` của trace gần nhất, danh sách `sources` (tên file trace) và `reflex` ứng viên. Ứng viên chỉ được lưu khi qua `ReflexValidator` với hello của trace; ứng viên không hợp lệ bị bỏ kèm dòng log.
- Playbook giữ tối đa 50 bài học; vượt quá thì bỏ bài có điểm `confirmations - contradictions` thấp nhất.
- **Đề xuất phản xạ:** bài học có `confirmations >= reflector.min_confirmations` (mặc định 3), `contradictions == 0`, có ứng viên hợp lệ và chưa từng được đề xuất. Runner ghi `AgentPlay/Inbox/reflex-<lesson>-<thời điểm>.json` (một phản xạ, trạng thái `proposed`, id `R-<lesson>`) và đánh dấu bài học đã đề xuất.
- Ghi `playbook.json`, sinh lại `playbook.md`, thêm trace vào `reflected.json`.

### 6.5 Playbook trong prompt của bộ não

Phần system của bộ não Opus có thêm hai mục, đọc một lần khi nhận hello (prompt vẫn ổn định trong phiên nên vẫn cache được):

- **Playbook:** tối đa 20 bài học, xếp theo `confirmations - contradictions`, mỗi dòng có ID, số lần chứng thực và trái ngược.
- **Phản xạ game tự chạy:** danh sách `hello.reflexes`, kèm câu giải thích rằng sự kiện do phản xạ xử lý không làm game dừng, nên Plan không cần luật trùng với phản xạ.

---

## 7. Cửa sổ AgentPlay

Cửa sổ `Window → AgentPlay` thêm hai phần. Chữ trên giao diện bằng tiếng Anh như phần đã có.

- **Learning:**
  - Đường dẫn runner (`AgentPlay.Runner.dll`), lưu trong EditorPrefs.
  - Nút "Learn from play sessions" chạy `dotnet <runner> reflect --project <gốc project>` ở tiến trình nền. Log ghi vào `AgentPlay/Runs/reflect-<thời điểm>.log`. Cửa sổ hiện trạng thái đang chạy, mã thoát và dòng tóm tắt cuối. Nút bị khóa trong Play mode và khi đang chạy.
  - Số trace `play` chưa học.
  - Nút mở `playbook.md`.
- **Reflexes:** danh sách theo trạng thái. Mỗi dòng có `text`, `when → do(args)`, thống kê và `note`. Nút theo trạng thái:
  - `proposed`, `needs_review`: Approve, Reject.
  - `active`: Disable.
  - `suspended`: Re-enable, Reject.
  - Mọi nút bị khóa trong Play mode.
- Inbox được nhập khi cửa sổ được focus, khi lệnh reflect chạy xong và khi Play mode bắt đầu. Phản xạ có id đã tồn tại thì không nhập lại.
- Logic (đọc, ghi, nhập Inbox, chuyển trạng thái) nằm trong `ReflexBook` có test; cửa sổ chỉ vẽ và gọi.

---

## 8. Telemetry

Sau mỗi lần runner chơi bằng một bộ não, runner ghi `AgentPlay/Telemetry/<yyyyMMdd-HHmmss>.json`:

- `game`, `version`, `session_tag`, `result`, `game_seconds` (thời gian game của sự kiện cuối).
- `model_calls`, `calls_per_game_minute`, `wait_ms` (trung vị và p90), token vào, ra, đọc cache và ghi cache.
- `plans_sent`, `plans_failed`.
- `important_events`: tổng số sự kiện quan trọng; `important_events_handled_by_reflex`: số sự kiện nằm trong `handled_events` của một tin `reflex`; `pauses_by_reason`.
- `reflex_fires`: số lần kích hoạt theo id, và số lần kết thúc `failed`.

Runner in một dòng tóm tắt telemetry khi kết thúc.

---

## 9. Thay đổi trong package và runner (tóm tắt)

| Vùng | Thay đổi |
| --- | --- |
| Contract | `ReflexFile`, `Reflex`, `ReflexPolicy`, `ReflexValidator`, `ReflexMonitor`, `ReflexLifecycle`; tin `reflex`, `reflex_end`; hello thêm `session_tag`, `reflexes`; protocol 3; `PlanValidator` tách phần kiểm action/điều kiện để dùng chung |
| Runtime | `ExecutorCore.SetReflexes`, `RemoveReflex`, tầng phản xạ; `SessionCore` gửi `reflex`/`reflex_end`, ghi trace, không dừng khi phản xạ xử lý; `TraceWriter` header có hello và hai dòng mới |
| Editor | `ReflexBook` (đọc, ghi, Inbox, chuyển trạng thái); controller nạp phản xạ khi Play, kiểm version, ghi thống kê khi kết thúc; snapshot 0,5 giây cho `play`; cửa sổ thêm hai phần |
| Runner | lệnh `reflect`; `TraceDigest`, `ReflectorSchema`, `Reflector`, `PlaybookStore`, `PlaybookMerger`; `PromptBuilder` thêm playbook và phản xạ; `Telemetry`; `--no-reflect`; `runner.json` thêm mục `reflector` |

---

## 10. Thay đổi trong demo Heist

Demo không thuộc phần tổng quát, nên được phép biết về AgentPlay và về chính nó.

### 10.1 Game

- Version lên `m3` (phản xạ của version cũ chuyển sang `needs_review`, kiểm được luôn luật đổi version).
- Action mới `hide_nearest()`: đi tới chỗ nấp gần nhất và cúi xuống. Phản xạ cần một action không phụ thuộc vị trí.

### 10.2 Người chơi điều khiển bằng bàn phím

WASD hoặc phím mũi tên để đi, C để cúi hoặc đứng, E để nhặt món gần nhất trong tầm. Heist dùng Input Manager cũ (`activeInputHandler: 0`), nên dùng `UnityEngine.Input`. Package AgentPlay không dính tới việc này. Bàn phím đi qua các hàm điều khiển của người (`ManualMove`, `ToggleCrouch`, `PickNearest`), không qua action của agent.

### 10.3 Người chơi giả cho E2E

Component `HeistPilot` chỉ được tạo khi biến môi trường `AGENTPLAY_HEIST_PILOT=1`. Nó gọi đúng các hàm điều khiển của người ở mục 10.2, không đụng tới Plan hay action. Chiến lược cố ý "liều" để có tình huống đáng học:
1. Lấy `item_a`.
2. Đi thẳng tới `item_b`.
3. Khi lính gác bắt đầu thấy mình, chạy tới chỗ nấp gần nhất và cúi.
4. Chờ tới khi lính gác không còn thấy mình và đang đi xa ra, rồi đi tiếp.
5. Làm tương tự với `item_c`, rồi ra lối thoát.

Trace của phiên này có người chơi `human`, không có dòng `control: agent`.

### 10.4 Móc cho E2E trong `HeistAutoPlay`

- `AGENTPLAY_SESSION_TAG=play|debug` đặt nhãn phiên trước khi vào Play mode, qua API public `AgentPlaySettings.SessionTag`.
- `AGENTPLAY_APPROVE_PROPOSED=1` nhập Inbox rồi duyệt mọi phản xạ `proposed`, qua API public `ReflexBook`, trước khi vào Play mode.

---

## 11. Xử lý lỗi

| Tình huống | Xử lý |
| --- | --- |
| `reflexes.json` hỏng hoặc sai định dạng | Unity log lỗi, không nạp phản xạ nào, không ghi đè file. Cửa sổ hiện lỗi |
| Phản xạ không hợp lệ với game hiện tại (action bị xóa, observe đổi tên) | Không nạp, log cảnh báo kèm lỗi của `ReflexValidator`; trạng thái giữ nguyên |
| File Inbox hỏng | Chuyển vào `Inbox/rejected/`, log cảnh báo |
| Action của phản xạ ném lỗi | Phản xạ kết thúc `failed`, tính là `bad`, Plan chạy tiếp |
| Reflector: model trả đầu ra hỏng hai lần | Trace không được đánh dấu, lệnh thoát mã 4, lần sau thử lại |
| Reflector: hết hạn mức gói hoặc lỗi đăng nhập | Như bộ não: dừng ngay, thoát mã 4 |
| `playbook.json` hỏng | Lệnh reflect dừng mã 2, không ghi đè; báo người dùng sửa hoặc xóa file |
| Không tìm thấy runner khi bấm nút trong Unity | Cửa sổ hiện lỗi và chỉ chỗ đặt đường dẫn |

---

## 12. Kiểm thử

### 12.1 .NET (`dotnet test`)

- `ReflexValidator`: action không có, action `Safe`, tham số sai, điều kiện sai kiểu, cooldown ngoài khoảng.
- `ReflexMonitor` và `ReflexLifecycle`: good, bad, cửa sổ chồng nhau, tự tắt đúng ngưỡng, version đổi, duyệt, từ chối, bật lại.
- `TraceDigest`: diff snapshot, làm tròn, giữ sự kiện khi cắt, trace không có hello.
- `ReflectorSchema`: biến thể theo action, `reflex` có thể null, `additionalProperties: false` ở mọi nơi.
- `PlaybookMerger`: new, confirmed, contradicted, ID lạ, đếm một lần mỗi trace, ngưỡng đề xuất, không đề xuất lại, giới hạn 50 bài.
- `Reflector` với model giả: đi hết luồng từ trace tới file; đầu ra hỏng thì thử lại một lần; trace `debug` bị bỏ.
- `PromptBuilder`: có playbook và phản xạ, prompt giống hệt nhau giữa các lượt.
- `Telemetry`: đếm sự kiện do phản xạ xử lý, trung vị và p90.
- `RunnerOptions`: `reflect`, `--project`, `--trace`, `--force`, `--no-reflect`.

### 12.2 Unity EditMode (`Tools/unity-tests.sh`)

- `ExecutorCore`: phản xạ ngắt bước rồi bước chạy lại; ngắt action của luật; không chạy khi chưa có Plan; cooldown; phản xạ hỏng không hỏng Plan; luật không được đánh giá trong lúc phản xạ chạy; quá 15 giây thì thất bại.
- `SessionCore`: sự kiện quan trọng trong frame có phản xạ không làm dừng; tin `reflex` có `handled_events`; trace có dòng `reflex`.
- `ReflexBook`: đọc và ghi, nhập Inbox, file Inbox hỏng, đổi version thành `needs_review`, ghi thống kê khi kết thúc phiên.

### 12.3 E2E

- `Tools/e2e-m3-reflex.sh` (không gọi model): đặt sẵn `reflexes.json` có phản xạ `active` bắt `event:guard.spotted` bằng `hide_nearest`. Bộ não kịch bản chơi một Plan không có luật cho `guard.spotted` và cố ý đi vào tầm nhìn lính gác. Kiểm tra:
  - Runner nhận tin `reflex` kèm `handled_events` chứa `guard.spotted`.
  - Không có lần dừng nào với lý do `event` trong frame đó.
  - Telemetry có `important_events_handled_by_reflex >= 1`.
  - `reflexes.json` sau phiên có `fired >= 1`.
  - Ván kết thúc thắng.
- `Tools/e2e-m3-learn.sh` (gọi Opus thật, tính vào hạn mức gói):
  1. Xóa playbook, Inbox và trace cũ của Heist.
  2. Chạy ba phiên người chơi giả có nhãn `play`.
  3. Chạy `reflect`.
  4. Kiểm tiêu chí 1: có bài học mà nguồn chứa trace của người chơi giả.
  5. Kiểm có ít nhất một đề xuất trong Inbox.
  6. Duyệt qua móc của AutoPlay, rồi chạy kịch bản của `e2e-m3-reflex.sh` với phản xạ học được thay cho phản xạ đặt sẵn.
  7. Cho Opus chơi một ván, rồi ghi telemetry vào ghi chú kiểm chứng.
- Hồi quy: `e2e-m2a.sh` và `e2e-m2c.sh script`.

---

## 13. Rủi ro và điều chưa kiểm chứng

1. **Reflector không giữ ID ổn định giữa các trace.** Nếu lần nào model cũng tạo bài mới thay vì xác nhận bài cũ, không bài nào đạt 3 lần chứng thực. Giảm thiểu: đưa bài học sẵn có vào prompt kèm ID và yêu cầu rõ. Kiểm ở E2E learn; nếu thất bại thì chỉnh hướng dẫn (ghi lại thành ruling).
2. **Structured outputs với `anyOf` có nhánh `null` qua `claude -p --json-schema`.** Kiểm bằng một lượt gọi thật ngay ở task đầu tiên của Reflector; dự phòng là trường `has_reflex` kiểu bool cộng `reflex` luôn có mặt.
3. **Trace người chơi không có phím bấm.** Bài học có thể thô. Snapshot 0,5 giây và sự kiện có trạng thái kèm theo là đủ cho tình huống "bị thấy thì nấp" của Heist; game phức tạp hơn có thể cần ghi input ở mốc sau.
4. **Kích thước digest.** Phiên Heist 1–2 phút với snapshot 0,5 giây khoảng vài trăm dòng thay đổi; dưới giới hạn 60.000 ký tự. Chưa đo trên phiên dài.
5. **Ngắt action giữa chừng.** Action của game có thể giữ trạng thái riêng khi bị ngắt (ví dụ đang cúi). Executor chỉ đảm bảo reset context của action; game nào cần dọn dẹp thì tự xử lý ở frame đầu của action (như `goto` của Heist đứng dậy ở frame đầu).
