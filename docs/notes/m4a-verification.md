# M4a: kết quả kiểm chứng

Ngày chạy: 2026-10-13 và 2026-10-17 (Windows 11, Unity 6000.0.77f1, .NET SDK 9.0.315, Claude Code CLI đăng nhập bằng gói Claude; model chiến thuật `claude-opus-5-5`, model nhanh `claude-haiku-5-5`)

## Test
- `dotnet test AgentPlay.sln`: 343/343 passed (sau vòng sửa theo review)
- `Tools/unity-tests.sh`: 146/146 passed (sau vòng sửa theo review)
- `Tools/check-generic.sh`: OK
- `python -I Tools/test_telemetry_trend.py`: passed
- `Tools/e2e-m4a-realtime.sh` (không gọi model): ALL CHECKS PASSED
- Hồi quy: `Tools/e2e-m2a.sh`, `Tools/e2e-m3-reflex.sh` và `Tools/e2e-m2c.sh script` đều ALL CHECKS PASSED, cả trước và sau vòng sửa theo review. `e2e-m3-reflex` vẫn chạy ở chế độ `turn_based`, tức chế độ theo lượt vẫn là mặc định.

## Định nghĩa "xong" của spec M4a

1. **Thời gian thực, không gọi model: đạt.** Bộ não kịch bản `win.json` thắng Heist ở chế độ thời gian thực. Game không dừng lần nào sau `hello`, mỗi Plan được gửi sau một tin `attention`, và telemetry ghi `mode = realtime`, `model_calls = 0`.
2. **Thời gian thực với model thật: đạt.** Loạt ngày 2026-10-17 (`Tools/run-heist-realtime.sh 5`, có playbook M3 và phản xạ `R-L2`) thắng 5/5. Mỗi ván có 3–4 lượt gọi, xa dưới giới hạn 40.

   | Ván | Kết quả | Thời gian game | Lượt gọi | Xin trước dùng / xin | Chờ khi dùng Plan xin trước |
   | --- | --- | --- | --- | --- | --- |
   | 1 | thắng | 38,6 s | 3 | 2/3 | 6454 ms; 1,6 ms |
   | 2 | thắng | 38,7 s | 3 | 2/3 | 8134 ms; 0,3 ms |
   | 3 | thắng | 51,9 s | 4 | 2/3 | 9282 ms; 3491 ms |
   | 4 | thắng | 38,6 s | 4 | 2/3 | 4100 ms; 6540 ms |
   | 5 | thắng | 51,8 s | 4 | 2/3 | 15432 ms; 3799 ms |

   Các loạt trước, ngày 2026-10-13, cho kết quả 5/5, 5/5 và 4/5. Ván thua của loạt cuối được giải thích ở mục 3.
3. **Định tuyến: đạt theo ruling.** Loạt ở mục 2 không có lượt Haiku nào, vì tên trộm không bị phát hiện lần nào nên không có sự kiện bất ngờ. Bằng chứng lấy từ phiên 2 của loạt học ngày 2026-10-17, cùng chế độ, cùng router và cùng code:
   - lính gác phát hiện ở t=20,27, luật `then: replan` làm Plan p1 hỏng (`plan_failed reason=replan:0`);
   - lượt kế tiếp đi sang Haiku và trả Plan p2 hợp lệ sau 10,2 giây; ván thắng;
   - các lượt khác đều là Opus (chiến thuật và xin trước).

   Trong mọi run log đã xem, không lượt Haiku nào phát sinh ngoài sự kiện bất ngờ hoặc luật `replan`.
4. **Xin trước: đạt.** Ván 1 và ván 2 ở mục 2 dùng Plan xin trước với thời gian chờ 1,6 ms và 0,3 ms, dưới mức 1 giây.
5. **Tiêu chí 5 của spec gốc: đạt, nhưng xu hướng yếu.** `Tools/run-heist-learning.sh 5` ngày 2026-10-17: 5 phiên có nhãn `play`, Reflector học từ trace của chính phiên đó sau mỗi phiên, và cả 5 phiên đều thắng.

   | Phiên | Lượt gọi | Lượt / phút game |
   | --- | --- | --- |
   | 1 | 3 | 4,35 |
   | 2 | 2 | 3,26 |
   | 3 | 3 | 3,45 |
   | 4 | 2 | 4,73 |
   | 5 | 2 | 3,09 |

   Phiên 5 thấp hơn phiên 1 và độ dốc hồi quy là −0,103 mỗi phiên. Số lượt gọi chỉ dao động từ 2 đến 3, nên một lượt gọi dư ở một phiên đủ làm đổi dấu xu hướng. Không có đề xuất phản xạ nào, vì không phiên nào có sự kiện lặp lại để học thành phản xạ. Playbook học được 8 bài (L1–L8). Bài L7 khuyên gói cả vụ trộm vào một Plan; việc này có thể giải thích vì sao các phiên sau chỉ cần 2 lượt gọi, nhưng chưa được kiểm chứng riêng.
6. **Không hồi quy: đạt** (xem mục Test).

