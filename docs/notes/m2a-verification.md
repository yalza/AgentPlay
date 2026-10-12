# M2a: kết quả kiểm chứng

Ngày chạy: 2026-10-12 (Windows 11, Unity 6000.0.77f1, .NET SDK 9.0.315)

## Test
- `dotnet test AgentPlay.sln`: 177/177 passed, 0 failed (sau vòng sửa theo review)
- `Tools/unity-tests.sh`: 108/108 passed, 0 failed (sau vòng sửa theo review)
- `Tools/test-link-agentplay.sh`: tất cả PASS
- `Tools/check-generic.sh`: OK
- `Tools/e2e-m1.sh`: ALL CHECKS PASSED (hồi quy M1, có domain reload trong Play mode)
- `Tools/e2e-m2a.sh`: ALL CHECKS PASSED

## Ván thắng theo kịch bản
- Thời gian game tới khi thắng: 29,09 giây (sự kiện `game.won` trong trace).
- Số Plan đã gửi: 4; số lần dừng theo lượt: 5 (một lần khi bật chế độ theo lượt, bốn lần khi từng Plan kết thúc).
- Điều chỉnh trong lúc chạy E2E: action của game demo trả `Done` thay vì `Failed` sau khi ván đã kết thúc. Lý do: `HeistPlayer.Update` báo thắng ngay khi người chơi vào vùng exit (bán kính 1 m), nên `goto` đang chạy dở bị báo hỏng. Không đổi kịch bản và không nới điều kiện kiểm tra.

## Giới hạn đã biết
- Snapshot được dựng mỗi frame khi có Plan (để đánh giá điều kiện); chưa tối ưu.
- Sự kiện phát ra trong lúc action chạy chỉ được luật nhìn thấy ở frame sau.
- Tên hàm điều kiện (`min`, `max`, `count`, `any`) là từ khóa, không dùng làm tên observe.
- Hai Editor cùng chạy AgentPlay: chưa xử lý (hoãn từ M1).
- Chưa phát sự kiện `agent.action_error` và chưa cảnh báo khi game thiếu action `Safe` (spec M2 mục 13); action `Safe` ném lỗi thì bị bỏ qua không log.
- Registry chưa kiểm tên observe/sự kiện theo ngữ pháp điều kiện (chữ hoa, gạch ngang, tên hàm `min/max/count/any`).
- Cờ `Important` khai báo trong `[AgentEventInfo]` và cờ truyền vào `Emit` là hai nguồn riêng.
- Package chưa có trạng thái "hết ván": sau `game.won`/`game.lost` executor vẫn chạy action.
