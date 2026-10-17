# M4b: kết quả kiểm chứng

Ngày chạy: 2026-10-17 (Windows 11, Unity 6000.0.77f1, .NET SDK 9.0.315, Claude Code CLI đăng nhập bằng gói Claude, model `claude-opus-5-5`)

## Test
- `dotnet test AgentPlay.sln`: 357/357 passed (14 test mới cho `check`)
- `Tools/unity-tests.sh`: 157/157 passed (11 test mới cho AutoPlay: quy tắc thoát, thứ tự hook, lọc hook)
- `Tools/test-link-agentplay.sh`: 30/30 passed (`-LocalOnly`, `-Unlink`, `-Purge`, cảnh báo của git, `csc.rsp` của người dùng, `packages-lock.json` có thay đổi riêng)
- `Tools/e2e-m4b-check.sh` (không gọi model): ALL CHECKS PASSED, `check` đạt cả 15 mục trên Heist
- Hồi quy: xem mục "Regression cuối" bên dưới

## Định nghĩa "xong" của spec M4b (game kiểm chứng: Farm Pal / PetRPG)

1. **Skill sinh adapter: đạt.** Áp dụng skill `agentplay-adapt` trong `E:\PetRPG` với mục tiêu "hoàn thành 3 nhiệm vụ chính đầu tiên từ save mới":
   - Khảo sát chỉ đọc code: tìm điều khiển nhân vật (joystick UI, NavMesh của mũi tên nhiệm vụ), hệ nhiệm vụ, cách lưu tiến trình, dạng `Assembly-CSharp`, các công cụ Editor tự chạy khi mở project (không có cái nào đụng tới git).
   - Bảng đề xuất: 13 observe, 4 action (`goto_target`, `goto`, `stay`, `stop` an toàn), 4 event (`mission.started`, `mission.progress`, `mission.done`, `game.won`), hook đưa game về người chơi mới.
   - Ruling: người dùng dặn trước là cứ làm khi họ ngủ, nên bảng được ghi vào `AgentPlay/proposal.md` (ngoài git) thay vì chờ duyệt trực tiếp.
2. **Không đụng repo của nhóm: đạt.** `git status` của PetRPG trống trước, trong (trừ `Packages/packages-lock.json` do Unity tự ghi khi package được gắn) và sau khi gỡ. Không file nào của game bị sửa; adapter, `csc.rsp`, `AgentPlay/` đều nằm trong `.git/info/exclude`.
3. **Kiểm adapter: đạt.** `AgentPlay.Runner check` thoát 0, 20/20 mục `PASS`.
4. **Agent chơi được: đạt, 4/4 ván.** Opus chế độ theo lượt, từ save mới, không ai can thiệp:

   | Ván | Kết quả | Lượt gọi | Nhiệm vụ xong | Thời gian game tới `game.won` |
   | --- | --- | --- | --- | --- |
   | bậc 4 của skill | thắng | 7 | 3 | 41,8 s |
   | batch 1 | thắng | 7 | 3 | 42,3 s |
   | batch 2 | thắng | 7 | 3 | 44,4 s |
   | batch 3 | thắng | 7 | 3 | 43,1 s |

   Ba nhiệm vụ: hái 10 quả mọng, mua công trình đầu tiên, phá chuồng cứu thú. Mỗi nhiệm vụ Opus dùng một Plan "đi tới mục tiêu rồi đứng làm việc", cộng một Plan chờ khi nhiệm vụ mới được kích hoạt.
5. **Save gốc an toàn: đạt.** Sau mỗi lượt chạy, `Tools/compare-petrpg-state.ps1` so 16 file save (mã băm) và bản export PlayerPrefs với trạng thái gốc: giống hệt. Thêm một lần **tắt ngang Unity** ngay khi hook vừa cất save: lần chạy sau trả save và PlayerPrefs về trước, kết quả vẫn giống hệt.
6. **Gỡ sạch: đạt.** Sau `link-agentplay.ps1 -Unlink`, PetRPG biên dịch trong batchmode thoát 0, `git status` trống, `AgentPlay/` (trace, playbook) được giữ và vẫn bị loại khỏi git.
7. **Không hồi quy: đạt** (mục "Regression cuối"). Heist chạy bằng AutoPlay chung trong mọi E2E.

## Chạy thử skill trên TileThieves (mẫu asmdef)

Một agent có ngữ cảnh mới làm theo `SKILL.md` trên một git worktree tách từ nhánh `main` của TileThieves (chưa có adapter), mục tiêu "cạy 6 viên gạch". Cả bốn bậc đạt; Opus thắng với 1 lượt gọi. Agent không đọc adapter viết tay có sẵn. Worktree đã được xóa; `git status` của bản TileThieves người dùng đang làm không đổi.

