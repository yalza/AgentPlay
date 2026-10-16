# AgentPlay M4b: skill `agentplay-adapt` sinh adapter cho game mới

**Ngày:** 2026-10-17
**Trạng thái:** Người dùng đã duyệt thiết kế trong hội thoại (hướng A, ba phần), chờ duyệt spec viết
**Spec gốc:** `docs/superpowers/specs/2026-10-11-agentplay-design.md` (mục 3.1, mục 4, mục 12 mốc M4). Kế thừa M1–M4a đã merge (`v0.4.0-m4a`).

---

## 1. Mục tiêu

Một người có game Unity riêng chạy skill `agentplay-adapt` trong Claude Code, nói mục tiêu cho agent ("chơi 3 nhiệm vụ đầu"), và nhận về một adapter chạy được: agent của AgentPlay chơi game đó tới mục tiêu mà không cần ai viết adapter bằng tay. Đây là nửa "mở rộng" của M4 trong spec gốc: "một game thứ hai của người dùng chạy được với adapter do skill sinh".

Game kiểm chứng là **PetRPG (Farm Pal)** ở `E:\PetRPG`, một game mobile farm/tycoon kết hợp RPG, đang được một nhóm phát triển. Mục tiêu của agent trong game này: tự chơi chuỗi nhiệm vụ đầu của người chơi mới, như một bản playtest tự động.

### Định nghĩa "xong"

1. **Skill sinh adapter:** chạy skill trong PetRPG với mục tiêu "hoàn thành 3 nhiệm vụ đầu từ save mới". Skill tự đi hết các bước ở mục 2 và tạo ra adapter, `brief.md`, plan kịch bản và hook save. Người dùng chỉ duyệt bảng đề xuất ở bước 2.
2. **Không đụng repo của nhóm:** sau khi skill chạy xong, `git status` của PetRPG sạch (mọi file thêm vào đều nằm trong `.git/info/exclude`).
3. **Kiểm adapter đạt:** `AgentPlay.Runner check` thoát mã 0 trên PetRPG.
4. **Agent chơi được:** Opus ở chế độ theo lượt hoàn thành 3 nhiệm vụ đầu từ save mới trong ít nhất 2 trên 3 lần chạy liền nhau, không ai can thiệp.
5. **Save gốc an toàn:** save của người dùng trong `Application.persistentDataPath` sau các lần chạy giống hệt trước khi chạy (so khớp nội dung file).
6. **Gỡ sạch:** sau `link-agentplay.ps1 -Unlink`, PetRPG biên dịch không lỗi và `git status` vẫn sạch.
7. **Không hồi quy:** `dotnet test`, `Tools/unity-tests.sh`, `Tools/check-generic.sh` và các E2E không gọi model (`e2e-m2a`, `e2e-m3-reflex`, `e2e-m2c script`, `e2e-m4a-realtime`) vẫn đạt. Heist chạy bằng AutoPlay chung (mục 3.1).

### Ngoài phạm vi M4b

- InputDriver mức 0 (chơi bằng ảnh chụp Game view). Để sang một mốc sau.
- Chế độ thời gian thực, combat và dungeon của PetRPG.
- Chạy trong bản build. Adapter và AgentPlay chỉ hoạt động trong Editor, như spec gốc.
- Commit adapter vào repo PetRPG. Việc đưa adapter vào repo của nhóm là quyết định của nhóm, không phải của skill.

---

## 2. Skill `agentplay-adapt`

### 2.1 Vị trí và cài đặt

- Mã nguồn: `.claude/skills/agentplay-adapt/` trong repo AgentPlay (đúng vị trí spec gốc mục 3.1).
- Dùng từ project game: README hướng dẫn tạo junction hoặc chép thư mục vào `~/.claude/skills/agentplay-adapt`. Skill đọc đường dẫn repo AgentPlay từ biến `AGENTPLAY_HOME`; nếu không có thì hỏi người dùng một lần.

### 2.2 Nội dung

