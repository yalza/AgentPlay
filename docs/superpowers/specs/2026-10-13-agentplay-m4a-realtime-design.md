# AgentPlay M4a: Chơi thời gian thực, định tuyến model và xin trước kế hoạch

**Ngày:** 2026-10-13
**Trạng thái:** Người dùng cho đi một mạch ("tiếp tục đi" sau M3, trước đó "làm một mạch, tôi luôn duyệt")
**Spec gốc:** `docs/superpowers/specs/2026-10-11-agentplay-design.md` (mục 1 tiêu chí 5, mục 7.2, mục 11, mục 12). Kế thừa M1–M3 đã merge.

---

## 1. Mục tiêu

Agent chơi được khi game không dừng lại chờ model. Lượt gọi rẻ và nhanh cho phản ứng nhỏ, lượt gọi mạnh cho chiến thuật, và kế hoạch tiếp theo được xin trước để không phải chờ. Telemetry chứng minh số lượt gọi mỗi phút giảm dần khi agent học qua các phiên (tiêu chí 5 của spec gốc).

M4 của spec gốc được tách làm hai: **M4a** (spec này) là phần "nhanh"; **M4b** là phần "mở rộng" (skill `agentplay-adapt`, game thứ hai của người dùng, InputDriver mức 0) và có spec riêng.

### Số đo đầu vào (đo ngày 2026-10-13 qua `claude -p`)

| Model | Effort | Thời gian thực một lượt gọi tối thiểu | Trong đó gọi API |
| --- | --- | --- | --- |
| Haiku 5.5 | low | 3,2–3,3 s | 1,2–1,4 s |
| Opus 5.5 | low | 4,1 s | 2,1 s |
| Opus 5.5 | high, prompt Heist | 8–29 s (M2), trung vị 16 s | |

Khoảng 2 giây mỗi lượt là thời gian khởi động tiến trình CLI. Giữ tiến trình sống giữa các lượt nằm ngoài phạm vi M4a.

### Định nghĩa "xong"

1. **Thời gian thực, không gọi model:** E2E bộ não kịch bản chơi Heist ở chế độ thời gian thực. Game không dừng lần nào. Mỗi Plan được gửi sau một tin `attention`. Ván thắng.
2. **Thời gian thực với model thật:** trong 5 ván Heist thời gian thực (playbook và phản xạ của M3 được phép có), ít nhất 3 ván thắng, mỗi ván tối đa 40 lượt gọi.
3. **Định tuyến:** telemetry của các ván ở mục 2 cho thấy cả Haiku lẫn Opus đều được dùng, và mọi lượt Haiku đều do sự kiện bất ngờ hoặc luật `replan` gây ra.
4. **Xin trước:** trong các ván ở mục 2, ít nhất một Plan xin trước được dùng. Thời gian chờ của lần dùng đó (từ khi Plan cũ xong tới khi gửi Plan mới) dưới 1 giây.
5. **Tiêu chí 5 của spec gốc:** 5 phiên thời gian thực liên tiếp trên Heist, có nhãn `play`, Reflector chạy sau mỗi phiên, và đề xuất phản xạ được duyệt tự động trước phiên kế tiếp. Số lượt gọi mỗi phút game của phiên 5 thấp hơn phiên 1, và độ dốc hồi quy tuyến tính theo thứ tự phiên là âm.
6. **Không hồi quy:** `dotnet test`, `Tools/unity-tests.sh`, `Tools/check-generic.sh`, `Tools/e2e-m2a.sh`, `Tools/e2e-m3-reflex.sh` và `Tools/e2e-m2c.sh script` vẫn đạt. Chế độ theo lượt vẫn là mặc định.

### Ngoài phạm vi M4a

- Giữ tiến trình `claude -p` sống giữa các lượt gọi.
- Dự đoán trạng thái tương lai bằng mô phỏng. "Trạng thái dự đoán" ở đây là trạng thái hiện tại cộng giả định Plan đang chạy hoàn thành.
- Chạy song song nhiều lượt gọi chiến thuật.
- TileThieves ở chế độ thời gian thực (prototype không có nguy hiểm cần phản ứng nhanh; vẫn chạy được, không kiểm).

---

## 2. Unity: tín hiệu `attention`

Ở chế độ theo lượt, `SessionCore` dừng game và gửi `paused` khi cần model. Ở chế độ thời gian thực, nó gửi `attention` vào đúng những thời điểm đó và không dừng game:

- Plan kết thúc (xong, hỏng, hoặc bị từ chối vì không hợp lệ).
- Sự kiện quan trọng trong một frame không có luật hay phản xạ nào xử lý.
- Ngay sau `set_mode realtime` (lý do `mode`), để runner bắt đầu.

```json
{ "type": "attention", "reason": "plan|event|mode", "t": 12.4, "state": { ... }, "time_split": { "safe": 3.1, "plan": 9.0, "reflex": 0.3 } }
```

