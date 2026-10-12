# AgentPlay M2a (Động cơ) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Runner gửi kế hoạch (Plan) cho Unity, Unity chạy Plan từng frame với luật ứng phó, và một game demo "trộm nhỏ" được thắng trọn vẹn bằng chuỗi Plan định sẵn, không gọi model.

**Architecture:** Contract có thêm Plan DSL, parser và bộ đánh giá điều kiện, và `PlanValidator`. Cả ba dùng chung cho Unity và runner. Runtime có thêm `ExecutorCore` (lõi thuần, test được không cần Play mode), `ActionInvoker` (gọi action bằng reflection) và `AgentExecutorHost` (MonoBehaviour gọi executor mỗi frame sau script game). `SessionCore` nhận message `plan` và quyết định dừng theo lượt khi Plan kết thúc hoặc có sự kiện quan trọng mà không luật nào bắt. Runner có giao diện `IBrain`, bản đầu là `ScriptBrain` đọc chuỗi Plan từ file JSON. Game demo nằm trong project Unity riêng `Demos/Heist`.

**Tech Stack:** Unity 6000.0.77f1 (C# 9), Newtonsoft.Json (Unity package 3.2.1, NuGet 13.0.3), NUnit (Unity Test Framework 1.6.0), .NET 9 + xUnit, Python 3.12 cho script kiểm tra E2E, PowerShell 5.1 cho script gắn package.

**Spec:** `docs/superpowers/specs/2026-10-12-agentplay-m2-design.md` (M2a = mục 15 dòng đầu; các mục 2, 3, 4, 5, 6, 7, 10, 12, 13, 14). Spec gốc `docs/superpowers/specs/2026-10-11-agentplay-design.md`.

### Ghi chú so với spec M2 (sẽ đồng bộ vào spec ở Task 12)

- **Game demo nằm ở project riêng `Demos/Heist`**, không ở `Testbed`. Registry quét theo type trên toàn project, nên hai game có `[AgentGame]` trong cùng một project sẽ báo lỗi "More than one [AgentGame]". `Testbed` giữ nguyên cho test package và E2E M1. Có hai project Unity khác nhau dùng chung package cũng chứng minh thêm tính tổng quát.
- **`goto` của demo đi thẳng tới vùng**, không dùng đồ thị điểm đi. Bản đồ là sàn trống, nhân vật di chuyển kinematic.
- **Runner chưa có `--brain opus` và `--brain manual`** (thuộc M2b). Không có `--brain` thì runner chạy như M1.
- **Điều kiện `until` được đánh giá trên snapshot chụp đầu frame**, trước lời gọi action của frame đó.

## Global Constraints

- Code trong `Packages/com.agentplay.core` biên dịch với C# 9: không dùng file-scoped namespace, `global using`, `record`, `required`.
- `Packages/com.agentplay.core/Contract/` không dùng `UnityEngine` và phải build bằng `dotnet build` (`netstandard2.1`, `LangVersion 9.0`).
- Chỉ dùng Newtonsoft.Json cho JSON.
- `ProtocolVersion.Current = 2`. Message mới: `plan`, `plan_status`, `waiting` (Unity gửi runner), `stop_plan` (runner gửi Unity). `hello` có thêm `brief`, `events`, `project_root`. `paused` có thêm `state`.
- Plan: tối đa 12 bước, 8 luật, `timeout_s` trong khoảng (0, 600]. `then` là `resume`, `next`, `replan`, `abort`.
- Trạng thái Plan: `step_done`, `plan_done`, `plan_failed`, `rule_fired`.
- Sự kiện dành riêng: `game.won`, `game.lost`.
- Danh sách observe tối đa 16 phần tử.
- Action `Safe` không được có tham số.
- Package và runner không được nhắc tới tên game cụ thể (`TileThieves`, `Testbed`, `Heist`). `Tools/check-generic.sh` kiểm điều này.
- `AgentPlay/brief.md` ở gốc project game là mô tả game gửi trong `hello.brief`.
- Code, comment và tên biến bằng tiếng Anh. Số trong log dùng `CultureInfo.InvariantCulture`.
- Mọi commit kết thúc bằng (trong các bước commit, `<trailers>` nghĩa là đúng hai dòng này):
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
  `Claude-Session: https://claude.ai/code/session_01XTqvn3xKuC12ugWwPzQueZ`
  Dùng hai cờ `-m`: một cho tiêu đề, một cho hai dòng trailer.
- Commit `.meta` Unity sinh ra cùng file của nó.
- Không mở `Testbed` hay `Demos/Heist` trong Editor khác khi chạy script test.

## Review Focus

1. **Plan không hợp lệ tới đúng lúc game đang dừng** (trường hợp bình thường của chế độ theo lượt): runner nhận `plan_status plan_failed` với lý do `invalid: ...`, và `resume` gửi ngay sau đó không được làm game chạy khi không có Plan. Test ở Task 8.
2. **Sự kiện quan trọng được một luật của Plan bắt trong cùng frame**: không dừng game. Không luật nào bắt thì dừng. Test ở Task 8.
3. **Action ném exception hoặc tham số sai kiểu giữa chừng Plan**: Plan hỏng với lý do rõ ràng, game không ném exception ra Console, executor chuyển sang hành vi an toàn. Test ở Task 6 và Task 7.
4. **Điều kiện tham chiếu observe đang null, không tồn tại, hoặc danh sách rỗng**: trả sai, không ném exception. Test ở Task 3.
5. **Domain reload hoặc thoát Play mode khi Plan đang chạy**: host executor bị hủy, không có `NullReferenceException`, phiên mới bắt đầu sạch. Kiểm bằng E2E M1 (có reload) ở Task 9 và E2E heist ở Task 12.

---

## Cấu trúc file

```
Packages/com.agentplay.core/
  Contract/
    Protocol/MessageTypes.cs          (thay) thêm message, trạng thái, sự kiện dành riêng, ValueKinds.List
    Protocol/Messages.cs              (thay) hello v2, PausedMessage.State, PlanMessage, PlanStatusMessage, WaitingMessage, EventInfo
    Plans/Plan.cs                     Plan, PlanStep, PlanRule, RuleThen, PlanLimits
    Plans/PlanValidator.cs
    Conditions/ConditionAst.cs        các node
    Conditions/ConditionParser.cs
    Conditions/ConditionEvaluator.cs  + IConditionContext, SnapshotContext
    Conditions/ConditionSchema.cs     kiểm kiểu điều kiện theo hello
  Runtime/
    Attributes.cs                     (thay) thêm AgentObserveList, AgentEventInfo, AgentActionContext
    Registry/AgentRegistry.cs         (thay) list observe, event info, context param, Safe không tham số
    Registry/TypeScanner.cs           (sửa) IsAnnotated nhận thêm hai attribute mới
    Registry/SnapshotBuilder.cs       (thay) đọc list observe
    Execution/ActionInvoker.cs        IActionRunner, ActionInvoker, AgentActionException
    Execution/ExecutorCore.cs         ExecutorOutput, ExecutorOutputKinds, ExecutorCore
    Execution/AgentExecutorHost.cs    IPlanExecutor, AgentExecutorHost
    Session/StepPolicy.cs             (thay) PauseReason.Plan, OnPlanEnded
    Session/SessionCore.cs            (thay) plan, stop_plan, OnFrameEnd
    Trace/TraceWriter.cs              (sửa) WriteControl, WritePlan, WritePlanStatus
  Editor/AgentPlaySessionController.cs (sửa) brief, project_root, executor host
  Tests/EditMode/                     RegistryV2Tests.cs, FakesV2.cs, ActionInvokerTests.cs, ExecutorCoreTests.cs,
                                      SessionCorePlanTests.cs (mới); SessionCoreTests.cs, TraceTests.cs (sửa)
Runner/
  RunnerOptions.cs                    (thay) --brain script <file>
  RunnerSession.cs                    (thay) chế độ brain
  Program.cs                          (thay) gọi brain
  Brains/BrainContext.cs              BrainContext, IBrain
  Brains/ScriptBrain.cs
Runner.Tests/
  Contract/PlanModelTests.cs, ConditionParserTests.cs, ConditionEvaluatorTests.cs, PlanValidatorTests.cs (mới)
  Contract/MessagesTests.cs           (sửa) protocol 2
  RunnerBrainTests.cs                 (mới)
Demos/Heist/                          project Unity mới
  Packages/manifest.json
  Assets/Heist/HeistLevel.cs, HeistGame.cs, HeistPlayer.cs, HeistGuard.cs
  Assets/Editor/HeistAutoPlay.cs
  AgentPlay/brief.md
  AgentPlay/scripts/win.json
Tools/
  check-generic.sh, link-agentplay.ps1, test-link-agentplay.sh, e2e-m2a.sh, verify_m2a.py
docs/notes/m2a-verification.md
```

---

### Task 1: Giao thức v2 và mô hình Plan (Contract)

**Files:**
- Replace: `Packages/com.agentplay.core/Contract/Protocol/MessageTypes.cs`
- Replace: `Packages/com.agentplay.core/Contract/Protocol/Messages.cs`
- Create: `Packages/com.agentplay.core/Contract/Plans/Plan.cs`
- Modify: `Runner.Tests/Contract/MessagesTests.cs` (test `HelloRoundTrips`)
- Test: `Runner.Tests/Contract/PlanModelTests.cs`

**Interfaces:**
- Produces:
  - `ProtocolVersion.Current = 2`.
  - `MessageTypes.Plan = "plan"`, `PlanStatus = "plan_status"`, `Waiting = "waiting"`, `StopPlan = "stop_plan"` (cùng các hằng M1).
  - `ValueKinds.List = "list"`.
  - `PlanStatuses.StepDone/PlanDone/PlanFailed/RuleFired`.
  - `ReservedEvents.GameWon = "game.won"`, `ReservedEvents.GameLost = "game.lost"`.
  - `ObserveInfo.Fields : List<ObserveInfo>` (null nếu không phải danh sách).
  - `EventInfo { string Name; string Description; bool Important; string Outcome; }`.
  - `HelloMessage.Brief : string`, `HelloMessage.Events : List<EventInfo>`, `HelloMessage.ProjectRoot : string`.
  - `PausedMessage.State : JObject`.
  - `PlanMessage { Plan Plan; }`, `PlanStatusMessage { string PlanId; string Status; int Step; string Reason; }`, `WaitingMessage { double T; }`.
  - `Plan { string Id; string Intent; List<PlanStep> Steps; List<PlanRule> Rules; double TimeoutSeconds; }`, `PlanStep { string Do; JObject Args; string Until; }`, `PlanRule { string When; string Do; JObject Args; string Then; }`.
  - `RuleThen.Resume/Next/Replan/Abort`, `RuleThen.IsKnown(string)`.
  - `PlanLimits.MaxSteps = 12`, `MaxRules = 8`, `MaxTimeoutSeconds = 600`.

- [ ] **Step 1: Viết test thất bại**

Trong `Runner.Tests/Contract/MessagesTests.cs`, test `HelloRoundTrips`, đổi dòng `Assert.Equal(1, back.Protocol);` thành:

```csharp
            Assert.Equal(ProtocolVersion.Current, back.Protocol);
```

Tạo `Runner.Tests/Contract/PlanModelTests.cs`:

```csharp
using System.Collections.Generic;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class PlanModelTests
    {
        const string PlanJson =
            "{\"id\":\"p-1\",\"intent\":\"grab\",\"timeout_s\":30," +
            "\"steps\":[{\"do\":\"goto\",\"args\":{\"zone\":\"item_a\"},\"until\":\"x > 1\"}]," +
            "\"rules\":[{\"when\":\"event:guard.spotted\",\"do\":\"hide\",\"args\":{\"zone\":\"cover_1\"},\"then\":\"replan\"}]}";

        [Fact]
        public void PlanParsesFromSnakeCaseJson()
        {
            var plan = JObject.Parse(PlanJson).ToObject<Plan>(ContractJson.Serializer);
            Assert.Equal("p-1", plan.Id);
            Assert.Equal(30.0, plan.TimeoutSeconds);
            Assert.Equal("goto", plan.Steps[0].Do);
            Assert.Equal("item_a", (string)plan.Steps[0].Args["zone"]);
            Assert.Equal("x > 1", plan.Steps[0].Until);
            Assert.Equal("replan", plan.Rules[0].Then);
            Assert.Equal("cover_1", (string)plan.Rules[0].Args["zone"]);
        }

        [Fact]
        public void PlanMessageRoundTrips()
        {
            var plan = JObject.Parse(PlanJson).ToObject<Plan>(ContractJson.Serializer);
            var line = MessageCodec.Encode(MessageTypes.Plan, 3, new PlanMessage { Plan = plan });
            Assert.Contains("\"timeout_s\":30", line);
            var back = MessageCodec.Decode(line).As<PlanMessage>().Plan;
            Assert.Equal("hide", back.Rules[0].Do);
        }

        [Fact]
        public void PlanDefaultsToEmptyListsAndSixtySeconds()
        {
            var plan = JObject.Parse("{\"id\":\"p\"}").ToObject<Plan>(ContractJson.Serializer);
            Assert.Empty(plan.Steps);
            Assert.Empty(plan.Rules);
            Assert.Equal(60.0, plan.TimeoutSeconds);
        }

        [Fact]
        public void HelloV2CarriesBriefEventsAndListFields()
        {
            var hello = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G", Brief = "Steal things", ProjectRoot = "C:/Game" };
            hello.Observes.Add(new ObserveInfo
            {
                Path = "guards",
                ValueType = ValueKinds.List,
                Fields = new List<ObserveInfo> { new ObserveInfo { Path = "distance", ValueType = ValueKinds.Float } },
            });
            hello.Events.Add(new EventInfo { Name = "guard.spotted", Important = true, Outcome = "bad" });

            var back = MessageCodec.Decode(MessageCodec.Encode(MessageTypes.Hello, 1, hello)).As<HelloMessage>();

            Assert.Equal(2, back.Protocol);
            Assert.Equal("Steal things", back.Brief);
            Assert.Equal("C:/Game", back.ProjectRoot);
            Assert.Equal("distance", back.Observes[0].Fields[0].Path);
            Assert.True(back.Events[0].Important);
        }

        [Fact]
        public void PausedCarriesState()
        {
            var line = MessageCodec.Encode(MessageTypes.Paused, 1, new PausedMessage { Reason = "plan", T = 2, State = new JObject { ["x"] = 1 } });
            Assert.Equal(1, (int)MessageCodec.Decode(line).As<PausedMessage>().State["x"]);
        }

        [Fact]
        public void PlanStatusUsesSnakeCase()
        {
            var line = MessageCodec.Encode(MessageTypes.PlanStatus, 1,
                new PlanStatusMessage { PlanId = "p", Status = PlanStatuses.PlanFailed, Step = 2, Reason = "timeout" });
            var o = JObject.Parse(line);
            Assert.Equal("p", (string)o["plan_id"]);
            Assert.Equal("plan_failed", (string)o["status"]);
            Assert.Equal(2, (int)o["step"]);
        }

        [Theory]
        [InlineData("resume", true)]
        [InlineData("next", true)]
        [InlineData("replan", true)]
        [InlineData("abort", true)]
        [InlineData("retry", false)]
        [InlineData(null, false)]
        public void RuleThenKnowsFourValues(string value, bool known)
        {
            Assert.Equal(known, RuleThen.IsKnown(value));
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: build FAIL với `The type or namespace name 'Plan' could not be found`.

- [ ] **Step 3: Viết code**

Thay toàn bộ `Packages/com.agentplay.core/Contract/Protocol/MessageTypes.cs`:

```csharp
namespace AgentPlay.Contract
{
    public static class ProtocolVersion
    {
        public const int Current = 2;
    }

    public static class MessageTypes
    {
        // Unity -> runner
        public const string Hello = "hello";
        public const string State = "state";
        public const string Event = "event";
        public const string Paused = "paused";
        public const string Error = "error";
        public const string PlanStatus = "plan_status";
        public const string Waiting = "waiting";
        // Runner -> Unity
        public const string SetMode = "set_mode";
        public const string Resume = "resume";
        public const string GetState = "get_state";
        public const string Plan = "plan";
        public const string StopPlan = "stop_plan";
    }

    public static class Modes
    {
        public const string TurnBased = "turn_based";
        public const string Realtime = "realtime";

        public static bool IsKnown(string mode) => mode == TurnBased || mode == Realtime;
    }

    public static class ValueKinds
    {
        public const string Float = "float";
        public const string Int = "int";
        public const string Bool = "bool";
        public const string String = "string";
        public const string Enum = "enum";
        public const string Vector3 = "vector3";
        public const string List = "list";
    }

    public static class PlanStatuses
    {
        public const string StepDone = "step_done";
        public const string PlanDone = "plan_done";
        public const string PlanFailed = "plan_failed";
        public const string RuleFired = "rule_fired";
    }

    /// <summary>Event names every game uses to end a round; the runner stops asking the model after either.</summary>
    public static class ReservedEvents
    {
        public const string GameWon = "game.won";
        public const string GameLost = "game.lost";
    }
}
```

Thay toàn bộ `Packages/com.agentplay.core/Contract/Protocol/Messages.cs`:

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Contract
{
    public sealed class ObserveInfo
    {
        [JsonProperty("path")] public string Path;
        [JsonProperty("type")] public string ValueType;
        [JsonProperty("description")] public string Description;
        /// <summary>Element fields when ValueType is "list"; null otherwise.</summary>
        [JsonProperty("fields")] public List<ObserveInfo> Fields;
    }

    public sealed class ActionParamInfo
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("type")] public string ValueType;
    }

    public sealed class ActionInfo
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;
        [JsonProperty("safe")] public bool Safe;
        [JsonProperty("params")] public List<ActionParamInfo> Params = new List<ActionParamInfo>();
    }

    public sealed class EventInfo
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;
        [JsonProperty("important")] public bool Important;
        /// <summary>"neutral", "good" or "bad".</summary>
        [JsonProperty("outcome")] public string Outcome;
    }

    public sealed class HelloMessage
    {
        [JsonProperty("protocol")] public int Protocol;
        [JsonProperty("game")] public string Game;
        [JsonProperty("version")] public string Version;
        /// <summary>Contents of AgentPlay/brief.md in the game project; empty when missing.</summary>
        [JsonProperty("brief")] public string Brief = "";
        [JsonProperty("project_root")] public string ProjectRoot;
        [JsonProperty("observes")] public List<ObserveInfo> Observes = new List<ObserveInfo>();
        [JsonProperty("actions")] public List<ActionInfo> Actions = new List<ActionInfo>();
        [JsonProperty("events")] public List<EventInfo> Events = new List<EventInfo>();
        [JsonProperty("modes")] public List<string> Modes = new List<string>();
    }

    public sealed class EventMessage
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("t")] public double T;
        [JsonProperty("frame")] public long Frame;
        [JsonProperty("important")] public bool Important;
        [JsonProperty("outcome")] public string Outcome;
        [JsonProperty("payload")] public JObject Payload;
        [JsonProperty("state")] public JObject State;
    }

    public sealed class StateMessage
    {
        [JsonProperty("t")] public double T;
        [JsonProperty("frame")] public long Frame;
        [JsonProperty("state")] public JObject State;
    }

    public sealed class PausedMessage
    {
        /// <summary>"mode", "event", "budget" or "plan".</summary>
        [JsonProperty("reason")] public string Reason;
        [JsonProperty("t")] public double T;
        [JsonProperty("state")] public JObject State;
    }

    public sealed class SetModeMessage
    {
        [JsonProperty("mode")] public string Mode;
    }

    public sealed class ResumeMessage
    {
        /// <summary>Game seconds to run before pausing again; 0 or less means until the next stop condition.</summary>
        [JsonProperty("max_game_seconds")] public double MaxGameSeconds;
    }

    public sealed class ErrorMessage
    {
        [JsonProperty("message")] public string Message;
    }

    public sealed class PlanMessage
    {
        [JsonProperty("plan")] public Plan Plan;
    }

    public sealed class PlanStatusMessage
    {
        [JsonProperty("plan_id")] public string PlanId;
        [JsonProperty("status")] public string Status;
        [JsonProperty("step")] public int Step;
        [JsonProperty("reason")] public string Reason;
    }

    public sealed class WaitingMessage
    {
        [JsonProperty("t")] public double T;
    }
}
```

Tạo `Packages/com.agentplay.core/Contract/Plans/Plan.cs`:

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Contract
{
    public sealed class Plan
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("intent")] public string Intent;
        [JsonProperty("steps")] public List<PlanStep> Steps = new List<PlanStep>();
        [JsonProperty("rules")] public List<PlanRule> Rules = new List<PlanRule>();
        [JsonProperty("timeout_s")] public double TimeoutSeconds = 60;
    }

    public sealed class PlanStep
    {
        [JsonProperty("do")] public string Do;
        [JsonProperty("args")] public JObject Args;
        /// <summary>Optional condition; when true the step ends even if the action is still running.</summary>
        [JsonProperty("until")] public string Until;
    }

    public sealed class PlanRule
    {
        [JsonProperty("when")] public string When;
        [JsonProperty("do")] public string Do;
        [JsonProperty("args")] public JObject Args;
        [JsonProperty("then")] public string Then;
    }

    public static class RuleThen
    {
        public const string Resume = "resume";
        public const string Next = "next";
        public const string Replan = "replan";
        public const string Abort = "abort";

        public static bool IsKnown(string value) => value == Resume || value == Next || value == Replan || value == Abort;
    }

    public static class PlanLimits
    {
        public const int MaxSteps = 12;
        public const int MaxRules = 8;
        public const double MaxTimeoutSeconds = 600;
    }
}
```

- [ ] **Step 4: Chạy test để thấy pass**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: PASS, `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add Packages Runner.Tests
git commit -m "feat(contract): protocol v2 messages and plan model" -m "<trailers>"
```

---

### Task 2: Parser điều kiện (Contract)

**Files:**
- Create: `Packages/com.agentplay.core/Contract/Conditions/ConditionAst.cs`
- Create: `Packages/com.agentplay.core/Contract/Conditions/ConditionParser.cs`
- Test: `Runner.Tests/Contract/ConditionParserTests.cs`

**Interfaces:**
- Produces:
  - `abstract class CondNode`; `LiteralNode(object value)` với `Value` là `double`, `bool` hoặc `string`; `EventNode(string name)` với `Name`; `PathNode(string path)` với `Path`; `CallNode(string function, string path)` với `Function`, `Path`; `NotNode(CondNode operand)` với `Operand`; `BinaryNode(string op, CondNode left, CondNode right)` với `Op`, `Left`, `Right`. `ToString()` in dạng chuẩn có ngoặc.
  - `ConditionParser.Parse(string text) : CondNode`, ném `ConditionException` (có `Position`).
  - `ConditionParser.Functions = { "min", "max", "count", "any" }`.

Ngữ pháp theo spec M2 mục 5. Tên hàm là từ khóa, không dùng làm tên path. Path: các đoạn chữ thường `[a-z_][a-z0-9_]*` nối bằng dấu chấm. Tên sự kiện: chữ thường, số, `_`, `.`.

- [ ] **Step 1: Viết test thất bại**

`Runner.Tests/Contract/ConditionParserTests.cs`:

```csharp
using AgentPlay.Contract;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class ConditionParserTests
    {
        [Theory]
        [InlineData("a.b < 3", "(a.b < 3)")]
        [InlineData("a || b && !c", "(a || (b && !(c)))")]
        [InlineData("(a || b) && c", "((a || b) && c)")]
        [InlineData("min(guards.distance) >= 2.5", "(min(guards.distance) >= 2.5)")]
        [InlineData("event:guard.spotted && count(guards) == 0", "(event:guard.spotted && (count(guards) == 0))")]
        [InlineData("player.zone == 'exit'", "(player.zone == 'exit')")]
        [InlineData("-1.5 < x", "(-1.5 < x)")]
        [InlineData("a != b", "(a != b)")]
        [InlineData("!a", "!(a)")]
        [InlineData("  any( guards.sees_player )  ", "any(guards.sees_player)")]
        [InlineData("true || false", "(true || false)")]
        [InlineData("x_1 <= 0.25", "(x_1 <= 0.25)")]
        public void ParsesToCanonicalForm(string text, string expected)
        {
            Assert.Equal(expected, ConditionParser.Parse(text).ToString());
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("a <")]
        [InlineData("a < < b")]
        [InlineData("min(")]
        [InlineData("min(a")]
        [InlineData("min a")]
        [InlineData("'abc")]
        [InlineData("event:")]
        [InlineData("Player.X > 1")]
        [InlineData("a b")]
        [InlineData("foo(a)")]
        [InlineData("1.2.3 > 0")]
        [InlineData("a..b > 1")]
        [InlineData("(a > 1")]
        [InlineData("a < b < c")]
        [InlineData("a # b")]
        public void RejectsInvalidText(string text)
        {
            Assert.Throws<ConditionException>(() => ConditionParser.Parse(text));
        }

        [Fact]
        public void ErrorReportsPosition()
        {
            var e = Assert.Throws<ConditionException>(() => ConditionParser.Parse("a > 1 && #"));
            Assert.Equal(9, e.Position);
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: build FAIL với `The name 'ConditionParser' does not exist in the current context`.

- [ ] **Step 3: Viết code**

`Packages/com.agentplay.core/Contract/Conditions/ConditionAst.cs`:

```csharp
using System;
using System.Globalization;

namespace AgentPlay.Contract
{
    public abstract class CondNode
    {
    }

    public sealed class LiteralNode : CondNode
    {
        /// <summary>double, bool or string.</summary>
        public readonly object Value;

        public LiteralNode(object value)
        {
            Value = value;
        }

        public override string ToString()
        {
            switch (Value)
            {
                case double d: return d.ToString("R", CultureInfo.InvariantCulture);
                case bool b: return b ? "true" : "false";
                default: return "'" + Value + "'";
            }
        }
    }

    public sealed class EventNode : CondNode
    {
        public readonly string Name;

        public EventNode(string name)
        {
            Name = name;
        }

        public override string ToString() => "event:" + Name;
    }

    public sealed class PathNode : CondNode
    {
        public readonly string Path;

        public PathNode(string path)
        {
            Path = path;
        }

        public override string ToString() => Path;
    }

    public sealed class CallNode : CondNode
    {
        public readonly string Function;
        public readonly string Path;

        public CallNode(string function, string path)
        {
            Function = function;
            Path = path;
        }

        public override string ToString() => Function + "(" + Path + ")";
    }

    public sealed class NotNode : CondNode
    {
        public readonly CondNode Operand;

        public NotNode(CondNode operand)
        {
            Operand = operand;
        }

        public override string ToString() => "!(" + Operand + ")";
    }

    public sealed class BinaryNode : CondNode
    {
        /// <summary>"&amp;&amp;", "||", "&lt;", "&lt;=", "&gt;", "&gt;=", "==" or "!=".</summary>
        public readonly string Op;
        public readonly CondNode Left;
        public readonly CondNode Right;

        public BinaryNode(string op, CondNode left, CondNode right)
        {
            Op = op;
            Left = left;
            Right = right;
        }

        public override string ToString() => "(" + Left + " " + Op + " " + Right + ")";
    }

    public sealed class ConditionException : Exception
    {
        public int Position { get; }

        public ConditionException(string message, int position) : base(message + " at " + position)
        {
            Position = position;
        }
    }
}
```

`Packages/com.agentplay.core/Contract/Conditions/ConditionParser.cs`:

```csharp
using System;
using System.Globalization;

namespace AgentPlay.Contract
{
    /// <summary>Recursive-descent parser for plan conditions (spec M2 section 5).</summary>
    public static class ConditionParser
    {
        public static readonly string[] Functions = { "min", "max", "count", "any" };

        static readonly string[] CompareOps = { "<=", ">=", "==", "!=", "<", ">" };

        public static CondNode Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ConditionException("Empty condition", 0);
            var p = new Impl(text);
            var node = p.ParseOr();
            p.SkipWhitespace();
            if (!p.AtEnd) throw new ConditionException("Unexpected '" + p.Current + "'", p.Index);
            return node;
        }

        sealed class Impl
        {
            readonly string _s;
            public int Index;

            public Impl(string s)
            {
                _s = s;
            }

            public bool AtEnd => Index >= _s.Length;
            public char Current => _s[Index];

            char At(int offset) => Index + offset < _s.Length ? _s[Index + offset] : '\0';

            public void SkipWhitespace()
            {
                while (!AtEnd && char.IsWhiteSpace(Current)) Index++;
            }

            bool Match(string token)
            {
                SkipWhitespace();
                if (Index + token.Length > _s.Length) return false;
                if (string.CompareOrdinal(_s, Index, token, 0, token.Length) != 0) return false;
                Index += token.Length;
                return true;
            }

            public CondNode ParseOr()
            {
                var left = ParseAnd();
                while (Match("||")) left = new BinaryNode("||", left, ParseAnd());
                return left;
            }

            CondNode ParseAnd()
            {
                var left = ParseUnary();
                while (Match("&&")) left = new BinaryNode("&&", left, ParseUnary());
                return left;
            }

            CondNode ParseUnary()
            {
                SkipWhitespace();
                if (!AtEnd && Current == '!' && At(1) != '=')
                {
                    Index++;
                    return new NotNode(ParseUnary());
                }
                return ParseCompare();
            }

            CondNode ParseCompare()
            {
                var left = ParseTerm();
                foreach (var op in CompareOps)
                    if (Match(op)) return new BinaryNode(op, left, ParseTerm());
                return left;
            }

            CondNode ParseTerm()
            {
                SkipWhitespace();
                if (AtEnd) throw new ConditionException("Expected a value", Index);
                char c = Current;
                if (c == '(')
                {
                    Index++;
                    var inner = ParseOr();
                    if (!Match(")")) throw new ConditionException("Expected ')'", Index);
                    return inner;
                }
                if (c == '\'') return ParseString();
                if (char.IsDigit(c) || (c == '-' && (char.IsDigit(At(1)) || At(1) == '.')) || (c == '.' && char.IsDigit(At(1))))
                    return ParseNumber();
                if (IsIdentStart(c)) return ParseWord();
                throw new ConditionException("Unexpected '" + c + "'", Index);
            }

            CondNode ParseString()
            {
                int start = Index;
                Index++;
                int end = _s.IndexOf('\'', Index);
                if (end < 0) throw new ConditionException("Unterminated string", start);
                var value = _s.Substring(Index, end - Index);
                Index = end + 1;
                return new LiteralNode(value);
            }

            CondNode ParseNumber()
            {
                int start = Index;
                if (Current == '-') Index++;
                while (!AtEnd && (char.IsDigit(Current) || Current == '.')) Index++;
                var text = _s.Substring(start, Index - start);
                if (!double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d))
                    throw new ConditionException("Invalid number '" + text + "'", start);
                return new LiteralNode(d);
            }

            CondNode ParseWord()
            {
                int start = Index;
                string word = ReadWord();
                if (word == "true") return new LiteralNode(true);
                if (word == "false") return new LiteralNode(false);
                if (word == "event" && !AtEnd && Current == ':')
                {
                    Index++;
                    int nameStart = Index;
                    while (!AtEnd && IsEventChar(Current)) Index++;
                    if (Index == nameStart) throw new ConditionException("Expected an event name", nameStart);
                    return new EventNode(_s.Substring(nameStart, Index - nameStart));
                }
                if (Array.IndexOf(Functions, word) >= 0)
                {
                    if (!Match("(")) throw new ConditionException("Expected '(' after " + word, Index);
                    SkipWhitespace();
                    int pathStart = Index;
                    var path = ReadWord();
                    CheckPath(path, pathStart);
                    if (!Match(")")) throw new ConditionException("Expected ')'", Index);
                    return new CallNode(word, path);
                }
                CheckPath(word, start);
                return new PathNode(word);
            }

            string ReadWord()
            {
                int start = Index;
                while (!AtEnd && IsWordChar(Current)) Index++;
                return _s.Substring(start, Index - start);
            }

            static bool IsIdentStart(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
            static bool IsWordChar(char c) => IsIdentStart(c) || char.IsDigit(c) || c == '.';
            static bool IsEventChar(char c) => (c >= 'a' && c <= 'z') || char.IsDigit(c) || c == '_' || c == '.';

            static void CheckPath(string path, int position)
            {
                bool ok = path.Length > 0;
                if (ok)
                {
                    foreach (var segment in path.Split('.'))
                    {
                        if (segment.Length == 0 || !(segment[0] == '_' || (segment[0] >= 'a' && segment[0] <= 'z')))
                        {
                            ok = false;
                            break;
                        }
                        foreach (var ch in segment)
                        {
                            if (!(ch == '_' || char.IsDigit(ch) || (ch >= 'a' && ch <= 'z')))
                            {
                                ok = false;
                                break;
                            }
                        }
                        if (!ok) break;
                    }
                }
                if (!ok) throw new ConditionException("Invalid path '" + path + "' (use lowercase segments separated by dots)", position);
            }
        }
    }
}
```

- [ ] **Step 4: Chạy test để thấy pass**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: PASS, `Failed: 0`.

`ErrorReportsPosition`: chuỗi `"a > 1 && #"` có `#` ở chỉ số 9; `ParseTerm` ném `Unexpected '#'` tại đó.

- [ ] **Step 5: Commit**

```bash
git add Packages Runner.Tests
git commit -m "feat(contract): condition language parser" -m "<trailers>"
```

---

### Task 3: Bộ đánh giá điều kiện (Contract)

**Files:**
- Create: `Packages/com.agentplay.core/Contract/Conditions/ConditionEvaluator.cs`
- Test: `Runner.Tests/Contract/ConditionEvaluatorTests.cs`

**Interfaces:**
- Consumes: các node của Task 2.
- Produces:
  - `interface IConditionContext { JToken Lookup(string key); bool HasEvent(string name); }`
  - `SnapshotContext(JObject snapshot, ICollection<string> events)` cài đặt `IConditionContext`.
  - `ConditionEvaluator.Evaluate(CondNode node, IConditionContext context) : bool`.
  - `ConditionEvaluator.FindList(IConditionContext context, string path, out JArray list, out string field) : bool`.

Ngữ nghĩa: snapshot là object phẳng, khóa là path đầy đủ (ví dụ `"player.zone"`). Danh sách là một khóa có giá trị `JArray` các `JObject`. Path trong hàm danh sách tìm tiền tố dài nhất là khóa danh sách; phần còn lại là tên trường. Giá trị không xác định (null, thiếu, mảng, sai kiểu) làm phép so sánh trả sai; `!` của giá trị không xác định cũng là sai.

- [ ] **Step 1: Viết test thất bại**

`Runner.Tests/Contract/ConditionEvaluatorTests.cs`:

```csharp
using System.Collections.Generic;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class ConditionEvaluatorTests
    {
        static readonly JObject Snapshot = JObject.Parse(
            "{\"noise.value\":40,\"player.zone\":\"exit\",\"player.hidden\":true,\"maybe\":null,\"pos\":[1,2,3]," +
            "\"guards\":[{\"distance\":6.5,\"sees_player\":false},{\"distance\":2.0,\"sees_player\":true}]," +
            "\"empty\":[]}");

        static bool Eval(string text, params string[] events) =>
            ConditionEvaluator.Evaluate(ConditionParser.Parse(text), new SnapshotContext(Snapshot, new HashSet<string>(events)));

        [Theory]
        [InlineData("noise.value > 30", true)]
        [InlineData("noise.value >= 40 && noise.value <= 40", true)]
        [InlineData("noise.value < 40", false)]
        [InlineData("player.zone == 'exit'", true)]
        [InlineData("player.zone != 'start'", true)]
        [InlineData("player.hidden", true)]
        [InlineData("!player.hidden", false)]
        [InlineData("player.hidden == true", true)]
        [InlineData("min(guards.distance) < 3", true)]
        [InlineData("max(guards.distance) > 6", true)]
        [InlineData("count(guards) == 2", true)]
        [InlineData("count(guards.sees_player) == 1", true)]
        [InlineData("any(guards.sees_player)", true)]
        [InlineData("any(guards)", true)]
        [InlineData("missing.path > 0", false)]
        [InlineData("!(missing.path > 0)", true)]
        [InlineData("!missing.path", false)]
        [InlineData("maybe == 1", false)]
        [InlineData("pos > 1", false)]
        [InlineData("player.zone > 1", false)]
        [InlineData("noise.value > 'a'", false)]
        [InlineData("min(empty.d) < 1000", false)]
        [InlineData("max(empty.d) > -1000", false)]
        [InlineData("min(guards) < 3", false)]
        [InlineData("any(empty)", false)]
        [InlineData("count(empty) == 0", true)]
        [InlineData("count(nothing) == 0", false)]
        [InlineData("true || missing > 1", true)]
        [InlineData("false && true", false)]
        public void EvaluatesAgainstSnapshot(string text, bool expected)
        {
            Assert.Equal(expected, Eval(text));
        }

        [Fact]
        public void EventIsTrueOnlyWhenEmittedThisFrame()
        {
            Assert.True(Eval("event:guard.spotted", "guard.spotted"));
            Assert.False(Eval("event:guard.spotted", "other"));
            Assert.False(Eval("event:guard.spotted"));
        }

        [Fact]
        public void NullSnapshotAndNullEventsAreTreatedAsEmpty()
        {
            var ctx = new SnapshotContext(null, null);
            Assert.False(ConditionEvaluator.Evaluate(ConditionParser.Parse("x > 1"), ctx));
            Assert.False(ConditionEvaluator.Evaluate(ConditionParser.Parse("event:a"), ctx));
        }

        [Fact]
        public void FindListSplitsLongestListPrefix()
        {
            var ctx = new SnapshotContext(Snapshot, null);
            Assert.True(ConditionEvaluator.FindList(ctx, "guards.distance", out var list, out var field));
            Assert.Equal(2, list.Count);
            Assert.Equal("distance", field);
            Assert.True(ConditionEvaluator.FindList(ctx, "guards", out _, out var none));
            Assert.Null(none);
            Assert.False(ConditionEvaluator.FindList(ctx, "noise.value", out _, out _));
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: build FAIL với `The type or namespace name 'SnapshotContext' could not be found`.

- [ ] **Step 3: Viết code**

`Packages/com.agentplay.core/Contract/Conditions/ConditionEvaluator.cs`:

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Contract
{
    public interface IConditionContext
    {
        /// <summary>Value of a flat snapshot key, or null.</summary>
        JToken Lookup(string key);
        bool HasEvent(string name);
    }

    public sealed class SnapshotContext : IConditionContext
    {
        readonly JObject _snapshot;
        readonly ICollection<string> _events;

        public SnapshotContext(JObject snapshot, ICollection<string> events)
        {
            _snapshot = snapshot;
            _events = events;
        }

        public JToken Lookup(string key) => _snapshot?[key];

        public bool HasEvent(string name) => _events != null && _events.Contains(name);
    }

    /// <summary>Evaluates parsed conditions. Unknown or mistyped values make comparisons false; it never throws.</summary>
    public static class ConditionEvaluator
    {
        public static bool Evaluate(CondNode node, IConditionContext context) => Value(node, context) is bool b && b;

        static object Value(CondNode node, IConditionContext ctx)
        {
            switch (node)
            {
                case LiteralNode l: return l.Value;
                case EventNode e: return ctx.HasEvent(e.Name);
                case PathNode p: return Scalar(ctx.Lookup(p.Path));
                case CallNode c: return Call(c, ctx);
                case NotNode n: return Value(n.Operand, ctx) is bool b ? !b : (object)null;
                case BinaryNode bin when bin.Op == "&&": return Truth(Value(bin.Left, ctx)) && Truth(Value(bin.Right, ctx));
                case BinaryNode bin when bin.Op == "||": return Truth(Value(bin.Left, ctx)) || Truth(Value(bin.Right, ctx));
                case BinaryNode bin: return Compare(bin.Op, Value(bin.Left, ctx), Value(bin.Right, ctx));
                default: return null;
            }
        }

        static bool Truth(object value) => value is bool b && b;

        static object Scalar(JToken token)
        {
            if (token == null) return null;
            switch (token.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float: return (double)token;
                case JTokenType.Boolean: return (bool)token;
                case JTokenType.String: return (string)token;
                default: return null;
            }
        }

        static object Compare(string op, object left, object right)
        {
            if (left is double a && right is double b)
            {
                switch (op)
                {
                    case "<": return a < b;
                    case "<=": return a <= b;
                    case ">": return a > b;
                    case ">=": return a >= b;
                    case "==": return a == b;
                    case "!=": return a != b;
                }
            }
            if ((left is bool || left is string) && right != null && left.GetType() == right.GetType())
            {
                bool equal = Equals(left, right);
                if (op == "==") return equal;
                if (op == "!=") return !equal;
            }
            return false;   // unknown or mismatched operands: the comparison is false (so "!(missing > 0)" is true)
        }

        static object Call(CallNode call, IConditionContext ctx)
        {
            if (!FindList(ctx, call.Path, out var list, out var field)) return null;
            switch (call.Function)
            {
                case "count":
                    if (field == null) return (double)list.Count;
                    int count = 0;
                    foreach (var item in list) if (Truth(Scalar(Field(item, field)))) count++;
                    return (double)count;
                case "any":
                    if (field == null) return list.Count > 0;
                    foreach (var item in list) if (Truth(Scalar(Field(item, field)))) return true;
                    return false;
                case "min":
                case "max":
                    if (field == null) return null;
                    bool min = call.Function == "min";
                    double best = min ? double.PositiveInfinity : double.NegativeInfinity;
                    foreach (var item in list)
                    {
                        if (Scalar(Field(item, field)) is double d) best = min ? Math.Min(best, d) : Math.Max(best, d);
                    }
                    return best;
                default:
                    return null;
            }
        }

        /// <summary>Element field; elements that are not objects have no fields (never throws).</summary>
        static JToken Field(JToken item, string field) => item is JObject o ? o[field] : null;

        /// <summary>Finds the longest prefix of <paramref name="path"/> whose snapshot value is an array.</summary>
        public static bool FindList(IConditionContext ctx, string path, out JArray list, out string field)
        {
            var segments = path.Split('.');
            for (int k = segments.Length; k >= 1; k--)
            {
                if (ctx.Lookup(string.Join(".", segments, 0, k)) is JArray array)
                {
                    list = array;
                    field = k < segments.Length ? string.Join(".", segments, k, segments.Length - k) : null;
                    return true;
                }
            }
            list = null;
            field = null;
            return false;
        }
    }
}
```

`Field` trả null cho phần tử không phải `JObject` (ví dụ số), vì indexer chuỗi của Newtonsoft trên `JValue` sẽ ném exception.

- [ ] **Step 4: Chạy test để thấy pass**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: PASS, `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add Packages Runner.Tests
git commit -m "feat(contract): condition evaluator over flat snapshots and list observes" -m "<trailers>"
```

---

### Task 4: Kiểm kiểu điều kiện và PlanValidator (Contract)

**Files:**
- Create: `Packages/com.agentplay.core/Contract/Conditions/ConditionSchema.cs`
- Create: `Packages/com.agentplay.core/Contract/Plans/PlanValidator.cs`
- Test: `Runner.Tests/Contract/PlanValidatorTests.cs`

**Interfaces:**
- Consumes: `HelloMessage`, `ObserveInfo.Fields`, `EventInfo` (Task 1); parser (Task 2).
- Produces:
  - `ConditionSchema.FromHello(HelloMessage hello) : ConditionSchema`; `ConditionSchema.Check(CondNode node) : List<string>`.
  - `PlanValidator.Validate(Plan plan, HelloMessage hello) : List<string>`. Danh sách rỗng nghĩa là hợp lệ. Mỗi lỗi có tiền tố vị trí, ví dụ `steps[0].args.zone: required`.

- [ ] **Step 1: Viết test thất bại**

`Runner.Tests/Contract/PlanValidatorTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class PlanValidatorTests
    {
        static HelloMessage Hello()
        {
            var h = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G" };
            h.Observes.Add(new ObserveInfo { Path = "noise.value", ValueType = ValueKinds.Float });
            h.Observes.Add(new ObserveInfo { Path = "player.zone", ValueType = ValueKinds.String });
            h.Observes.Add(new ObserveInfo { Path = "player.hidden", ValueType = ValueKinds.Bool });
            h.Observes.Add(new ObserveInfo { Path = "player.pos", ValueType = ValueKinds.Vector3 });
            h.Observes.Add(new ObserveInfo
            {
                Path = "guards",
                ValueType = ValueKinds.List,
                Fields = new List<ObserveInfo>
                {
                    new ObserveInfo { Path = "distance", ValueType = ValueKinds.Float },
                    new ObserveInfo { Path = "sees_player", ValueType = ValueKinds.Bool },
                },
            });
            h.Actions.Add(new ActionInfo { Name = "goto", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } } });
            h.Actions.Add(new ActionInfo { Name = "wait", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "seconds", ValueType = ValueKinds.Float } } });
            h.Actions.Add(new ActionInfo { Name = "repeat", Params = new List<ActionParamInfo>
            {
                new ActionParamInfo { Name = "times", ValueType = ValueKinds.Int },
                new ActionParamInfo { Name = "loud", ValueType = ValueKinds.Bool },
            } });
            h.Actions.Add(new ActionInfo { Name = "stop", Safe = true });
            h.Events.Add(new EventInfo { Name = "guard.spotted", Important = true, Outcome = "bad" });
            return h;
        }

        static Plan Parse(string json) => JObject.Parse(json).ToObject<Plan>(ContractJson.Serializer);

        static List<string> Errors(string json) => PlanValidator.Validate(Parse(json), Hello());

        const string Valid =
            "{\"id\":\"p\",\"steps\":[{\"do\":\"goto\",\"args\":{\"zone\":\"a\"},\"until\":\"min(guards.distance) < 3\"}," +
            "{\"do\":\"repeat\",\"args\":{\"times\":2,\"loud\":false}},{\"do\":\"stop\"}]," +
            "\"rules\":[{\"when\":\"event:guard.spotted || any(guards.sees_player)\",\"do\":\"wait\",\"args\":{\"seconds\":1.5},\"then\":\"replan\"}]}";

        [Fact]
        public void ValidPlanHasNoErrors()
        {
            Assert.Empty(Errors(Valid));
        }

        [Fact]
        public void NullPlanIsReported()
        {
            Assert.Contains("plan: missing", PlanValidator.Validate(null, Hello()));
        }

        [Theory]
        [InlineData("{\"steps\":[{\"do\":\"stop\"}]}", "id: required")]
        [InlineData("{\"id\":\"p\",\"steps\":[]}", "steps: at least one step")]
        [InlineData("{\"id\":\"p\",\"timeout_s\":0,\"steps\":[{\"do\":\"stop\"}]}", "timeout_s:")]
        [InlineData("{\"id\":\"p\",\"timeout_s\":601,\"steps\":[{\"do\":\"stop\"}]}", "timeout_s:")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"fly\"}]}", "steps[0].do: unknown action 'fly'")]
        [InlineData("{\"id\":\"p\",\"steps\":[{}]}", "steps[0].do: required")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"goto\"}]}", "steps[0].args.zone: required")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"goto\",\"args\":{\"zone\":5}}]}", "steps[0].args.zone: expected string")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"goto\",\"args\":{\"zone\":\"a\",\"fast\":true}}]}", "steps[0].args.fast: unknown parameter")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"repeat\",\"args\":{\"times\":2.5,\"loud\":true}}]}", "steps[0].args.times: expected int")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"repeat\",\"args\":{\"times\":2,\"loud\":\"yes\"}}]}", "steps[0].args.loud: expected bool")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"wait\",\"args\":{\"seconds\":\"1\"}}]}", "steps[0].args.seconds: expected float")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"stop\",\"until\":\"a <\"}]}", "steps[0].until: Expected a value")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"stop\"}],\"rules\":[{\"do\":\"stop\",\"then\":\"next\"}]}", "rules[0].when: required")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"stop\"}],\"rules\":[{\"when\":\"player.hidden\",\"do\":\"stop\",\"then\":\"retry\"}]}", "rules[0].then: must be")]
        [InlineData("{\"id\":\"p\",\"steps\":[{\"do\":\"stop\"}],\"rules\":[{\"when\":\"player.hidden\",\"then\":\"next\"}]}", "rules[0].do: required")]
        public void ReportsStructuralErrors(string json, string expected)
        {
            var errors = Errors(json);
            Assert.True(errors.Any(e => e.Contains(expected)), "expected '" + expected + "' in: " + string.Join(" | ", errors));
        }

        [Theory]
        [InlineData("noise.value > 'a'", "Cannot compare number with string")]
        [InlineData("guards.distance < 3", "can only be used inside min/max/count/any")]
        [InlineData("min(guards.sees_player) < 1", "needs a numeric field")]
        [InlineData("min(guards) < 1", "needs a numeric field")]
        [InlineData("any(guards.distance)", "needs a boolean field")]
        [InlineData("count(guards.distance) > 1", "needs a boolean field")]
        [InlineData("min(guards.speed) < 1", "has no field 'speed'")]
        [InlineData("min(noise.value) < 1", "is not a list observe")]
        [InlineData("player.zone < 'a'", "Only == and != work on string")]
        [InlineData("player.hidden > true", "Only == and != work on bool")]
        [InlineData("player.pos == 1", "vector3 values cannot be compared")]
        [InlineData("noise.value", "must be true/false")]
        [InlineData("event:nope", "Unknown event 'nope'")]
        [InlineData("nope.x > 1", "Unknown observe 'nope.x'")]
        [InlineData("!noise.value", "'!' needs a boolean")]
        [InlineData("noise.value && player.hidden", "needs booleans on both sides")]
        public void ReportsConditionTypeErrors(string condition, string expected)
        {
            var json = "{\"id\":\"p\",\"steps\":[{\"do\":\"stop\",\"until\":" + Newtonsoft.Json.JsonConvert.ToString(condition) + "}]}";
            var errors = Errors(json);
            Assert.True(errors.Any(e => e.StartsWith("steps[0].until: ") && e.Contains(expected)),
                "expected '" + expected + "' in: " + string.Join(" | ", errors));
        }

        [Fact]
        public void TooManyStepsAndRules()
        {
            var plan = new Plan { Id = "p" };
            for (int i = 0; i < PlanLimits.MaxSteps + 1; i++) plan.Steps.Add(new PlanStep { Do = "stop" });
            for (int i = 0; i < PlanLimits.MaxRules + 1; i++) plan.Rules.Add(new PlanRule { When = "player.hidden", Do = "stop", Then = "next" });
            var errors = PlanValidator.Validate(plan, Hello());
            Assert.Contains(errors, e => e.StartsWith("steps: at most"));
            Assert.Contains(errors, e => e.StartsWith("rules: at most"));
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: build FAIL với `The name 'PlanValidator' does not exist in the current context`.

- [ ] **Step 3: Viết code**

`Packages/com.agentplay.core/Contract/Conditions/ConditionSchema.cs`:

```csharp
using System.Collections.Generic;

namespace AgentPlay.Contract
{
    /// <summary>Static type check of a condition against the observes and events a game declared in hello.</summary>
    public sealed class ConditionSchema
    {
        const string Number = "number";
        const string Bool = "bool";
        const string Text = "string";
        const string Other = "other";
        const string Unknown = "unknown";

        readonly Dictionary<string, string> _scalars = new Dictionary<string, string>();
        readonly Dictionary<string, Dictionary<string, string>> _lists = new Dictionary<string, Dictionary<string, string>>();
        readonly HashSet<string> _events = new HashSet<string>();

        public static ConditionSchema FromHello(HelloMessage hello)
        {
            var schema = new ConditionSchema();
            if (hello == null) return schema;
            foreach (var o in hello.Observes)
            {
                if (o.ValueType == ValueKinds.List)
                {
                    var fields = new Dictionary<string, string>();
                    if (o.Fields != null)
                        foreach (var f in o.Fields) fields[f.Path] = f.ValueType;
                    schema._lists[o.Path] = fields;
                }
                else
                {
                    schema._scalars[o.Path] = o.ValueType;
                }
            }
            if (hello.Events != null)
                foreach (var e in hello.Events) schema._events.Add(e.Name);
            return schema;
        }

        public List<string> Check(CondNode node)
        {
            var errors = new List<string>();
            var type = TypeOf(node, errors);
            if (type != Bool && type != Unknown) errors.Add("Condition must be true/false, not a " + type);
            return errors;
        }

        string TypeOf(CondNode node, List<string> errors)
        {
            switch (node)
            {
                case LiteralNode l:
                    return l.Value is double ? Number : l.Value is bool ? Bool : Text;
                case EventNode e:
                    if (!_events.Contains(e.Name)) errors.Add("Unknown event '" + e.Name + "'");
                    return Bool;
                case PathNode p:
                    return PathType(p.Path, errors);
                case CallNode c:
                    return CallType(c, errors);
                case NotNode n:
                {
                    var t = TypeOf(n.Operand, errors);
                    if (t != Bool && t != Unknown) errors.Add("'!' needs a boolean, got " + t);
                    return Bool;
                }
                case BinaryNode b when b.Op == "&&" || b.Op == "||":
                {
                    var l = TypeOf(b.Left, errors);
                    var r = TypeOf(b.Right, errors);
                    if ((l != Bool && l != Unknown) || (r != Bool && r != Unknown))
                        errors.Add("'" + b.Op + "' needs booleans on both sides");
                    return Bool;
                }
                case BinaryNode b:
                    return CompareType(b, errors);
                default:
                    errors.Add("Unsupported expression");
                    return Unknown;
            }
        }

        string PathType(string path, List<string> errors)
        {
            if (_scalars.TryGetValue(path, out var kind)) return KindToType(kind);
            if (FindList(path, out _, out _))
            {
                errors.Add("List '" + path + "' can only be used inside min/max/count/any");
                return Unknown;
            }
            errors.Add("Unknown observe '" + path + "'");
            return Unknown;
        }

        static string KindToType(string kind)
        {
            if (kind == ValueKinds.Float || kind == ValueKinds.Int) return Number;
            if (kind == ValueKinds.Bool) return Bool;
            if (kind == ValueKinds.String || kind == ValueKinds.Enum) return Text;
            return Other;
        }

        bool FindList(string path, out string listPath, out string field)
        {
            var segments = path.Split('.');
            for (int k = segments.Length; k >= 1; k--)
            {
                var prefix = string.Join(".", segments, 0, k);
                if (_lists.ContainsKey(prefix))
                {
                    listPath = prefix;
                    field = k < segments.Length ? string.Join(".", segments, k, segments.Length - k) : null;
                    return true;
                }
            }
            listPath = null;
            field = null;
            return false;
        }

        string CallType(CallNode c, List<string> errors)
        {
            string result = c.Function == "any" ? Bool : Number;
            if (!FindList(c.Path, out var list, out var field))
            {
                errors.Add("'" + c.Path + "' is not a list observe");
                return result;
            }
            string fieldType = null;
            if (field != null)
            {
                if (!_lists[list].TryGetValue(field, out var kind))
                {
                    errors.Add("List '" + list + "' has no field '" + field + "'");
                    return result;
                }
                fieldType = KindToType(kind);
            }
            switch (c.Function)
            {
                case "min":
                case "max":
                    if (field == null) errors.Add(c.Function + "() needs a numeric field, e.g. " + c.Function + "(" + list + ".distance)");
                    else if (fieldType != Number) errors.Add(c.Function + "() needs a numeric field, '" + field + "' is " + fieldType);
                    break;
                case "count":
                    if (field != null && fieldType != Bool) errors.Add("count() needs a boolean field, '" + field + "' is " + fieldType);
                    break;
                default:
                    if (field != null && fieldType != Bool) errors.Add("any() needs a boolean field, '" + field + "' is " + fieldType);
                    break;
            }
            return result;
        }

        string CompareType(BinaryNode b, List<string> errors)
        {
            var l = TypeOf(b.Left, errors);
            var r = TypeOf(b.Right, errors);
            if (l == Unknown || r == Unknown) return Bool;
            if (l == Other || r == Other)
            {
                errors.Add("vector3 values cannot be compared");
                return Bool;
            }
            if (l != r)
            {
                errors.Add("Cannot compare " + l + " with " + r);
                return Bool;
            }
            if (l != Number && b.Op != "==" && b.Op != "!=") errors.Add("Only == and != work on " + l + " values");
            return Bool;
        }
    }
}
```

`Packages/com.agentplay.core/Contract/Plans/PlanValidator.cs`:

```csharp
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Contract
{
    /// <summary>Checks a plan against the actions, observes and events a game declared. Used by Unity and the runner.</summary>
    public static class PlanValidator
    {
        public static List<string> Validate(Plan plan, HelloMessage hello)
        {
            var errors = new List<string>();
            if (plan == null)
            {
                errors.Add("plan: missing");
                return errors;
            }
            if (string.IsNullOrWhiteSpace(plan.Id)) errors.Add("id: required");
            if (plan.Steps == null || plan.Steps.Count == 0) errors.Add("steps: at least one step is required");
            else if (plan.Steps.Count > PlanLimits.MaxSteps) errors.Add("steps: at most " + PlanLimits.MaxSteps + " steps");
            if (plan.Rules != null && plan.Rules.Count > PlanLimits.MaxRules) errors.Add("rules: at most " + PlanLimits.MaxRules + " rules");
            if (!(plan.TimeoutSeconds > 0 && plan.TimeoutSeconds <= PlanLimits.MaxTimeoutSeconds))
                errors.Add("timeout_s: must be greater than 0 and at most " + PlanLimits.MaxTimeoutSeconds);

            var schema = ConditionSchema.FromHello(hello);
            var actions = new Dictionary<string, ActionInfo>();
            if (hello != null)
                foreach (var a in hello.Actions) actions[a.Name] = a;

            if (plan.Steps != null)
            {
                for (int i = 0; i < plan.Steps.Count; i++)
                {
                    var at = "steps[" + i + "]";
                    var step = plan.Steps[i];
                    if (step == null)
                    {
                        errors.Add(at + ": missing");
                        continue;
                    }
                    CheckAction(at, step.Do, step.Args, actions, errors);
                    if (!string.IsNullOrWhiteSpace(step.Until)) CheckCondition(at + ".until", step.Until, schema, errors);
                }
            }
            if (plan.Rules != null)
            {
                for (int i = 0; i < plan.Rules.Count; i++)
                {
                    var at = "rules[" + i + "]";
                    var rule = plan.Rules[i];
                    if (rule == null)
                    {
                        errors.Add(at + ": missing");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(rule.When)) errors.Add(at + ".when: required");
                    else CheckCondition(at + ".when", rule.When, schema, errors);
                    CheckAction(at, rule.Do, rule.Args, actions, errors);
                    if (!RuleThen.IsKnown(rule.Then)) errors.Add(at + ".then: must be resume, next, replan or abort");
                }
            }
            return errors;
        }

        static void CheckCondition(string at, string text, ConditionSchema schema, List<string> errors)
        {
            CondNode node;
            try
            {
                node = ConditionParser.Parse(text);
            }
            catch (ConditionException e)
            {
                errors.Add(at + ": " + e.Message);
                return;
            }
            foreach (var error in schema.Check(node)) errors.Add(at + ": " + error);
        }

        static void CheckAction(string at, string name, JObject args, Dictionary<string, ActionInfo> actions, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add(at + ".do: required");
                return;
            }
            if (!actions.TryGetValue(name, out var action))
            {
                errors.Add(at + ".do: unknown action '" + name + "'");
                return;
            }
            var known = new HashSet<string>();
            foreach (var p in action.Params)
            {
                known.Add(p.Name);
                var token = args?[p.Name];
                if (token == null || token.Type == JTokenType.Null)
                {
                    errors.Add(at + ".args." + p.Name + ": required");
                    continue;
                }
                if (!Matches(p.ValueType, token)) errors.Add(at + ".args." + p.Name + ": expected " + p.ValueType);
            }
            if (args != null)
                foreach (var prop in args.Properties())
                    if (!known.Contains(prop.Name)) errors.Add(at + ".args." + prop.Name + ": unknown parameter");
        }

        static bool Matches(string kind, JToken token)
        {
            switch (kind)
            {
                case ValueKinds.Float: return token.Type == JTokenType.Float || token.Type == JTokenType.Integer;
                case ValueKinds.Int: return token.Type == JTokenType.Integer;
                case ValueKinds.Bool: return token.Type == JTokenType.Boolean;
                case ValueKinds.String: return token.Type == JTokenType.String;
                default: return false;
            }
        }
    }
}
```

- [ ] **Step 4: Chạy test để thấy pass**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: PASS, `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add Packages Runner.Tests
git commit -m "feat(contract): condition type checking and plan validator" -m "<trailers>"
```

---

### Task 5: Attribute v2, registry và snapshot danh sách (Runtime)

**Files:**
- Replace: `Packages/com.agentplay.core/Runtime/Attributes.cs`
- Replace: `Packages/com.agentplay.core/Runtime/Registry/AgentRegistry.cs`
- Modify: `Packages/com.agentplay.core/Runtime/Registry/TypeScanner.cs` (hàm `IsAnnotated`)
- Replace: `Packages/com.agentplay.core/Runtime/Registry/SnapshotBuilder.cs`
- Test: `Packages/com.agentplay.core/Tests/EditMode/FakesV2.cs`, `RegistryV2Tests.cs`

**Interfaces:**
- Consumes: `ObserveInfo.Fields`, `EventInfo`, `ValueKinds.List` (Task 1).
- Produces:
  - `AgentObserveListAttribute(string path, string description = "")`; `AgentEventInfoAttribute(string name, string description = "")` với `Important`, `Outcome` (AllowMultiple, đặt trên class); `AgentActionContext { bool IsFirstFrame; double Elapsed; }`.
  - `ObserveEntry.Fields : List<ObserveEntry>`, `ObserveEntry.ElementType : Type` (cho danh sách).
  - `ActionEntry.TakesContext : bool`.
  - `RegistryModel.Events : List<EventInfo>`, `RegistryModel.SafeAction : string` (null nếu không có).
  - `SnapshotBuilder.MaxListItems = 16`.
  - Type làm phần tử danh sách không đóng góp observe cấp cao nhất.

- [ ] **Step 1: Viết test thất bại**

`Packages/com.agentplay.core/Tests/EditMode/FakesV2.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace AgentPlay.Tests
{
    public class FakeGuardView
    {
        [AgentObserve("distance", "Metres to the player")] public float Distance;
        [AgentObserve("sees_player")] public bool SeesPlayer;
        [AgentObserve("ignored.static")] public static int Static = 1;
    }

    [AgentEventInfo("guard.spotted", "Guard sees the player", Important = true, Outcome = AgentOutcome.Bad)]
    [AgentEventInfo("game.won", Outcome = AgentOutcome.Good)]
    public class FakeHeist
    {
        public List<FakeGuardView> GuardList = new List<FakeGuardView>
        {
            new FakeGuardView { Distance = 4.25f },
            new FakeGuardView { Distance = 9f, SeesPlayer = true },
        };

        [AgentObserveList("guards", "Guards in the level")] public IEnumerable<FakeGuardView> Guards => GuardList;

        [AgentAction("goto")] public AgentActionStatus GoTo(AgentActionContext ctx, string zone) => AgentActionStatus.Done;
        [AgentAction("stop", Safe = true)] public AgentActionStatus Stop() => AgentActionStatus.Done;
    }

    public class FakeBadV2
    {
        [AgentObserveList("nothing")] public IEnumerable<int> Ints => null;
        [AgentObserveList("notlist")] public int NotList;
        [AgentAction("ctx.late")] public AgentActionStatus Late(string a, AgentActionContext ctx) => AgentActionStatus.Done;
        [AgentAction("safe.args", Safe = true)] public AgentActionStatus SafeArgs(int x) => AgentActionStatus.Done;
    }

    [AgentEventInfo("dup")]
    [AgentEventInfo("dup")]
    public class FakeDupEvents { }

    public class FakeManyGuards
    {
        [AgentObserveList("crowd")]
        public IEnumerable<FakeGuardView> Crowd => Enumerable.Range(0, 20).Select(i => new FakeGuardView { Distance = i });
    }
}
```

`Packages/com.agentplay.core/Tests/EditMode/RegistryV2Tests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using Newtonsoft.Json;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class RegistryV2Tests
    {
        [Test]
        public void ListObserveCollectsFieldsAndHidesElementTypeFromTopLevel()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeHeist), typeof(FakeGuardView) });
            CollectionAssert.IsEmpty(m.Errors);
            CollectionAssert.AreEqual(new[] { "guards" }, m.Observes.Select(o => o.Path).ToArray());
            var guards = m.Observes[0];
            Assert.AreEqual(ValueKinds.List, guards.ValueKind);
            CollectionAssert.AreEqual(new[] { "distance", "sees_player" }, guards.Fields.Select(f => f.Path).ToArray());
            Assert.AreEqual(typeof(FakeGuardView), guards.ElementType);
        }

        [Test]
        public void ActionContextParameterIsNotAPlanParameter()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeHeist) });
            var go = m.Actions.Single(a => a.Name == "goto");
            Assert.IsTrue(go.TakesContext);
            CollectionAssert.AreEqual(new[] { "zone" }, go.Params.Select(p => p.Name).ToArray());
            Assert.AreEqual("stop", m.SafeAction);
        }

        [Test]
        public void EventInfosAreCollectedSorted()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeHeist) });
            CollectionAssert.AreEqual(new[] { "game.won", "guard.spotted" }, m.Events.Select(e => e.Name).ToArray());
            var spotted = m.Events[1];
            Assert.IsTrue(spotted.Important);
            Assert.AreEqual("bad", spotted.Outcome);
            Assert.AreEqual("Guard sees the player", spotted.Description);
            Assert.AreEqual("good", m.Events[0].Outcome);
        }

        [Test]
        public void InvalidV2DeclarationsAreReported()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeBadV2) });
            string all = string.Join("\n", m.Errors);
            StringAssert.Contains("'nothing': element type Int32 has no [AgentObserve] members", all);
            StringAssert.Contains("'notlist' must be an IEnumerable<T>", all);
            StringAssert.Contains("AgentActionContext must be the first parameter", all);
            StringAssert.Contains("Safe action 'safe.args' must take no parameters", all);
            Assert.IsNull(m.SafeAction);
        }

        [Test]
        public void DuplicateEventIsReported()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeDupEvents) });
            StringAssert.Contains("Duplicate event 'dup'", string.Join("\n", m.Errors));
            Assert.AreEqual(1, m.Events.Count);
        }

        [Test]
        public void HelloCarriesListFieldsAndEvents()
        {
            var hello = AgentRegistry.ToHello(AgentRegistry.Build(new[] { typeof(FakeHeist) }), "P");
            var guards = hello.Observes.Single(o => o.Path == "guards");
            Assert.AreEqual(ValueKinds.List, guards.ValueType);
            Assert.AreEqual("float", guards.Fields.Single(f => f.Path == "distance").ValueType);
            Assert.AreEqual(2, hello.Events.Count);
            Assert.IsNull(hello.Observes.Single(o => o.Path == "guards").Fields.Single(f => f.Path == "sees_player").Fields);
        }

        [Test]
        public void SnapshotOfListObserve()
        {
            var model = AgentRegistry.Build(new[] { typeof(FakeHeist) });
            var heist = new FakeHeist();
            var s = SnapshotBuilder.Build(model, t => t == typeof(FakeHeist) ? heist : null);
            Assert.AreEqual("[{\"distance\":4.25,\"sees_player\":false},{\"distance\":9.0,\"sees_player\":true}]",
                s["guards"].ToString(Formatting.None));
        }

        [Test]
        public void SnapshotListIsCappedWithOneWarning()
        {
            var model = AgentRegistry.Build(new[] { typeof(FakeManyGuards) });
            var warnings = new List<string>();
            var s = SnapshotBuilder.Build(model, t => new FakeManyGuards(), warnings.Add);
            Assert.AreEqual(SnapshotBuilder.MaxListItems, ((Newtonsoft.Json.Linq.JArray)s["crowd"]).Count);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("more than 16", warnings[0]);
        }

        [Test]
        public void NullListBecomesEmptyArray()
        {
            var model = AgentRegistry.Build(new[] { typeof(FakeHeist) });
            var heist = new FakeHeist { GuardList = null };
            var s = SnapshotBuilder.Build(model, t => heist);
            Assert.AreEqual("[]", s["guards"].ToString(Formatting.None));
        }

        [Test]
        public void EventOnlyClassIsAnnotated()
        {
            Assert.IsTrue(TypeScanner.IsAnnotated(typeof(FakeDupEvents)));
            Assert.IsTrue(TypeScanner.IsAnnotated(typeof(FakeManyGuards)));
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `cd /e/Unity-WorkSpace/AgentPlay && bash Tools/unity-tests.sh`
Expected: `error CS0246: The type or namespace name 'AgentEventInfo' could not be found` (hoặc `AgentObserveList`).

- [ ] **Step 3: Viết code**

Thay toàn bộ `Packages/com.agentplay.core/Runtime/Attributes.cs`:

```csharp
using System;

namespace AgentPlay
{
    /// <summary>Exposes a field or property to the agent under a stable path.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class AgentObserveAttribute : Attribute
    {
        public string Path { get; }
        public string Description { get; }

        public AgentObserveAttribute(string path, string description = "")
        {
            Path = path;
            Description = description ?? "";
        }
    }

    /// <summary>
    /// Exposes an IEnumerable&lt;T&gt; as a list observe. T's [AgentObserve] members become the element fields,
    /// and T contributes no top-level observes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class AgentObserveListAttribute : Attribute
    {
        public string Path { get; }
        public string Description { get; }

        public AgentObserveListAttribute(string path, string description = "")
        {
            Path = path;
            Description = description ?? "";
        }
    }

    /// <summary>
    /// Marks a method the agent may call every frame until it returns Done or Failed. Parameters: an optional
    /// AgentActionContext first, then float/int/bool/string.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class AgentActionAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }
        /// <summary>The single default action run while no plan is available. Must take no parameters.</summary>
        public bool Safe { get; set; }

        public AgentActionAttribute(string name, string description = "")
        {
            Name = name;
            Description = description ?? "";
        }
    }

    /// <summary>Declares an event the game emits, so plans can react to it with "event:name".</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class AgentEventInfoAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }
        public bool Important { get; set; }
        public AgentOutcome Outcome { get; set; }

        public AgentEventInfoAttribute(string name, string description = "")
        {
            Name = name;
            Description = description ?? "";
        }
    }

    /// <summary>Names the game. Version is literal, or read from a static string property/method named by VersionMember.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AgentGameAttribute : Attribute
    {
        public string Name { get; }
        public string Version { get; set; }
        public string VersionMember { get; set; }

        public AgentGameAttribute(string name)
        {
            Name = name;
        }
    }

    /// <summary>Optional first parameter of an action: lets it tell its first frame from later ones.</summary>
    public sealed class AgentActionContext
    {
        public bool IsFirstFrame { get; set; } = true;
        /// <summary>Game seconds since the action started.</summary>
        public double Elapsed { get; set; }
    }

    public enum AgentActionStatus { Running, Done, Failed }

    public enum AgentOutcome { Neutral, Good, Bad }
}
```

Thay toàn bộ `Packages/com.agentplay.core/Runtime/Registry/AgentRegistry.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AgentPlay.Contract;
using UnityEngine;

namespace AgentPlay
{
    public sealed class ObserveEntry
    {
        public string Path;
        public string Description;
        public Type DeclaringType;
        public MemberInfo Member;
        public string ValueKind;
        public bool IsStatic;
        /// <summary>List observes only: element type and its fields (paths relative to the element).</summary>
        public Type ElementType;
        public List<ObserveEntry> Fields;
    }

    public sealed class ActionEntry
    {
        public string Name;
        public string Description;
        public bool Safe;
        public Type DeclaringType;
        public MethodInfo Method;
        public bool TakesContext;
        public List<ActionParamInfo> Params = new List<ActionParamInfo>();
    }

    public sealed class RegistryModel
    {
        public readonly List<ObserveEntry> Observes = new List<ObserveEntry>();
        public readonly List<ActionEntry> Actions = new List<ActionEntry>();
        public readonly List<EventInfo> Events = new List<EventInfo>();
        public readonly List<string> Errors = new List<string>();
        /// <summary>Null when no type carries [AgentGame].</summary>
        public string GameName;
        public string GameVersion = "";

        public string SafeAction
        {
            get
            {
                foreach (var a in Actions)
                    if (a.Safe) return a.Name;
                return null;
            }
        }
    }

    public static class AgentRegistry
    {
        const BindingFlags MemberFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        const BindingFlags ElementFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static RegistryModel Build(IEnumerable<Type> types)
        {
            var model = new RegistryModel();
            var ordered = types.Distinct().OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
            var observeOwners = new Dictionary<string, string>();
            var actionOwners = new Dictionary<string, string>();
            var eventNames = new HashSet<string>();
            string safeAction = null;

            var elementTypes = new HashSet<Type>();
            foreach (var type in ordered)
                foreach (var member in type.GetMembers(MemberFlags))
                    if (member.IsDefined(typeof(AgentObserveListAttribute), false) && ElementTypeOf(MemberType(member)) is Type element)
                        elementTypes.Add(element);

            foreach (var type in ordered)
            {
                ReadGame(type, model);
                ReadEvents(type, model, eventNames);
                foreach (var member in type.GetMembers(MemberFlags).OrderBy(m => m.Name, StringComparer.Ordinal))
                {
                    var observe = member.GetCustomAttribute<AgentObserveAttribute>(false);
                    if (observe != null && (member is FieldInfo || member is PropertyInfo) && !elementTypes.Contains(type))
                    {
                        var entry = MakeObserve(type, member, observe.Path, observe.Description, model.Errors);
                        if (entry != null) Register(entry, model, observeOwners);
                    }

                    var list = member.GetCustomAttribute<AgentObserveListAttribute>(false);
                    if (list != null && (member is FieldInfo || member is PropertyInfo))
                        AddObserveList(type, member, list, model, observeOwners);

                    var action = member.GetCustomAttribute<AgentActionAttribute>(false);
                    if (action != null && member is MethodInfo method)
                        AddAction(type, method, action, model, actionOwners, ref safeAction);
                }
            }

            model.Observes.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            model.Actions.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            model.Events.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return model;
        }

        public static HelloMessage ToHello(RegistryModel model, string fallbackGameName)
        {
            var hello = new HelloMessage
            {
                Protocol = ProtocolVersion.Current,
                Game = model.GameName ?? fallbackGameName,
                Version = model.GameVersion ?? "",
            };
            foreach (var o in model.Observes) hello.Observes.Add(ToInfo(o));
            foreach (var a in model.Actions)
                hello.Actions.Add(new ActionInfo { Name = a.Name, Description = a.Description, Safe = a.Safe, Params = new List<ActionParamInfo>(a.Params) });
            hello.Events.AddRange(model.Events);
            hello.Modes.Add(Modes.TurnBased);
            hello.Modes.Add(Modes.Realtime);
            return hello;
        }

        static ObserveInfo ToInfo(ObserveEntry o)
        {
            var info = new ObserveInfo { Path = o.Path, ValueType = o.ValueKind, Description = o.Description };
            if (o.Fields != null) info.Fields = o.Fields.Select(ToInfo).ToList();
            return info;
        }

        public static string KindOf(Type t)
        {
            if (t == typeof(float) || t == typeof(double)) return ValueKinds.Float;
            if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)) return ValueKinds.Int;
            if (t == typeof(bool)) return ValueKinds.Bool;
            if (t == typeof(string)) return ValueKinds.String;
            if (t.IsEnum) return ValueKinds.Enum;
            if (t == typeof(Vector3)) return ValueKinds.Vector3;
            return null;
        }

        static Type MemberType(MemberInfo member) =>
            member is FieldInfo f ? f.FieldType : member is PropertyInfo p ? p.PropertyType : null;

        static Type ElementTypeOf(Type type)
        {
            if (type == null || type == typeof(string)) return null;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return type.GetGenericArguments()[0];
            foreach (var i in type.GetInterfaces())
                if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return i.GetGenericArguments()[0];
            return null;
        }

        /// <summary>Validates one observe member; returns null (and records an error) when it cannot be read.</summary>
        static ObserveEntry MakeObserve(Type type, MemberInfo member, string path, string description, List<string> errors)
        {
            string where = type.Name + "." + member.Name;
            if (string.IsNullOrWhiteSpace(path))
            {
                errors.Add("Observe on " + where + " has an empty path");
                return null;
            }
            if (!TryReadable(member, out var valueType, out var isStatic))
            {
                errors.Add("Observe '" + path + "' on " + where + " needs a readable, non-indexed property");
                return null;
            }
            string kind = KindOf(valueType);
            if (kind == null)
            {
                errors.Add("Observe '" + path + "' on " + where + " has unsupported type " + valueType.Name);
                return null;
            }
            return new ObserveEntry
            {
                Path = path,
                Description = description,
                DeclaringType = type,
                Member = member,
                ValueKind = kind,
                IsStatic = isStatic,
            };
        }

        static bool TryReadable(MemberInfo member, out Type valueType, out bool isStatic)
        {
            if (member is FieldInfo field)
            {
                valueType = field.FieldType;
                isStatic = field.IsStatic;
                return true;
            }
            var prop = (PropertyInfo)member;
            var getter = prop.GetGetMethod(true);
            valueType = prop.PropertyType;
            isStatic = getter != null && getter.IsStatic;
            return getter != null && prop.GetIndexParameters().Length == 0;
        }

        static void Register(ObserveEntry entry, RegistryModel model, Dictionary<string, string> owners)
        {
            string where = entry.DeclaringType.Name + "." + entry.Member.Name;
            if (owners.TryGetValue(entry.Path, out var first))
            {
                model.Errors.Add("Duplicate observe path '" + entry.Path + "' on " + first + " and " + where);
                return;
            }
            owners[entry.Path] = where;
            model.Observes.Add(entry);
        }

        static void AddObserveList(Type type, MemberInfo member, AgentObserveListAttribute attr, RegistryModel model, Dictionary<string, string> owners)
        {
            string where = type.Name + "." + member.Name;
            if (string.IsNullOrWhiteSpace(attr.Path))
            {
                model.Errors.Add("Observe list on " + where + " has an empty path");
                return;
            }
            if (!TryReadable(member, out var valueType, out var isStatic))
            {
                model.Errors.Add("Observe list '" + attr.Path + "' on " + where + " needs a readable, non-indexed property");
                return;
            }
            var element = ElementTypeOf(valueType);
            if (element == null)
            {
                model.Errors.Add("Observe list '" + attr.Path + "' must be an IEnumerable<T> (" + where + " is " + valueType.Name + ")");
                return;
            }
            var fields = new List<ObserveEntry>();
            foreach (var m in element.GetMembers(ElementFlags).OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                var observe = m.GetCustomAttribute<AgentObserveAttribute>(true);
                if (observe == null || !(m is FieldInfo || m is PropertyInfo)) continue;
                var field = MakeObserve(element, m, observe.Path, observe.Description, model.Errors);
                if (field != null) fields.Add(field);
            }
            if (fields.Count == 0)
            {
                model.Errors.Add("Observe list '" + attr.Path + "': element type " + element.Name + " has no [AgentObserve] members");
                return;
            }
            fields.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            Register(new ObserveEntry
            {
                Path = attr.Path,
                Description = attr.Description,
                DeclaringType = type,
                Member = member,
                ValueKind = ValueKinds.List,
                IsStatic = isStatic,
                ElementType = element,
                Fields = fields,
            }, model, owners);
        }

        static void AddAction(Type type, MethodInfo method, AgentActionAttribute attr, RegistryModel model,
            Dictionary<string, string> owners, ref string safeAction)
        {
            string where = type.Name + "." + method.Name;
            if (string.IsNullOrWhiteSpace(attr.Name))
            {
                model.Errors.Add("Action on " + where + " has an empty name");
                return;
            }
            if (method.ReturnType != typeof(AgentActionStatus))
            {
                model.Errors.Add("Action '" + attr.Name + "' on " + where + " must return AgentActionStatus");
                return;
            }

            var parameters = method.GetParameters();
            bool takesContext = parameters.Length > 0 && parameters[0].ParameterType == typeof(AgentActionContext);
            var ps = new List<ActionParamInfo>();
            for (int i = takesContext ? 1 : 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p.ParameterType == typeof(AgentActionContext))
                {
                    model.Errors.Add("Action '" + attr.Name + "' on " + where + ": AgentActionContext must be the first parameter");
                    return;
                }
                string kind = KindOf(p.ParameterType);
                if (kind == null || kind == ValueKinds.Enum || kind == ValueKinds.Vector3)
                {
                    model.Errors.Add("Action '" + attr.Name + "' on " + where + ": parameter '" + p.Name + "' has unsupported type " + p.ParameterType.Name);
                    return;
                }
                ps.Add(new ActionParamInfo { Name = p.Name, ValueType = kind });
            }

            if (owners.TryGetValue(attr.Name, out var first))
            {
                model.Errors.Add("Duplicate action '" + attr.Name + "' on " + first + " and " + where);
                return;
            }
            if (attr.Safe)
            {
                if (ps.Count > 0)
                {
                    model.Errors.Add("Safe action '" + attr.Name + "' must take no parameters");
                    return;
                }
                if (safeAction != null)
                {
                    model.Errors.Add("Only one safe action is allowed: '" + safeAction + "' and '" + attr.Name + "'");
                    return;
                }
                safeAction = attr.Name;
            }

            owners[attr.Name] = where;
            model.Actions.Add(new ActionEntry
            {
                Name = attr.Name,
                Description = attr.Description,
                Safe = attr.Safe,
                DeclaringType = type,
                Method = method,
                TakesContext = takesContext,
                Params = ps,
            });
        }

        static void ReadEvents(Type type, RegistryModel model, HashSet<string> names)
        {
            foreach (var e in type.GetCustomAttributes<AgentEventInfoAttribute>(false))
            {
                if (string.IsNullOrWhiteSpace(e.Name))
                {
                    model.Errors.Add("Event on " + type.Name + " has an empty name");
                    continue;
                }
                if (!names.Add(e.Name))
                {
                    model.Errors.Add("Duplicate event '" + e.Name + "' (" + type.Name + ")");
                    continue;
                }
                model.Events.Add(new EventInfo
                {
                    Name = e.Name,
                    Description = e.Description,
                    Important = e.Important,
                    Outcome = e.Outcome.ToString().ToLowerInvariant(),
                });
            }
        }

        static void ReadGame(Type type, RegistryModel model)
        {
            var game = type.GetCustomAttribute<AgentGameAttribute>(false);
            if (game == null) return;
            if (model.GameName != null)
            {
                model.Errors.Add("More than one [AgentGame]: '" + model.GameName + "' and '" + game.Name + "'");
                return;
            }
            model.GameName = game.Name;
            model.GameVersion = ResolveVersion(type, game, model.Errors);
        }

        static string ResolveVersion(Type type, AgentGameAttribute game, List<string> errors)
        {
            if (game.Version != null) return game.Version;
            if (string.IsNullOrEmpty(game.VersionMember)) return "";

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            string where = type.Name + "." + game.VersionMember;
            try
            {
                var prop = type.GetProperty(game.VersionMember, flags);
                if (prop != null && prop.PropertyType == typeof(string))
                    return (string)prop.GetValue(null) ?? "";
                var method = type.GetMethod(game.VersionMember, flags, null, Type.EmptyTypes, null);
                if (method != null && method.ReturnType == typeof(string))
                    return (string)method.Invoke(null, null) ?? "";
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                errors.Add("[AgentGame] version member " + where + " threw: " + inner.Message);
                return "";
            }
            errors.Add("[AgentGame] version member " + where + " must be a static string property or a parameterless static method returning string");
            return "";
        }
    }
}
```

Lưu ý so với M1: thông báo lỗi observe sai kiểu và observe trùng giữ nguyên chuỗi (test `ReportsEveryInvalidDeclarationAndSkipsIt` của M1 dựa vào chúng). Observe chỉ có setter vẫn ra thông báo `'write.only'`, vì đi qua `TryReadable`.

Trong `Packages/com.agentplay.core/Runtime/Registry/TypeScanner.cs`, thay thân hàm `IsAnnotated`:

```csharp
        public static bool IsAnnotated(Type t)
        {
            if (t.IsDefined(typeof(AgentGameAttribute), false) || t.IsDefined(typeof(AgentEventInfoAttribute), false)) return true;
            foreach (var m in t.GetMembers(MemberFlags))
                if (m.IsDefined(typeof(AgentObserveAttribute), false) || m.IsDefined(typeof(AgentObserveListAttribute), false)
                    || m.IsDefined(typeof(AgentActionAttribute), false))
                    return true;
            return false;
        }
```

Thay toàn bộ `Packages/com.agentplay.core/Runtime/Registry/SnapshotBuilder.cs`:

```csharp
using System;
using System.Collections;
using System.Reflection;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AgentPlay
{
    public static class SnapshotBuilder
    {
        public const int MaxListItems = 16;

        /// <summary>Reads every observe into a flat object keyed by path. Unreadable values become null.</summary>
        public static JObject Build(RegistryModel model, Func<Type, object> resolveInstance, Action<string> warn = null)
        {
            var o = new JObject();
            foreach (var e in model.Observes)
            {
                object target = null;
                if (!e.IsStatic)
                {
                    target = resolveInstance(e.DeclaringType);
                    if (target == null)
                    {
                        o[e.Path] = e.ValueKind == ValueKinds.List ? (JToken)new JArray() : JValue.CreateNull();
                        continue;
                    }
                }
                o[e.Path] = e.ValueKind == ValueKinds.List ? ReadList(e, target, warn) : Read(e, target, warn);
            }
            return o;
        }

        static JToken ReadList(ObserveEntry e, object target, Action<string> warn)
        {
            var array = new JArray();
            if (!(ReadRaw(e, target, warn, out var raw) && raw is IEnumerable items)) return array;
            foreach (var item in items)
            {
                if (item == null) continue;
                if (array.Count == MaxListItems)
                {
                    warn?.Invoke("Observe list '" + e.Path + "' has more than " + MaxListItems + " items; extra items are dropped");
                    break;
                }
                var element = new JObject();
                foreach (var field in e.Fields) element[field.Path] = Read(field, item, warn);
                array.Add(element);
            }
            return array;
        }

        static JToken Read(ObserveEntry e, object target, Action<string> warn) =>
            ReadRaw(e, target, warn, out var value) ? ToToken(e.ValueKind, value) : JValue.CreateNull();

        static bool ReadRaw(ObserveEntry e, object target, Action<string> warn, out object value)
        {
            try
            {
                value = e.Member is FieldInfo f ? f.GetValue(target) : ((PropertyInfo)e.Member).GetValue(target);
                return true;
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                warn?.Invoke("Observe '" + e.Path + "' threw: " + inner.Message);
                value = null;
                return false;
            }
        }

        static JToken ToToken(string kind, object value)
        {
            if (value == null) return JValue.CreateNull();
            switch (kind)
            {
                case ValueKinds.Float: return Number(Convert.ToDouble(value));
                case ValueKinds.Int: return new JValue(Convert.ToInt64(value));
                case ValueKinds.Bool: return new JValue((bool)value);
                case ValueKinds.String: return new JValue((string)value);
                case ValueKinds.Enum: return new JValue(value.ToString());
                case ValueKinds.Vector3:
                    var v = (Vector3)value;
                    return new JArray(Number(v.x), Number(v.y), Number(v.z));
                default: return JValue.CreateNull();
            }
        }

        static JToken Number(double d)
        {
            var c = UnityJson.Clean(d);
            return c.HasValue ? new JValue(c.Value) : JValue.CreateNull();
        }
    }
}
```

`SnapshotListIsCappedWithOneWarning` cần đúng một cảnh báo cho mỗi lần dựng snapshot. Controller của Editor đã lọc trùng cảnh báo qua `WarnOnce`, nên ở game thật cảnh báo chỉ hiện một lần.

- [ ] **Step 4: Chạy test để thấy pass**

Run: `cd /e/Unity-WorkSpace/AgentPlay && bash Tools/unity-tests.sh`
Expected: `result="Passed"`, `failed="0"`. Toàn bộ test M1 cũng phải pass.

- [ ] **Step 5: Commit**

```bash
git add Packages
git commit -m "feat(runtime): list observes, declared events and action context in the registry" -m "<trailers>"
```

---

### Task 6: ActionInvoker (Runtime)

**Files:**
- Create: `Packages/com.agentplay.core/Runtime/Execution/ActionInvoker.cs`
- Test: `Packages/com.agentplay.core/Tests/EditMode/ActionInvokerTests.cs`

**Interfaces:**
- Consumes: `RegistryModel`, `ActionEntry.TakesContext` (Task 5), `AgentActionContext`.
- Produces:
  - `interface IActionRunner { bool Has(string action); AgentActionStatus Run(string action, JObject args, AgentActionContext context); }`
  - `ActionInvoker(RegistryModel model, Func<Type, object> resolveInstance) : IActionRunner`.
  - `AgentActionException : Exception`. `Run` ném lỗi này cho mọi lỗi: action không có, thiếu instance, tham số sai, action ném exception.

- [ ] **Step 1: Viết test thất bại**

`Packages/com.agentplay.core/Tests/EditMode/ActionInvokerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class FakeActor
    {
        public readonly List<string> Calls = new List<string>();
        public AgentActionContext LastContext;

        [AgentAction("move")]
        public AgentActionStatus Move(AgentActionContext ctx, string zone, float speed, int times, bool run)
        {
            LastContext = ctx;
            Calls.Add(zone + "|" + speed.ToString(CultureInfo.InvariantCulture) + "|" + times + "|" + run);
            return AgentActionStatus.Running;
        }

        [AgentAction("boom")] public AgentActionStatus Boom() => throw new InvalidOperationException("kaput");

        [AgentAction("ping")] public static AgentActionStatus Ping() => AgentActionStatus.Done;
    }

    public class ActionInvokerTests
    {
        FakeActor _actor;
        ActionInvoker _invoker;

        [SetUp]
        public void SetUp()
        {
            _actor = new FakeActor();
            _invoker = new ActionInvoker(AgentRegistry.Build(new[] { typeof(FakeActor) }), t => t == typeof(FakeActor) ? _actor : null);
        }

        static JObject Args(string json) => JObject.Parse(json);

        [Test]
        public void ConvertsArgumentsAndPassesContext()
        {
            var ctx = new AgentActionContext();
            var status = _invoker.Run("move", Args("{\"zone\":\"a\",\"speed\":1.5,\"times\":3,\"run\":true}"), ctx);
            Assert.AreEqual(AgentActionStatus.Running, status);
            CollectionAssert.AreEqual(new[] { "a|1.5|3|True" }, _actor.Calls);
            Assert.AreSame(ctx, _actor.LastContext);
        }

        [Test]
        public void IntegerArgumentIsAcceptedForFloat()
        {
            _invoker.Run("move", Args("{\"zone\":\"a\",\"speed\":2,\"times\":1,\"run\":false}"), new AgentActionContext());
            CollectionAssert.AreEqual(new[] { "a|2|1|False" }, _actor.Calls);
        }

        [TestCase("{\"speed\":1,\"times\":1,\"run\":true}", "missing argument 'zone'")]
        [TestCase("{\"zone\":5,\"speed\":1,\"times\":1,\"run\":true}", "argument 'zone' expected a string")]
        [TestCase("{\"zone\":\"a\",\"speed\":\"x\",\"times\":1,\"run\":true}", "argument 'speed' expected a number")]
        [TestCase("{\"zone\":\"a\",\"speed\":1,\"times\":2.5,\"run\":true}", "argument 'times' expected a whole number")]
        [TestCase("{\"zone\":\"a\",\"speed\":1,\"times\":1,\"run\":\"yes\"}", "argument 'run' expected true or false")]
        [TestCase("{\"zone\":\"a\",\"speed\":1,\"times\":99999999999,\"run\":true}", "argument 'times'")]
        public void BadArgumentsThrowActionException(string json, string expected)
        {
            var e = Assert.Throws<AgentActionException>(() => _invoker.Run("move", Args(json), new AgentActionContext()));
            StringAssert.Contains(expected, e.Message);
        }

        [Test]
        public void ThrowingActionIsWrapped()
        {
            var e = Assert.Throws<AgentActionException>(() => _invoker.Run("boom", null, new AgentActionContext()));
            StringAssert.Contains("kaput", e.Message);
        }

        [Test]
        public void UnknownActionAndMissingInstance()
        {
            StringAssert.Contains("Unknown action 'fly'",
                Assert.Throws<AgentActionException>(() => _invoker.Run("fly", null, new AgentActionContext())).Message);
            var orphan = new ActionInvoker(AgentRegistry.Build(new[] { typeof(FakeActor) }), t => null);
            StringAssert.Contains("No FakeActor instance",
                Assert.Throws<AgentActionException>(() => orphan.Run("move", Args("{\"zone\":\"a\",\"speed\":1,\"times\":1,\"run\":true}"), new AgentActionContext())).Message);
        }

        [Test]
        public void StaticActionNeedsNoInstance()
        {
            var orphan = new ActionInvoker(AgentRegistry.Build(new[] { typeof(FakeActor) }), t => null);
            Assert.AreEqual(AgentActionStatus.Done, orphan.Run("ping", null, new AgentActionContext()));
            Assert.IsTrue(orphan.Has("ping"));
            Assert.IsFalse(orphan.Has("fly"));
            Assert.IsFalse(orphan.Has(null));
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `cd /e/Unity-WorkSpace/AgentPlay && bash Tools/unity-tests.sh`
Expected: `error CS0246: The type or namespace name 'ActionInvoker' could not be found`.

- [ ] **Step 3: Viết code**

`Packages/com.agentplay.core/Runtime/Execution/ActionInvoker.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace AgentPlay
{
    public sealed class AgentActionException : Exception
    {
        public AgentActionException(string message) : base(message) { }
    }

    public interface IActionRunner
    {
        bool Has(string action);
        /// <summary>Runs one frame of an action. Throws AgentActionException on any failure to call it.</summary>
        AgentActionStatus Run(string action, JObject args, AgentActionContext context);
    }

    /// <summary>Calls [AgentAction] methods by reflection with arguments converted from JSON.</summary>
    public sealed class ActionInvoker : IActionRunner
    {
        readonly Dictionary<string, ActionEntry> _byName = new Dictionary<string, ActionEntry>();
        readonly Func<Type, object> _resolveInstance;

        public ActionInvoker(RegistryModel model, Func<Type, object> resolveInstance)
        {
            foreach (var a in model.Actions) _byName[a.Name] = a;
            _resolveInstance = resolveInstance;
        }

        public bool Has(string action) => action != null && _byName.ContainsKey(action);

        public AgentActionStatus Run(string action, JObject args, AgentActionContext context)
        {
            if (action == null || !_byName.TryGetValue(action, out var entry))
                throw new AgentActionException("Unknown action '" + action + "'");

            object target = null;
            if (!entry.Method.IsStatic)
            {
                target = _resolveInstance(entry.DeclaringType);
                if (target == null) throw new AgentActionException("No " + entry.DeclaringType.Name + " instance for action '" + action + "'");
            }

            var parameters = entry.Method.GetParameters();
            var values = new object[parameters.Length];
            int first = 0;
            if (entry.TakesContext)
            {
                values[0] = context ?? new AgentActionContext();
                first = 1;
            }
            for (int i = first; i < parameters.Length; i++) values[i] = Convert(args, parameters[i], action);

            try
            {
                return (AgentActionStatus)entry.Method.Invoke(target, values);
            }
            catch (TargetInvocationException e)
            {
                throw new AgentActionException("Action '" + action + "' threw: " + (e.InnerException ?? e).Message);
            }
        }

        static object Convert(JObject args, ParameterInfo p, string action)
        {
            var token = args?[p.Name];
            if (token == null || token.Type == JTokenType.Null)
                throw new AgentActionException("Action '" + action + "': missing argument '" + p.Name + "'");
            try
            {
                if (p.ParameterType == typeof(string))
                {
                    if (token.Type != JTokenType.String) throw new FormatException("expected a string");
                    return (string)token;
                }
                if (p.ParameterType == typeof(bool))
                {
                    if (token.Type != JTokenType.Boolean) throw new FormatException("expected true or false");
                    return (bool)token;
                }
                if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) throw new FormatException("expected a number");
                double d = (double)token;
                if (p.ParameterType == typeof(float)) return (float)d;
                if (p.ParameterType == typeof(double)) return d;
                if (d != Math.Floor(d)) throw new FormatException("expected a whole number");
                return System.Convert.ChangeType((long)d, p.ParameterType, CultureInfo.InvariantCulture);
            }
            catch (Exception e) when (e is FormatException || e is OverflowException || e is InvalidCastException)
            {
                throw new AgentActionException("Action '" + action + "': argument '" + p.Name + "' " + e.Message);
            }
        }
    }
}
```

- [ ] **Step 4: Chạy test để thấy pass**

Run: `cd /e/Unity-WorkSpace/AgentPlay && bash Tools/unity-tests.sh`
Expected: `result="Passed"`, `failed="0"`.

- [ ] **Step 5: Commit**

```bash
git add Packages
git commit -m "feat(runtime): reflection action invoker with JSON argument conversion" -m "<trailers>"
```

---

### Task 7: ExecutorCore (Runtime)

**Files:**
- Create: `Packages/com.agentplay.core/Runtime/Execution/ExecutorCore.cs`
- Test: `Packages/com.agentplay.core/Tests/EditMode/ExecutorCoreTests.cs`

**Interfaces:**
- Consumes: `IActionRunner` (Task 6); `Plan`, `RuleThen`, `PlanStatuses` (Task 1); `ConditionParser`, `ConditionEvaluator`, `SnapshotContext` (Task 2, 3).
- Produces:
  - `ExecutorOutput { string Kind; string PlanId; int Step; string Reason; }`. `Kind` là một trong `PlanStatuses.*` hoặc `ExecutorOutputKinds.Waiting = "waiting"`.
  - `ExecutorCore(IActionRunner actions, string safeAction)`: `Load(Plan plan)`, `Stop()`, `IReadOnlyList<ExecutorOutput> Tick(double deltaTime, Func<JObject> snapshot, ICollection<string> events)`, `string PlanId`, `bool ControlTaken`.

Hành vi (spec M2 mục 5):
- Không có Plan: chạy action `Safe` mỗi frame, nhưng chỉ sau khi đã nhận Plan đầu tiên (`ControlTaken`). Snapshot không được gọi.
- Mỗi frame có Plan: cộng thời gian, hết `timeout_s` thì hỏng với lý do `timeout`. Đánh giá mọi luật; luật đầu tiên có sườn lên (khác luật đang chạy) thì bắn và phát `rule_fired` (`Step` là chỉ số luật, `Reason` là `then`). Có luật đang chạy thì chỉ chạy action của luật; xong thì làm theo `then`. Không có luật đang chạy thì chạy bước hiện tại; `Done` hoặc `until` đúng thì phát `step_done` và sang bước kế; hết bước thì `plan_done`.
- Mỗi lần Plan kết thúc (`plan_done` hoặc `plan_failed`), phát thêm một `waiting` ngay sau đó.
- Lý do hỏng: `timeout`, `action_failed:<tên>`, `action_error:<tên>: <thông điệp>`, `replan:<chỉ số luật>`, `abort:<chỉ số luật>`.
- `AgentActionContext`: `IsFirstFrame` đúng ở lần gọi đầu của mỗi lần bắt đầu action (bước mới, bước được làm lại sau `resume`, luật mới bắn). `Elapsed` cộng `deltaTime` từ lần gọi thứ hai.

- [ ] **Step 1: Viết test thất bại**

`Packages/com.agentplay.core/Tests/EditMode/ExecutorCoreTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class ExecutorCoreTests
    {
        sealed class FakeRunner : IActionRunner
        {
            public readonly Dictionary<string, Func<AgentActionContext, AgentActionStatus>> Behaviour =
                new Dictionary<string, Func<AgentActionContext, AgentActionStatus>>();
            public readonly List<string> Calls = new List<string>();
            public readonly List<bool> FirstFrames = new List<bool>();
            public readonly List<double> Elapsed = new List<double>();

            public bool Has(string action) => Behaviour.ContainsKey(action);

            public AgentActionStatus Run(string action, JObject args, AgentActionContext context)
            {
                Calls.Add(action);
                FirstFrames.Add(context.IsFirstFrame);
                Elapsed.Add(context.Elapsed);
                return Behaviour[action](context);
            }
        }

        FakeRunner _runner;
        ExecutorCore _core;
        JObject _state;
        HashSet<string> _events;
        int _snapshots;

        [SetUp]
        public void SetUp()
        {
            _runner = new FakeRunner();
            _runner.Behaviour["safe"] = _ => AgentActionStatus.Done;
            _core = new ExecutorCore(_runner, "safe");
            _state = new JObject { ["x"] = 0 };
            _events = new HashSet<string>();
            _snapshots = 0;
        }

        IReadOnlyList<ExecutorOutput> Tick(double dt = 0.1)
        {
            var outputs = _core.Tick(dt, () => { _snapshots++; return _state; }, _events);
            _events.Clear();
            return outputs;
        }

        static string Kinds(IEnumerable<ExecutorOutput> outputs) => string.Join(",", outputs.Select(o => o.Kind));

        static Plan Plan(params PlanStep[] steps) => new Plan { Id = "p", Steps = steps.ToList(), TimeoutSeconds = 60 };

        static PlanStep Step(string action, string until = null) => new PlanStep { Do = action, Until = until };

        static PlanRule Rule(string when, string action, string then) => new PlanRule { When = when, Do = action, Then = then };

        [Test]
        public void RunsStepsInOrderAndEndsWithDoneAndWaiting()
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Done;
            _runner.Behaviour["b"] = _ => AgentActionStatus.Done;
            _core.Load(Plan(Step("a"), Step("b")));

            Assert.AreEqual("step_done", Kinds(Tick()));
            var last = Tick();
            Assert.AreEqual("step_done,plan_done,waiting", Kinds(last));
            Assert.AreEqual(1, last[0].Step);
            Assert.AreEqual("p", last[1].PlanId);
            CollectionAssert.AreEqual(new[] { "a", "b" }, _runner.Calls);
            Assert.IsNull(_core.PlanId);
        }

        [Test]
        public void RunningActionGetsFirstFrameAndElapsed()
        {
            _runner.Behaviour["a"] = ctx => ctx.Elapsed >= 0.25 ? AgentActionStatus.Done : AgentActionStatus.Running;
            _core.Load(Plan(Step("a")));
            for (int i = 0; i < 3; i++) Assert.AreEqual("", Kinds(Tick()));
            Assert.AreEqual("step_done,plan_done,waiting", Kinds(Tick()));
            CollectionAssert.AreEqual(new[] { true, false, false, false }, _runner.FirstFrames);
            Assert.AreEqual(0.3, _runner.Elapsed[3], 1e-9);
        }

        [Test]
        public void UntilEndsAStepThatIsStillRunning()
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Running;
            _core.Load(Plan(Step("a", "x > 1")));
            Assert.AreEqual("", Kinds(Tick()));
            _state["x"] = 2;
            Assert.AreEqual("step_done,plan_done,waiting", Kinds(Tick()));
        }

        [Test]
        public void FailedActionFailsThePlan()
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Failed;
            _core.Load(Plan(Step("a")));
            var outputs = Tick();
            Assert.AreEqual("plan_failed,waiting", Kinds(outputs));
            Assert.AreEqual("action_failed:a", outputs[0].Reason);
        }

        [Test]
        public void ThrowingActionFailsThePlanWithItsMessage()
        {
            _runner.Behaviour["a"] = _ => throw new AgentActionException("boom");
            _core.Load(Plan(Step("a")));
            var outputs = Tick();
            Assert.AreEqual("plan_failed", outputs[0].Kind);
            Assert.AreEqual("action_error:a: boom", outputs[0].Reason);
        }

        [Test]
        public void TimeoutFailsThePlan()
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Running;
            var plan = Plan(Step("a"));
            plan.TimeoutSeconds = 0.3;
            _core.Load(plan);
            Assert.AreEqual("", Kinds(Tick(0.2)));
            var outputs = Tick(0.2);
            Assert.AreEqual("plan_failed,waiting", Kinds(outputs));
            Assert.AreEqual("timeout", outputs[0].Reason);
        }

        [Test]
        public void RuleFiresOnRisingEdgeAndResumeRestartsTheStep()
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Running;
            _runner.Behaviour["r"] = _ => AgentActionStatus.Done;
            var plan = Plan(Step("a"));
            plan.Rules.Add(Rule("x > 1", "r", "resume"));
            _core.Load(plan);

            Tick();                               // a (first frame)
            _state["x"] = 2;
            var fired = Tick();                   // rule fires, r runs and is done
            Assert.AreEqual("rule_fired", Kinds(fired));
            Assert.AreEqual(0, fired[0].Step);
            Assert.AreEqual("resume", fired[0].Reason);
            Tick();                               // still true: no new fire, a restarts
            _state["x"] = 0;
            Tick();
            _state["x"] = 2;
            Tick();                               // rising again
            CollectionAssert.AreEqual(new[] { "a", "r", "a", "a", "r" }, _runner.Calls);
            CollectionAssert.AreEqual(new[] { true, true, true, false, true }, _runner.FirstFrames);
        }

        [Test]
        public void RuleActionRunsAcrossFramesAndBlocksTheStep()
        {
            int ruleFrames = 0;
            _runner.Behaviour["a"] = _ => AgentActionStatus.Running;
            _runner.Behaviour["r"] = _ => ++ruleFrames < 3 ? AgentActionStatus.Running : AgentActionStatus.Done;
            var plan = Plan(Step("a"));
            plan.Rules.Add(Rule("x > 1", "r", "next"));
            _state["x"] = 2;
            _core.Load(plan);

            Assert.AreEqual("rule_fired", Kinds(Tick()));
            Assert.AreEqual("", Kinds(Tick()));
            Assert.AreEqual("step_done,plan_done,waiting", Kinds(Tick()));
            CollectionAssert.AreEqual(new[] { "r", "r", "r" }, _runner.Calls);
        }

        [TestCase("replan", "replan:0")]
        [TestCase("abort", "abort:0")]
        public void ReplanAndAbortEndThePlan(string then, string reason)
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Running;
            _runner.Behaviour["r"] = _ => AgentActionStatus.Done;
            var plan = Plan(Step("a"));
            plan.Rules.Add(Rule("event:hit", "r", then));
            _core.Load(plan);
            Tick();
            _events.Add("hit");
            var outputs = Tick();
            Assert.AreEqual("rule_fired,plan_failed,waiting", Kinds(outputs));
            Assert.AreEqual(reason, outputs[1].Reason);
        }

        [Test]
        public void FailingRuleActionFailsThePlan()
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Running;
            _runner.Behaviour["r"] = _ => AgentActionStatus.Failed;
            var plan = Plan(Step("a"));
            plan.Rules.Add(Rule("x == 0", "r", "resume"));
            _core.Load(plan);
            var outputs = Tick();
            Assert.AreEqual("rule_fired,plan_failed,waiting", Kinds(outputs));
            Assert.AreEqual("action_failed:r", outputs[1].Reason);
        }

        [Test]
        public void ConditionTrueAtPlanStartFiresImmediately()
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Running;
            _runner.Behaviour["r"] = _ => AgentActionStatus.Done;
            var plan = Plan(Step("a"));
            plan.Rules.Add(Rule("x == 0", "r", "resume"));
            _core.Load(plan);
            Assert.AreEqual("rule_fired", Kinds(Tick()));
        }

        [Test]
        public void SafeActionRunsOnlyAfterControlIsTaken()
        {
            Tick();
            CollectionAssert.IsEmpty(_runner.Calls);
            Assert.AreEqual(0, _snapshots);
            Assert.IsFalse(_core.ControlTaken);

            _runner.Behaviour["a"] = _ => AgentActionStatus.Done;
            _core.Load(Plan(Step("a")));
            Tick();
            Tick();
            CollectionAssert.AreEqual(new[] { "a", "safe" }, _runner.Calls);
            Assert.IsTrue(_core.ControlTaken);
        }

        [Test]
        public void StopSwitchesToTheSafeAction()
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Running;
            _core.Load(Plan(Step("a")));
            Tick();
            _core.Stop();
            Assert.AreEqual("", Kinds(Tick()));
            CollectionAssert.AreEqual(new[] { "a", "safe" }, _runner.Calls);
            Assert.IsNull(_core.PlanId);
        }

        [Test]
        public void LoadingANewPlanReplacesTheOldOne()
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Running;
            _runner.Behaviour["b"] = _ => AgentActionStatus.Running;
            _core.Load(Plan(Step("a")));
            Tick();
            var second = Plan(Step("b"));
            second.Id = "q";
            _core.Load(second);
            Tick();
            CollectionAssert.AreEqual(new[] { "a", "b" }, _runner.Calls);
            Assert.AreEqual("q", _core.PlanId);
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `cd /e/Unity-WorkSpace/AgentPlay && bash Tools/unity-tests.sh`
Expected: `error CS0246: The type or namespace name 'ExecutorCore' could not be found`.

