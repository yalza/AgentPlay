# M2c: kết quả kiểm chứng

Ngày chạy: 2026-10-13 (Windows 11, Unity 6000.0.77f1, .NET SDK 9.0.315, TileThieves nhánh `feat/agentplay-adapter`)

## Test
- `bash Tools/e2e-m2c.sh script`: ALL CHECKS PASSED (bộ não kịch bản, `AgentPlay/scripts/pry6.json`)
- `bash Tools/e2e-m2c.sh opus`: ALL CHECKS PASSED ba lần liên tiếp (tiêu chí 3)
- `Tools/check-generic.sh`: OK (quét cả assembly `AgentPlay.InputSystem`)
- Gỡ junction: TileThieves vẫn biên dịch, adapter bị loại sạch, `Tools/LogicTests` 217/217

## TileThieves `Prototype_Pry` với Opus thật

| Lần | Kết quả | Lượt gọi | Thời gian game tới khi thắng | Độ trễ lượt gọi | Token ra | Cache |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | thắng, túi 6 viên, không Lockdown | 1 | 4,92 s | 11,5 s | 735 | ghi 8334 |
| 2 | thắng, túi 6 viên, không Lockdown | 1 | 5,23 s | 13,0 s | 673 | đọc 8334 |
| 3 | thắng, túi 6 viên, không Lockdown | 1 | 4,88 s | 18,0 s | 804 | đọc 8334 |

- Cả ba lần Opus chỉ cần một Plan: ngồi xuống, tới bức tranh gần nhất, nhắm một ô, cạy 6 ô; kèm luật dừng để tính lại nếu độ ồn tăng cấp. Độ ồn không tăng cấp trước khi đủ 6 viên.
- Bộ kiểm `Tools/verify_m2c.py` xác nhận: tay cầm ảo được tạo và gỡ khi hết Play mode, trace có ít nhất 6 sự kiện `tile.pried`, có `game.won` với `sack.count >= 6`, không có `game.lost`, không có lỗi biên dịch hay NullReferenceException.
- Trace ghi Plan và kết quả (tiêu chí 5): file trace của lần 3 có dòng `control`, dòng `plan` và năm dòng `plan_status`.

## Giới hạn đã biết
- Ở lần 3, luật `abort` của Plan kích hoạt đúng frame có `game.won`, nên trace ghi `plan_failed` ngay sau khi thắng. Kết quả ván không bị ảnh hưởng.
- Adapter chỉ phủ prototype cạy gạch (một phòng, không bảo vệ); các action khác của game (vác bao, bảo vệ, ghép tranh) chưa có.
- Thay đổi `Packages/packages-lock.json` do package nhúng được script E2E tự hoàn tác; không commit vào TileThieves.