- `StepPolicy` giữ nguyên quy tắc quyết định. `SessionCore` chỉ chọn giữa dừng và gửi `attention` theo chế độ.
- `resume` không cần ở chế độ thời gian thực. Nếu có, Unity bỏ qua như hiện nay.
- `time_split` là thời gian game cộng dồn trong phiên, do `ExecutorCore` đo mỗi frame: đang chạy action an toàn (sau khi agent đã nhận quyền điều khiển), đang chạy Plan, hoặc đang chạy phản xạ. `paused` cũng mang `time_split`.
- Protocol lên 4: thêm tin `attention`, thêm `time_split` vào `paused`.

---

## 3. Runner: vòng lặp thời gian thực

- `--mode realtime` dùng được với `--brain`, trước đây bị từ chối. Mặc định vẫn là `turn_based`.
- Lượt gọi model chạy ở tác vụ nền. Vòng đọc tin từ Unity vẫn chạy trong lúc chờ. Chỉ có một lượt gọi chiến thuật tại một thời điểm.
- `attention` mở một yêu cầu quyết định, mang lý do, trạng thái và các sự kiện kể từ Plan trước, giống `paused` ở chế độ theo lượt.
- Có `attention` mới trong lúc đang gọi model:
  - Lượt gọi vẫn chạy tiếp. Khi có kết quả, Plan đó vẫn được gửi (nó tốt hơn đứng yên).
  - Ngay sau đó, runner mở lượt gọi mới cho `attention` mới nhất, nếu `attention` đó đến sau thời điểm bắt đầu lượt gọi cũ.
  - Nhiều `attention` dồn lại chỉ tạo một lượt gọi.
- Ở chế độ thời gian thực, runner không gửi `resume` sau Plan.
- Ván kết thúc khi có `game.won` hoặc `game.lost` như hiện nay. Lượt gọi đang dở bị hủy và kết quả của nó bị bỏ.

---

## 4. Định tuyến model (`ModelRouter`)

`runner.json` thêm mục `router`:

```json
"router": { "fast_model": "claude-haiku-5-5", "fast_effort": "low", "fast_max_steps": 3, "escalate_after": 2, "escalate_window_s": 30 }
```

Quy tắc (spec gốc mục 7.2):

| Tình huống | Model |
| --- | --- |
| `attention`/`paused` lý do `event` khi đang có Plan; hoặc Plan hỏng vì luật `then: replan` | **nhanh** (Haiku, effort `low`, Plan tối đa `fast_max_steps` bước) |
| Lý do `mode` (bắt đầu), Plan xong, Plan hỏng vì lý do khác (timeout, action hỏng, không hợp lệ) | **chiến thuật** (Opus, effort trong `runner.json`) |
| Đã có `escalate_after` lượt nhanh liên tiếp trong `escalate_window_s` giây game | **chiến thuật** |

- Plan của lượt nhanh có nhiều hơn `fast_max_steps` bước thì bị từ chối và xin lại một lần, như Plan không hợp lệ. Prompt nói rõ giới hạn này.
- Định tuyến áp dụng ở cả hai chế độ. Ở chế độ theo lượt nó giảm thời gian chờ thực, dù game đang dừng.
- `--router off` dùng một model cho mọi lượt như M3.
- Mọi lượt gọi được ghi kèm tên model và vai trò (`fast`, `strategy`, `prefetch`) vào run log và telemetry.

---

## 5. Xin trước (`Prefetcher`)

Chỉ dùng ở chế độ thời gian thực.

- **Khi xin:** `plan_status step_done` cho thấy Plan hiện tại vừa vào bước cuối, và không có lượt gọi nào đang chạy.
- **Xin gì:** gọi model chiến thuật với lý do `prefetch`. Prompt có trạng thái hiện tại và câu "Plan `<id>` is on its last step (`<do>`). Plan what to do after it finishes, assuming it succeeds."
- **Khi Plan cũ xong** (`attention` lý do `plan` với `plan_done` của đúng Plan đó):
  - Nếu từ lúc xin tới giờ không có sự kiện quan trọng nào chưa được xử lý, Plan xin trước được dùng: gửi ngay nếu đã có, hoặc chờ lượt gọi đang chạy.
  - Ngược lại, Plan xin trước bị bỏ và runner gọi như bình thường.
- **Hủy:** khi có `attention` lý do `event`, hoặc Plan cũ hỏng, lượt xin trước bị hủy (tác vụ bị hủy, tiến trình CLI bị kill). Telemetry ghi lại lần hủy.
- Plan xin trước vẫn qua `PlanValidator`. Nếu không hợp lệ thì bị bỏ, và runner gọi như bình thường.

---

## 6. Telemetry

Thêm vào file telemetry của M3:

