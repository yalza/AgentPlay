# M3: kết quả kiểm chứng

Ngày chạy: 2026-10-13 (Windows 11, Unity 6000.0.77f1, .NET SDK 9.0.315, Claude Code CLI đăng nhập bằng gói Claude, model `claude-opus-5-5`)

## Test
- `dotnet test AgentPlay.sln`: 288/288 passed
- `Tools/unity-tests.sh`: 136/136 passed
- `Tools/check-generic.sh`: OK
- `Tools/e2e-m3-reflex.sh` (không gọi model): ALL CHECKS PASSED, ba lần (phản xạ đặt sẵn hai lần, phản xạ học được một lần)
- `Tools/e2e-m3-learn.sh` (gọi model thật): ALL CHECKS PASSED
- Hồi quy: `Tools/e2e-m2a.sh` và `Tools/e2e-m2c.sh script`: ALL CHECKS PASSED
- `AgentPlay.Runner --reflector-smoke`: đầu ra có cấu trúc hợp lệ, 7,9 giây

## Định nghĩa "xong" của spec M3

1. **Học từ người chơi (tiêu chí 3 của spec gốc): đạt.**
   - Ba phiên Heist có nhãn `play` do người chơi giả điều khiển: chỉ dùng hàm điều khiển của người, trace không có dòng `control: agent`. Cả ba phiên đều bị lính gác phát hiện khi đi lấy `item_b`, chạy đi nấp rồi thắng (khoảng 38 giây game).
   - `reflect` xử lý ba trace:
     - Trace 1: 5 bài học mới, 66,8 giây, có một lần lỗi mạng ENOTFOUND được tự thử lại.
     - Trace 2 và trace 3: mỗi trace xác nhận lại cả 5 bài, 23,4 giây và 23,3 giây.
   - Cả 5 bài có nguồn là ba trace đó.
   - Các bài học:
     - L1: lấy `item_a` trước.
     - L2: bị phát hiện thì nấp ngay ở chỗ nấp gần nhất.
     - L3: chỉ lấy `item_b` khi lính gác đang đi về phía nam.
     - L4: lấy `item_c` khi lính gác quay lên phía bắc ở đầu nam.
     - L5: không rời chỗ nấp khi lính gác đang quay đầu ở đầu bắc.
2. **Phản xạ thay cho lượt gọi model (tiêu chí 4): đạt.** E2E không gọi model, dùng phản xạ `active` bắt `event:guard.spotted` bằng `hide_nearest`.
   - Runner nhận tin `reflex` có `handled_events: [guard.spotted]`, và game không dừng lần nào với lý do `event`.
   - Telemetry: `important_events_handled_by_reflex = 1`, `model_calls = 0`, `reflex_fires = {R-fixture: 1}`.
   - `reflexes.json` sau phiên ghi `fired 1, good 1, bad 0`.
3. **Cả chuỗi từ người chơi tới phản xạ: đạt.**
   - Bài L2 đạt 3 lần xác nhận và không có lần trái ngược nào, nên runner thả đề xuất `R-L2` (`when event:guard.spotted do hide_nearest()`) vào Inbox.
   - Móc AutoPlay duyệt đề xuất này. Kịch bản tất định chạy lại với `R-L2` thay cho phản xạ đặt sẵn và đạt mọi kiểm tra.
   - Ván Opus tham khảo, có `R-L2` và playbook trong prompt:

     | Thắng sau | Lượt gọi | Plan | Độ trễ trung vị / p90 | Sự kiện quan trọng | Do phản xạ xử lý |
     | --- | --- | --- | --- | --- | --- |
     | 13,3 s game | 3 | 3 | 8,6 s / 14,5 s | 2 | 1 |

     - Opus đi theo playbook: lấy `item_a` trước, lấy `item_b` khi lính gác đi về phía nam, lấy `item_c` khi lính gác vừa quay lên phía bắc.
     - Lần bị phát hiện ở t=7,08 do `R-L2` xử lý trong 0,76 giây game, không tốn lượt gọi model.
     - So với M2 không có playbook: 3–4 lượt gọi, 15,75–18,44 giây game, xem `m2b-verification.md`.
4. **Vòng đời: đạt.** Test đơn vị trong `ReflexTests` và `ReflexBookTests` phủ các chuyển trạng thái:
   - duyệt, từ chối, tắt, bật lại;
   - tự tắt khi tỉ lệ thất bại 0,40 sau 5 kết quả;
   - chuyển sang `needs_review` khi đổi version.
5. **Không hồi quy: đạt** (xem mục Test).

## Rủi ro trong spec M3 mục 13
1. **Reflector giữ ID ổn định:** đạt ngay lần đầu, không phải chỉnh hướng dẫn. Trace 2 và 3 xác nhận đúng L1–L5 thay vì tạo bài trùng.
2. **`anyOf` có nhánh `null` qua `claude -p --json-schema`:** chạy được, không cần phương án dự phòng.
3. **Trace người chơi không có phím bấm:** đủ cho Heist. Reflector suy ra "người chơi đã nấp" từ `player.hidden` và vị trí.
4. **Kích thước digest:** phiên 38 giây với snapshot 0,5 giây nằm dưới giới hạn 60.000 ký tự. Lượt gọi trace 1 tốn 1944 token ra.
5. **Ngắt action giữa chừng:** `goto` bị `R-L2` ngắt, rồi chạy lại từ đầu sau khi phản xạ xong, không lỗi.

## Phát hiện và điều chỉnh trong lúc làm
- `claude -p` mặc định nạp settings (`language: Vietnamese`) và CLAUDE.md của người dùng, nên intent và bài học ra tiếng Việt. Runner giờ gọi với `--safe-mode --setting-sources project`, nên prompt của game không lẫn cấu hình cá nhân. Lượt gọi cũng nhanh hơn (smoke 17,8 giây xuống 7,9 giây).
- Kết thúc Play mode từng tạo `reflexes.json` rỗng cho game không có phản xạ. Đã sửa, có test.
- Người chơi giả ban đầu không bao giờ bị phát hiện nên trace không có gì để học. Đã sửa để nó đi lấy `item_b` đúng lúc lính gác đang đi tới.
- Một lần `e2e-m2a` bị treo ở bước thoát Unity sau khi thắng (Unity không chạy lệnh `Exit` hẹn trước). Chạy lại thì đạt. Lỗi nằm ở đường thoát của AutoPlay demo, không phải ở AgentPlay.

## Giới hạn đã biết
- Giao diện cửa sổ AgentPlay mới được biên dịch, chưa được người dùng thao tác thật. Logic duyệt nằm trong `ReflexBook` và đã có test.
- Bài học và phản xạ đều theo một game version. Đổi version thì phản xạ chuyển sang `needs_review`; bài học giữ nguyên và chỉ cập nhật version ở lần xác nhận sau.
- Telemetry được ghi vào `AgentPlay/Telemetry/` của game. TileThieves chưa bỏ qua thư mục này trong `.gitignore`.
- Ván Opus tham khảo chỉ chạy một lần, chưa đủ để kết luận số lượt gọi giảm (tiêu chí 5 thuộc M4).