## Phát hiện và điều chỉnh trong lúc làm
- **Xin trước bắt đầu ngay khi gửi Plan** (ruling, `1bc0ccf`): loạt đầu cho thấy Plan xin trước về trễ 5–12 giây, vì Opus (8–12 giây) chậm hơn bước cuối của hầu hết các Plan. Giờ runner xin trước ngay khi gửi Plan ở chế độ thời gian thực. Cái giá là khoảng một lượt Opus thêm mỗi Plan.
- **Lượt xin trước bắt đầu ngay sau khi lượt gọi xong** (`7043dfe`), thay vì đợi tin kế tiếp từ Unity (khoảng 2 giây sau).
- **Loạt học ngày 2026-10-13 không hợp lệ.** Phiên 5 chạm giới hạn dùng hàng tuần của Claude (`429`, runner thoát mã 4). `telemetry_trend.py` lại tính phiên đó là 0 lượt mỗi phút và báo "decreasing". Đã sửa ở `6924062`: công cụ trend chỉ tính phiên thắng hoặc thua và liệt kê các phiên khác là `skipped`; `run-heist-learning.sh` dừng với mã 4 khi một phiên hoặc lần học của nó gọi model lỗi, vì tiêu chí 5 cần các phiên liên tiếp.
- **Reflector học trễ một phiên.** Runner chạy `reflect` ngay khi ván kết thúc, lúc Unity vẫn còn trong Play mode và đang mở trace để ghi. `StreamWriter` của Unity chỉ cho đọc chung khi không ai ghi, nên trace không đọc được và bị bỏ qua với lý do "tag unknown". Đã sửa ở `5e75b6d`, có test tái hiện: runner chờ tối đa 30 giây cho mọi trace được đóng rồi mới học.
- **Ván thua ngày 2026-10-13 (loạt 3, ván 3)** không phải lỗi: lượt Haiku được khởi động ở t=20,53, nhưng ván thua 2,6 giây sau, trước khi Haiku kịp trả lời. Run log ghi 4 lượt đã khởi động, trong đó 2 lượt bị hủy khi ván kết thúc.
- **Haiku chậm hơn số đo ban đầu:** lượt nhanh thật mất 7,8–10,2 giây, so với 3,2 giây đo được trong spec. Trong Heist, cách phản ứng nhanh duy nhất chắc chắn kịp vẫn là phản xạ.
- **Môi trường:** một lần ổ C: đầy (0 GB trống) làm Unity mất 13 phút mới thoát và hỏng loạt chạy. Loạt đó bị bỏ và chạy lại sau khi dọn ổ.

## Review độc lập
Một reviewer có ngữ cảnh mới (Opus, chỉ đọc code) kết luận "merge được sau khi sửa": 0 lỗi nghiêm trọng, 4 lỗi quan trọng, 9 lỗi nhỏ. Năm tình huống trong Review Focus của plan đều đạt.

Đã sửa ở `3dba2be`, có test fail trước khi sửa:
- **Hai Plan khác nhau cùng một id.** Một quyết định xếp hàng sau lượt gọi đang chạy được chép trước khi Plan của lượt đó được gửi, nên cũng nhận id `p2`. Giờ bản chép cập nhật `PlansSent`, `LastPlan` và `History` ngay trước khi lượt gọi của nó bắt đầu.
- **Router đếm theo `attention`, không theo lượt gọi.** Các quyết định bị gộp hoặc được Plan xin trước trả lời vẫn làm đổi bộ đếm leo thang. Giờ router chỉ chạy khi một lượt quyết định thật sự bắt đầu.
- **Sự kiện đi sang Haiku cả khi chưa có Plan nào chạy.** Spec chỉ cho phép khi "đang có Plan". Ruling: việc "đang có Plan" được xét trên bản chép lúc sự kiện xảy ra.
- **Kết quả của lượt xin trước đã bị hủy vẫn có thể được nhận** nếu tiến trình kịp thoát trước lệnh hủy. Giờ mọi kết quả của lượt gọi mà runner đã hủy đều bị bỏ.

Hai lỗi nhỏ cũng được sửa: lý do bộ não dừng lại hiện trong log, và `run-heist-learning.sh` dừng khi runner thoát với mã khác 0 hoặc 3. Sau vòng sửa, 4 E2E không gọi model và 2 ván Opus thời gian thực (thắng 2/2, id Plan p1–p3 đúng thứ tự) đều đạt. Bảy lỗi nhỏ còn lại được hoãn, có danh sách trong ledger của plan. Đáng chú ý là hai lỗi hiếm: một sự kiện quan trọng trùng frame với lúc Plan kết thúc thì Plan xin trước vẫn được dùng; và một ván thắng mà không có `attention` theo sau thì có thể kết thúc với mã 6.

## Giới hạn đã biết
- Haiku chỉ được kiểm bằng một lượt gọi thật trong loạt học, cộng với test đơn vị của `ModelRouter`.
- Xu hướng ở tiêu chí 5 dựa trên 5 phiên với số lượt gọi rất nhỏ, nên chưa có ý nghĩa thống kê.
- Giữ tiến trình `claude -p` sống giữa các lượt gọi vẫn nằm ngoài phạm vi; thời gian khởi động CLI chiếm phần lớn độ trễ.
