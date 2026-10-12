# M2b: kết quả kiểm chứng

Ngày chạy: 2026-10-13 (Windows 11, Unity 6000.0.77f1, .NET SDK 9.0.315, Claude Code CLI đăng nhập bằng gói Claude)

## Test
- `dotnet test AgentPlay.sln`: 219/219 passed, 0 failed (sau vòng sửa theo review)
- `Tools/unity-tests.sh`: 108/108 passed, 0 failed
- `Tools/check-generic.sh`: OK
- `AgentPlay.Runner --brain opus --smoke`: trả về Plan hợp lệ sau 8,2 giây

## Heist với Opus thật (tiêu chí 2)
Lệnh: `bash Tools/run-heist-opus.sh 5` (backend `claude-code`, model `claude-opus-5-5`, effort `high`, tối đa 40 lượt gọi mỗi ván). Ván 5 bị ngắt giữa chừng vì phiên Claude Code điều khiển kết thúc; đã chạy lại riêng ván 5 bằng đúng lệnh runner và Unity của script.

| Ván | Kết quả | Lượt gọi | Plan gửi | Thời gian game tới khi thắng | Độ trễ từng lượt gọi (giây) | Token ra |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | thắng | 4 | 3 | 18,11 s | 10,5 / 11,2 / 19,3 (bị từ chối) / 28,6 | 4070 |
| 2 | thắng | 3 | 3 | 16,14 s | 17,8 / 18,9 / 23,1 | 2283 |
| 3 | thắng | 3 | 3 | 15,75 s | 11,0 / 16,2 / 17,3 | 2035 |
| 4 | thắng | 3 | 3 | 18,44 s | 11,1 / 12,5 / 19,5 | 2345 |
| 5 | thắng | 4 | 4 | 17,41 s | 8,0 / 10,7 / 17,5 / 12,8 | 3451 |

- **Kết quả: 5/5 ván thắng** (yêu cầu ít nhất 3/5).
- 17 lượt gọi: độ trễ trung vị 16,2 giây, thấp nhất 8,0 giây, cao nhất 28,6 giây. Game đứng yên trong lúc chờ nhờ chế độ theo lượt, nên độ trễ không ảnh hưởng kết quả; một ván mất khoảng một phút thời gian thực.
- Phần system được cache: từ lượt thứ hai mỗi ván đọc 3388 token từ cache. Với backend `claude-code`, mức dùng tính vào hạn mức của gói; runner chưa ghi số tiền quy đổi (`total_cost_usd`).
- Luật phản xạ hoạt động thật: ở ván 5, Plan p3 có luật `guard.spotted → replan`; luật kích hoạt sau 0,95 giây game, Opus lập Plan p4 nấp sau `cover_1`, rồi thắng.
- Một Plan bị từ chối (ván 1): Opus viết `guards[0]...` trong `until`, mà ngôn ngữ điều kiện không có chỉ số danh sách. Bộ não gửi lại lỗi kiểm tra cho model, lần gọi sau trả Plan hợp lệ. Cái giá là một lượt gọi thêm (19,3 giây).

## Sửa sau review
- System prompt truyền qua `--system-prompt-file` thay vì dòng lệnh (Windows giới hạn 32.767 ký tự). Smoke test thật vẫn đạt (9,7 giây).
- CLI thoát trước khi đọc stdin không còn làm runner tưởng mất kết nối Unity; khi quá thời gian, lỗi kèm phần cuối stderr.
- Lỗi chưa đăng nhập, hết credit, hết hạn mức mà CLI báo không kèm mã HTTP: không thử lại. Ba lỗi tạm thời liên tiếp thì bộ não dừng, không đốt hết ngân sách lượt gọi.
- Đường `api`: 408 và 409 được thử lại; `refusal` so khớp không phân biệt hoa thường.

## Rủi ro trong spec M2 mục 14
1. SDK `Anthropic`: đã kiểm biên dịch. Đường `api` chưa chạy thật vì người dùng chỉ có gói Claude.
2. Opus 5.5 không cho ép `tool_choice`: chuyển sang structured outputs, đã chạy thật qua `--json-schema`.

## Giới hạn đã biết
- Ngôn ngữ điều kiện không có chỉ số danh sách (`list[0]`); model đôi khi vẫn thử viết như vậy và tốn thêm một lượt gọi.
- Độ trễ mỗi lượt gọi 8–29 giây với effort `high`, gồm cả thời gian khởi động tiến trình `claude -p`. Chưa thử effort thấp hơn.
- Đường `api` (`AnthropicModelClient`) mới được kiểm bằng test đơn vị và biên dịch, chưa gọi API thật.
- Hết hạn mức gói giữa chừng: bộ não dừng với lý do rõ ràng và runner thoát mã 4 (có test); chưa gặp thật.