- [ ] **Step 3: Viết code**

`Packages/com.agentplay.core/Runtime/Execution/ExecutorCore.cs`:

```csharp
using System;
using System.Collections.Generic;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;

namespace AgentPlay
{
    public sealed class ExecutorOutput
    {
        /// <summary>One of PlanStatuses.* or ExecutorOutputKinds.Waiting.</summary>
        public string Kind;
        public string PlanId;
        /// <summary>Step index, or rule index for rule_fired.</summary>
        public int Step;
        public string Reason;
    }

    public static class ExecutorOutputKinds
    {
        public const string Waiting = "waiting";
    }

    /// <summary>Runs one plan frame by frame (spec M2 section 5). Pure: no Unity calls, main thread only.</summary>
    public sealed class ExecutorCore
    {
        static readonly IReadOnlyList<ExecutorOutput> None = new ExecutorOutput[0];

        readonly IActionRunner _actions;
        readonly string _safeAction;
        readonly AgentActionContext _stepContext = new AgentActionContext();
        readonly AgentActionContext _ruleContext = new AgentActionContext();
        readonly AgentActionContext _safeContext = new AgentActionContext();

        Plan _plan;
        CondNode[] _until;
        CondNode[] _when;
        bool[] _ruleWasTrue;
        int _step;
        int _activeRule = -1;
        double _planTime;

        public ExecutorCore(IActionRunner actions, string safeAction)
        {
            _actions = actions;
            _safeAction = safeAction;
        }

        public string PlanId => _plan?.Id;

        /// <summary>True once any plan has been loaded; before that the safe action does not run.</summary>
        public bool ControlTaken { get; private set; }

        /// <summary>Loads a validated plan, replacing the current one.</summary>
        public void Load(Plan plan)
        {
            var rules = plan.Rules ?? new List<PlanRule>();
            _until = new CondNode[plan.Steps.Count];
            for (int i = 0; i < plan.Steps.Count; i++)
                _until[i] = string.IsNullOrWhiteSpace(plan.Steps[i].Until) ? null : ConditionParser.Parse(plan.Steps[i].Until);
            _when = new CondNode[rules.Count];
            for (int i = 0; i < rules.Count; i++) _when[i] = ConditionParser.Parse(rules[i].When);

            _plan = plan;
            _plan.Rules = rules;
            _ruleWasTrue = new bool[_when.Length];
            _step = 0;
            _activeRule = -1;
            _planTime = 0;
            Reset(_stepContext);
            ControlTaken = true;
        }

        public void Stop()
        {
            _plan = null;
            _activeRule = -1;
            Reset(_safeContext);
        }

        public IReadOnlyList<ExecutorOutput> Tick(double deltaTime, Func<JObject> snapshot, ICollection<string> events)
        {
            if (_plan == null)
            {
                RunSafe(deltaTime);
                return None;
            }

            var outputs = new List<ExecutorOutput>();
            _planTime += deltaTime;
            if (_planTime > _plan.TimeoutSeconds)
            {
                End(outputs, PlanStatuses.PlanFailed, "timeout");
                return outputs;
            }

            var context = new SnapshotContext(snapshot(), events);
            int fire = -1;
            for (int i = 0; i < _when.Length; i++)
            {
                bool now = ConditionEvaluator.Evaluate(_when[i], context);
                if (now && !_ruleWasTrue[i] && fire < 0 && i != _activeRule) fire = i;
                _ruleWasTrue[i] = now;
            }
            if (fire >= 0)
            {
                _activeRule = fire;
                Reset(_ruleContext);
                outputs.Add(new ExecutorOutput { Kind = PlanStatuses.RuleFired, PlanId = _plan.Id, Step = fire, Reason = _plan.Rules[fire].Then });
            }

            if (_activeRule >= 0) RunRule(deltaTime, outputs);
            else RunStep(deltaTime, context, outputs);
            return outputs;
        }

        void RunRule(double deltaTime, List<ExecutorOutput> outputs)
        {
            var rule = _plan.Rules[_activeRule];
            if (!TryRun(rule.Do, rule.Args, _ruleContext, deltaTime, out var status, out var error))
            {
                End(outputs, PlanStatuses.PlanFailed, error);
                return;
            }
            if (status == AgentActionStatus.Running) return;
            if (status == AgentActionStatus.Failed)
            {
                End(outputs, PlanStatuses.PlanFailed, "action_failed:" + rule.Do);
                return;
            }

            int index = _activeRule;
            _activeRule = -1;
            switch (rule.Then)
            {
                case RuleThen.Resume:
                    Reset(_stepContext);
                    return;
                case RuleThen.Next:
                    Advance(outputs);
                    return;
                case RuleThen.Replan:
                    End(outputs, PlanStatuses.PlanFailed, "replan:" + index);
                    return;
                default:
                    End(outputs, PlanStatuses.PlanFailed, "abort:" + index);
                    return;
            }
        }

        void RunStep(double deltaTime, SnapshotContext context, List<ExecutorOutput> outputs)
        {
            var step = _plan.Steps[_step];
            if (!TryRun(step.Do, step.Args, _stepContext, deltaTime, out var status, out var error))
            {
                End(outputs, PlanStatuses.PlanFailed, error);
                return;
            }
            if (status == AgentActionStatus.Failed)
            {
                End(outputs, PlanStatuses.PlanFailed, "action_failed:" + step.Do);
                return;
            }
            if (status == AgentActionStatus.Done || (_until[_step] != null && ConditionEvaluator.Evaluate(_until[_step], context)))
                Advance(outputs);
        }

        void Advance(List<ExecutorOutput> outputs)
        {
            outputs.Add(new ExecutorOutput { Kind = PlanStatuses.StepDone, PlanId = _plan.Id, Step = _step });
            _step++;
            Reset(_stepContext);
            if (_step >= _plan.Steps.Count) End(outputs, PlanStatuses.PlanDone, null);
        }

        void End(List<ExecutorOutput> outputs, string kind, string reason)
        {
            outputs.Add(new ExecutorOutput { Kind = kind, PlanId = _plan.Id, Step = _step, Reason = reason });
            outputs.Add(new ExecutorOutput { Kind = ExecutorOutputKinds.Waiting, PlanId = _plan.Id });
            Stop();
        }

        void RunSafe(double deltaTime)
        {
            if (!ControlTaken || _safeAction == null) return;
            if (TryRun(_safeAction, null, _safeContext, deltaTime, out var status, out _) && status != AgentActionStatus.Running)
                Reset(_safeContext);
        }

        bool TryRun(string action, JObject args, AgentActionContext context, double deltaTime, out AgentActionStatus status, out string error)
        {
            if (!context.IsFirstFrame) context.Elapsed += deltaTime;
            try
            {
                status = _actions.Run(action, args, context);
                error = null;
                return true;
            }
            catch (Exception e)
            {
                status = AgentActionStatus.Failed;
                error = "action_error:" + action + ": " + e.Message;
                return false;
            }
            finally
            {
                context.IsFirstFrame = false;
            }
        }

        static void Reset(AgentActionContext context)
        {
            context.IsFirstFrame = true;
            context.Elapsed = 0;
        }
    }
}
```