| File | Nội dung |
| --- | --- |
| `SKILL.md` | Mô tả khi nào dùng, 5 bước ở mục 2.3, các điểm dừng, các lệnh kiểm |
| `reference/attributes.md` | Cách dùng `[AgentGame]`, `[AgentObserve]`, `[AgentObserveList]`, `[AgentAction]` (kể cả `Safe`), `[AgentEventInfo]`, `AgentEvents.Emit`, `AgentActionContext`, `AgentActionStatus` |
| `reference/adapter-shapes.md` | Hai mẫu adapter ở mục 4, kèm khung file đầy đủ cho từng mẫu |
| `reference/pitfalls.md` | Bẫy đã gặp: vùng chết của stick (`VirtualGamepad.PastDeadzone`), không cấp phát trong `Update`, observe phải rẻ, action phải trả `Done` khi ván đã kết thúc, sự kiện quan trọng phải có `Outcome` |
| `reference/brief-template.md` | Khung `brief.md`: mục tiêu, cách thắng, cách thua, những gì không thấy được trong observe |

Hai adapter đã chạy thật (Heist trong repo, TileThieves nhánh `feat/agentplay-adapter`) là ví dụ mà `adapter-shapes.md` trích dẫn.

### 2.3 Quy trình 5 bước

1. **Khảo sát (chỉ đọc).** Tìm:
   - nhân vật người chơi và component điều khiển nó;
   - đường input: Input System (tay cầm ảo dùng được), joystick UI tự viết, hay bàn phím cũ;
   - các manager nắm trạng thái liên quan tới mục tiêu (Singleton, `EventBus`, ScriptableObject dữ liệu);
   - cách game lưu tiến trình (đường dẫn, định dạng, lúc ghi);
   - asmdef của code game, để chọn mẫu adapter (mục 4);
   - scene khởi đầu.

   Kết quả là một ghi chú khảo sát ngắn trong hội thoại, có trích `file:dòng`.
2. **Đề xuất bề mặt adapter, rồi dừng chờ duyệt.** Một bảng gồm:
   - observe: đường dẫn, kiểu, nguồn trong code game;
   - action: tên, tham số, cách thực hiện, action `Safe`;
   - event: tên, khi nào phát, `Important`, `Outcome`;
   - điều kiện `game.won` và `game.lost` theo mục tiêu;
   - cách đưa game về điểm xuất phát (ví dụ save mới) và hook save.

   Nguyên tắc cho action: đi qua đúng lối mà input của người chơi đi qua (đặt giá trị joystick, nhấn nút tay cầm ảo, gọi đúng hàm mà nút UI gọi). Không dịch chuyển tức thời, không sửa thẳng tiền hay tiến độ. Ngoại lệ duy nhất là dựng bối cảnh test trong hook, như tạo save mới. Skill không viết file nào trước khi người dùng duyệt bảng này.
3. **Gắn vào project.**
   - Chạy `Tools/link-agentplay.ps1 -ProjectPath <game> -LocalOnly`.
   - Viết adapter theo mẫu đã chọn.
   - Viết `AgentPlay/brief.md`.
   - Viết `AgentPlay/scripts/smoke.json`: một plan kịch bản đi được ít nhất một bước có ý nghĩa của mục tiêu mà không cần model.
   - Viết hook `IAgentAutoPlayHook` nếu game có lưu tiến trình (mục 3.1).
4. **Tự kiểm theo bậc.** Dừng ở bậc đầu tiên bị hỏng, sửa adapter, chạy lại từ bậc đó:
   1. Biên dịch sạch: Unity batchmode `-quit`, log không có `error CS`.
   2. `AgentPlay.Runner check` thoát 0 (mục 3.2).
   3. Plan kịch bản: AutoPlay chung với `--brain script smoke.json` chạy hết plan, không `plan_failed`.
   4. Opus theo lượt chơi tới mục tiêu: AutoPlay chung với `--brain opus`, thoát mã 0.

   Mỗi bậc có lệnh cụ thể trong `SKILL.md`. Skill không được nới tiêu chí của bậc để cho qua.
