# M1: kết quả kiểm chứng

Ngày chạy: 2026-10-11 (Windows 11, Unity 6000.0.77f1, .NET SDK 9.0.315)

## Test
- `dotnet test AgentPlay.sln`: 42/42 passed, 0 failed (sau vòng sửa theo review)
- `Tools/unity-tests.sh`: 55/55 passed, 0 failed (sau vòng sửa theo review)
- `Tools/e2e-m1.sh`: ALL CHECKS PASSED

## Rủi ro trong spec mục 13
1. **Pause + EditorApplication.update:** đạt. Runner suy nghĩ giả lập 3 giây trước mỗi `resume` với ngân sách 2 giây game. Thời gian game giữa các lần pause là `[2.0, 2.0, 1.37, 2.0, 2.0]` giây (1.37 là pause do sự kiện quan trọng). Thời gian thực tương ứng là `[5.02, 5.09, 4.49, 5.1, 5.09]` giây. Game không chạy trong lúc runner suy nghĩ.
   - Phát hiện trong lúc chạy E2E: với `runInBackground: 0` (mặc định của project mới), Editor ngừng chạy game khi cửa sổ mất focus, và phiên sau domain reload đứng yên. Đã sửa: controller bật `Application.runInBackground` trong suốt phiên rồi trả lại giá trị cũ. `ProjectSettings.asset` không bị thay đổi.
2. **Socket và domain reload:** đạt. `EditorUtility.RequestScriptReload()` gọi giữa Play mode khiến domain reload ngay lập tức. Bridge đóng trong `beforeAssemblyReload` rồi mở lại ở static constructor. Log có hai dòng `Bridge listening on 127.0.0.1:47800` (cùng cổng), không có `SocketException`. Runner tự kết nối lại và nhận `hello` mới.
3. **Nhúng Contract vào package:** đạt. Contract biên dịch trong Unity (asmdef `AgentPlay.Contract`, `noEngineReferences`) và trong .NET (`Contract/AgentPlay.Contract.csproj` liên kết cùng file `.cs`). Đã kiểm ở Task 3.
4. **SDK C# của Anthropic:** chưa kiểm, thuộc M2.
5. **Thời gian quét attribute:** Testbed mất 8 ms và 11 ms (hai lần Begin trong autoplay.log), dưới mục tiêu 50 ms. Chưa đo trên TileThieves.

## Giới hạn đã biết
- Sự kiện phát trước khi controller Editor đăng ký (ví dụ trong Awake của scene đầu tiên) bị mất.
- Runner thứ hai kết nối vào cùng cổng sẽ chờ tới khi runner đầu ngắt, và in cảnh báo sau 5 giây không nhận được hello.
- Kiểm tra hết ngân sách thời gian chạy theo EditorApplication.update, nên có thể vượt ngân sách vài frame.
- Domain reload giữa Play mode tạo một file trace mới cho phần còn lại của phiên.
- Mở hai Editor cùng chạy AgentPlay: runner luôn chọn cổng thấp nhất đang mở, không chọn được Editor theo tên game. Nếu Editor đó đã có runner khác, runner mới chờ và chỉ in cảnh báo sau 5 giây. Dự kiến thêm lựa chọn `--game` và trả lời "busy" ở M2.
- `AgentEvents.Emit` gọi từ thread phụ bị bỏ qua (kèm một cảnh báo), không được xếp hàng.
- Payload sự kiện lớn hơn 65.536 ký tự bị thay bằng `{"error": "Payload too large ..."}`.