- [ ] **Step 4: Chạy test để thấy pass**

Run: `cd /e/Unity-WorkSpace/AgentPlay && bash Tools/unity-tests.sh`
Expected: `result="Passed"`, `failed="0"`.

- [ ] **Step 5: Commit**

```bash
git add Packages
git commit -m "feat(runtime): plan executor with rules, until conditions, timeout and safe action" -m "<trailers>"
```

---

### Task 8: SessionCore v2, trace và host executor (Runtime)

**Files:**
- Replace: `Packages/com.agentplay.core/Runtime/Session/StepPolicy.cs`
- Replace: `Packages/com.agentplay.core/Runtime/Session/SessionCore.cs`
- Modify: `Packages/com.agentplay.core/Runtime/Trace/TraceWriter.cs` (thêm 3 hàm)
- Create: `Packages/com.agentplay.core/Runtime/Execution/AgentExecutorHost.cs`
- Modify: `Packages/com.agentplay.core/Tests/EditMode/SessionCoreTests.cs` (4 test dùng sự kiện quan trọng)
- Modify: `Packages/com.agentplay.core/Tests/EditMode/TraceTests.cs` (thêm 1 test)
- Test: `Packages/com.agentplay.core/Tests/EditMode/SessionCorePlanTests.cs`

**Interfaces:**
- Consumes: `ExecutorOutput`, `ExecutorOutputKinds`, `ExecutorCore` (Task 7); `PlanValidator` (Task 4); message v2 (Task 1).
- Produces:
  - `PauseReason.Plan`; `StepPolicy.OnPlanEnded() : PauseReason`.
  - `interface IPlanExecutor { void Load(Plan plan); void Stop(); }`.
  - `SessionCore(HelloMessage hello, Func<JObject> snapshot, ISessionHost host, TraceWriter trace, double snapshotIntervalSeconds, IPlanExecutor executor = null)`; `SessionCore.OnFrameEnd(IReadOnlyList<ExecutorOutput> outputs)`.
  - `TraceWriter.WriteControl(string who, double t)`, `WritePlan(JObject plan, double t)`, `WritePlanStatus(PlanStatusMessage status, double t)`.
  - `AgentExecutorHost.Create(ExecutorCore core, Func<JObject> snapshot, Action<IReadOnlyList<ExecutorOutput>> onFrame) : AgentExecutorHost`; `AgentExecutorHost.Shutdown()`.