5. **Báo cáo.** Liệt kê các file đã thêm (đều nằm ngoài git), cách gỡ (`-Unlink`), kết quả từng bậc kèm log, và những gì adapter chưa phủ.

---

## 3. Phần thêm vào package và runner

### 3.1 AutoPlay chung

`AgentPlay.Editor.AutoPlay.Run`, gọi bằng `Unity.exe -projectPath <game> -executeMethod AgentPlay.Editor.AutoPlay.Run`.

- Biến môi trường:
  - `AGENTPLAY_AUTOPLAY_SCENE`: đường dẫn scene cần mở; không đặt thì giữ scene đang mở;
  - `AGENTPLAY_AUTOPLAY_SECONDS`: hạn chót tính bằng giây thực, mặc định 240;
  - `AGENTPLAY_SESSION_TAG`: `play` hoặc `debug`, mặc định `debug`, như `HeistAutoPlay`.
- Trình tự:
  1. Gọi `BeforePlay()` của mọi hook.
  2. Mở scene và vào Play mode.
  3. Nghe `AgentEvents.Emitted`. Khi gặp `game.won` hoặc `game.lost`, đợi 2 giây rồi thoát Play mode. Mã thoát: 0 nếu thắng, 3 nếu thua, 1 nếu hết giờ.
  4. Gọi `AfterPlay()` của mọi hook **sau khi đã thoát Play mode**, kể cả khi hết giờ.
- `IAgentAutoPlayHook` (namespace `AgentPlay.Editor`): `void BeforePlay(); void AfterPlay();`, được tìm bằng `TypeCache.GetTypesDerivedFrom`, tạo bằng constructor không tham số. Lỗi trong một hook được log và làm AutoPlay thoát mã 2, sau khi đã gọi `AfterPlay` của các hook đã chạy `BeforePlay`.
- Trạng thái qua domain reload (hạn chót, thời điểm kết thúc ván, danh sách hook đã chạy) giữ trong `SessionState`, như `HeistAutoPlay`.
- `HeistAutoPlay.Run` chuyển sang gọi AutoPlay chung. `ApproveOnly` vẫn giữ trong demo vì đó là thao tác riêng của demo.

### 3.2 `AgentPlay.Runner check`

```
AgentPlay.Runner check [--ports A-B] [--idle-exit SECONDS]
```

1. Kết nối như khi chơi, đọc `hello`.
2. Gửi `get_state`, rồi `resume` 2 giây game, rồi `get_state` lần nữa.
3. Kiểm và in mỗi mục một dòng `PASS` hoặc `FAIL`:
   - `hello` có ít nhất một observe và một action;
   - có đúng một action `Safe`;
   - có khai báo event `game.won`;
   - `brief` trong `hello` không rỗng;
   - mỗi observe có giá trị khác null trong ít nhất một trong hai snapshot;
   - mọi tham số action có kiểu được Contract hỗ trợ;
   - không có tin `error` nào từ Unity.
4. Thoát 0 khi mọi mục đạt, 1 khi có mục hỏng, 6 khi không kết nối được trong `--idle-exit` giây.

`check` không gọi model và không gửi plan. Nó chạy cùng AutoPlay chung với hạn chót ngắn.

### 3.3 `link-agentplay.ps1`

- `-LocalOnly`: thay vì thêm dòng vào `.gitignore` (file của nhóm), ghi các mẫu loại trừ vào `.git/info/exclude`:
  - `/Packages/com.agentplay.core`;
  - `/AgentPlay/`;
  - thư mục adapter và `.meta` của nó;
  - `/Assets/csc.rsp` (mẫu `Assembly-CSharp`).

  Nếu `Packages/packages-lock.json` bị Unity sửa vì package nhúng, script in hướng dẫn hoàn tác bằng `git checkout -- Packages/packages-lock.json` sau mỗi phiên, như E2E M2c đã làm.