- `mode`: `turn_based` hoặc `realtime`.
- `calls_by_model`: `{ "<model>": { "role": { "fast": n, "strategy": n, "prefetch": n }, "wait_ms": { "median": x, "p90": y } } }`.
- `prefetch`: `{ "requested": n, "used": n, "discarded": n, "used_wait_ms": [ ... ] }`. `used_wait_ms` là thời gian thực từ lúc nhận `attention` lý do `plan` tới lúc gửi Plan xin trước.
- `time_split`: lấy từ `attention`/`paused` cuối cùng, tính bằng giây game và tỉ lệ.
- `decision_wait_ms`: thời gian thực từ lúc nhận `attention`/`paused` tới lúc gửi Plan (trung vị và p90). Ở chế độ thời gian thực, đây là thời gian nhân vật đứng làm hành động an toàn.

`Tools/telemetry_trend.py <thư mục Telemetry> [--last N]` in bảng các phiên (thời điểm, chế độ, kết quả, số lượt gọi, số lượt mỗi phút game, sự kiện do phản xạ xử lý) và độ dốc hồi quy của số lượt mỗi phút. Nó trả mã 0 khi phiên cuối thấp hơn phiên đầu và độ dốc âm.

---

## 7. Kiểm thử

### 7.1 .NET
- Router: bảng định tuyến, leo thang sau 2 lượt nhanh trong 30 giây, cửa sổ trượt theo thời gian game, `--router off`.
- Giới hạn bước của lượt nhanh: Plan dài bị từ chối rồi xin lại.
- Vòng thời gian thực (`RealtimeSession`, logic thuần):
  - `attention` mở yêu cầu.
  - `attention` đến trong lúc đang gọi được gộp và gọi lại sau.
  - Không gửi `resume`.
  - Thắng hoặc thua thì hủy lượt gọi đang dở.
- Prefetcher:
  - Xin khi vào bước cuối.
  - Dùng khi Plan xong mà không có bất ngờ.
  - Bỏ khi có sự kiện quan trọng hoặc Plan hỏng.
  - Chờ lượt xin trước đang chạy.
  - Plan xin trước không hợp lệ thì gọi lại như bình thường.
- `ClaudeCodeModelClient`: hủy giữa chừng thì kill tiến trình.
- Telemetry: các trường mới; `telemetry_trend.py` có test Python nhỏ, chạy bằng `python -I`.

### 7.2 Unity EditMode
- `SessionCore` ở chế độ thời gian thực gửi `attention` thay vì dừng, cho cả ba lý do, kèm trạng thái và `time_split`.
- Ở chế độ theo lượt, hành vi giữ nguyên.
- `ExecutorCore` đo `time_split` đúng cho action an toàn, Plan và phản xạ. Trước khi agent nhận quyền điều khiển thì không đếm.

### 7.3 E2E
- `Tools/e2e-m4a-realtime.sh` (không gọi model): bộ não kịch bản `win.json` ở chế độ thời gian thực. Kiểm:
  - không có `paused` nào sau `set_mode`;
  - có ít nhất 4 tin `attention`;
  - thắng;
  - telemetry có `mode: realtime` và `time_split`.
- `Tools/run-heist-realtime.sh N` (gọi model): N ván thời gian thực với router và xin trước, kiểm mục 2–4 của "xong".
- `Tools/run-heist-learning.sh 5` (gọi model): 5 phiên thời gian thực có nhãn `play`. Giữa các phiên, duyệt tự động đề xuất bằng `HeistAutoPlay.ApproveOnly`. Cuối cùng chạy `telemetry_trend.py` (mục 5 của "xong").

---

## 8. Rủi ro

1. **Lính gác bắt nhân vật trong lúc chờ model.** Hành động an toàn của Heist là đứng yên. Giảm thiểu: phản xạ "bị phát hiện thì nấp" (đã học ở M3), lượt nhanh dùng Haiku, và xin trước. Nếu tỉ lệ thắng dưới 3/5, chỉnh hướng dẫn của runner (ghi lại thành ruling), không chỉnh game.
2. **Tiêu chí 5 phụ thuộc may rủi.** Năm phiên với model thật có thể dao động. Giảm thiểu: định nghĩa "giảm dần" bằng độ dốc âm cộng phiên cuối thấp hơn phiên đầu, không đòi giảm đơn điệu. Nếu không đạt, chạy lại một lần và báo cả hai kết quả.
3. **Plan cũ được gửi muộn.** Plan trả về sau khi tình huống đã đổi có thể sai. Giảm thiểu: Plan nào cũng có luật; Unity luôn kiểm Plan; `attention` mới sẽ gây một lượt gọi mới.
4. **Hủy tiến trình CLI giữa chừng.** `ClaudeCodeModelClient` đã kill khi bị hủy; cần kiểm chắc không để lại tiến trình mồ côi.