Thay đổi hành vi so với M1: `OnEvent` không còn dừng game ngay. Sự kiện quan trọng được ghi nhận và quyết định ở `OnFrameEnd`, khi biết frame đó có luật nào bắn không. Host executor gọi `OnFrameEnd` mỗi frame game chạy, kể cả khi chưa có Plan, nên chế độ theo lượt của M1 vẫn hoạt động.

- [ ] **Step 1: Sửa test M1 và viết test mới (thất bại)**

Trong `SessionCoreTests.cs`, thêm hàm hỗ trợ ngay dưới `static AgentEventRecord Ev(...)`:

```csharp
        void Frame() => _core.OnFrameEnd(new ExecutorOutput[0]);
```

Rồi thay toàn bộ bốn test sau:

```csharp
        [Test]
        public void ImportantEventPausesOnlyInTurnBasedAndAfterSendingTheEvent()
        {
            _core.OnEvent(Ev("e1", true));
            Frame();
            Assert.IsFalse(_host.Paused, "realtime never pauses");

            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _core.OnLine(Line(MessageTypes.Resume, new ResumeMessage { MaxGameSeconds = 0 }));
            _host.Sent.Clear();

            _core.OnEvent(Ev("minor", false));
            Frame();
            Assert.IsFalse(_host.Paused);
            _core.OnEvent(Ev("e2", true));
            Frame();
            Assert.IsTrue(_host.Paused);
            CollectionAssert.AreEqual(new[] { MessageTypes.Event, MessageTypes.Event, MessageTypes.Paused },
                _host.Sent.Select(e => e.Type).ToArray());
            Assert.AreEqual("event", _host.Sent[2].As<PausedMessage>().Reason);
        }

        [Test]
        public void SecondImportantEventWhileWaitingDoesNotSendAnotherPaused()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _core.OnEvent(Ev("a", true));
            Frame();
            _core.OnEvent(Ev("b", true));
            Frame();
            Assert.AreEqual(1, _host.OfType(MessageTypes.Paused).Count);
            Assert.IsTrue(_core.AwaitingResume);
        }

        [Test]
        public void ManualUnpauseClearsWaitingSoNextEventPausesAgain()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _host.Paused = false;          // user pressed the Editor pause button
            _core.Tick(true);
            Assert.IsFalse(_core.AwaitingResume);
            _core.OnEvent(Ev("a", true));
            Frame();
            Assert.IsTrue(_host.Paused);
            Assert.AreEqual(2, _host.OfType(MessageTypes.Paused).Count);
        }

        [Test]
        public void SetTurnBasedWhileWaitingAnnouncesThePauseAgain()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _core.OnEvent(Ev("a", true));
            Frame();
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            Assert.AreEqual(2, _host.OfType(MessageTypes.Paused).Count);
            Assert.IsTrue(_host.Paused);
        }
```