- `-Unlink`: xóa junction, thư mục adapter, `Assets/csc.rsp` (chỉ khi file đó do AgentPlay tạo, nhận ra qua dòng chú thích đầu file), các dòng AgentPlay trong `.git/info/exclude`, và hoàn tác `packages-lock.json`. Thư mục `AgentPlay/` (trace, playbook) được giữ lại, trừ khi có thêm `-Purge`.
- Hành vi mặc định khi không có cờ giữ như M2c, để không phá TileThieves.

---

## 4. Hai mẫu adapter

| | Mẫu asmdef | Mẫu `Assembly-CSharp` |
| --- | --- | --- |
| Khi nào | Code game nằm trong asmdef (TileThieves) | Code game không có asmdef (PetRPG) |
| Vị trí | `Assets/AgentPlayAdapter/` với asmdef riêng tham chiếu asmdef của game | `Assets/AgentPlayAdapter/` không có asmdef |
| Chỉ biên dịch khi có package | `defineConstraints` cộng `versionDefines` trên `com.agentplay.core` | Mọi file bọc trong `#if AGENTPLAY`; symbol đặt bằng `Assets/csc.rsp` (`-define:AGENTPLAY`), file này cũng chỉ trên máy |
| Phần Editor (hook save) | Asmdef Editor riêng | Thư mục `Assets/AgentPlayAdapter/Editor/` |
| Gỡ junction mà quên gỡ adapter | Asmdef bị loại, game biên dịch | `csc.rsp` vẫn còn thì lỗi biên dịch; `-Unlink` xóa cả hai cùng lúc. Nếu chỉ xóa `csc.rsp`, code bọc `#if` bị bỏ qua và game vẫn biên dịch |

Cả hai mẫu: adapter là một `MonoBehaviour` tự tạo bằng `[RuntimeInitializeOnLoadMethod]` (chỉ trong Editor), đọc trạng thái qua API công khai của game, chuyển thay đổi trạng thái thành sự kiện, và không có file game nào phụ thuộc vào nó.

---

## 5. Kiểm chứng trên PetRPG

Quy trình chạy:
1. Ghi lại `git status` và băm nội dung thư mục save (`Application.persistentDataPath/LocalStorage/saves/`) trước khi bắt đầu.
2. Chạy skill trong PetRPG với mục tiêu "hoàn thành 3 nhiệm vụ đầu từ save mới"; người dùng duyệt bảng ở bước 2.
3. Skill đi hết bước 4. Sau đó chạy `Tools/run-adapter-batch.sh <game> 3` (mới, gọi model thật). Script này chạy 3 lần Opus theo lượt bằng AutoPlay chung và in số lần thắng, số lượt gọi và số nhiệm vụ đã xong ở mỗi lần.
4. So khớp băm save và `git status` với bước 1.
5. Chạy `-Unlink`, biên dịch PetRPG trong batchmode, kiểm `git status`.

Kết quả ghi vào `docs/notes/m4b-verification.md` (tiếng Việt), như các mốc trước.

Các điểm đã biết về PetRPG từ khảo sát sơ bộ (skill sẽ tự xác nhận lại):
- Nhiệm vụ là ScriptableObject `MissionData` với các mục tiêu `MissionGoal` (`Collect`, `Purchase`, `Upgrade`, `BuildingTrigger`, `Kill`, ...). `MissionManager` có `GetActiveMissions()`, `MissionCompleteCount()`, `IsMissionCompleted(id)`.
- Người chơi di chuyển bằng joystick UI tự viết (`GTJoyStick`, `GTFloatingJoyStick`) qua `MovementController`. Game có mũi tên dẫn đường tới mục tiêu nhiệm vụ.
- Save là JSON theo từng phần trong `GameSaveManager`, ghi tại `Application.persistentDataPath/LocalStorage/saves/<slot>`. CLAUDE.md của PetRPG cấm phá save, nên hook save phải sao lưu toàn bộ thư mục save trước khi chạy và khôi phục sau khi chạy.
- Game có nhiều SDK (quảng cáo, Firebase, Adjust). Adapter không gọi tới chúng. Nếu chúng chặn luồng chơi trong Editor (ví dụ popup quảng cáo hoặc màn hình đồng ý), skill phải ghi nhận và xử lý trong hook, không sửa code game.