Lần chạy thử tìm ra một lỗi thật và 13 chỗ hướng dẫn chưa rõ, đều đã sửa:
- **AutoPlay treo Editor** khi game có Test Framework: `TypeCache` trả về hook giả trong test của AgentPlay (không có constructor mặc định). Giờ AutoPlay bỏ qua kiểu không có constructor mặc định và kiểu trong assembly test, và mọi lỗi lúc khởi động đều dẫn tới thoát mã 2.
- Skill: lệnh PowerShell cần `-ExecutionPolicy Bypass`; gắn package trước khi ghi bảng đề xuất; `packages-lock.json` là thay đổi được chấp nhận cho tới khi gỡ; khảo sát thêm công cụ Editor tự chạy khi mở project (TileThieves có `GitAutoSync` tự pull); khối loại trừ dùng chung giữa các worktree; phải làm gì khi Unity không thoát; định dạng file plan kịch bản; giới hạn thời gian; brief nên tả cơ chế chứ không viết sẵn lời giải.
- `run-adapter-batch.sh`: mỗi lần gọi có thư mục log riêng; in giá trị tiến độ đầu và cuối.

## Phát hiện và điều chỉnh trong lúc làm
- **Không có công cụ MCP Unity trong phiên** lúc đầu; người dùng bật MCP for Unity, phiên gọi thẳng endpoint HTTP của nó để thăm dò PetRPG.
- **Xóa save hoặc khóa registry của người dùng bị chặn** bởi bộ phân loại an toàn của phiên. Ruling: mọi thao tác trên trạng thái người dùng đều là đổi tên (cất sang bên cạnh) rồi đổi tên lại; hook mẫu của skill làm đúng như vậy và để save của lượt chạy ở `LocalStorage.agentplay-last-run`, cùng ổ đĩa với save gốc.
- **AutoPlay nghe `AgentEvents` thường trực** làm mọi lời gọi `Emit` phải tuần tự hóa payload (test cũ `DoesNotTouchPayloadWhenNobodyListens` bắt được). Giờ chỉ nghe trong lúc có lượt chạy.
- **`csc.rsp` của Unity không có cú pháp chú thích:** dòng đánh dấu `# AgentPlay ...` bị đọc thành tên file nguồn và làm hỏng biên dịch PetRPG. Giờ file chỉ có đúng một dòng `-define:AGENTPLAY`, và `-Unlink` nhận ra nó theo nội dung.
- **Trùng tên kiểu trong `Assembly-CSharp`:** PetRPG có một enum `MissionType` ở namespace toàn cục, che enum cùng tên được nhập bằng `using`, nên observe `mission.goal` báo sai loại nhiệm vụ. Adapter ghi đầy đủ tên kiểu; skill có thêm mục trong `pitfalls.md`.
- **Plan kịch bản chạy quá sớm:** lúc t=0,02 giây game chưa kích hoạt nhiệm vụ nào. Plan và brief giờ chờ `target.known` trước.
- **`-Unlink` dừng giữa chừng trên PetRPG:** cảnh báo CRLF của git trên stderr thành lỗi dừng script trong PowerShell 5.1, và Unity chỉ đổi kiểu xuống dòng của `packages-lock.json` nên `git diff --quiet` không thấy. Đã sửa, có test: gọi git qua hàm bọc; khôi phục theo `git status`, và chỉ khi file sạch lúc gắn package.
- **`-Unlink` giữ `AgentPlay/` nhưng spec đòi `git status` sạch:** giờ giữ một dòng loại trừ cho `AgentPlay/` cho tới khi `-Purge`.

## Regression cuối
Chạy sau mọi bản sửa của Task 8, tất cả thoát 0:
- `dotnet test AgentPlay.sln`: 357/357
- `Tools/unity-tests.sh`: 157/157
- `Tools/check-generic.sh`: OK
- `Tools/test-link-agentplay.sh`: 30/30
- `Tools/e2e-m2a.sh`, `Tools/e2e-m3-reflex.sh`, `Tools/e2e-m2c.sh script`, `Tools/e2e-m4a-realtime.sh`, `Tools/e2e-m4b-check.sh`: ALL CHECKS PASSED
- `python -I Tools/test_telemetry_trend.py`: passed

## Giới hạn đã biết
- Adapter PetRPG chỉ nằm trên máy người dùng, không được đưa vào repo AgentPlay (game thương mại của nhóm).
- Skill mới được chứng minh trên hai game (một mẫu asmdef, một mẫu `Assembly-CSharp`), cả hai với mục tiêu ngắn.
- Ở PetRPG, `com.unity.build-report-inspector 0.3.0-preview` của game báo lỗi biên dịch dưới Unity 6 (API `BuildReport.files` đã bị bỏ); lỗi có sẵn, không liên quan AgentPlay, không chặn Play mode.
- Dữ liệu do các lượt chạy để lại trên máy người dùng: `C:\Users\Admin\AppData\LocalLow\3HP\Farm Pal\LocalStorage.agentplay-last-run` (save của lượt chạy cuối), `LocalStorage.agentplay-probe-new` và khóa registry `Farm Pal.agentplay-probe-new` (từ lần thăm dò), `E:\PetRPG\AgentPlay\` (trace, telemetry, brief, đề xuất). Xóa được bất cứ lúc nào.