Thêm vào `TraceTests.cs`:

```csharp
        [Test]
        public void WritesControlPlanAndPlanStatusLines()
        {
            var sw = new StringWriter();
            using (var w = new TraceWriter(sw))
            {
                w.WriteControl("agent", 1.5);
                w.WritePlan(new JObject { ["id"] = "p1" }, 1.5);
                w.WritePlanStatus(new AgentPlay.Contract.PlanStatusMessage { PlanId = "p1", Status = "plan_done", Step = 2 }, 4.0);
            }
            var lines = Lines(sw);
            Assert.AreEqual("control", (string)lines[0]["type"]);
            Assert.AreEqual("agent", (string)lines[0]["who"]);
            Assert.AreEqual("p1", (string)lines[1]["plan"]["id"]);
            Assert.AreEqual("plan_status", (string)lines[2]["type"]);
            Assert.AreEqual("plan_done", (string)lines[2]["status"]);
            Assert.AreEqual(2, (int)lines[2]["step"]);
        }
```

Tạo `Packages/com.agentplay.core/Tests/EditMode/SessionCorePlanTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class SessionCorePlanTests
    {
        sealed class FakeHost : ISessionHost
        {
            public double GameTime { get; set; }
            public long Frame { get; set; }
            public bool Paused;
            public readonly List<Envelope> Sent = new List<Envelope>();
            public void SetPaused(bool paused) => Paused = paused;
            public void Send(string line) => Sent.Add(MessageCodec.Decode(line));
            public List<Envelope> OfType(string type) => Sent.Where(e => e.Type == type).ToList();
        }

        sealed class FakeExecutor : IPlanExecutor
        {
            public readonly List<Plan> Loaded = new List<Plan>();
            public int Stops;
            public void Load(Plan plan) => Loaded.Add(plan);
            public void Stop() => Stops++;
        }

        FakeHost _host;
        FakeExecutor _executor;
        StringWriter _trace;
        SessionCore _core;

        static HelloMessage Hello()
        {
            var h = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G" };
            h.Observes.Add(new ObserveInfo { Path = "x", ValueType = ValueKinds.Float });
            h.Actions.Add(new ActionInfo { Name = "go", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } } });
            h.Actions.Add(new ActionInfo { Name = "stop", Safe = true });
            return h;
        }

        [SetUp]
        public void SetUp()
        {
            _host = new FakeHost();
            _executor = new FakeExecutor();
            _trace = new StringWriter();
            _core = new SessionCore(Hello(), () => new JObject { ["x"] = 7 }, _host, new TraceWriter(_trace), 2.0, _executor);
        }

        static string Line(string type, object body) => MessageCodec.Encode(type, 1, body);

        const string ValidPlan = "{\"id\":\"p1\",\"steps\":[{\"do\":\"go\",\"args\":{\"zone\":\"a\"},\"until\":\"x > 1\"}]}";
        const string InvalidPlan = "{\"id\":\"p2\",\"steps\":[{\"do\":\"fly\"}]}";

        static string PlanLine(string planJson) => "{\"type\":\"plan\",\"seq\":1,\"plan\":" + planJson + "}";

        void TurnBasedRunning()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _core.OnLine(Line(MessageTypes.Resume, new ResumeMessage { MaxGameSeconds = 0 }));
            _host.Sent.Clear();
        }

        [Test]
        public void ValidPlanIsLoadedAndTracedWithControl()
        {
            _core.OnLine(PlanLine(ValidPlan));
            Assert.AreEqual("p1", _executor.Loaded.Single().Id);
            var text = _trace.ToString();
            StringAssert.Contains("\"type\":\"control\"", text);
            StringAssert.Contains("\"who\":\"agent\"", text);
            StringAssert.Contains("\"type\":\"plan\"", text);
            Assert.AreEqual(0, _host.OfType(MessageTypes.Error).Count);
        }

        [Test]
        public void InvalidPlanWhilePausedIsRejectedAndResumeDoesNotRunTheGame()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _core.OnLine(PlanLine(InvalidPlan));
            var status = _host.OfType(MessageTypes.PlanStatus).Single().As<PlanStatusMessage>();
            Assert.AreEqual(PlanStatuses.PlanFailed, status.Status);
            StringAssert.StartsWith("invalid: ", status.Reason);
            StringAssert.Contains("unknown action 'fly'", status.Reason);
            CollectionAssert.IsEmpty(_executor.Loaded);

            _core.OnLine(Line(MessageTypes.Resume, new ResumeMessage { MaxGameSeconds = 0 }));
            Assert.IsTrue(_host.Paused, "resume after an invalid plan must not run the game");
            var paused = _host.OfType(MessageTypes.Paused);
            Assert.AreEqual(2, paused.Count);
            Assert.AreEqual("plan", paused[1].As<PausedMessage>().Reason);

            _core.OnLine(PlanLine(ValidPlan));
            _core.OnLine(Line(MessageTypes.Resume, new ResumeMessage { MaxGameSeconds = 0 }));
            Assert.IsFalse(_host.Paused, "a valid plan clears the re-pause");
        }

        [Test]
        public void InvalidPlanWhileRunningPausesTurnBasedPlay()
        {
            TurnBasedRunning();
            _core.OnLine(PlanLine(InvalidPlan));
            Assert.IsTrue(_host.Paused);
            Assert.AreEqual("plan", _host.OfType(MessageTypes.Paused).Single().As<PausedMessage>().Reason);
        }

        [Test]
        public void PlanEndSendsStatusWaitingThenPauses()
        {
            TurnBasedRunning();
            _core.OnFrameEnd(new[]
            {
                new ExecutorOutput { Kind = PlanStatuses.PlanDone, PlanId = "p1", Step = 1 },
                new ExecutorOutput { Kind = ExecutorOutputKinds.Waiting, PlanId = "p1" },
            });
            CollectionAssert.AreEqual(new[] { MessageTypes.PlanStatus, MessageTypes.Waiting, MessageTypes.Paused },
                _host.Sent.Select(e => e.Type).ToArray());
            Assert.AreEqual("plan", _host.Sent[2].As<PausedMessage>().Reason);
            Assert.AreEqual(7, (int)_host.Sent[2].As<PausedMessage>().State["x"]);
            StringAssert.Contains("\"type\":\"plan_status\"", _trace.ToString());
        }

        [Test]
        public void PlanEndDoesNotPauseInRealtime()
        {
            _core.OnFrameEnd(new[] { new ExecutorOutput { Kind = PlanStatuses.PlanDone, PlanId = "p1" } });
            Assert.IsFalse(_host.Paused);
        }

        [Test]
        public void RuleFiredInTheSameFrameSuppressesTheImportantPause()
        {
            TurnBasedRunning();
            _core.OnEvent(new AgentEventRecord("hit", null, true, AgentOutcome.Bad, 0, 0));
            _core.OnFrameEnd(new[] { new ExecutorOutput { Kind = PlanStatuses.RuleFired, PlanId = "p1", Step = 0, Reason = "resume" } });
            Assert.IsFalse(_host.Paused);
            _core.OnFrameEnd(new ExecutorOutput[0]);
            Assert.IsFalse(_host.Paused, "the important flag is cleared after the frame");
        }

        [Test]
        public void StopPlanStopsTheExecutorAndReportsWaiting()
        {
            _core.OnLine(Line(MessageTypes.StopPlan, null));
            Assert.AreEqual(1, _executor.Stops);
            Assert.AreEqual(1, _host.OfType(MessageTypes.Waiting).Count);
        }

        [Test]
        public void PausedCarriesTheSnapshot()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            Assert.AreEqual(7, (int)_host.OfType(MessageTypes.Paused).Single().As<PausedMessage>().State["x"]);
        }

        [Test]
        public void SessionWithoutExecutorRejectsPlans()
        {
            var core = new SessionCore(Hello(), () => new JObject(), _host, null, 2.0);
            core.OnLine(PlanLine(ValidPlan));
            StringAssert.Contains("no plan executor", _host.OfType(MessageTypes.Error).Single().As<ErrorMessage>().Message);
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `cd /e/Unity-WorkSpace/AgentPlay && bash Tools/unity-tests.sh`
Expected: lỗi biên dịch `'SessionCore' does not contain a definition for 'OnFrameEnd'` và `The type or namespace name 'IPlanExecutor' could not be found`.

- [ ] **Step 3: Viết code**

Thay toàn bộ `Packages/com.agentplay.core/Runtime/Session/StepPolicy.cs`:

```csharp
using AgentPlay.Contract;

namespace AgentPlay
{
    public enum PauseReason { None, Mode, Event, Budget, Plan }

    /// <summary>Decides when turn-based play must stop and wait for the runner.</summary>
    public sealed class StepPolicy
    {
        double _budgetEnd = double.PositiveInfinity;

        public string Mode { get; private set; } = Modes.Realtime;

        public bool SetMode(string mode)
        {
            if (!Modes.IsKnown(mode)) return false;
            Mode = mode;
            _budgetEnd = double.PositiveInfinity;
            return true;
        }

        public PauseReason OnImportantEvent() => Stop(PauseReason.Event);

        /// <summary>A plan finished or failed (including a rejected plan).</summary>
        public PauseReason OnPlanEnded() => Stop(PauseReason.Plan);

        public PauseReason OnTick(double gameTime)
        {
            if (Mode != Modes.TurnBased || gameTime < _budgetEnd) return PauseReason.None;
            _budgetEnd = double.PositiveInfinity;
            return PauseReason.Budget;
        }

        public void OnResume(double gameTime, double maxGameSeconds)
        {
            _budgetEnd = maxGameSeconds > 0 ? gameTime + maxGameSeconds : double.PositiveInfinity;
        }

        PauseReason Stop(PauseReason reason)
        {
            if (Mode != Modes.TurnBased) return PauseReason.None;
            _budgetEnd = double.PositiveInfinity;
            return reason;
        }
    }
}
```

Thay toàn bộ `Packages/com.agentplay.core/Runtime/Session/SessionCore.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;

namespace AgentPlay
{
    public interface ISessionHost
    {
        double GameTime { get; }
        long Frame { get; }
        void SetPaused(bool paused);
        /// <summary>Sends one encoded line; drops it silently when no runner is connected.</summary>
        void Send(string line);
    }

    /// <summary>Protocol and turn-based logic for one Play-mode session. Main thread only.</summary>
    public sealed class SessionCore
    {
        public const int RecentLimit = 10;

        readonly HelloMessage _hello;
        readonly Func<JObject> _snapshot;
        readonly ISessionHost _host;
        readonly TraceWriter _trace;
        readonly IntervalTimer _snapshotTimer;
        readonly IPlanExecutor _executor;
        readonly StepPolicy _step = new StepPolicy();
        readonly SequenceCounter _seq = new SequenceCounter();
        readonly List<string> _recent = new List<string>();
        PauseReason _pauseReason;
        bool _importantPending;
        bool _agentControl;
        bool _repauseOnResume;