---

## 6. Test

- **Runner (`dotnet test`):** `check` được tách thành lớp thuần `AdapterCheck` nhận `hello` và hai snapshot, trả danh sách mục `PASS`/`FAIL`. Có test cho từng mục hỏng và cho trường hợp đạt. Phân tích tham số dòng lệnh của `check` có test trong `RunnerOptionsTests`.
- **Unity (`Tools/unity-tests.sh`):** test EditMode cho phần quyết định của AutoPlay (thắng/thua/hết giờ ra mã nào, hook được gọi theo thứ tự, hook lỗi ra mã 2 và vẫn gọi `AfterPlay`), viết sao cho không cần vào Play mode thật.
- **E2E không gọi model:** Heist chạy qua AutoPlay chung trong `e2e-m3-reflex` và `e2e-m4a-realtime`. Thêm `Tools/e2e-m4b-check.sh`: chạy `check` trên Heist, mong đợi mã 0. Các trường hợp hỏng (observe luôn null, thiếu `game.won`, không có action `Safe`) được phủ bằng test đơn vị của `AdapterCheck`, không bằng E2E, vì thêm observe hỏng vào Testbed sẽ làm đổi `hello` mà các verifier khác dựa vào.
- **`link-agentplay.ps1`:** `Tools/test-link-agentplay.sh` có sẵn được mở rộng: trong một repo git tạm, `-LocalOnly` không làm đổi `git status`, và `-Unlink` trả repo về đúng trạng thái ban đầu.
- **Skill:** kiểm bằng chính lần chạy trên PetRPG (mục 5). Trước đó, chạy thử skill một lần trên một git worktree tách từ nhánh `main` của TileThieves (chưa có adapter; mẫu asmdef; mục tiêu "cạy đủ 6 viên gạch trong `Prototype_Pry`", đã biết là làm được), để bắt lỗi trong `SKILL.md` trên game nhỏ. Worktree nằm ngoài thư mục làm việc của người dùng và bị xóa sau khi chạy. Heist không dùng được cho việc này vì attribute của nó nằm ngay trong code game.

---

## 7. Rủi ro và điều chưa kiểm chứng

1. **PetRPG có chạy được trong Editor từ save mới mà không vướng SDK không?** Màn hình đồng ý (UMP), quảng cáo hay Firebase có thể chặn hoặc ném lỗi khi vào Play mode. Kiểm đầu tiên trong plan, trước khi viết skill.
2. **Joystick UI tự viết có nhận giá trị đặt từ code không?** Nếu `GTJoyStick` chỉ đọc từ sự kiện chạm, action di chuyển phải đi qua `MovementController` ở mức thấp nhất có thể. Đây vẫn là "lối input của người chơi" nếu nút UI cũng đi qua đúng hàm đó; skill ghi rõ lựa chọn này trong bảng đề xuất.
3. **Ba nhiệm vụ đầu có thể có bước chỉ làm được bằng UI** (mở popup, bấm nâng cấp). Action khi đó gọi đúng hàm mà nút UI gọi. Nếu nhiệm vụ đòi xem quảng cáo (`MissionType.Ad`), mục tiêu được đổi thành 3 nhiệm vụ đầu không thuộc loại `Ad`, và điều này được ghi trong notes.
4. **`Assets/csc.rsp` áp dụng cho mọi assembly trong project,** kể cả asmdef của SDK. Symbol `AGENTPLAY` không được trùng với symbol nào sẵn có trong PetRPG; plan kiểm bằng grep trước khi dùng.
5. **Nhập lại asset của PetRPG khi mở bằng Unity batchmode** có thể rất lâu. Nếu dài hơn vài phút, các bước kiểm của skill chạy trên Editor đang mở qua MCP for Unity (PetRPG đã cài sẵn), thay vì batchmode.