        public string Mode => _step.Mode;
        public int EventCount { get; private set; }
        public IReadOnlyList<string> RecentEvents => _recent;
        public bool AwaitingResume { get; private set; }

        public SessionCore(HelloMessage hello, Func<JObject> snapshot, ISessionHost host, TraceWriter trace,
            double snapshotIntervalSeconds, IPlanExecutor executor = null)
        {
            _hello = hello;
            _snapshot = snapshot;
            _host = host;
            _trace = trace;
            _executor = executor;
            _snapshotTimer = new IntervalTimer(snapshotIntervalSeconds);
        }

        /// <summary>Sends hello; a runner attaching while the game waits for resume also learns that it is paused.</summary>
        public void OnConnected()
        {
            Send(MessageTypes.Hello, _hello);
            if (AwaitingResume) AnnouncePause();
        }

        public void OnLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            Envelope env;
            try
            {
                env = MessageCodec.Decode(line);
            }
            catch (Exception e)   // ProtocolException, or any codec slip: never let runner input throw into the Editor
            {
                SendError(e.Message);
                return;
            }
            try
            {
                Dispatch(env);
            }
            catch (Exception e)
            {
                SendError("Failed to handle '" + env.Type + "': " + e.Message);
            }
        }

        public void OnEvent(AgentEventRecord rec)
        {
            EventCount++;
            _recent.Add(rec.Time.ToString("0.00", CultureInfo.InvariantCulture) + "s " + rec.Name + (rec.Important ? " !" : ""));
            if (_recent.Count > RecentLimit) _recent.RemoveAt(0);

            var state = _snapshot();
            _trace?.WriteEvent(rec, state);
            Send(MessageTypes.Event, new EventMessage
            {
                Name = rec.Name,
                T = rec.Time,
                Frame = rec.Frame,
                Important = rec.Important,
                Outcome = rec.Outcome.ToString().ToLowerInvariant(),
                Payload = rec.Payload,
                State = state,
            });
            if (rec.Important) _importantPending = true;   // decided in OnFrameEnd, once rules have had their chance
        }

        /// <summary>Called once per game frame after the executor ran; decides turn-based stops (spec M2 section 7).</summary>
        public void OnFrameEnd(IReadOnlyList<ExecutorOutput> outputs)
        {
            bool ended = false;
            bool ruleFired = false;
            double t = _host.GameTime;
            foreach (var o in outputs)
            {
                if (o.Kind == ExecutorOutputKinds.Waiting)
                {
                    Send(MessageTypes.Waiting, new WaitingMessage { T = t });
                    continue;
                }
                var status = new PlanStatusMessage { PlanId = o.PlanId, Status = o.Kind, Step = o.Step, Reason = o.Reason };
                _trace?.WritePlanStatus(status, t);
                Send(MessageTypes.PlanStatus, status);
                if (o.Kind == PlanStatuses.PlanDone || o.Kind == PlanStatuses.PlanFailed) ended = true;
                if (o.Kind == PlanStatuses.RuleFired) ruleFired = true;
            }

            bool important = _importantPending;
            _importantPending = false;
            var reason = PauseReason.None;
            if (ended) reason = _step.OnPlanEnded();
            else if (important && !ruleFired) reason = _step.OnImportantEvent();
            if (reason != PauseReason.None) Pause(reason);
        }

        /// <summary>Call every Editor update. gameRunning = Play mode and not paused.</summary>
        public void Tick(bool gameRunning)
        {
            if (!gameRunning) return;
            AwaitingResume = false;   // running means someone resumed, possibly by hand

            double t = _host.GameTime;
            if (_snapshotTimer.Tick(t)) _trace?.WriteSnapshot(t, _host.Frame, _snapshot());
            var reason = _step.OnTick(t);
            if (reason != PauseReason.None) Pause(reason);
        }

        void Dispatch(Envelope env)
        {
            switch (env.Type)
            {
                case MessageTypes.SetMode:
                    var mode = env.As<SetModeMessage>().Mode;
                    if (!_step.SetMode(mode))
                    {
                        SendError("Unknown mode '" + mode + "'");
                        return;
                    }
                    if (mode == Modes.TurnBased)
                    {
                        if (AwaitingResume) AnnouncePause();
                        else Pause(PauseReason.Mode);
                    }
                    else
                    {
                        AwaitingResume = false;
                        _repauseOnResume = false;
                        _host.SetPaused(false);
                    }
                    return;

                case MessageTypes.Resume:
                    var resume = env.As<ResumeMessage>();
                    if (_repauseOnResume)
                    {
                        _repauseOnResume = false;
                        AnnouncePause();   // the plan sent with this resume was rejected; stay paused
                        return;
                    }
                    _step.OnResume(_host.GameTime, resume.MaxGameSeconds);
                    AwaitingResume = false;
                    _host.SetPaused(false);
                    return;

                case MessageTypes.GetState:
                    Send(MessageTypes.State, new StateMessage { T = _host.GameTime, Frame = _host.Frame, State = _snapshot() });
                    return;

                case MessageTypes.Plan:
                    HandlePlan(env);
                    return;

                case MessageTypes.StopPlan:
                    _executor?.Stop();
                    Send(MessageTypes.Waiting, new WaitingMessage { T = _host.GameTime });
                    return;

                default:
                    SendError("Unknown message type '" + env.Type + "'");
                    return;
            }
        }

        void HandlePlan(Envelope env)
        {
            var plan = env.As<PlanMessage>().Plan;
            double t = _host.GameTime;
            var errors = PlanValidator.Validate(plan, _hello);
            if (errors.Count > 0)
            {
                var status = new PlanStatusMessage
                {
                    PlanId = plan?.Id,
                    Status = PlanStatuses.PlanFailed,
                    Step = 0,
                    Reason = "invalid: " + string.Join("; ", errors),
                };
                _trace?.WritePlanStatus(status, t);
                Send(MessageTypes.PlanStatus, status);
                if (AwaitingResume)
                {
                    _pauseReason = PauseReason.Plan;
                    _repauseOnResume = _step.Mode == Modes.TurnBased;
                }
                else
                {
                    var reason = _step.OnPlanEnded();
                    if (reason != PauseReason.None) Pause(reason);
                }
                return;
            }
            if (_executor == null)
            {
                SendError("This session has no plan executor");
                return;
            }
            _repauseOnResume = false;
            if (!_agentControl)
            {
                _agentControl = true;
                _trace?.WriteControl("agent", t);
            }
            _trace?.WritePlan(env.Body["plan"] as JObject, t);
            _executor.Load(plan);
        }

        void Pause(PauseReason reason)
        {
            if (AwaitingResume) return;
            AwaitingResume = true;
            _pauseReason = reason;
            _host.SetPaused(true);
            AnnouncePause();
        }

        void AnnouncePause()
        {
            Send(MessageTypes.Paused, new PausedMessage
            {
                Reason = _pauseReason.ToString().ToLowerInvariant(),
                T = _host.GameTime,
                State = _snapshot(),
            });
        }

        void SendError(string message) => Send(MessageTypes.Error, new ErrorMessage { Message = message });

        void Send<T>(string type, T dto) => _host.Send(MessageCodec.Encode(type, _seq.Next(), dto));
    }
}
```

Thêm vào `TraceWriter` (ngay trước `void WriteLine(JObject o)`), cùng `using AgentPlay.Contract;` ở đầu file:

```csharp
        /// <summary>Marks who controls the player from here on ("agent" once the runner sends its first plan).</summary>
        public void WriteControl(string who, double t)
        {
            WriteLine(new JObject { ["type"] = "control", ["t"] = t, ["who"] = who });
        }

        public void WritePlan(JObject plan, double t)
        {
            WriteLine(new JObject { ["type"] = "plan", ["t"] = t, ["plan"] = plan });
        }

        public void WritePlanStatus(PlanStatusMessage status, double t)
        {
            WriteLine(new JObject
            {
                ["type"] = "plan_status",
                ["t"] = t,
                ["plan_id"] = status.PlanId,
                ["status"] = status.Status,
                ["step"] = status.Step,
                ["reason"] = status.Reason,
            });
        }
```

`Packages/com.agentplay.core/Runtime/Execution/AgentExecutorHost.cs`:

```csharp
using System;
using System.Collections.Generic;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AgentPlay
{
    public interface IPlanExecutor
    {
        void Load(Plan plan);
        void Stop();
    }

    /// <summary>
    /// Hidden object that ticks the executor once per frame after game scripts, then reports the frame to the session.
    /// Created and destroyed by the Editor session controller.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [AddComponentMenu("")]
    public sealed class AgentExecutorHost : MonoBehaviour, IPlanExecutor
    {
        ExecutorCore _core;
        Func<JObject> _snapshot;
        Action<IReadOnlyList<ExecutorOutput>> _onFrame;
        HashSet<string> _collecting = new HashSet<string>();
        HashSet<string> _frameEvents = new HashSet<string>();

        public static AgentExecutorHost Create(ExecutorCore core, Func<JObject> snapshot, Action<IReadOnlyList<ExecutorOutput>> onFrame)
        {
            var go = new GameObject("AgentPlay Executor") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            var host = go.AddComponent<AgentExecutorHost>();
            host._core = core;
            host._snapshot = snapshot;
            host._onFrame = onFrame;
            AgentEvents.Emitted += host.OnEvent;
            return host;
        }

        void OnEvent(AgentEventRecord record) => _collecting.Add(record.Name);

        void Update()
        {
            if (_core == null) return;
            // Events emitted by actions during this tick are seen by rules on the next frame.
            var events = _collecting;
            _collecting = _frameEvents;
            _collecting.Clear();
            _frameEvents = events;
            var outputs = _core.Tick(Time.deltaTime, _snapshot, _frameEvents);
            _onFrame?.Invoke(outputs);
        }

        public void Load(Plan plan) => _core?.Load(plan);

        public void Stop() => _core?.Stop();

        public void Shutdown()
        {
            AgentEvents.Emitted -= OnEvent;
            _core = null;
            _onFrame = null;
            if (this != null) DestroyImmediate(gameObject);
        }
    }
}
```

- [ ] **Step 4: Chạy test để thấy pass**

Run: `cd /e/Unity-WorkSpace/AgentPlay && bash Tools/unity-tests.sh`
Expected: `result="Passed"`, `failed="0"`.

- [ ] **Step 5: Commit**

```bash
git add Packages
git commit -m "feat(runtime): plans in the session, per-frame stop decisions and executor host" -m "<trailers>"
```

---

### Task 9: Nối vào controller của Editor, kèm `brief` và `project_root`

Phần này nối vào API Editor, không có unit test riêng. Kiểm bằng toàn bộ test package và E2E M1 (có domain reload trong Play mode).

**Files:**
- Modify: `Packages/com.agentplay.core/Editor/AgentPlaySessionController.cs`

**Interfaces:**
- Consumes: `ActionInvoker` (Task 6), `ExecutorCore` (Task 7), `AgentExecutorHost`, `SessionCore` ctor mới (Task 8), `HelloMessage.Brief/ProjectRoot` (Task 1), `RegistryModel.SafeAction` (Task 5).
- Produces: log `[AgentPlay] No AgentPlay/brief.md in <root>; the agent gets no game description` khi thiếu brief.

- [ ] **Step 1: Sửa controller**

Thêm field cạnh các field tĩnh khác:

```csharp
        static AgentExecutorHost _executorHost;
```

Trong `Begin()`, thay đoạn từ `var hello = AgentRegistry.ToHello(...)` tới dòng tạo `_core` bằng:

```csharp
            var hello = AgentRegistry.ToHello(_model, Application.productName);
            hello.ProjectRoot = projectRoot;
            hello.Brief = ReadBrief(projectRoot);
            _trace.WriteHeader(hello.Game, hello.Version, Who, tag, DateTime.UtcNow);

            _server = new BridgeServer();
            Host.Server = _server;
            var executor = new ExecutorCore(new ActionInvoker(_model, _resolver.Resolve), _model.SafeAction);
            _executorHost = AgentExecutorHost.Create(executor, Snapshot, outputs => _core?.OnFrameEnd(outputs));
            _core = new SessionCore(hello, Snapshot, Host, _trace, SnapshotIntervalSeconds, _executorHost);
```

Trong `End()`, ngay sau `AgentEvents.Emitted -= OnEvent;`:

```csharp
            _executorHost?.Shutdown();
            _executorHost = null;
```

Thêm hàm:

```csharp
        static string ReadBrief(string projectRoot)
        {
            string path = Path.Combine(projectRoot, "AgentPlay", "brief.md");
            if (File.Exists(path)) return File.ReadAllText(path);
            Debug.Log("[AgentPlay] No AgentPlay/brief.md in " + projectRoot + "; the agent gets no game description");
            return "";
        }
```

- [ ] **Step 2: Chạy test package và E2E M1**

Run (timeout 600000 ms mỗi lệnh):

```bash
cd /e/Unity-WorkSpace/AgentPlay
bash Tools/unity-tests.sh
bash Tools/e2e-m1.sh
```

Expected: `result="Passed"`; E2E M1 `ALL CHECKS PASSED`. E2E M1 chứng minh: sự kiện quan trọng vẫn dừng game qua `OnFrameEnd` khi chưa có Plan, và host executor sống sót qua domain reload trong Play mode (check "no SocketException" và có hai lần `Bridge listening`).

Kiểm thêm log Unity không có `NullReferenceException`: dùng công cụ Grep tìm `NullReferenceException` trong `Testbed/Logs/autoplay.log`; Expected: không có kết quả.

- [ ] **Step 3: Commit**

```bash
git add Packages
git commit -m "feat(editor): wire executor host, brief and project root into the session" -m "<trailers>"
```

---

### Task 10: Runner với bộ não theo kịch bản

**Files:**
- Create: `Runner/Brains/BrainContext.cs`, `Runner/Brains/ScriptBrain.cs`
- Replace: `Runner/RunnerOptions.cs`, `Runner/RunnerSession.cs`, `Runner/Program.cs`
- Test: `Runner.Tests/RunnerBrainTests.cs`

**Interfaces:**
- Consumes: `PlanValidator` (Task 4), message v2 (Task 1).
- Produces:
  - `RunnerOptions.Brain : string` (null hoặc `"script"`), `RunnerOptions.ScriptPath : string`.
  - `BrainContext { HelloMessage Hello; JObject State; string PauseReason; List<EventMessage> RecentEvents; List<PlanStatusMessage> PlanStatuses; Plan LastPlan; int PlansSent; }`.
  - `interface IBrain { Task<Plan> NextPlanAsync(BrainContext context, CancellationToken ct); }`.
  - `ScriptBrain(IEnumerable<Plan> plans)`, `ScriptBrain.Load(string path)`, `ScriptBrain.Remaining`.
  - `RunnerSession`: `DecisionPending`, `GameResult` (`"won"` hoặc `"lost"`), `Finished`, `ExitCode`, `TakeDecisionContext()`, `OnPlan(Plan plan) : IReadOnlyList<Outgoing>`. Hằng `ExitWon = 0`, `ExitLost = 3`, `ExitBrainStopped = 4`, `ExitProtocol = 5`.
  - Log runner: `sent plan <id>: <intent>`, `plan_status <id> <status> step=<n> reason=<r>`, `waiting`, `game over: won|lost`, `brain has no more plans`, `finished with exit code <n>`. Observe danh sách in `  observe guards : list [distance:float, ...]`.

- [ ] **Step 1: Viết test thất bại**

`Runner.Tests/RunnerBrainTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AgentPlay.Contract;
using AgentPlay.Runner;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class RunnerBrainTests
    {
        readonly List<string> _printed = new List<string>();

        RunnerSession Session() => new RunnerSession(RunnerOptions.Parse(new[] { "--brain", "script", "win.json" }), _printed.Add);

        static Envelope Env<T>(string type, T body) => MessageCodec.Decode(MessageCodec.Encode(type, 1, body));

        static HelloMessage Hello(int protocol = ProtocolVersion.Current)
        {
            var h = new HelloMessage { Protocol = protocol, Game = "G", Version = "1" };
            h.Observes.Add(new ObserveInfo
            {
                Path = "guards",
                ValueType = ValueKinds.List,
                Fields = new List<ObserveInfo> { new ObserveInfo { Path = "distance", ValueType = ValueKinds.Float } },
            });
            h.Actions.Add(new ActionInfo { Name = "goto", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } } });
            h.Actions.Add(new ActionInfo { Name = "stop", Safe = true });
            h.Events.Add(new EventInfo { Name = ReservedEvents.GameWon, Outcome = "good" });
            return h;
        }

        static Plan GotoPlan(string id) => JObject.Parse(
            "{\"id\":\"" + id + "\",\"intent\":\"go\",\"steps\":[{\"do\":\"goto\",\"args\":{\"zone\":\"exit\"}}]}").ToObject<Plan>(ContractJson.Serializer);

        [Fact]
        public void BrainOptionForcesTurnBased()
        {
            var o = RunnerOptions.Parse(new[] { "--brain", "script", "win.json" });
            Assert.Equal("script", o.Brain);
            Assert.Equal("win.json", o.ScriptPath);
            Assert.Equal(Modes.TurnBased, o.Mode);
        }

        [Theory]
        [InlineData("--brain script", "--brain script needs a value")]
        [InlineData("--brain opus", "M2b")]
        [InlineData("--brain manual", "M2b")]
        [InlineData("--brain fancy", "Unknown brain")]
        [InlineData("--brain script f.json --mode realtime", "turn_based")]
        [InlineData("--brain script f.json --auto-resume", "--auto-resume")]
        public void InvalidBrainOptions(string commandLine, string expected)
        {
            var e = Assert.Throws<ArgumentException>(() => RunnerOptions.Parse(commandLine.Split(' ')));
            Assert.Contains(expected, e.Message);
        }

        [Fact]
        public void HelloPrintsListFieldsAndSetsTurnBased()
        {
            var s = Session();
            var outgoing = s.Handle(Env(MessageTypes.Hello, Hello()));
            Assert.Contains("  observe guards : list [distance:float]", _printed);
            Assert.Equal(MessageTypes.SetMode, Assert.Single(outgoing).Type);
        }

        [Fact]
        public void ProtocolMismatchFinishes()
        {
            var s = Session();
            s.Handle(Env(MessageTypes.Hello, Hello(protocol: 1)));
            Assert.True(s.Finished);
            Assert.Equal(RunnerSession.ExitProtocol, s.ExitCode);
        }

        [Fact]
        public void PausedRequestsADecisionWithTheState()
        {
            var s = Session();
            s.Handle(Env(MessageTypes.Hello, Hello()));
            var outgoing = s.Handle(Env(MessageTypes.Paused, new PausedMessage { Reason = "mode", T = 1, State = new JObject { ["x"] = 3 } }));
            Assert.Empty(outgoing);
            Assert.True(s.DecisionPending);
            var ctx = s.TakeDecisionContext();
            Assert.False(s.DecisionPending);
            Assert.Equal(3, (int)ctx.State["x"]);
            Assert.Equal("mode", ctx.PauseReason);
            Assert.Equal("G", ctx.Hello.Game);
        }

        [Fact]
        public void OnPlanSendsPlanThenResume()
        {
            var s = Session();
            s.Handle(Env(MessageTypes.Hello, Hello()));
            var outgoing = s.OnPlan(GotoPlan("p1"));
            Assert.Equal(new[] { MessageTypes.Plan, MessageTypes.Resume }, outgoing.Select(o => o.Type));
            Assert.Equal("p1", ((PlanMessage)outgoing[0].Body).Plan.Id);
            Assert.Equal(0.0, ((ResumeMessage)outgoing[1].Body).MaxGameSeconds);
            Assert.Contains("sent plan p1: go", _printed);
        }

        [Fact]
        public void InvalidPlanFinishesWithBrainStopped()
        {
            var s = Session();
            s.Handle(Env(MessageTypes.Hello, Hello()));
            var bad = JObject.Parse("{\"id\":\"b\",\"steps\":[{\"do\":\"fly\"}]}").ToObject<Plan>(ContractJson.Serializer);
            Assert.Empty(s.OnPlan(bad));
            Assert.True(s.Finished);
            Assert.Equal(RunnerSession.ExitBrainStopped, s.ExitCode);
            Assert.Contains(_printed, l => l.StartsWith("brain produced an invalid plan b:"));
        }

        [Fact]
        public void NoMorePlansFinishes()
        {
            var s = Session();
            s.Handle(Env(MessageTypes.Hello, Hello()));
            Assert.Empty(s.OnPlan(null));
            Assert.Equal(RunnerSession.ExitBrainStopped, s.ExitCode);
            Assert.Contains("brain has no more plans", _printed);
        }

        [Theory]
        [InlineData("game.won", "won", RunnerSession.ExitWon)]
        [InlineData("game.lost", "lost", RunnerSession.ExitLost)]
        public void GameEndThenPausedFinishes(string eventName, string result, int exitCode)
        {
            var s = Session();
            s.Handle(Env(MessageTypes.Hello, Hello()));
            s.Handle(Env(MessageTypes.Event, new EventMessage { Name = eventName, T = 5 }));
            s.Handle(Env(MessageTypes.Paused, new PausedMessage { Reason = "event", T = 5 }));
            Assert.True(s.Finished);
            Assert.False(s.DecisionPending);
            Assert.Equal(result, s.GameResult);
            Assert.Equal(exitCode, s.ExitCode);
            Assert.Contains("game over: " + result, _printed);
        }

        [Fact]
        public void RecentEventsAreCappedAndClearedAfterAPlan()
        {
            var s = Session();
            s.Handle(Env(MessageTypes.Hello, Hello()));
            for (int i = 0; i < RunnerSession.MaxRecentEvents + 5; i++)
                s.Handle(Env(MessageTypes.Event, new EventMessage { Name = "e" + i, T = i }));
            s.Handle(Env(MessageTypes.PlanStatus, new PlanStatusMessage { PlanId = "p0", Status = PlanStatuses.PlanDone, Step = 1 }));
            var ctx = s.TakeDecisionContext();
            Assert.Equal(RunnerSession.MaxRecentEvents, ctx.RecentEvents.Count);
            Assert.Equal("e5", ctx.RecentEvents[0].Name);
            Assert.Single(ctx.PlanStatuses);
            Assert.Contains("plan_status p0 plan_done step=1 reason=", _printed);
            s.OnPlan(GotoPlan("p1"));
            Assert.Empty(ctx.RecentEvents);
            Assert.Empty(ctx.PlanStatuses);
            Assert.Equal(1, ctx.PlansSent);
            Assert.Equal("p1", ctx.LastPlan.Id);
        }

        [Fact]
        public void ScriptBrainReturnsPlansInOrderThenNull()
        {
            var path = Path.Combine(Path.GetTempPath(), "agentplay-script-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{\"plans\":[{\"id\":\"a\",\"steps\":[{\"do\":\"stop\"}]},{\"id\":\"b\",\"steps\":[{\"do\":\"stop\"}]}]}");
            try
            {
                var brain = ScriptBrain.Load(path);
                Assert.Equal(2, brain.Remaining);
                Assert.Equal("a", brain.NextPlanAsync(new BrainContext(), CancellationToken.None).Result.Id);
                Assert.Equal("b", brain.NextPlanAsync(new BrainContext(), CancellationToken.None).Result.Id);
                Assert.Null(brain.NextPlanAsync(new BrainContext(), CancellationToken.None).Result);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ScriptBrainRejectsFilesWithoutPlans()
        {
            var path = Path.Combine(Path.GetTempPath(), "agentplay-script-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{\"steps\":[]}");
            try
            {
                Assert.Throws<InvalidDataException>(() => ScriptBrain.Load(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: build FAIL với `The type or namespace name 'Brains' does not exist in the namespace 'AgentPlay.Runner'`.

- [ ] **Step 3: Viết code**

`Runner/Brains/BrainContext.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner.Brains
{
    /// <summary>What the runner knows when it must choose the next plan.</summary>
    public sealed class BrainContext
    {
        public HelloMessage Hello;
        /// <summary>Snapshot carried by the paused message that asked for this decision.</summary>
        public JObject State;
        public string PauseReason;
        /// <summary>Events since the previous plan was sent, oldest first.</summary>
        public readonly List<EventMessage> RecentEvents = new List<EventMessage>();
        /// <summary>plan_status messages since the previous plan was sent.</summary>
        public readonly List<PlanStatusMessage> PlanStatuses = new List<PlanStatusMessage>();
        public Plan LastPlan;
        public int PlansSent;
    }

    public interface IBrain
    {
        /// <summary>Returns the next plan, or null when the brain has nothing more to do.</summary>
        Task<Plan> NextPlanAsync(BrainContext context, CancellationToken ct);
    }
}
```

`Runner/Brains/ScriptBrain.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner.Brains
{
    /// <summary>Plays a fixed list of plans from a JSON file {"plans": [...]}, in order. For tests without a model.</summary>
    public sealed class ScriptBrain : IBrain
    {
        readonly Queue<Plan> _plans;

        public ScriptBrain(IEnumerable<Plan> plans)
        {
            _plans = new Queue<Plan>(plans);
        }

        public int Remaining => _plans.Count;

        public static ScriptBrain Load(string path)
        {
            var root = JObject.Parse(File.ReadAllText(path));
            if (!(root["plans"] is JArray array)) throw new InvalidDataException(path + ": expected {\"plans\": [...]}");
            var plans = new List<Plan>();
            foreach (var item in array) plans.Add(item.ToObject<Plan>(ContractJson.Serializer));
            return new ScriptBrain(plans);
        }

        public Task<Plan> NextPlanAsync(BrainContext context, CancellationToken ct) =>
            Task.FromResult(_plans.Count > 0 ? _plans.Dequeue() : null);
    }
}
```

Thay toàn bộ `Runner/RunnerOptions.cs`:

```csharp
using System;
using System.Globalization;
using AgentPlay.Contract;

namespace AgentPlay.Runner
{
    public sealed class RunnerOptions
    {
        public const string Usage =
            "Usage: AgentPlay.Runner [--brain script <file>] [--mode turn_based|realtime] [--auto-resume] " +
            "[--resume-seconds N] [--think-ms N] [--ports A-B] [--idle-exit SECONDS]";

        public string Host = "127.0.0.1";
        public int FirstPort = 47800;
        public int LastPort = 47809;
        public string Mode;
        public bool AutoResume;
        public double ResumeSeconds = 3;
        public int ThinkMs;
        public double IdleExitSeconds;
        /// <summary>null keeps the M1 behaviour (print and optionally auto-resume); "script" plays plans from a file.</summary>
        public string Brain;
        public string ScriptPath;

        public static RunnerOptions Parse(string[] args)
        {
            var o = new RunnerOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--brain":
                        var brain = Value(args, ref i, a);
                        if (brain == "script")
                        {
                            o.Brain = brain;
                            o.ScriptPath = Value(args, ref i, "--brain script");
                        }
                        else if (brain == "opus" || brain == "manual")
                        {
                            throw new ArgumentException("--brain " + brain + " is not available yet (arrives in M2b)");
                        }
                        else
                        {
                            throw new ArgumentException("Unknown brain: " + brain);
                        }
                        break;
                    case "--mode":
                        o.Mode = Value(args, ref i, a);
                        if (!Modes.IsKnown(o.Mode)) throw new ArgumentException("Unknown mode: " + o.Mode);
                        break;
                    case "--auto-resume":
                        o.AutoResume = true;
                        break;
                    case "--resume-seconds":
                        o.ResumeSeconds = Number(Value(args, ref i, a), a);
                        break;
                    case "--think-ms":
                        o.ThinkMs = (int)Number(Value(args, ref i, a), a);
                        break;
                    case "--idle-exit":
                        o.IdleExitSeconds = Number(Value(args, ref i, a), a);
                        break;
                    case "--ports":
                        var parts = Value(args, ref i, a).Split('-');
                        if (parts.Length != 2) throw new ArgumentException("--ports expects A-B");
                        o.FirstPort = (int)Number(parts[0], a);
                        o.LastPort = (int)Number(parts[1], a);
                        if (o.FirstPort > o.LastPort) throw new ArgumentException("--ports: first port is greater than last port");
                        break;
                    default:
                        throw new ArgumentException("Unknown argument: " + a);
                }
            }
            if (o.Brain != null)
            {
                if (o.Mode == Modes.Realtime) throw new ArgumentException("--brain plays in turn_based mode only");
                if (o.AutoResume) throw new ArgumentException("--brain decides when to resume; drop --auto-resume");
                o.Mode = Modes.TurnBased;
            }
            return o;
        }

        static string Value(string[] args, ref int i, string name)
        {
            if (i + 1 >= args.Length) throw new ArgumentException(name + " needs a value");
            return args[++i];
        }

        static double Number(string text, string name)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || d < 0)
                throw new ArgumentException(name + " expects a non-negative number, got '" + text + "'");
            return d;
        }
    }
}
```

Thay toàn bộ `Runner/RunnerSession.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AgentPlay.Contract;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner
{
    public sealed class Outgoing
    {
        public string Type;
        public object Body;
        public int DelayMs;
    }

    /// <summary>Reacts to Unity messages. Pure: printing, sending and asking the brain are done by the caller.</summary>
    public sealed class RunnerSession
    {
        public const int MaxRecentEvents = 20;
        public const int ExitWon = 0;
        public const int ExitLost = 3;
        public const int ExitBrainStopped = 4;
        public const int ExitProtocol = 5;

        readonly RunnerOptions _options;
        readonly Action<string> _print;
        readonly BrainContext _context = new BrainContext();

        public bool HasHello { get; private set; }
        public HelloMessage Hello { get; private set; }
        public bool DecisionPending { get; private set; }
        /// <summary>"won" or "lost" once the game emitted game.won / game.lost.</summary>
        public string GameResult { get; private set; }
        public bool Finished { get; private set; }
        public int ExitCode { get; private set; }

        bool BrainMode => _options.Brain != null;

        public RunnerSession(RunnerOptions options, Action<string> print)
        {
            _options = options;
            _print = print;
        }

        public void Reset()
        {
            HasHello = false;
            Hello = null;
            DecisionPending = false;
        }

        public IReadOnlyList<Outgoing> Handle(Envelope env)
        {
            var outgoing = new List<Outgoing>();
            try
            {
                Dispatch(env, outgoing);
            }
            catch (Exception e) when (e is JsonException || e is FormatException || e is InvalidCastException || e is ArgumentException)
            {
                // Field type mismatches from Unity must not crash the runner.
                _print("bad '" + env.Type + "' message from Unity: " + e.Message);
                outgoing.Clear();
            }
            return outgoing;
        }

        public BrainContext TakeDecisionContext()
        {
            DecisionPending = false;
            return _context;
        }

        /// <summary>Turns the brain's answer into messages: the plan, then resume. Null or invalid plans finish the run.</summary>
        public IReadOnlyList<Outgoing> OnPlan(Plan plan)
        {
            if (plan == null)
            {
                _print("brain has no more plans");
                Finish(ExitBrainStopped);
                return new Outgoing[0];
            }
            var errors = PlanValidator.Validate(plan, Hello);
            if (errors.Count > 0)
            {
                _print("brain produced an invalid plan " + plan.Id + ": " + string.Join("; ", errors));
                Finish(ExitBrainStopped);
                return new Outgoing[0];
            }
            _context.PlansSent++;
            _context.LastPlan = plan;
            _context.RecentEvents.Clear();
            _context.PlanStatuses.Clear();
            _print("sent plan " + plan.Id + ": " + plan.Intent);
            return new[]
            {
                new Outgoing { Type = MessageTypes.Plan, Body = new PlanMessage { Plan = plan }, DelayMs = _options.ThinkMs },
                new Outgoing { Type = MessageTypes.Resume, Body = new ResumeMessage { MaxGameSeconds = 0 } },
            };
        }

        void Dispatch(Envelope env, List<Outgoing> outgoing)
        {
            switch (env.Type)
            {
                case MessageTypes.Hello:
                    Hello = env.As<HelloMessage>();
                    HasHello = true;
                    _context.Hello = Hello;
                    PrintHello();
                    if (Hello.Protocol != ProtocolVersion.Current)
                    {
                        _print("WARNING: Unity speaks protocol " + Hello.Protocol + ", runner speaks " + ProtocolVersion.Current);
                        if (BrainMode)
                        {
                            Finish(ExitProtocol);
                            break;
                        }
                    }
                    if (_options.Mode != null)
                        outgoing.Add(new Outgoing { Type = MessageTypes.SetMode, Body = new SetModeMessage { Mode = _options.Mode } });
                    break;

                case MessageTypes.Event:
                    var e = env.As<EventMessage>();
                    _print("event t=" + Fmt(e.T) + " frame=" + e.Frame + " " + e.Name + (e.Important ? " (important)" : "") + " payload=" + Compact(e.Payload));
                    _context.RecentEvents.Add(e);
                    if (_context.RecentEvents.Count > MaxRecentEvents) _context.RecentEvents.RemoveAt(0);
                    if (e.Name == ReservedEvents.GameWon) GameResult = "won";
                    else if (e.Name == ReservedEvents.GameLost) GameResult = "lost";
                    break;

                case MessageTypes.Paused:
                    var p = env.As<PausedMessage>();
                    _print("paused t=" + Fmt(p.T) + " reason=" + p.Reason);
                    if (BrainMode)
                    {
                        if (GameResult != null)
                        {
                            _print("game over: " + GameResult);
                            Finish(GameResult == "won" ? ExitWon : ExitLost);
                        }
                        else
                        {
                            _context.State = p.State;
                            _context.PauseReason = p.Reason;
                            DecisionPending = true;
                        }
                    }
                    else if (_options.AutoResume)
                    {
                        outgoing.Add(new Outgoing
                        {
                            Type = MessageTypes.Resume,
                            Body = new ResumeMessage { MaxGameSeconds = _options.ResumeSeconds },
                            DelayMs = _options.ThinkMs,
                        });
                    }
                    break;

                case MessageTypes.PlanStatus:
                    var s = env.As<PlanStatusMessage>();
                    _print("plan_status " + s.PlanId + " " + s.Status + " step=" + s.Step + " reason=" + s.Reason);
                    _context.PlanStatuses.Add(s);
                    break;

                case MessageTypes.Waiting:
                    _print("waiting");
                    break;

                case MessageTypes.State:
                    var st = env.As<StateMessage>();
                    _print("state t=" + Fmt(st.T) + " " + Compact(st.State));
                    break;

                case MessageTypes.Error:
                    _print("unity error: " + env.As<ErrorMessage>().Message);
                    break;

                default:
                    _print("unknown message '" + env.Type + "'");
                    break;
            }
        }

        void PrintHello()
        {
            _print("hello: game=" + Hello.Game + " version=" + Hello.Version + " observes=" + Hello.Observes.Count + " actions=" + Hello.Actions.Count);
            foreach (var o in Hello.Observes)
            {
                var fields = o.Fields != null && o.Fields.Count > 0
                    ? " [" + string.Join(", ", o.Fields.Select(f => f.Path + ":" + f.ValueType)) + "]"
                    : "";
                _print("  observe " + o.Path + " : " + o.ValueType + fields);
            }
            foreach (var a in Hello.Actions)
                _print("  action " + a.Name + "(" + string.Join(", ", a.Params.Select(x => x.Name + ":" + x.ValueType)) + ")" + (a.Safe ? " [safe]" : ""));
            if (Hello.Events != null)
                foreach (var ev in Hello.Events)
                    _print("  event " + ev.Name + (ev.Important ? " (important)" : ""));
            if (BrainMode && string.IsNullOrWhiteSpace(Hello.Brief))
                _print("WARNING: the game sent no brief (AgentPlay/brief.md)");
        }

        void Finish(int code)
        {
            Finished = true;
            ExitCode = code;
            DecisionPending = false;
        }

        static string Fmt(double d) => d.ToString("0.00", CultureInfo.InvariantCulture);

        static string Compact(JObject o) => o == null ? "null" : o.ToString(Formatting.None);
    }
}
```

Thay toàn bộ `Runner/Program.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AgentPlay.Contract;
using AgentPlay.Runner.Brains;

namespace AgentPlay.Runner
{
    public static class Program
    {
        static readonly object PrintLock = new object();
        static readonly SequenceCounter Seq = new SequenceCounter();
        static volatile BridgeClient _current;

        public static async Task<int> Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            RunnerOptions options;
            try
            {
                options = RunnerOptions.Parse(args);
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine(RunnerOptions.Usage);
                return 2;
            }

            IBrain brain = null;
            if (options.Brain == "script")
            {
                try
                {
                    var script = ScriptBrain.Load(options.ScriptPath);
                    Log("script brain loaded " + script.Remaining + " plans from " + options.ScriptPath);
                    brain = script;
                }
                catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is Newtonsoft.Json.JsonException)
                {
                    Console.Error.WriteLine("Cannot load script " + options.ScriptPath + ": " + e.Message);
                    return 2;
                }
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            _ = Task.Run(() => ReadKeyboard(cts));

            var session = new RunnerSession(options, Log);
            var idle = Stopwatch.StartNew();
            bool everConnected = false;
            Log("runner started; looking for Unity on " + options.Host + ":" + options.FirstPort + "-" + options.LastPort);

            while (!cts.IsCancellationRequested && !session.Finished)
            {
                if (everConnected && options.IdleExitSeconds > 0 && idle.Elapsed.TotalSeconds >= options.IdleExitSeconds)
                {
                    Log("no Unity connection for " + options.IdleExitSeconds + " s; exiting");
                    break;
                }

                BridgeClient client;
                try
                {
                    client = await BridgeClient.TryConnectAsync(options.Host, options.FirstPort, options.LastPort, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                if (client == null)
                {
                    try { await Task.Delay(1000, cts.Token); } catch (OperationCanceledException) { break; }
                    continue;
                }

                everConnected = true;
                _current = client;
                session.Reset();
                Log("connected to " + options.Host + ":" + client.Port);
                _ = WarnIfNoHello(session, client);

                try
                {
                    await foreach (var line in client.ReadLinesAsync(cts.Token))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        Envelope env;
                        try
                        {
                            env = MessageCodec.Decode(line);
                        }
                        catch (ProtocolException e)
                        {
                            Log("bad message from Unity: " + e.Message);
                            continue;
                        }
                        foreach (var msg in session.Handle(env)) await Send(client, msg, cts.Token);
                        if (session.DecisionPending && brain != null)
                        {
                            var plan = await brain.NextPlanAsync(session.TakeDecisionContext(), cts.Token);
                            foreach (var msg in session.OnPlan(plan)) await Send(client, msg, cts.Token);
                        }
                        if (session.Finished) break;
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (IOException e)
                {
                    Log("connection error: " + e.Message);
                }

                _current = null;
                client.Dispose();
                idle.Restart();
                if (!cts.IsCancellationRequested && !session.Finished) Log("disconnected; waiting for Unity Play mode");
            }

            if (session.Finished)
            {
                Log("finished with exit code " + session.ExitCode);
                return session.ExitCode;
            }
            return 0;
        }

        static async Task Send(BridgeClient client, Outgoing msg, CancellationToken ct)
        {
            if (msg.DelayMs > 0) await Task.Delay(msg.DelayMs, ct);
            await client.SendAsync(MessageCodec.Encode(msg.Type, Seq.Next(), msg.Body), ct);
        }

        static async Task WarnIfNoHello(RunnerSession session, BridgeClient client)
        {
            await Task.Delay(5000);
            if (!session.HasHello && _current == client)
                Log("WARNING: connected but no hello after 5 s (is another runner attached to this port?)");
        }

        static async Task ReadKeyboard(CancellationTokenSource cts)
        {
            string line;
            while ((line = Console.ReadLine()) != null)
            {
                var client = _current;
                try
                {
                    switch (line.Trim())
                    {
                        case "q":
                            cts.Cancel();
                            return;
                        case "s":
                            if (client != null) await client.SendAsync(MessageCodec.Encode(MessageTypes.GetState, Seq.Next(), (object)null), cts.Token);
                            break;
                        case "r":
                            if (client != null) await client.SendAsync(MessageCodec.Encode(MessageTypes.Resume, Seq.Next(), new ResumeMessage { MaxGameSeconds = 0 }), cts.Token);
                            break;
                    }
                }
                catch (Exception e) when (e is IOException || e is ObjectDisposedException || e is OperationCanceledException)
                {
                    Log("keyboard command failed: " + e.Message);
                }
            }
        }

        static void Log(string message)
        {
            lock (PrintLock)
                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "] " + message);
        }
    }
}
```

- [ ] **Step 4: Chạy test để thấy pass**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: PASS, `Failed: 0`. Test M1 (`RunnerSessionTests`, `RunnerOptionsTests`) cũng phải pass vì hành vi M1 giữ nguyên khi không có `--brain`.

- [ ] **Step 5: Commit**

```bash
git add Runner Runner.Tests
git commit -m "feat(runner): brain interface, script brain and turn-based decision loop" -m "<trailers>"
```

---

### Task 11: Kiểm tra tính tổng quát và script gắn package

**Files:**
- Create: `Tools/check-generic.sh`
- Create: `Tools/link-agentplay.ps1`
- Create: `Tools/test-link-agentplay.sh`

**Interfaces:**
- Produces: `bash Tools/check-generic.sh` (exit 0 khi sạch, in `check-generic: OK`); `powershell -File Tools/link-agentplay.ps1 -ProjectPath <project>`; `bash Tools/test-link-agentplay.sh` (exit 0 khi pass).

**Cảnh báo an toàn:** junction trỏ vào `Packages/com.agentplay.core` thật. Không bao giờ dùng `rm -rf` lên junction, vì trong Git Bash lệnh đó có thể xóa nội dung đích. Gỡ junction chỉ bằng `cmd //c rmdir "<đường dẫn junction>"`.

- [ ] **Step 1: Viết test thất bại**

`Tools/test-link-agentplay.sh`:

```bash
#!/usr/bin/env bash
# Tests Tools/link-agentplay.ps1 against a throwaway fake Unity project.
# The junction is removed with "cmd //c rmdir" only: rm -rf could follow it into the real package.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TMP="$(mktemp -d)"
PROJECT="$TMP/FakeGame"
mkdir -p "$PROJECT/Packages"
echo '{"dependencies":{}}' > "$PROJECT/Packages/manifest.json"
printf 'Library/' > "$PROJECT/.gitignore"
fail=0
check() { if eval "$1"; then echo "PASS $2"; else echo "FAIL $2"; fail=1; fi; }

LINK_PS="$(cygpath -w "$ROOT/Tools/link-agentplay.ps1")"
PROJECT_W="$(cygpath -w "$PROJECT")"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$PROJECT_W"
check '[ $? -eq 0 ]' "first link succeeds"
check '[ -f "$PROJECT/Packages/com.agentplay.core/package.json" ]' "junction exposes the package"
check '[ "$(grep -c "^/Packages/com.agentplay.core$" "$PROJECT/.gitignore")" -eq 1 ]' "gitignore has the junction once"
check 'grep -q "^Library/$" "$PROJECT/.gitignore"' "existing gitignore line kept"
check '[ -f "$PROJECT/AgentPlay/brief.md" ]' "brief template created"

powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$PROJECT_W"
check '[ $? -eq 0 ]' "second link is idempotent"
check '[ "$(grep -c "^/Packages/com.agentplay.core$" "$PROJECT/.gitignore")" -eq 1 ]' "gitignore still has the line once"

mkdir -p "$TMP/NotUnity"
powershell -NoProfile -ExecutionPolicy Bypass -File "$LINK_PS" -ProjectPath "$(cygpath -w "$TMP/NotUnity")" 2>/dev/null
check '[ $? -ne 0 ]' "refuses a folder without Packages/manifest.json"

cmd //c rmdir "$(cygpath -w "$PROJECT/Packages/com.agentplay.core")"
check '[ -f "$ROOT/Packages/com.agentplay.core/package.json" ]' "real package untouched after unlinking"
rm -rf "$TMP"
exit $fail
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `cd /e/Unity-WorkSpace/AgentPlay && bash Tools/test-link-agentplay.sh`
Expected: các dòng `FAIL` vì `link-agentplay.ps1` chưa tồn tại (PowerShell báo không tìm thấy file), exit code khác 0. Dòng `real package untouched` vẫn phải `PASS`.

- [ ] **Step 3: Viết script**

`Tools/link-agentplay.ps1`:

```powershell
<#
.SYNOPSIS
  Embeds the AgentPlay package into a Unity project through a git-ignored junction, without editing manifest.json.
.EXAMPLE
  powershell -File Tools/link-agentplay.ps1 -ProjectPath E:\Unity-WorkSpace\MyGame
#>
param([Parameter(Mandatory = $true)][string]$ProjectPath)
$ErrorActionPreference = 'Stop'

$package = (Resolve-Path (Join-Path $PSScriptRoot '..\Packages\com.agentplay.core')).Path
$project = (Resolve-Path $ProjectPath).Path
if (-not (Test-Path (Join-Path $project 'Packages\manifest.json'))) {
    throw "$project is not a Unity project (no Packages\manifest.json)"
}

$link = Join-Path $project 'Packages\com.agentplay.core'
if (Test-Path $link) {
    $item = Get-Item $link -Force
    $target = @($item.Target) | Select-Object -First 1
    if ($item.LinkType -ne 'Junction' -or ((Resolve-Path $target).Path -ne $package)) {
        throw "$link already exists and is not a junction to $package"
    }
    Write-Host "Already linked: $link"
} else {
    New-Item -ItemType Junction -Path $link -Target $package | Out-Null
    Write-Host "Linked $link -> $package"
}

$gitignore = Join-Path $project '.gitignore'
$line = '/Packages/com.agentplay.core'
$present = (Test-Path $gitignore) -and (Select-String -Path $gitignore -SimpleMatch -Pattern $line -Quiet)
if (-not $present) {
    $text = "`n# AgentPlay: local junction created by AgentPlay/Tools/link-agentplay.ps1`n$line`n"
    [System.IO.File]::AppendAllText($gitignore, $text, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Added $line to .gitignore"
}

$briefDir = Join-Path $project 'AgentPlay'
New-Item -ItemType Directory -Force $briefDir | Out-Null
$brief = Join-Path $briefDir 'brief.md'
if (-not (Test-Path $brief)) {
    $template = "# Brief`n`nGoal: ...`nHow to win: ...`nHow to lose: ...`nTips: ...`n"
    [System.IO.File]::WriteAllText($brief, $template, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Created $brief (fill it in: the agent reads it every session)"
}
```

`Tools/check-generic.sh`:

```bash
#!/usr/bin/env bash
# Fails when shared code (package Contract/Runtime/Editor and the runner) names a specific game (spec M2 section 2).
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
hits=$(grep -rniE "tilethieves|testbed|heist" \
  "$ROOT/Packages/com.agentplay.core/Contract" \
  "$ROOT/Packages/com.agentplay.core/Runtime" \
  "$ROOT/Packages/com.agentplay.core/Editor" \
  "$ROOT/Runner" \
  --include=*.cs --include=*.json --include=*.asmdef --include=*.csproj \
  | grep -v "/bin/" | grep -v "/obj/")
if [ -n "$hits" ]; then
  echo "Game-specific references in shared code:"
  echo "$hits"
  exit 1
fi
echo "check-generic: OK"
```

- [ ] **Step 4: Chạy test để thấy pass**

Run:

```bash
cd /e/Unity-WorkSpace/AgentPlay
bash Tools/test-link-agentplay.sh
bash Tools/check-generic.sh
```

Expected: mọi dòng `PASS`, exit 0; `check-generic: OK`.

- [ ] **Step 5: Commit**

```bash
git add Tools/check-generic.sh Tools/link-agentplay.ps1 Tools/test-link-agentplay.sh
git commit -m "chore: generic-code check and a script that links AgentPlay into any Unity project" -m "<trailers>"
```

---

### Task 12: Game demo Heist, E2E theo kịch bản, đồng bộ spec

**Files:**
- Create: project Unity `Demos/Heist/` (qua `-createProject`), `Demos/Heist/Packages/manifest.json`
- Create: `Demos/Heist/Assets/Heist/HeistLevel.cs`, `HeistGame.cs`, `HeistPlayer.cs`, `HeistGuard.cs`
- Create: `Demos/Heist/Assets/Editor/HeistAutoPlay.cs`
- Create: `Demos/Heist/AgentPlay/brief.md`, `Demos/Heist/AgentPlay/scripts/win.json`
- Create: `Tools/e2e-m2a.sh`, `Tools/verify_m2a.py`
- Modify: `.gitignore`
- Modify: `docs/superpowers/specs/2026-10-12-agentplay-m2-design.md` (đồng bộ)
- Create: `docs/notes/m2a-verification.md`

**Interfaces:**
- Consumes: toàn bộ package (Task 1–9), runner `--brain script` (Task 10), `check-generic.sh` (Task 11).
- Produces: `bash Tools/e2e-m2a.sh` in `ALL CHECKS PASSED` khi agent thắng game demo bằng kịch bản.

Thông số game (spec M2 mục 10, đã điều chỉnh theo ghi chú đầu plan):
- Sàn 20 × 12 m (x từ -10 tới 10, z từ -6 tới 6). Vùng: `start (-8,0)`, `exit (-9,-5)`, `item_a (-3,4)`, `item_b (4,4)`, `item_c (4,-4)`, `cover_1 (2,0)` có hộp che tại `(3,0)` kích thước 0,6 × 1,2 × 3 m, `cover_2 (-5,-2)` có hộp che tại `(-4,-2)`.
- Bảo vệ đi qua lại trên đường x = 6 giữa z = 5 và z = -5, tốc độ 1,5 m/s. Mắt cao 1,6 m, tầm nhìn 7 m, nửa góc 35°, bị hộp che chặn. Thấy liên tục 1,5 giây thì đuổi (3,5 m/s); mất dấu 3 giây thì quay lại tuần tra; cách người chơi 1 m thì bắt.
- Người chơi đi 3 m/s, đứng thì điểm nhìn ở 1,6 m, ngồi thì 0,5 m. Đi tới `exit` khi cầm đủ 3 món thì thắng.

- [ ] **Step 1: Tạo project Heist**

```bash
cd /e/Unity-WorkSpace/AgentPlay
mkdir -p Demos
"/e/6000.0.77f1/Editor/Unity.exe" -batchmode -nographics -quit -createProject "$(cygpath -m Demos/Heist)" -logFile Demos-create.log
tail -2 Demos-create.log; rm -f Demos-create.log
```

Ghi đè `Demos/Heist/Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.agentplay.core": "file:../../../Packages/com.agentplay.core",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.physics": "1.0.0"
  }
}
```

Xóa `Demos/Heist/Packages/packages-lock.json` nếu có. Thêm vào cuối `.gitignore`:

```gitignore
# Demo Unity projects
/Demos/*/[Ll]ibrary/
/Demos/*/[Tt]emp/
/Demos/*/[Ll]ogs/
/Demos/*/[Oo]bj/
/Demos/*/[Uu]ser[Ss]ettings/
/Demos/*/*.csproj
/Demos/*/*.sln
/Demos/*/AgentPlay/Traces/
/Demos/*/AgentPlay/Runs/
```

- [ ] **Step 2: Viết code game demo**

`Demos/Heist/Assets/Heist/HeistLevel.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace HeistDemo
{
    /// <summary>Fixed layout of the heist room (x -10..10, z -6..6).</summary>
    public static class HeistLevel
    {
        public const float ZoneRadius = 1f;

        public static readonly Dictionary<string, Vector3> Zones = new Dictionary<string, Vector3>
        {
            { "start", new Vector3(-8f, 0f, 0f) },
            { "exit", new Vector3(-9f, 0f, -5f) },
            { "item_a", new Vector3(-3f, 0f, 4f) },
            { "item_b", new Vector3(4f, 0f, 4f) },
            { "item_c", new Vector3(4f, 0f, -4f) },
            { "cover_1", new Vector3(2f, 0f, 0f) },
            { "cover_2", new Vector3(-5f, 0f, -2f) },
        };

        /// <summary>Box that hides a thief crouching at the cover spot of the same name.</summary>
        public static readonly Dictionary<string, Vector3> CoverBoxes = new Dictionary<string, Vector3>
        {
            { "cover_1", new Vector3(3f, 0.6f, 0f) },
            { "cover_2", new Vector3(-4f, 0.6f, -2f) },
        };

        public static readonly Vector3 CoverBoxSize = new Vector3(0.6f, 1.2f, 3f);

        public static readonly Vector3[] GuardRoute = { new Vector3(6f, 1f, 5f), new Vector3(6f, 1f, -5f) };

        public static readonly string[] ItemNames = { "item_a", "item_b", "item_c" };

        /// <summary>Name of the zone within ZoneRadius of <paramref name="position"/> on the floor plane, or "".</summary>
        public static string ZoneAt(Vector3 position)
        {
            string best = "";
            float bestDistance = ZoneRadius;
            foreach (var zone in Zones)
            {
                float d = FlatDistance(position, zone.Value);
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = zone.Key;
                }
            }
            return best;
        }

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
```

`Demos/Heist/Assets/Heist/HeistGame.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using AgentPlay;
using UnityEngine;

namespace HeistDemo
{
    [AgentGame("AgentPlay Heist", Version = "m2")]
    [AgentEventInfo("item.picked", "The thief picked up an item")]
    [AgentEventInfo("guard.spotted", "A guard started seeing the thief", Important = true, Outcome = AgentOutcome.Bad)]
    [AgentEventInfo("guard.lost_sight", "A guard stopped seeing the thief")]
    [AgentEventInfo("game.won", "The thief left through the exit with all three items", Important = true, Outcome = AgentOutcome.Good)]
    [AgentEventInfo("game.lost", "A guard caught the thief", Important = true, Outcome = AgentOutcome.Bad)]
    public sealed class HeistGame : MonoBehaviour
    {
        public static HeistGame Instance { get; private set; }
        /// <summary>"won" or "lost" once the round is over; null while playing.</summary>
        public static string Result { get; private set; }

        public HeistPlayer Player { get; private set; }
        public readonly List<HeistGuard> GuardList = new List<HeistGuard>();
        public readonly Dictionary<string, GameObject> Items = new Dictionary<string, GameObject>();

        [AgentObserve("items.left", "Items still lying in the room")]
        public int ItemsLeft => Items.Values.Count(item => item.activeSelf);

        [AgentObserveList("guards", "Guards in the room")]
        public IEnumerable<HeistGuard> Guards => GuardList;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Build()
        {
            if (Instance != null) return;
            Result = null;
            Instance = new GameObject("HeistGame").AddComponent<HeistGame>();
            Instance.BuildLevel();
        }

        void BuildLevel()
        {
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(0f, 20f, 0f), Quaternion.Euler(90f, 0f, 0f));
            camera.orthographic = true;
            camera.orthographicSize = 7f;
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(2f, 1f, 1.2f);

            foreach (var box in HeistLevel.CoverBoxes)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Cover_" + box.Key;
                go.transform.position = box.Value;
                go.transform.localScale = HeistLevel.CoverBoxSize;
            }

            foreach (var name in HeistLevel.ItemNames)
            {
                var item = Visual(PrimitiveType.Cube, "Item_" + name, HeistLevel.Zones[name] + Vector3.up * 0.2f, Color.yellow);
                item.transform.localScale = Vector3.one * 0.4f;
                Items[name] = item;
            }
            Visual(PrimitiveType.Cylinder, "Exit", HeistLevel.Zones["exit"], Color.green).transform.localScale = new Vector3(1.6f, 0.02f, 1.6f);

            Player = Visual(PrimitiveType.Capsule, "Thief", HeistLevel.Zones["start"] + Vector3.up, Color.blue).AddComponent<HeistPlayer>();
            GuardList.Add(Visual(PrimitiveType.Capsule, "Guard", HeistLevel.GuardRoute[0], Color.red).AddComponent<HeistGuard>());
        }

        /// <summary>A primitive without a collider, so guard sight rays only ever hit cover boxes.</summary>
        static GameObject Visual(PrimitiveType type, string name, Vector3 position, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.position = position;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().material.color = color;
            return go;
        }

        public void CheckWin()
        {
            if (Result != null || Player.Carried.Count < HeistLevel.ItemNames.Length || Player.Zone != "exit") return;
            Result = "won";
            Debug.Log("[Heist] result=won");
            AgentEvents.Emit("game.won", new { items = Player.Carried.Count }, important: true, outcome: AgentOutcome.Good);
        }

        public void Lose(string guard)
        {
            if (Result != null) return;
            Result = "lost";
            Debug.Log("[Heist] result=lost");
            AgentEvents.Emit("game.lost", new { guard }, important: true, outcome: AgentOutcome.Bad);
        }
    }
}
```

`Demos/Heist/Assets/Heist/HeistPlayer.cs`:

```csharp
using System.Collections.Generic;
using AgentPlay;
using UnityEngine;

namespace HeistDemo
{
    public sealed class HeistPlayer : MonoBehaviour
    {
        public const float Speed = 3f;
        public const float PickRange = 1.5f;

        public readonly List<string> Carried = new List<string>();
        public bool Crouched { get; private set; }

        /// <summary>Point guards look at: head height standing, low when crouched.</summary>
        public Vector3 EyeTarget => new Vector3(transform.position.x, Crouched ? 0.5f : 1.6f, transform.position.z);

        [AgentObserve("player.pos")] public Vector3 Pos => transform.position;

        [AgentObserve("player.zone", "Named zone the thief stands in, or empty")]
        public string Zone => HeistLevel.ZoneAt(transform.position);

        [AgentObserve("player.carrying", "Number of items carried")] public int Carrying => Carried.Count;

        [AgentObserve("player.crouched")] public bool IsCrouched => Crouched;

        [AgentObserve("player.hidden", "Crouched at a cover spot; guards on the far side of its box cannot see the thief")]
        public bool Hidden => Crouched && Zone.StartsWith("cover_");

        [AgentAction("goto", "Walk straight to a zone: start, exit, item_a, item_b, item_c, cover_1, cover_2")]
        public AgentActionStatus GoTo(AgentActionContext ctx, string zone)
        {
            if (HeistGame.Result != null || !HeistLevel.Zones.TryGetValue(zone, out var target)) return AgentActionStatus.Failed;
            if (ctx.IsFirstFrame) Crouched = false;
            if (!MoveTowards(target)) return AgentActionStatus.Running;
            HeistGame.Instance.CheckWin();
            return AgentActionStatus.Done;
        }

        [AgentAction("pick", "Pick up an item within 1.5 m: item_a, item_b or item_c")]
        public AgentActionStatus Pick(string item)
        {
            if (HeistGame.Result != null) return AgentActionStatus.Failed;
            if (!HeistGame.Instance.Items.TryGetValue(item, out var go) || !go.activeSelf) return AgentActionStatus.Failed;
            if (HeistLevel.FlatDistance(transform.position, go.transform.position) > PickRange) return AgentActionStatus.Failed;
            go.SetActive(false);
            Carried.Add(item);
            AgentEvents.Emit("item.picked", new { item, carrying = Carried.Count });
            return AgentActionStatus.Done;
        }

        [AgentAction("hide", "Walk to a cover spot (cover_1 or cover_2) and crouch behind its box")]
        public AgentActionStatus Hide(AgentActionContext ctx, string zone)
        {
            if (HeistGame.Result != null || !zone.StartsWith("cover_") || !HeistLevel.Zones.TryGetValue(zone, out var target))
                return AgentActionStatus.Failed;
            if (ctx.IsFirstFrame) Crouched = false;
            if (!MoveTowards(target)) return AgentActionStatus.Running;
            Crouched = true;
            return AgentActionStatus.Done;
        }

        [AgentAction("wait", "Stay still for the given number of seconds")]
        public AgentActionStatus Wait(AgentActionContext ctx, float seconds) =>
            ctx.Elapsed >= seconds ? AgentActionStatus.Done : AgentActionStatus.Running;

        [AgentAction("stop", "Stay still (used whenever there is no plan)", Safe = true)]
        public AgentActionStatus Stop() => AgentActionStatus.Done;

        bool MoveTowards(Vector3 target)
        {
            var p = transform.position;
            target.y = p.y;
            transform.position = Vector3.MoveTowards(p, target, Speed * Time.deltaTime);
            return HeistLevel.FlatDistance(transform.position, target) < 0.05f;
        }

        void Update()
        {
            var p = transform.position;
            transform.position = new Vector3(p.x, Crouched ? 0.5f : 1f, p.z);
            transform.localScale = new Vector3(1f, Crouched ? 0.5f : 1f, 1f);
            if (HeistGame.Instance != null) HeistGame.Instance.CheckWin();
        }
    }
}
```

`Demos/Heist/Assets/Heist/HeistGuard.cs`:

```csharp
using AgentPlay;
using UnityEngine;

namespace HeistDemo
{
    public sealed class HeistGuard : MonoBehaviour
    {
        public const float PatrolSpeed = 1.5f;
        public const float ChaseSpeed = 3.5f;
        public const float ViewDistance = 7f;
        public const float HalfAngle = 35f;
        public const float ChaseAfter = 1.5f;
        public const float GiveUpAfter = 3f;
        public const float CatchDistance = 1f;

        int _target = 1;
        float _seenFor;
        float _unseenFor;
        bool _sees;
        bool _chasing;
        Vector3 _forward = Vector3.back;

        HeistPlayer Player => HeistGame.Instance != null ? HeistGame.Instance.Player : null;

        [AgentObserve("distance", "Metres from this guard to the thief")]
        public float Distance => Player != null ? HeistLevel.FlatDistance(transform.position, Player.transform.position) : float.PositiveInfinity;

        [AgentObserve("sees_player")] public bool SeesPlayer => _sees;
        [AgentObserve("chasing")] public bool Chasing => _chasing;
        [AgentObserve("moving_north", "True while walking towards +z")] public bool MovingNorth => _forward.z > 0f;
        [AgentObserve("x")] public float X => transform.position.x;
        [AgentObserve("z")] public float Z => transform.position.z;
        [AgentObserve("zone")] public string Zone => HeistLevel.ZoneAt(transform.position);

        void Update()
        {
            var player = Player;
            if (HeistGame.Result != null || player == null) return;

            bool sees = CanSee(player);
            if (sees && !_sees) AgentEvents.Emit("guard.spotted", new { guard = name, distance = Distance }, important: true, outcome: AgentOutcome.Bad);
            if (!sees && _sees) AgentEvents.Emit("guard.lost_sight", new { guard = name });
            _sees = sees;
            _seenFor = sees ? _seenFor + Time.deltaTime : 0f;
            _unseenFor = sees ? 0f : _unseenFor + Time.deltaTime;
            if (!_chasing && _seenFor >= ChaseAfter) _chasing = true;
            if (_chasing && _unseenFor >= GiveUpAfter) _chasing = false;

            if (_chasing)
            {
                Step(player.transform.position, ChaseSpeed);
                if (Distance <= CatchDistance) HeistGame.Instance.Lose(name);
            }
            else if (Step(HeistLevel.GuardRoute[_target], PatrolSpeed))
            {
                _target = 1 - _target;
            }
        }

        bool Step(Vector3 target, float speed)
        {
            var p = transform.position;
            target.y = p.y;
            var next = Vector3.MoveTowards(p, target, speed * Time.deltaTime);
            var delta = next - p;
            if (delta.sqrMagnitude > 1e-8f) _forward = delta.normalized;
            transform.position = next;
            return (next - target).sqrMagnitude < 0.0025f;
        }

        bool CanSee(HeistPlayer player)
        {
            var eye = new Vector3(transform.position.x, 1.6f, transform.position.z);
            var to = player.EyeTarget - eye;
            var flat = new Vector3(to.x, 0f, to.z);
            if (flat.magnitude > ViewDistance) return false;
            if (Vector3.Angle(_forward, flat) > HalfAngle) return false;
            if (Physics.Raycast(eye, to.normalized, out var hit, to.magnitude) && hit.collider.name.StartsWith("Cover_")) return false;
            return true;
        }
    }
}
```

`Demos/Heist/Assets/Editor/HeistAutoPlay.cs`:

```csharp
using System;
using UnityEditor;
using UnityEngine;

namespace HeistDemo
{
    /// <summary>
    /// Run with: Unity.exe -projectPath Demos/Heist -executeMethod HeistDemo.HeistAutoPlay.Run
    /// Enters Play mode and quits 2 s after the round ends (exit 0 won, 3 lost) or at AGENTPLAY_AUTOPLAY_SECONDS (exit 1).
    /// </summary>
    [InitializeOnLoad]
    public static class HeistAutoPlay
    {
        const string DeadlineKey = "HeistDemo.AutoPlay.Deadline";
        const string EndedKey = "HeistDemo.AutoPlay.EndedAt";

        static HeistAutoPlay()
        {
            EditorApplication.update += Watch;
        }

        public static void Run()
        {
            if (!int.TryParse(Environment.GetEnvironmentVariable("AGENTPLAY_AUTOPLAY_SECONDS"), out var seconds) || seconds <= 0) seconds = 240;
            SessionState.SetFloat(DeadlineKey, (float)(EditorApplication.timeSinceStartup + seconds));
            SessionState.EraseFloat(EndedKey);
            Debug.Log("[Heist] AutoPlay for at most " + seconds + " s");
            EditorApplication.EnterPlaymode();
        }

        static void Watch()
        {
            float deadline = SessionState.GetFloat(DeadlineKey, 0f);
            if (deadline <= 0f) return;
            double now = EditorApplication.timeSinceStartup;
            if (EditorApplication.isPlaying && HeistGame.Result != null)
            {
                float endedAt = SessionState.GetFloat(EndedKey, 0f);
                if (endedAt <= 0f)
                {
                    SessionState.SetFloat(EndedKey, (float)now);
                    return;
                }
                if (now - endedAt >= 2.0) Quit(HeistGame.Result == "won" ? 0 : 3);
                return;
            }
            if (now >= deadline)
            {
                Debug.Log("[Heist] AutoPlay deadline reached");
                Quit(1);
            }
        }

        static void Quit(int code)
        {
            SessionState.EraseFloat(DeadlineKey);
            SessionState.EraseFloat(EndedKey);
            Debug.Log("[Heist] AutoPlay quitting with " + code);
            EditorApplication.isPaused = false;
            EditorApplication.ExitPlaymode();
            EditorApplication.delayCall += () => EditorApplication.Exit(code);
        }
    }
}
```

`Demos/Heist/AgentPlay/brief.md`:

```markdown
# Heist

You are a thief in a small room seen from above. Steal all three items (item_a, item_b, item_c) and leave through the exit.

- Win: reach the exit zone while carrying all three items (event game.won).
- Lose: a guard catches you (event game.lost).
- The guard walks back and forth along x = 6 between z = 5 and z = -5. It sees 7 m ahead within 35 degrees of the way it walks. If it sees you for 1.5 s it chases you; if it loses you for 3 s it goes back to patrolling.
- Cover boxes block sight. Crouching at a cover spot (action hide) keeps you out of sight from the far side of its box.
- item_b is near the north end of the guard's path and item_c near the south end: grab each one while the guard walks away from it.
- pick only works within 1.5 m of the item, so goto the item first.
```

`Demos/Heist/AgentPlay/scripts/win.json`:

```json
{
  "plans": [
    {
      "id": "s1",
      "intent": "Take item_a, which the guard cannot see, then hide behind cover_1",
      "steps": [
        { "do": "goto", "args": { "zone": "item_a" } },
        { "do": "pick", "args": { "item": "item_a" } },
        { "do": "hide", "args": { "zone": "cover_1" } }
      ],
      "rules": [
        { "when": "event:guard.spotted", "do": "hide", "args": { "zone": "cover_1" }, "then": "replan" }
      ],
      "timeout_s": 30
    },
    {
      "id": "s2",
      "intent": "Wait until the guard walks south past the middle, then grab item_b and hide",
      "steps": [
        { "do": "wait", "args": { "seconds": 30 }, "until": "!any(guards.moving_north) && max(guards.z) < 1 && max(guards.z) > -1" },
        { "do": "goto", "args": { "zone": "item_b" } },
        { "do": "pick", "args": { "item": "item_b" } },
        { "do": "hide", "args": { "zone": "cover_1" } }
      ],
      "rules": [
        { "when": "event:guard.spotted", "do": "hide", "args": { "zone": "cover_1" }, "then": "replan" }
      ],
      "timeout_s": 60
    },
    {
      "id": "s3",
      "intent": "Wait until the guard walks north past the middle, then grab item_c and hide",
      "steps": [
        { "do": "wait", "args": { "seconds": 30 }, "until": "any(guards.moving_north) && min(guards.z) > -1 && min(guards.z) < 1" },
        { "do": "goto", "args": { "zone": "item_c" } },
        { "do": "pick", "args": { "item": "item_c" } },
        { "do": "hide", "args": { "zone": "cover_1" } }
      ],
      "rules": [
        { "when": "event:guard.spotted", "do": "hide", "args": { "zone": "cover_1" }, "then": "replan" }
      ],
      "timeout_s": 60
    },
    {
      "id": "s4",
      "intent": "Leave through the exit with all three items",
      "steps": [
        { "do": "goto", "args": { "zone": "exit" } }
      ],
      "timeout_s": 30
    }
  ]
}
```

Vì sao kịch bản này thắng: từ `cover_1 (2,0)`, bảo vệ trên đường x = 6 không bao giờ thấy được người đứng (góc tới người ít nhất 38,7°, lớn hơn 35°), và người ngồi thì bị hộp che chặn. `item_b` lấy lúc bảo vệ đi về phía nam qua giữa phòng; nó quay lại hướng bắc và chỉ thấy được `item_b` khi tới z ≈ -2,7, tức sau khoảng 4,8 giây. Người chơi cần khoảng 3 giây để đi tới, nhặt và quay về. `item_c` đối xứng.

- [ ] **Step 3: Viết script E2E và kiểm tra**

`Tools/e2e-m2a.sh`:

```bash
#!/usr/bin/env bash
# M2a end-to-end: the script brain plays the Heist demo turn-based until it wins.
# Demos/Heist must not be open in another Editor. Opens a Unity window for one to two minutes.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
PROJECT="$ROOT/Demos/Heist"
LOGS="$PROJECT/Logs"
mkdir -p "$LOGS"
rm -rf "$PROJECT/AgentPlay/Traces" "$LOGS/runner.log" "$LOGS/autoplay.log"

dotnet build "$ROOT/Runner/AgentPlay.Runner.csproj" -c Debug -v q || exit 1
dotnet "$ROOT/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll" \
  --brain script "$(cygpath -w "$PROJECT/AgentPlay/scripts/win.json")" --idle-exit 20 \
  > "$LOGS/runner.log" 2>&1 < /dev/null &
RUNNER_PID=$!

AGENTPLAY_AUTOPLAY_SECONDS=240 "$UNITY_EXE" -projectPath "$(cygpath -m "$PROJECT")" \
  -executeMethod HeistDemo.HeistAutoPlay.Run -logFile "$(cygpath -m "$LOGS/autoplay.log")"
UNITY_CODE=$?
echo "Unity exited with $UNITY_CODE"

wait "$RUNNER_PID"
RUNNER_CODE=$?
echo "Runner exited with $RUNNER_CODE"

bash "$ROOT/Tools/check-generic.sh" || exit 1
python -I "$ROOT/Tools/verify_m2a.py" "$ROOT" "$RUNNER_CODE" "$UNITY_CODE"
```

`Tools/verify_m2a.py`:

```python
"""Checks logs and traces from Tools/e2e-m2a.sh. Usage: verify_m2a.py ROOT RUNNER_EXIT UNITY_EXIT"""
import json
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
runner_code = int(sys.argv[2])
unity_code = int(sys.argv[3])
project = root / "Demos" / "Heist"
logs = project / "Logs"
runner_log = (logs / "runner.log").read_text(encoding="utf-8", errors="replace")
unity_log = (logs / "autoplay.log").read_text(encoding="utf-8", errors="replace")
failures = []


def check(ok, message):
    print(("PASS " if ok else "FAIL ") + message)
    if not ok:
        failures.append(message)


check(runner_code == 0, "runner exited with 0 (won), got %d" % runner_code)
check(unity_code == 0, "Unity exited with 0 (won), got %d" % unity_code)
check("hello: game=AgentPlay Heist version=m2" in runner_log, "runner received hello from the Heist demo")
check("  observe guards : list [" in runner_log, "hello lists the guards list observe with its fields")
check("  event game.won (important)" in runner_log, "hello lists the declared game.won event")
check("WARNING: the game sent no brief" not in runner_log, "the game sent its brief")
for plan_id in ("s1", "s2", "s3", "s4"):
    check(("sent plan %s:" % plan_id) in runner_log, "runner sent plan %s" % plan_id)
check(runner_log.count("plan_done") >= 4, "four plans reported plan_done")
check("plan_failed" not in runner_log, "no plan failed")
check("unity error:" not in runner_log, "Unity reported no protocol errors")
check("game over: won" in runner_log, "runner saw the win")
check("[AgentPlay] Registry: 7 observes, 5 actions" in unity_log, "registry found 7 observes and 5 actions")
check("[Heist] result=won" in unity_log, "the game ended in a win")
check("NullReferenceException" not in unity_log, "no NullReferenceException in the Unity log")
check("AgentActionException" not in unity_log, "no AgentActionException in the Unity log")

traces = sorted((project / "AgentPlay" / "Traces").glob("*.ndjson"))
records = []
for path in traces:
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            records.append(json.loads(line))
check(any(r.get("type") == "control" and r.get("who") == "agent" for r in records), "trace marks agent control")
check(sum(1 for r in records if r.get("type") == "plan") >= 4, "trace has the four plans")
check(sum(1 for r in records if r.get("type") == "plan_status" and r.get("status") == "plan_done") >= 4, "trace has four plan_done statuses")
check(any(r.get("type") == "event" and r.get("name") == "game.won" for r in records), "trace has the game.won event")

print("%d check(s) failed" % len(failures) if failures else "ALL CHECKS PASSED")
sys.exit(1 if failures else 0)
```

- [ ] **Step 4: Chạy E2E**

Run (timeout 600000 ms): `cd /e/Unity-WorkSpace/AgentPlay && bash Tools/e2e-m2a.sh`
Expected: dòng cuối `ALL CHECKS PASSED`.

Nếu fail vì kịch bản: đọc `Demos/Heist/Logs/runner.log` để biết Plan nào hỏng và lý do. Được phép chỉnh dữ liệu kịch bản (`win.json`: điều kiện `until`, thứ tự bước) hoặc thông số game demo (vị trí, tốc độ), và ghi vào ledger thành ruling. Không được nới điều kiện trong `verify_m2a.py`. Nếu fail vì code package hoặc runner, quay lại task sở hữu code đó và sửa theo TDD.

- [ ] **Step 5: Đồng bộ spec M2 với code**

Trong `docs/superpowers/specs/2026-10-12-agentplay-m2-design.md`:

1. Mục 1, phần "Đã chốt với người dùng", dòng phạm vi: thay "một game demo "trộm nhỏ" trong Testbed của AgentPlay" bằng "một game demo "trộm nhỏ" trong project Unity riêng `Demos/Heist` của repo AgentPlay".
2. Mục 1, "Định nghĩa xong", tiêu chí 1 và 2: thay "Testbed" bằng "Heist".
3. Mục 2, dòng kiểm tra: thay "tìm chuỗi `TileThieves` và `Testbed`" bằng "tìm chuỗi `TileThieves`, `Testbed` và `Heist`".
4. Mục 10: tiêu đề thành "Game demo `Demos/Heist`: "trộm nhỏ"". Câu đầu thành: "Toàn bộ code demo nằm trong project Unity riêng `Demos/Heist` (một project chỉ có một `[AgentGame]`; `Testbed` giữ cho test package và E2E M1)." Gạch đầu dòng **Bản đồ**: thay câu về đồ thị điểm đi bằng "`goto` đi thẳng tới vùng; sàn trống nên không cần tìm đường." Gạch đầu dòng **Bảo vệ**: thay "đi vòng qua 4 điểm tuần tra, tầm nhìn hình nón 70° và 7 m" bằng "đi qua lại trên đường x = 6 giữa z = 5 và z = -5, tầm nhìn 7 m, nửa góc 35°". Thêm vào **Observe** các trường `moving_north`, `x`, `z` của `guards` và observe `items.left`, `player.crouched`. Câu kịch bản: `Demos/Heist/AgentPlay/scripts/win.json`.
5. Mục 5, sau đoạn "**Bước:**", thêm: "Điều kiện `until` được đánh giá trên snapshot chụp đầu frame."
6. Mục 8, gạch đầu dòng **Chế độ bộ não**: thêm "M2a chỉ có `--brain script`; không có `--brain` thì runner chạy như M1. `opus` và `manual` đến ở M2b."

- [ ] **Step 6: Ghi kết quả kiểm chứng**

Tạo `docs/notes/m2a-verification.md` với số liệu thật từ lần chạy:

```markdown
# M2a: kết quả kiểm chứng

Ngày chạy: <ngày chạy thật>

## Test
- `dotnet test AgentPlay.sln`: <passed>/<tổng>, 0 failed
- `Tools/unity-tests.sh`: <passed>/<tổng>, 0 failed
- `Tools/test-link-agentplay.sh`: tất cả PASS
- `Tools/check-generic.sh`: OK
- `Tools/e2e-m1.sh`: ALL CHECKS PASSED (hồi quy M1)
- `Tools/e2e-m2a.sh`: ALL CHECKS PASSED

## Ván thắng theo kịch bản
- Thời gian game tới khi thắng: <t của sự kiện game.won trong trace>
- Số Plan đã gửi: 4; số lần dừng theo lượt: <đếm "paused" trong runner.log>
- Điều chỉnh kịch bản hoặc thông số demo trong lúc chạy E2E: <liệt kê, hoặc "không có">

## Giới hạn đã biết
- Snapshot được dựng mỗi frame khi có Plan (để đánh giá điều kiện); chưa tối ưu.
- Sự kiện phát ra trong lúc action chạy chỉ được luật nhìn thấy ở frame sau.
- Tên hàm điều kiện (`min`, `max`, `count`, `any`) là từ khóa, không dùng làm tên observe.
```

Mỗi chỗ `<...>` phải thay bằng số liệu thật trước khi commit.

- [ ] **Step 7: Chạy lại toàn bộ kiểm tra lần cuối**

```bash
cd /e/Unity-WorkSpace/AgentPlay
dotnet test AgentPlay.sln
bash Tools/unity-tests.sh
bash Tools/test-link-agentplay.sh
bash Tools/check-generic.sh
bash Tools/e2e-m1.sh
bash Tools/e2e-m2a.sh
```

Expected: tất cả pass; hai E2E in `ALL CHECKS PASSED`.

- [ ] **Step 8: Commit**

```bash
git add .gitignore Demos/Heist/Assets Demos/Heist/Packages Demos/Heist/ProjectSettings Demos/Heist/AgentPlay Tools docs Packages
git status --short
git commit -m "test: Heist demo won end to end by the script brain; M2 spec synced" -m "<trailers>"
```

Expected trong `git status` trước khi commit: không có file nào trong `Demos/Heist/Library`, `Demos/Heist/Logs`, `Demos/Heist/AgentPlay/Traces`.
