# AgentPlay M2b (Bộ não Opus) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Runner có `--brain opus`: mỗi lần game dừng theo lượt, runner hỏi Claude Opus 5.5 một Plan qua structured outputs, kiểm Plan, gửi cho Unity, ghi log chi phí, và dừng khi đạt giới hạn lượt gọi.

**Architecture:** Toàn bộ thay đổi nằm trong runner. `PlanSchema` sinh JSON schema của Plan từ `hello`, với `anyOf` theo từng action (structured outputs bắt buộc `additionalProperties: false` nên `args` không thể là object tự do). `PromptBuilder` dựng phần system cố định (được cache) và phần mỗi lượt. `OpusBrain` cài `IBrain`, gọi `IModelClient`, kiểm Plan và xin lại một lần khi sai, thử lại khi lỗi mạng, và đếm lượt gọi. `AnthropicModelClient` là lớp mỏng bọc SDK C# chính thức. Mỗi lượt gọi được ghi vào `RunLog`.

**Tech Stack:** .NET 9, NuGet `Anthropic` 12.55.0 (SDK C# chính thức; dùng `client.Beta.Messages.Create` vì cần server-side fallback), Newtonsoft.Json cho Contract, System.Text.Json chỉ ở chỗ giao tiếp với SDK, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-12-agentplay-m2-design.md` (mục 8, 13, 14, 15 dòng M2b).

### Ghi chú so với spec M2 (đồng bộ ở Task 6)

- **Plan lấy qua structured outputs (`output_config.format`)**, không qua tool `submit_plan`. Opus 5.5 trả lỗi 400 nếu ép `tool_choice` kiểu `tool` hoặc `any`.
- **Schema theo từng action:** `steps` và `rules` là mảng `anyOf` gồm các biến thể `{do: const, args: {...}}`. `until` là chuỗi bắt buộc, chuỗi rỗng nghĩa là không có điều kiện.
- **Thinking không tắt được trên Opus 5.5.** Chỉ đặt `effort` (mặc định cấu hình là `high`).
- **Server-side fallback khi bị từ chối:** bật mặc định `fallbacks: "default"` với beta `server-side-fallback-2026-07-01`, theo hướng dẫn hiện hành của API.
- **Mỗi lượt là một request độc lập** (system cố định cộng một tin nhắn user mô tả tình huống). Không giữ lịch sử hội thoại, nên không vướng ràng buộc "preserved thinking"; tóm tắt các Plan trước nằm trong tin nhắn user.
- **Bỏ `--brain manual`:** luồng đọc bàn phím của runner đã dùng stdin.
- **Không thoát ngay khi thiếu `ANTHROPIC_API_KEY`:** SDK còn đọc được profile của `ant auth login`. Runner chỉ cảnh báo, rồi báo lỗi rõ ràng nếu lượt gọi đầu thất bại vì xác thực.

## Global Constraints

- Model mặc định `claude-opus-5-5`; fallback `"default"`; `effort` mặc định `high`; `max_tokens` mặc định 16000; `max_calls` mặc định 40 mỗi phiên. Tất cả nằm trong `runner.json` cạnh file chạy, ghi đè được bằng `--model`, `--effort`, `--max-calls`.
- Khóa API không bao giờ ghi vào file; SDK tự đọc `ANTHROPIC_API_KEY` hoặc profile.
- Không có test tự động nào gọi API thật. Mọi test dùng `IModelClient` giả.
- Runner không được nhắc tên game cụ thể (`Tools/check-generic.sh`).
- Mã thoát runner: 0 thắng, 3 thua, 4 bộ não dừng (hết Plan, Plan sai hai lần, hết lượt gọi), 5 lệch giao thức, 6 Unity mất kết nối trước khi xong.
- Commit kết thúc bằng hai dòng trailer như M2a (`<trailers>`):
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
  `Claude-Session: https://claude.ai/code/session_01XTqvn3xKuC12ugWwPzQueZ`

## Review Focus

1. **Model trả JSON không parse được, hoặc Plan đúng schema nhưng sai ngữ nghĩa** (điều kiện sai kiểu, sự kiện không khai báo): xin lại đúng một lần kèm lỗi; vẫn sai thì dừng với lý do rõ ràng, không gửi Plan sai cho Unity. Test ở Task 3.
2. **API lỗi tạm thời hoặc hết thời gian:** thử lại, mỗi lần thử tính vào giới hạn lượt gọi; không vòng lặp vô hạn. Test ở Task 3.
3. **Model bị từ chối (`stop_reason` = `refusal`) dù đã có fallback:** coi như Plan sai, không parse nội dung. Test ở Task 3.
4. **Phần system phải giống hệt nhau giữa các lượt** để cache trúng: không chứa thời gian, trạng thái hay thứ tự không ổn định. Test ở Task 2.
5. **Hết giới hạn lượt gọi giữa chừng:** runner dừng với mã 4 và in tổng token, không gọi thêm. Test ở Task 3.

---

### Task 1: PlanSchema

**Files:**
- Create: `Runner/Brains/PlanSchema.cs`
- Test: `Runner.Tests/PlanSchemaTests.cs`

**Interfaces:**
- Produces: `PlanSchema.Build(HelloMessage hello) : JObject` (JSON schema của Plan cho game đó).

- [ ] **Step 1: Viết test thất bại**

`Runner.Tests/PlanSchemaTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class PlanSchemaTests
    {
        static HelloMessage Hello()
        {
            var h = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G" };
            h.Actions.Add(new ActionInfo { Name = "goto", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } } });
            h.Actions.Add(new ActionInfo { Name = "repeat", Params = new List<ActionParamInfo>
            {
                new ActionParamInfo { Name = "times", ValueType = ValueKinds.Int },
                new ActionParamInfo { Name = "speed", ValueType = ValueKinds.Float },
                new ActionParamInfo { Name = "loud", ValueType = ValueKinds.Bool },
            } });
            h.Actions.Add(new ActionInfo { Name = "stop", Safe = true });
            return h;
        }

        static IEnumerable<JObject> AllObjects(JToken t)
        {
            if (t is JObject o)
            {
                yield return o;
                foreach (var p in o.Properties())
                    foreach (var inner in AllObjects(p.Value)) yield return inner;
            }
            else if (t is JArray a)
            {
                foreach (var item in a)
                    foreach (var inner in AllObjects(item)) yield return inner;
            }
        }

        [Fact]
        public void TopLevelRequiresEveryPlanField()
        {
            var s = PlanSchema.Build(Hello());
            Assert.Equal("object", (string)s["type"]);
            Assert.False((bool)s["additionalProperties"]);
            Assert.Equal(new[] { "id", "intent", "steps", "rules", "timeout_s" }, s["required"].Select(x => (string)x));
            Assert.Equal("number", (string)s["properties"]["timeout_s"]["type"]);
        }

        [Fact]
        public void StepsAreOneVariantPerActionWithExactArgs()
        {
            var variants = (JArray)PlanSchema.Build(Hello())["properties"]["steps"]["items"]["anyOf"];
            Assert.Equal(3, variants.Count);
            var go = variants.First(v => (string)v["properties"]["do"]["const"] == "goto");
            Assert.Equal(new[] { "do", "args", "until" }, go["required"].Select(x => (string)x));
            Assert.Equal("string", (string)go["properties"]["args"]["properties"]["zone"]["type"]);
            Assert.Equal(new[] { "zone" }, go["properties"]["args"]["required"].Select(x => (string)x));
            var rep = variants.First(v => (string)v["properties"]["do"]["const"] == "repeat");
            Assert.Equal("integer", (string)rep["properties"]["args"]["properties"]["times"]["type"]);
            Assert.Equal("number", (string)rep["properties"]["args"]["properties"]["speed"]["type"]);
            Assert.Equal("boolean", (string)rep["properties"]["args"]["properties"]["loud"]["type"]);
            var stop = variants.First(v => (string)v["properties"]["do"]["const"] == "stop");
            Assert.Empty((JObject)stop["properties"]["args"]["properties"]);
        }

        [Fact]
        public void RulesCarryWhenAndThen()
        {
            var variants = (JArray)PlanSchema.Build(Hello())["properties"]["rules"]["items"]["anyOf"];
            var go = variants.First(v => (string)v["properties"]["do"]["const"] == "goto");
            Assert.Equal(new[] { "when", "do", "args", "then" }, go["required"].Select(x => (string)x));
            Assert.Equal(new[] { "resume", "next", "replan", "abort" }, go["properties"]["then"]["enum"].Select(x => (string)x));
        }

        [Fact]
        public void EveryObjectForbidsAdditionalProperties()
        {
            foreach (var o in AllObjects(PlanSchema.Build(Hello())))
                if ((string)o["type"] == "object")
                    Assert.False((bool)o["additionalProperties"], o.ToString());
        }

        [Fact]
        public void SameHelloGivesIdenticalSchema()
        {
            Assert.Equal(PlanSchema.Build(Hello()).ToString(), PlanSchema.Build(Hello()).ToString());
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: build FAIL với `The name 'PlanSchema' does not exist`.

- [ ] **Step 3: Viết code**

`Runner/Brains/PlanSchema.cs`:

```csharp
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner.Brains
{
    /// <summary>
    /// JSON schema of a plan for one game, for structured outputs. Structured outputs require
    /// additionalProperties:false on every object, so each action becomes its own anyOf variant
    /// with exact arguments. Limits (step count, timeout range) are checked by PlanValidator instead.
    /// </summary>
    public static class PlanSchema
    {
        public static JObject Build(HelloMessage hello)
        {
            var stepVariants = new JArray();
            var ruleVariants = new JArray();
            foreach (var action in hello.Actions)
            {
                stepVariants.Add(Closed(
                    new JObject
                    {
                        ["do"] = new JObject { ["const"] = action.Name },
                        ["args"] = Args(action),
                        ["until"] = new JObject { ["type"] = "string", ["description"] = "Condition that ends the step early; empty string for none" },
                    },
                    "do", "args", "until"));
                ruleVariants.Add(Closed(
                    new JObject
                    {
                        ["when"] = new JObject { ["type"] = "string" },
                        ["do"] = new JObject { ["const"] = action.Name },
                        ["args"] = Args(action),
                        ["then"] = new JObject { ["enum"] = new JArray(RuleThen.Resume, RuleThen.Next, RuleThen.Replan, RuleThen.Abort) },
                    },
                    "when", "do", "args", "then"));
            }

            return Closed(
                new JObject
                {
                    ["id"] = new JObject { ["type"] = "string" },
                    ["intent"] = new JObject { ["type"] = "string", ["description"] = "One sentence: what this plan tries to achieve" },
                    ["steps"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["anyOf"] = stepVariants } },
                    ["rules"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["anyOf"] = ruleVariants } },
                    ["timeout_s"] = new JObject { ["type"] = "number", ["description"] = "Game seconds before the plan fails; at most 600" },
                },
                "id", "intent", "steps", "rules", "timeout_s");
        }

        static JObject Args(ActionInfo action)
        {
            var properties = new JObject();
            var names = new JArray();
            foreach (var p in action.Params)
            {
                properties[p.Name] = new JObject { ["type"] = JsonType(p.ValueType) };
                names.Add(p.Name);
            }
            return new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = names,
                ["additionalProperties"] = false,
            };
        }

        static JObject Closed(JObject properties, params string[] required) => new JObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JArray(required),
            ["additionalProperties"] = false,
        };

        static string JsonType(string kind)
        {
            switch (kind)
            {
                case ValueKinds.Int: return "integer";
                case ValueKinds.Float: return "number";
                case ValueKinds.Bool: return "boolean";
                default: return "string";
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
git add Runner Runner.Tests
git commit -m "feat(runner): per-game plan JSON schema for structured outputs" -m "<trailers>"
```

---

### Task 2: PromptBuilder và lịch sử Plan

**Files:**
- Create: `Runner/Brains/PromptBuilder.cs`
- Modify: `Runner/Brains/BrainContext.cs` (thêm `History`, `StopReason`)
- Modify: `Runner/RunnerSession.cs` (ghi lịch sử khi Plan kết thúc; in `StopReason` khi bộ não dừng)
- Test: `Runner.Tests/PromptBuilderTests.cs`

**Interfaces:**
- Produces:
  - `BrainContext.History : List<string>` (mỗi Plan đã kết thúc một dòng `"<id>: <intent> -> <status>[ (<reason>)]"`), `BrainContext.StopReason : string`.
  - `PromptBuilder.BuildSystem(HelloMessage hello) : string` (cố định cho mỗi game).
  - `PromptBuilder.BuildTurn(BrainContext context) : string`.
  - `PromptBuilder.MaxHistory = 5`.

- [ ] **Step 1: Viết test thất bại**

`Runner.Tests/PromptBuilderTests.cs`:

```csharp
using System.Collections.Generic;
using AgentPlay.Contract;
using AgentPlay.Runner;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class PromptBuilderTests
    {
        static HelloMessage Hello()
        {
            var h = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "Demo", Version = "3", Brief = "Steal the gem and leave." };
            h.Observes.Add(new ObserveInfo { Path = "player.zone", ValueType = ValueKinds.String, Description = "Zone the player stands in" });
            h.Observes.Add(new ObserveInfo
            {
                Path = "guards",
                ValueType = ValueKinds.List,
                Description = "Guards",
                Fields = new List<ObserveInfo> { new ObserveInfo { Path = "distance", ValueType = ValueKinds.Float, Description = "Metres to the player" } },
            });
            h.Actions.Add(new ActionInfo { Name = "goto", Description = "Walk to a zone", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } } });
            h.Actions.Add(new ActionInfo { Name = "stop", Description = "Stand still", Safe = true });
            h.Events.Add(new EventInfo { Name = "guard.spotted", Description = "A guard sees you", Important = true, Outcome = "bad" });
            return h;
        }

        [Fact]
        public void SystemDescribesTheGameAndTheDsl()
        {
            var s = PromptBuilder.BuildSystem(Hello());
            Assert.Contains("Steal the gem and leave.", s);
            Assert.Contains("Demo", s);
            Assert.Contains("- player.zone (string): Zone the player stands in", s);
            Assert.Contains("- guards (list): Guards", s);
            Assert.Contains("  - distance (float): Metres to the player", s);
            Assert.Contains("- goto(zone: string): Walk to a zone", s);
            Assert.Contains("- stop(): Stand still [runs whenever there is no plan]", s);
            Assert.Contains("- guard.spotted: A guard sees you [important] [bad]", s);
            Assert.Contains("event:", s);
            Assert.Contains("min(", s);
        }

        [Fact]
        public void SystemIsIdenticalForTheSameGame()
        {
            Assert.Equal(PromptBuilder.BuildSystem(Hello()), PromptBuilder.BuildSystem(Hello()));
        }

        [Fact]
        public void TurnCarriesStateEventsLastPlanAndHistory()
        {
            var ctx = new BrainContext
            {
                Hello = Hello(),
                State = new JObject { ["player.zone"] = "start" },
                PauseReason = "plan",
                LastPlan = new Plan { Id = "p2", Intent = "grab the gem" },
            };
            ctx.RecentEvents.Add(new EventMessage { Name = "guard.spotted", T = 4.5, Payload = new JObject { ["guard"] = "g1" } });
            ctx.PlanStatuses.Add(new PlanStatusMessage { PlanId = "p2", Status = PlanStatuses.PlanFailed, Step = 1, Reason = "replan:0" });
            for (int i = 0; i < 7; i++) ctx.History.Add("h" + i + ": old -> plan_done");

            var t = PromptBuilder.BuildTurn(ctx);
            Assert.Contains("Paused because: plan", t);
            Assert.Contains("\"player.zone\": \"start\"", t);
            Assert.Contains("t=4.50 guard.spotted {\"guard\":\"g1\"}", t);
            Assert.Contains("p2 (grab the gem)", t);
            Assert.Contains("plan_failed at step 1: replan:0", t);
            Assert.DoesNotContain("h1: old", t);
            Assert.Contains("h2: old -> plan_done", t);
            Assert.Contains("h6: old -> plan_done", t);
        }

        [Fact]
        public void FirstTurnSaysThereIsNoPreviousPlan()
        {
            var t = PromptBuilder.BuildTurn(new BrainContext { Hello = Hello(), State = new JObject(), PauseReason = "mode" });
            Assert.Contains("No plan yet", t);
            Assert.Contains("(none)", t);
        }

        [Fact]
        public void SessionRecordsHistoryWhenAPlanEnds()
        {
            var printed = new List<string>();
            var s = new RunnerSession(RunnerOptions.Parse(new[] { "--brain", "script", "x.json" }), printed.Add);
            s.Handle(MessageCodec.Decode(MessageCodec.Encode(MessageTypes.Hello, 1, Hello())));
            var plan = JObject.Parse("{\"id\":\"p1\",\"intent\":\"go\",\"steps\":[{\"do\":\"goto\",\"args\":{\"zone\":\"a\"}}]}").ToObject<Plan>(ContractJson.Serializer);
            s.OnPlan(plan);
            s.Handle(MessageCodec.Decode(MessageCodec.Encode(MessageTypes.PlanStatus, 2,
                new PlanStatusMessage { PlanId = "p1", Status = PlanStatuses.PlanFailed, Step = 0, Reason = "timeout" })));
            Assert.Equal(new[] { "p1: go -> plan_failed (timeout)" }, s.TakeDecisionContext().History);
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: build FAIL với `The name 'PromptBuilder' does not exist` và `'BrainContext' does not contain a definition for 'History'`.

- [ ] **Step 3: Viết code**

Trong `Runner/Brains/BrainContext.cs`, thêm vào lớp `BrainContext` (sau `PlansSent`):

```csharp
        /// <summary>One line per finished plan: "id: intent -> status (reason)", oldest first.</summary>
        public readonly List<string> History = new List<string>();
        /// <summary>Set by a brain that returns null, to explain why it stopped.</summary>
        public string StopReason;
```

Trong `Runner/RunnerSession.cs`:
- Trong nhánh `case MessageTypes.PlanStatus:`, ngay sau dòng `_context.PlanStatuses.Add(s);` thêm:

```csharp
                    if ((s.Status == PlanStatuses.PlanDone || s.Status == PlanStatuses.PlanFailed) && _context.LastPlan != null && s.PlanId == _context.LastPlan.Id)
                        _context.History.Add(_context.LastPlan.Id + ": " + _context.LastPlan.Intent + " -> " + s.Status
                            + (string.IsNullOrEmpty(s.Reason) ? "" : " (" + s.Reason + ")"));
```

- Trong `OnPlan`, thay `_print("brain has no more plans");` bằng:

```csharp
                _print("brain stopped: " + (_context.StopReason ?? "no more plans"));
```

và sửa test cũ `NoMorePlansFinishes` trong `Runner.Tests/RunnerBrainTests.cs`: đổi `Assert.Contains("brain has no more plans", _printed);` thành `Assert.Contains("brain stopped: no more plans", _printed);`.

`Runner/Brains/PromptBuilder.cs`:

```csharp
using System.Globalization;
using System.Linq;
using System.Text;
using AgentPlay.Contract;
using Newtonsoft.Json;

namespace AgentPlay.Runner.Brains
{
    /// <summary>
    /// Builds the model prompt. The system part depends only on the game's hello, so it is byte-identical
    /// across turns and can be cached; everything that changes goes into the turn message.
    /// </summary>
    public static class PromptBuilder
    {
        public const int MaxHistory = 5;

        const string Instructions =
@"You control the player character of a video game through AgentPlay. The game is paused while you think, so take the time you need: nothing happens until you answer.

Each turn you receive the current state and what happened since your last plan. Answer with exactly one plan in the required JSON format. The game runs your plan frame by frame until it finishes, fails, or something important happens; then it pauses and asks you again.

How a plan runs:
- steps run in order. A step calls its action every frame until the action reports it is done. If ""until"" is a non-empty condition, the step also ends as soon as that condition becomes true. Use an empty string when the step should simply run to completion.
- rules are checked every frame before the current step. A rule fires once when its ""when"" condition turns true, runs its action to completion, then does ""then"": resume (restart the current step), next (skip to the next step), replan (stop and ask you again), or abort (stop).
- If an action fails, the plan fails and you are asked again with the reason.
- timeout_s limits the plan in game seconds (at most 600). A plan has 1 to 12 steps and at most 8 rules.

Conditions compare observe paths with numbers, true/false or 'single-quoted strings' using < <= > >= == !=, and combine with && || ! and parentheses. event:name is true in the frame that event happens. For list observes use min(list.field), max(list.field), count(list), count(list.boolfield) or any(list.boolfield); a list field cannot be used directly. A missing value makes a comparison false.

Good plans are short (2 to 6 steps), use rules to react to danger, and use until conditions to wait for the right moment. Plans are cheap: when the situation is unclear, make a short plan and look again. The plan id can be any short string; the runner renumbers it.";

        public static string BuildSystem(HelloMessage hello)
        {
            var sb = new StringBuilder();
            sb.Append(Instructions).Append("\n\n");
            sb.Append("# Game: ").Append(hello.Game);
            if (!string.IsNullOrEmpty(hello.Version)) sb.Append(" (version ").Append(hello.Version).Append(')');
            sb.Append("\n\n").Append(string.IsNullOrWhiteSpace(hello.Brief) ? "(The game sent no description.)" : hello.Brief.Trim()).Append("\n\n");

            sb.Append("## Observes (the state you receive)\n");
            foreach (var o in hello.Observes)
            {
                sb.Append("- ").Append(o.Path).Append(" (").Append(o.ValueType).Append(')').Append(Describe(o.Description)).Append('\n');
                if (o.Fields != null)
                    foreach (var f in o.Fields)
                        sb.Append("  - ").Append(f.Path).Append(" (").Append(f.ValueType).Append(')').Append(Describe(f.Description)).Append('\n');
            }

            sb.Append("\n## Actions\n");
            foreach (var a in hello.Actions)
            {
                sb.Append("- ").Append(a.Name).Append('(')
                  .Append(string.Join(", ", a.Params.Select(p => p.Name + ": " + p.ValueType)))
                  .Append(')').Append(Describe(a.Description));
                if (a.Safe) sb.Append(" [runs whenever there is no plan]");
                sb.Append('\n');
            }

            sb.Append("\n## Events\n");
            if (hello.Events == null || hello.Events.Count == 0) sb.Append("(none declared)\n");
            else
                foreach (var e in hello.Events)
                {
                    sb.Append("- ").Append(e.Name).Append(Describe(e.Description));
                    if (e.Important) sb.Append(" [important]");
                    if (!string.IsNullOrEmpty(e.Outcome) && e.Outcome != "neutral") sb.Append(" [").Append(e.Outcome).Append(']');
                    sb.Append('\n');
                }
            sb.Append("\nImportant events pause the game and ask you again unless a rule of your plan reacts to them. game.won and game.lost end the round.\n");
            return sb.ToString();
        }

        public static string BuildTurn(BrainContext context)
        {
            var sb = new StringBuilder();
            sb.Append("# Situation\n");
            sb.Append("Paused because: ").Append(context.PauseReason ?? "unknown").Append('\n');
            sb.Append("(mode = start of play, plan = your last plan finished or failed, event = an important event, budget = time budget used up)\n\n");
            sb.Append("State:\n```json\n").Append(context.State == null ? "{}" : context.State.ToString(Formatting.Indented)).Append("\n```\n\n");

            sb.Append("Events since your last plan (oldest first):\n");
            if (context.RecentEvents.Count == 0) sb.Append("(none)\n");
            foreach (var e in context.RecentEvents)
                sb.Append("- t=").Append(e.T.ToString("0.00", CultureInfo.InvariantCulture)).Append(' ').Append(e.Name)
                  .Append(' ').Append(e.Payload == null ? "{}" : e.Payload.ToString(Formatting.None)).Append('\n');

            sb.Append("\nYour last plan: ");
            if (context.LastPlan == null) sb.Append("No plan yet.\n");
            else
            {
                sb.Append(context.LastPlan.Id).Append(" (").Append(context.LastPlan.Intent).Append(")\n");
                foreach (var s in context.PlanStatuses)
                {
                    sb.Append("- ").Append(s.Status).Append(" at step ").Append(s.Step);
                    if (!string.IsNullOrEmpty(s.Reason)) sb.Append(": ").Append(s.Reason);
                    sb.Append('\n');
                }
            }

            var history = context.History.Skip(System.Math.Max(0, context.History.Count - MaxHistory)).ToList();
            if (history.Count > 0)
            {
                sb.Append("\nEarlier plans (oldest first):\n");
                foreach (var h in history) sb.Append("- ").Append(h).Append('\n');
            }
            sb.Append("\nAnswer with your next plan.");
            return sb.ToString();
        }

        static string Describe(string description) => string.IsNullOrWhiteSpace(description) ? "" : ": " + description.Trim();
    }
}
```

- [ ] **Step 4: Chạy test để thấy pass**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: PASS, `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add Runner Runner.Tests
git commit -m "feat(runner): cache-stable system prompt, turn prompt and plan history" -m "<trailers>"
```

---

### Task 3: IModelClient và OpusBrain

**Files:**
- Create: `Runner/Brains/ModelClient.cs`, `Runner/Brains/OpusBrain.cs`
- Test: `Runner.Tests/OpusBrainTests.cs`

**Interfaces:**
- Consumes: `PlanSchema` (Task 1), `PromptBuilder` (Task 2), `PlanValidator`.
- Produces:
  - `ModelRequest { string System; string User; JObject Schema; }`.
  - `ModelReply { string Text; string StopReason; string Model; long InputTokens; long OutputTokens; long CacheReadTokens; long CacheWriteTokens; long LatencyMs; }`.
  - `interface IModelClient { Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct); }`.
  - `ModelCallException : Exception` (lỗi gọi API có thể thử lại).
  - `CallRecord { DateTime Time; string Model; long LatencyMs; long InputTokens; long OutputTokens; long CacheReadTokens; long CacheWriteTokens; string StopReason; string PlanId; bool Valid; string Error; }`.
  - `OpusBrain(IModelClient client, int maxCalls, Action<string> log)`: `IBrain`; `Calls`, `InputTokens`, `OutputTokens`, `CacheReadTokens`, `CacheWriteTokens`; `event Action<BrainContext, CallRecord> Called`; `RetryDelaysMs : int[]` (mặc định `{2000, 10000}`).

Hành vi:
- Mỗi lượt quyết định: dựng system (chỉ dựng lại khi `hello` đổi), schema và tin nhắn lượt. Gọi model.
- Kết quả: `refusal` hoặc JSON hỏng hoặc `PlanValidator` báo lỗi thì xin lại đúng một lần, tin nhắn user thêm đoạn "Your previous answer was rejected: ...". Sai lần hai thì `StopReason = "the model produced two invalid plans in a row: ..."` và trả null.
- Plan hợp lệ: đặt lại `Id = "p" + (PlansSent + 1)` rồi trả về.
- `ModelCallException`: chờ `RetryDelaysMs[min(i, len-1)]` rồi thử lại. Mỗi lần thử tính một lượt gọi. Trước mỗi lần gọi, nếu `Calls >= maxCalls` thì `StopReason = "call budget of N reached"` và trả null.
- Sau mỗi lần gọi (thành công hay hỏng ở bước parse), phát `Called` với `CallRecord`.

- [ ] **Step 1: Viết test thất bại**

`Runner.Tests/OpusBrainTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentPlay.Contract;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class OpusBrainTests
    {
        sealed class FakeClient : IModelClient
        {
            public readonly Queue<Func<ModelRequest, ModelReply>> Replies = new Queue<Func<ModelRequest, ModelReply>>();
            public readonly List<ModelRequest> Requests = new List<ModelRequest>();

            public Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct)
            {
                Requests.Add(request);
                return Task.FromResult(Replies.Dequeue()(request));
            }
        }

        static ModelReply Reply(string text, string stop = "end_turn") =>
            new ModelReply { Text = text, StopReason = stop, Model = "claude-opus-5-5", InputTokens = 100, OutputTokens = 50, CacheReadTokens = 80, LatencyMs = 1200 };

        const string GoodPlan = "{\"id\":\"x\",\"intent\":\"leave\",\"steps\":[{\"do\":\"goto\",\"args\":{\"zone\":\"exit\"},\"until\":\"\"}],\"rules\":[],\"timeout_s\":30}";
        const string BadPlan = "{\"id\":\"x\",\"intent\":\"leave\",\"steps\":[{\"do\":\"goto\",\"args\":{\"zone\":\"exit\"},\"until\":\"nope > 1\"}],\"rules\":[],\"timeout_s\":30}";

        static BrainContext Context()
        {
            var h = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G" };
            h.Observes.Add(new ObserveInfo { Path = "x", ValueType = ValueKinds.Float });
            h.Actions.Add(new ActionInfo { Name = "goto", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } } });
            return new BrainContext { Hello = h, State = new JObject { ["x"] = 1 }, PauseReason = "mode" };
        }

        readonly List<string> _log = new List<string>();
        readonly List<CallRecord> _calls = new List<CallRecord>();

        OpusBrain Brain(FakeClient client, int maxCalls = 40)
        {
            var brain = new OpusBrain(client, maxCalls, _log.Add) { RetryDelaysMs = new[] { 0, 0 } };
            brain.Called += (ctx, record) => _calls.Add(record);
            return brain;
        }

        [Fact]
        public async Task ValidPlanIsReturnedRenumberedAndCounted()
        {
            var client = new FakeClient();
            client.Replies.Enqueue(_ => Reply(GoodPlan));
            var brain = Brain(client);
            var ctx = Context();
            ctx.PlansSent = 2;

            var plan = await brain.NextPlanAsync(ctx, CancellationToken.None);

            Assert.Equal("p3", plan.Id);
            Assert.Equal(1, brain.Calls);
            Assert.Equal(100, brain.InputTokens);
            Assert.Equal(80, brain.CacheReadTokens);
            Assert.Single(_calls);
            Assert.True(_calls[0].Valid);
            Assert.Equal("p3", _calls[0].PlanId);
            Assert.Contains("# Game: G", client.Requests[0].System);
            Assert.Contains("Paused because: mode", client.Requests[0].User);
            Assert.Equal("object", (string)client.Requests[0].Schema["type"]);
        }

        [Fact]
        public async Task InvalidPlanIsRetriedOnceWithTheErrors()
        {
            var client = new FakeClient();
            client.Replies.Enqueue(_ => Reply(BadPlan));
            client.Replies.Enqueue(_ => Reply(GoodPlan));
            var plan = await Brain(client).NextPlanAsync(Context(), CancellationToken.None);

            Assert.NotNull(plan);
            Assert.Equal(2, client.Requests.Count);
            Assert.Contains("Your previous answer was rejected", client.Requests[1].User);
            Assert.Contains("Unknown observe 'nope'", client.Requests[1].User);
            Assert.Equal(client.Requests[0].System, client.Requests[1].System);
            Assert.False(_calls[0].Valid);
            Assert.Contains("Unknown observe", _calls[0].Error);
        }

        [Theory]
        [InlineData("not json at all", "end_turn")]
        [InlineData(GoodPlan, "refusal")]
        public async Task TwoBadAnswersStopTheBrain(string text, string stopReason)
        {
            var client = new FakeClient();
            client.Replies.Enqueue(_ => Reply(text, stopReason));
            client.Replies.Enqueue(_ => Reply(text, stopReason));
            var ctx = Context();
            var plan = await Brain(client).NextPlanAsync(ctx, CancellationToken.None);

            Assert.Null(plan);
            Assert.StartsWith("the model produced two invalid plans in a row", ctx.StopReason);
            Assert.Equal(2, client.Requests.Count);
        }

        [Fact]
        public async Task ApiErrorsAreRetriedAndCounted()
        {
            var client = new FakeClient();
            client.Replies.Enqueue(_ => throw new ModelCallException("overloaded", null));
            client.Replies.Enqueue(_ => Reply(GoodPlan));
            var brain = Brain(client);
            var plan = await brain.NextPlanAsync(Context(), CancellationToken.None);

            Assert.NotNull(plan);
            Assert.Equal(2, brain.Calls);
            Assert.Contains(_log, l => l.Contains("overloaded"));
        }

        [Fact]
        public async Task CallBudgetStopsTheBrain()
        {
            var client = new FakeClient();
            for (int i = 0; i < 5; i++) client.Replies.Enqueue(_ => throw new ModelCallException("down", null));
            var ctx = Context();
            var brain = Brain(client, maxCalls: 3);
            var plan = await brain.NextPlanAsync(ctx, CancellationToken.None);

            Assert.Null(plan);
            Assert.Equal(3, brain.Calls);
            Assert.Equal("call budget of 3 reached", ctx.StopReason);
        }

        [Fact]
        public async Task SystemPromptIsBuiltOncePerGame()
        {
            var client = new FakeClient();
            client.Replies.Enqueue(_ => Reply(GoodPlan));
            client.Replies.Enqueue(_ => Reply(GoodPlan));
            var brain = Brain(client);
            var ctx = Context();
            await brain.NextPlanAsync(ctx, CancellationToken.None);
            ctx.State = new JObject { ["x"] = 9 };
            await brain.NextPlanAsync(ctx, CancellationToken.None);
            Assert.Same(client.Requests[0].System, client.Requests[1].System);
            Assert.NotEqual(client.Requests[0].User, client.Requests[1].User);
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: build FAIL với `The type or namespace name 'IModelClient' could not be found`.

- [ ] **Step 3: Viết code**

`Runner/Brains/ModelClient.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner.Brains
{
    public sealed class ModelRequest
    {
        /// <summary>Static per game; cached by the API.</summary>
        public string System;
        public string User;
        /// <summary>JSON schema the reply must follow (structured outputs).</summary>
        public JObject Schema;
    }

    public sealed class ModelReply
    {
        public string Text;
        public string StopReason;
        public string Model;
        public long InputTokens;
        public long OutputTokens;
        public long CacheReadTokens;
        public long CacheWriteTokens;
        public long LatencyMs;
    }

    public interface IModelClient
    {
        Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct);
    }

    /// <summary>A failed model call that is worth retrying (network, overload, timeout).</summary>
    public sealed class ModelCallException : Exception
    {
        public ModelCallException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class CallRecord
    {
        public DateTime Time;
        public string Model;
        public long LatencyMs;
        public long InputTokens;
        public long OutputTokens;
        public long CacheReadTokens;
        public long CacheWriteTokens;
        public string StopReason;
        public string PlanId;
        public bool Valid;
        public string Error;
    }
}
```

`Runner/Brains/OpusBrain.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentPlay.Contract;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner.Brains
{
    /// <summary>Asks a Claude model for each plan; validates, retries once on a bad plan, and respects a call budget.</summary>
    public sealed class OpusBrain : IBrain
    {
        readonly IModelClient _client;
        readonly int _maxCalls;
        readonly Action<string> _log;
        HelloMessage _helloForPrompt;
        string _system;
        JObject _schema;

        public int Calls { get; private set; }
        public long InputTokens { get; private set; }
        public long OutputTokens { get; private set; }
        public long CacheReadTokens { get; private set; }
        public long CacheWriteTokens { get; private set; }

        /// <summary>Waits before retrying a failed call; the last value repeats.</summary>
        public int[] RetryDelaysMs { get; set; } = { 2000, 10000 };

        public event Action<BrainContext, CallRecord> Called;

        public OpusBrain(IModelClient client, int maxCalls, Action<string> log)
        {
            _client = client;
            _maxCalls = maxCalls;
            _log = log;
        }

        public async Task<Plan> NextPlanAsync(BrainContext context, CancellationToken ct)
        {
            if (!ReferenceEquals(_helloForPrompt, context.Hello))
            {
                _helloForPrompt = context.Hello;
                _system = PromptBuilder.BuildSystem(context.Hello);
                _schema = PlanSchema.Build(context.Hello);
            }
            string turn = PromptBuilder.BuildTurn(context);
            string feedback = null;
            string lastError = null;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                var request = new ModelRequest { System = _system, User = feedback == null ? turn : turn + "\n\n" + feedback, Schema = _schema };
                var reply = await CallWithRetry(request, context, ct);
                if (reply == null) return null;

                var record = new CallRecord
                {
                    Time = DateTime.Now,
                    Model = reply.Model,
                    LatencyMs = reply.LatencyMs,
                    InputTokens = reply.InputTokens,
                    OutputTokens = reply.OutputTokens,
                    CacheReadTokens = reply.CacheReadTokens,
                    CacheWriteTokens = reply.CacheWriteTokens,
                    StopReason = reply.StopReason,
                };
                var plan = Parse(reply, context, out lastError);
                record.Valid = plan != null;
                record.Error = lastError;
                if (plan != null)
                {
                    plan.Id = "p" + (context.PlansSent + 1);
                    record.PlanId = plan.Id;
                }
                Called?.Invoke(context, record);
                _log("model " + reply.Model + " " + reply.LatencyMs + " ms, in " + reply.InputTokens + " (cache " + reply.CacheReadTokens
                     + ") out " + reply.OutputTokens + (plan != null ? ", plan ok" : ", rejected: " + lastError));
                if (plan != null) return plan;
                feedback = "Your previous answer was rejected: " + lastError + "\nSend a corrected plan.";
            }

            context.StopReason = "the model produced two invalid plans in a row: " + lastError;
            return null;
        }

        static Plan Parse(ModelReply reply, BrainContext context, out string error)
        {
            if (reply.StopReason == "refusal")
            {
                error = "the model declined to answer (refusal)";
                return null;
            }
            Plan plan;
            try
            {
                plan = JObject.Parse(reply.Text ?? "").ToObject<Plan>(ContractJson.Serializer);
            }
            catch (JsonException e)
            {
                error = "the answer is not valid plan JSON: " + e.Message;
                return null;
            }
            var errors = PlanValidator.Validate(plan, context.Hello);
            if (errors.Count > 0)
            {
                error = string.Join("; ", errors);
                return null;
            }
            error = null;
            return plan;
        }

        async Task<ModelReply> CallWithRetry(ModelRequest request, BrainContext context, CancellationToken ct)
        {
            for (int attempt = 0; ; attempt++)
            {
                if (Calls >= _maxCalls)
                {
                    context.StopReason = "call budget of " + _maxCalls + " reached";
                    return null;
                }
                Calls++;
                try
                {
                    var reply = await _client.CompleteAsync(request, ct);
                    InputTokens += reply.InputTokens;
                    OutputTokens += reply.OutputTokens;
                    CacheReadTokens += reply.CacheReadTokens;
                    CacheWriteTokens += reply.CacheWriteTokens;
                    return reply;
                }
                catch (ModelCallException e)
                {
                    int delay = RetryDelaysMs[Math.Min(attempt, RetryDelaysMs.Length - 1)];
                    _log("model call failed (" + e.Message + "); retrying in " + delay + " ms");
                    if (delay > 0) await Task.Delay(delay, ct);
                }
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
git add Runner Runner.Tests
git commit -m "feat(runner): model-backed brain with plan validation retry and call budget" -m "<trailers>"
```

---

### Task 4: RunnerConfig và RunLog

**Files:**
- Create: `Runner/RunnerConfig.cs`, `Runner/RunLog.cs`, `Runner/runner.json`
- Modify: `Runner/AgentPlay.Runner.csproj` (copy `runner.json` ra thư mục build)
- Test: `Runner.Tests/RunnerConfigTests.cs`

**Interfaces:**
- Produces:
  - `RunnerConfig { string Model = "claude-opus-5-5"; string Effort = "high"; int MaxTokens = 16000; int MaxCalls = 40; }`, `RunnerConfig.Load(string path)`, `RunnerConfig.Validate()` (ném `ArgumentException`), `RunnerConfig.Efforts = { low, medium, high, xhigh, max }`.
  - `RunLog.Open(string projectRoot, DateTime now) : RunLog` tạo `<root>/AgentPlay/Runs/<yyyyMMdd-HHmmss>.ndjson`; `RunLog(TextWriter)`; `WriteCall(CallRecord r)`; `WriteSummary(string result, int calls, long inputTokens, long outputTokens, long cacheRead, long cacheWrite)`; `Path`.

- [ ] **Step 1: Viết test thất bại**

`Runner.Tests/RunnerConfigTests.cs`:

```csharp
using System;
using System.IO;
using AgentPlay.Runner;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class RunnerConfigTests
    {
        [Fact]
        public void MissingFileGivesDefaults()
        {
            var c = RunnerConfig.Load(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N") + ".json"));
            Assert.Equal("claude-opus-5-5", c.Model);
            Assert.Equal("high", c.Effort);
            Assert.Equal(16000, c.MaxTokens);
            Assert.Equal(40, c.MaxCalls);
        }

        [Fact]
        public void FileOverridesDefaults()
        {
            var path = Path.Combine(Path.GetTempPath(), "runner-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{\"effort\":\"medium\",\"max_calls\":12}");
            try
            {
                var c = RunnerConfig.Load(path);
                Assert.Equal("medium", c.Effort);
                Assert.Equal(12, c.MaxCalls);
                Assert.Equal("claude-opus-5-5", c.Model);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Theory]
        [InlineData("{\"effort\":\"huge\"}", "effort")]
        [InlineData("{\"max_calls\":0}", "max_calls")]
        [InlineData("{\"max_tokens\":-1}", "max_tokens")]
        [InlineData("{\"model\":\"\"}", "model")]
        public void ValidateRejectsBadValues(string json, string field)
        {
            var c = JObject.Parse(json).ToObject<RunnerConfig>();
            var e = Assert.Throws<ArgumentException>(() => c.Validate());
            Assert.Contains(field, e.Message);
        }

        [Fact]
        public void RunLogWritesCallsAndSummary()
        {
            var sw = new StringWriter();
            using (var log = new RunLog(sw))
            {
                log.WriteCall(new CallRecord { Time = new DateTime(2026, 10, 12, 9, 0, 0), Model = "m", LatencyMs = 900, InputTokens = 10, OutputTokens = 5, CacheReadTokens = 8, StopReason = "end_turn", PlanId = "p1", Valid = true });
                log.WriteSummary("won", 3, 30, 15, 24, 6);
            }
            var lines = sw.ToString().Trim().Split('\n');
            var call = JObject.Parse(lines[0]);
            Assert.Equal("call", (string)call["type"]);
            Assert.Equal("p1", (string)call["plan_id"]);
            Assert.Equal(8, (long)call["cache_read"]);
            var summary = JObject.Parse(lines[1]);
            Assert.Equal("summary", (string)summary["type"]);
            Assert.Equal("won", (string)summary["result"]);
            Assert.Equal(3, (int)summary["calls"]);
        }

        [Fact]
        public void RunLogOpenCreatesTheRunsFolder()
        {
            var root = Path.Combine(Path.GetTempPath(), "agentplay-runs-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var log = RunLog.Open(root, new DateTime(2026, 10, 12, 9, 5, 7)))
                    Assert.Equal(Path.Combine(root, "AgentPlay", "Runs", "20261012-090507.ndjson"), log.Path);
                Assert.True(File.Exists(Path.Combine(root, "AgentPlay", "Runs", "20261012-090507.ndjson")));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: build FAIL với `The type or namespace name 'RunnerConfig' could not be found`.

- [ ] **Step 3: Viết code**

`Runner/RunnerConfig.cs`:

```csharp
using System;
using System.IO;
using Newtonsoft.Json;

namespace AgentPlay.Runner
{
    /// <summary>Model settings from runner.json next to the runner; command-line flags override them.</summary>
    public sealed class RunnerConfig
    {
        public static readonly string[] Efforts = { "low", "medium", "high", "xhigh", "max" };

        [JsonProperty("model")] public string Model = "claude-opus-5-5";
        [JsonProperty("effort")] public string Effort = "high";
        [JsonProperty("max_tokens")] public int MaxTokens = 16000;
        [JsonProperty("max_calls")] public int MaxCalls = 40;

        public static RunnerConfig Load(string path)
        {
            if (!File.Exists(path)) return new RunnerConfig();
            return JsonConvert.DeserializeObject<RunnerConfig>(File.ReadAllText(path)) ?? new RunnerConfig();
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Model)) throw new ArgumentException("runner config: model is required");
            if (Array.IndexOf(Efforts, Effort) < 0) throw new ArgumentException("runner config: effort must be one of " + string.Join(", ", Efforts));
            if (MaxTokens <= 0) throw new ArgumentException("runner config: max_tokens must be positive");
            if (MaxCalls <= 0) throw new ArgumentException("runner config: max_calls must be positive");
        }
    }
}
```

`Runner/RunLog.cs`:

```csharp
using System;
using System.Globalization;
using System.IO;
using System.Text;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner
{
    /// <summary>One NDJSON line per model call, plus a summary line, next to the game's traces.</summary>
    public sealed class RunLog : IDisposable
    {
        readonly TextWriter _out;

        public string Path { get; private set; }

        public RunLog(TextWriter output)
        {
            _out = output;
        }

        public static RunLog Open(string projectRoot, DateTime now)
        {
            string dir = System.IO.Path.Combine(projectRoot, "AgentPlay", "Runs");
            Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".ndjson");
            var writer = new StreamWriter(path, true, new UTF8Encoding(false)) { AutoFlush = true };
            return new RunLog(writer) { Path = path };
        }

        public void WriteCall(CallRecord r)
        {
            Write(new JObject
            {
                ["type"] = "call",
                ["time"] = r.Time.ToString("o", CultureInfo.InvariantCulture),
                ["model"] = r.Model,
                ["latency_ms"] = r.LatencyMs,
                ["input_tokens"] = r.InputTokens,
                ["output_tokens"] = r.OutputTokens,
                ["cache_read"] = r.CacheReadTokens,
                ["cache_write"] = r.CacheWriteTokens,
                ["stop_reason"] = r.StopReason,
                ["plan_id"] = r.PlanId,
                ["valid"] = r.Valid,
                ["error"] = r.Error,
            });
        }

        public void WriteSummary(string result, int calls, long inputTokens, long outputTokens, long cacheRead, long cacheWrite)
        {
            Write(new JObject
            {
                ["type"] = "summary",
                ["result"] = result,
                ["calls"] = calls,
                ["input_tokens"] = inputTokens,
                ["output_tokens"] = outputTokens,
                ["cache_read"] = cacheRead,
                ["cache_write"] = cacheWrite,
            });
        }

        void Write(JObject o)
        {
            _out.Write(o.ToString(Formatting.None));
            _out.Write('\n');
        }

        public void Dispose() => _out.Dispose();
    }
}
```

`Runner/runner.json`:

```json
{
  "model": "claude-opus-5-5",
  "effort": "high",
  "max_tokens": 16000,
  "max_calls": 40
}
```

Trong `Runner/AgentPlay.Runner.csproj`, thêm trước `</Project>`:

```xml
  <ItemGroup>
    <None Update="runner.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 4: Chạy test để thấy pass**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: PASS, `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add Runner Runner.Tests
git commit -m "feat(runner): runner.json model settings and per-call run log" -m "<trailers>"
```

---

### Task 5: AnthropicModelClient và nối `--brain opus`

Lớp bọc SDK không có unit test (phải gọi API thật). Nó được kiểm bằng `--smoke`: một lượt gọi nhỏ có chi phí cỡ vài cent, chạy ở Task 6 khi có thông tin đăng nhập. Phần tùy chọn dòng lệnh vẫn có test.

**Files:**
- Modify: `Runner/AgentPlay.Runner.csproj` (thêm `PackageReference` `Anthropic` 12.55.0)
- Create: `Runner/Brains/AnthropicModelClient.cs`
- Modify: `Runner/RunnerOptions.cs`, `Runner/Program.cs`
- Modify: `Runner.Tests/RunnerBrainTests.cs` (test tùy chọn)

**Interfaces:**
- Consumes: `IModelClient`, `ModelRequest`, `ModelReply`, `ModelCallException` (Task 3); `RunnerConfig`, `RunLog` (Task 4).
- Produces:
  - `AnthropicModelClient(RunnerConfig config)`: `IModelClient`.
  - `RunnerOptions.Brain` nhận thêm `"opus"`; `RunnerOptions.Model`, `Effort`, `MaxCalls` (null nghĩa là dùng `runner.json`); `RunnerOptions.Smoke : bool` (`--smoke`: gọi model một lần với game giả rồi thoát).

- [ ] **Step 1: Viết test thất bại**

Trong `Runner.Tests/RunnerBrainTests.cs`, theory `InvalidBrainOptions`: xóa hai dòng `[InlineData("--brain opus", "M2b")]` và `[InlineData("--brain manual", "M2b")]`, thêm `[InlineData("--brain manual", "Unknown brain")]` và `[InlineData("--brain opus --effort huge", "effort")]`. Thêm test:

```csharp
        [Fact]
        public void OpusBrainOptions()
        {
            var o = RunnerOptions.Parse(new[] { "--brain", "opus", "--model", "claude-opus-5-5", "--effort", "medium", "--max-calls", "7" });
            Assert.Equal("opus", o.Brain);
            Assert.Equal(Modes.TurnBased, o.Mode);
            Assert.Equal("claude-opus-5-5", o.Model);
            Assert.Equal("medium", o.Effort);
            Assert.Equal(7, o.MaxCalls);
            var smoke = RunnerOptions.Parse(new[] { "--smoke" });
            Assert.True(smoke.Smoke);
        }
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: build FAIL với `'RunnerOptions' does not contain a definition for 'Model'`.

- [ ] **Step 3: Viết code**

```bash
cd /e/Unity-WorkSpace/AgentPlay
dotnet add Runner/AgentPlay.Runner.csproj package Anthropic --version 12.55.0
```

`Runner/Brains/AnthropicModelClient.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Models.Beta.Messages;

namespace AgentPlay.Runner.Brains
{
    /// <summary>
    /// Calls the Messages API through the official C# SDK: structured outputs for the plan, a cached
    /// system prompt, explicit effort, and server-side refusal fallbacks. The beta endpoint is used for the fallbacks.
    /// </summary>
    public sealed class AnthropicModelClient : IModelClient
    {
        const string FallbackBeta = "server-side-fallback-2026-07-01";

        readonly AnthropicClient _client = new AnthropicClient();
        readonly RunnerConfig _config;

        public AnthropicModelClient(RunnerConfig config)
        {
            _config = config;
        }

        public async Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct)
        {
            var parameters = new MessageCreateParams
            {
                Model = _config.Model,
                MaxTokens = _config.MaxTokens,
                Betas = [FallbackBeta],
                Fallbacks = new BetaFallbacksParam(JsonSerializer.SerializeToElement("default")),
                System = new List<BetaTextBlockParam>
                {
                    new() { Text = request.System, CacheControl = new BetaCacheControlEphemeral() },
                },
                Messages = [new() { Role = Role.User, Content = request.User }],
                OutputConfig = new BetaOutputConfig
                {
                    Effort = ToEffort(_config.Effort),
                    Format = new BetaJsonOutputFormat
                    {
                        Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.Schema.ToString()),
                    },
                },
            };

            var watch = Stopwatch.StartNew();
            BetaMessage message;
            try
            {
                message = await _client.Beta.Messages.Create(parameters, cancellationToken: ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new ModelCallException(e.GetType().Name + ": " + e.Message, e);
            }
            watch.Stop();

            return new ModelReply
            {
                Text = string.Concat(message.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text)),
                StopReason = message.StopReason?.ToString(),
                Model = message.Model.ToString(),
                InputTokens = message.Usage.InputTokens,
                OutputTokens = message.Usage.OutputTokens,
                CacheReadTokens = message.Usage.CacheReadInputTokens ?? 0,
                CacheWriteTokens = message.Usage.CacheCreationInputTokens ?? 0,
                LatencyMs = watch.ElapsedMilliseconds,
            };
        }

        static Effort ToEffort(string effort)
        {
            switch (effort)
            {
                case "low": return Effort.Low;
                case "medium": return Effort.Medium;
                case "xhigh": return Effort.Xhigh;
                case "max": return Effort.Max;
                default: return Effort.High;
            }
        }
    }
}
```

Tên kiểu và thuộc tính của SDK đã được kiểm biên dịch bằng project nháp trước khi viết plan (`BetaFallbacksParam(JsonElement)`, `BetaTextBlockParam`, `BetaCacheControlEphemeral`, `BetaOutputConfig`, `BetaJsonOutputFormat`, `Effort.High`). Những chỗ chưa kiểm (`cancellationToken:`, `Effort.Xhigh`, kiểu nullable của các trường cache, `StopReason?.ToString()`): sửa theo lỗi biên dịch, giữ nguyên hành vi, rồi ghi lại thành ruling.

Trong `Runner/RunnerOptions.cs`:
- Thêm field: `public string Model; public string Effort; public int? MaxCalls; public bool Smoke;`.
- Trong `switch`, nhánh `case "--brain":` thay khối `else if (brain == "opus" || brain == "manual") { ... }` bằng:

```csharp
                        else if (brain == "opus")
                        {
                            o.Brain = brain;
                        }
```

- Thêm các nhánh:

```csharp
                    case "--model":
                        o.Model = Value(args, ref i, a);
                        break;
                    case "--effort":
                        o.Effort = Value(args, ref i, a);
                        if (Array.IndexOf(RunnerConfig.Efforts, o.Effort) < 0)
                            throw new ArgumentException("--effort must be one of " + string.Join(", ", RunnerConfig.Efforts));
                        break;
                    case "--max-calls":
                        o.MaxCalls = (int)Number(Value(args, ref i, a), a);
                        if (o.MaxCalls <= 0) throw new ArgumentException("--max-calls must be positive");
                        break;
                    case "--smoke":
                        o.Smoke = true;
                        break;
```

- `Usage` thành: `"Usage: AgentPlay.Runner [--brain script <file> | --brain opus [--model M] [--effort E] [--max-calls N]] [--smoke] [--mode turn_based|realtime] [--auto-resume] [--resume-seconds N] [--think-ms N] [--ports A-B] [--idle-exit SECONDS]"`.

Trong `Runner/Program.cs`:
- Sau khối nạp `ScriptBrain`, thêm:

```csharp
            OpusBrain opus = null;
            RunLog runLog = null;
            if (options.Brain == "opus" || options.Smoke)
            {
                var config = RunnerConfig.Load(Path.Combine(AppContext.BaseDirectory, "runner.json"));
                if (options.Model != null) config.Model = options.Model;
                if (options.Effort != null) config.Effort = options.Effort;
                if (options.MaxCalls.HasValue) config.MaxCalls = options.MaxCalls.Value;
                try
                {
                    config.Validate();
                }
                catch (ArgumentException e)
                {
                    Console.Error.WriteLine(e.Message);
                    return 2;
                }
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
                    Log("WARNING: ANTHROPIC_API_KEY is not set; the SDK will try other credentials (ant auth login profile)");
                opus = new OpusBrain(new AnthropicModelClient(config), config.MaxCalls, Log);
                opus.Called += (ctx, record) =>
                {
                    if (runLog == null && ctx.Hello?.ProjectRoot != null)
                    {
                        runLog = RunLog.Open(ctx.Hello.ProjectRoot, DateTime.Now);
                        Log("run log: " + runLog.Path);
                    }
                    runLog?.WriteCall(record);
                };
                brain = opus;
                Log("opus brain: model " + config.Model + ", effort " + config.Effort + ", at most " + config.MaxCalls + " calls");
                if (options.Smoke) return await Smoke(opus);
            }
```

- Thay đoạn cuối `Main` (từ `if (session.Finished) Log(...)`) bằng:

```csharp
            if (session.Finished) Log("finished with exit code " + session.ExitCode);
            else if (session.ExitCodeOnStop != 0) Log("stopped before the run finished; exit code " + session.ExitCodeOnStop);
            if (opus != null)
            {
                string result = session.GameResult ?? (session.Finished ? "stopped" : "unfinished");
                Log("model calls " + opus.Calls + ", input tokens " + opus.InputTokens + " (cache read " + opus.CacheReadTokens
                    + ", cache write " + opus.CacheWriteTokens + "), output tokens " + opus.OutputTokens);
                runLog?.WriteSummary(result, opus.Calls, opus.InputTokens, opus.OutputTokens, opus.CacheReadTokens, opus.CacheWriteTokens);
                runLog?.Dispose();
            }
            return session.ExitCodeOnStop;
```

- Thêm hàm:

```csharp
        /// <summary>One cheap model call against a made-up game, to check credentials and the request shape.</summary>
        static async Task<int> Smoke(OpusBrain brain)
        {
            var hello = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "Smoke test", Brief = "Walk to the exit." };
            hello.Observes.Add(new ObserveInfo { Path = "player.zone", ValueType = ValueKinds.String, Description = "Zone the player stands in" });
            hello.Actions.Add(new ActionInfo
            {
                Name = "goto",
                Description = "Walk to a zone: start or exit",
                Params = new System.Collections.Generic.List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } },
            });
            hello.Actions.Add(new ActionInfo { Name = "stop", Description = "Stand still", Safe = true });
            var context = new BrainContext { Hello = hello, State = new Newtonsoft.Json.Linq.JObject { ["player.zone"] = "start" }, PauseReason = "mode" };
            var plan = await brain.NextPlanAsync(context, CancellationToken.None);
            if (plan == null)
            {
                Log("smoke test failed: " + context.StopReason);
                return 1;
            }
            Log("smoke test plan: " + Newtonsoft.Json.JsonConvert.SerializeObject(plan));
            Log("smoke test tokens: in " + brain.InputTokens + ", out " + brain.OutputTokens);
            return 0;
        }
```

Thêm `using AgentPlay.Runner.Brains;` ở đầu `Program.cs` nếu chưa có.

- [ ] **Step 4: Chạy test để thấy pass**

Run:

```bash
cd /e/Unity-WorkSpace/AgentPlay
dotnet build Runner -v q -nologo 2>&1 | grep -E "error|Build succeeded"
dotnet test AgentPlay.sln
bash Tools/check-generic.sh
```

Expected: build thành công, tests PASS, `check-generic: OK`.

- [ ] **Step 5: Commit**

```bash
git add Runner Runner.Tests
git commit -m "feat(runner): Anthropic SDK model client and --brain opus wiring" -m "<trailers>"
```

---

### Task 6: Chạy Opus thật trên Heist, ghi kết quả, đồng bộ spec

**Cần người dùng trước khi chạy:** thông tin đăng nhập API (`ANTHROPIC_API_KEY` hoặc `ant auth login`) và đồng ý chi phí. Ước tính: mỗi lượt gọi khoảng 3–5 nghìn token vào (phần lớn được cache sau lượt đầu) và 2–6 nghìn token ra (kể cả thinking). Với giá Opus 5.5 ($4 vào, $20 ra, $0,20 đọc cache mỗi triệu token), một lượt khoảng $0,05–0,15; một ván 8–15 lượt khoảng $0,5–2; năm ván khoảng $3–10. Không chạy khi chưa có đồng ý.

**Files:**
- Create: `Tools/run-heist-opus.sh`
- Modify: `docs/superpowers/specs/2026-10-12-agentplay-m2-design.md`
- Create: `docs/notes/m2b-verification.md`

- [ ] **Step 1: Smoke test**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet build Runner -v q && dotnet Runner/bin/Debug/net9.0/AgentPlay.Runner.dll --smoke`
Expected: dòng `smoke test plan: {...}` có một bước `goto` tới `exit`, exit 0. Nếu lỗi xác thực: dừng và báo người dùng. Nếu API từ chối tham số (ví dụ fallback `"default"`), sửa `AnthropicModelClient` cho đúng tài liệu API và ghi ruling.

- [ ] **Step 2: Viết script chạy nhiều ván**

`Tools/run-heist-opus.sh`:

```bash
#!/usr/bin/env bash
# Plays the Heist demo N times with the Opus brain (real API calls, real cost) and tallies the results.
# Usage: bash Tools/run-heist-opus.sh [N]   (default 5)
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
PROJECT="$ROOT/Demos/Heist"
LOGS="$PROJECT/Logs"
RUNS="${1:-5}"
mkdir -p "$LOGS"
dotnet build "$ROOT/Runner/AgentPlay.Runner.csproj" -c Debug -v q || exit 1
SUMMARY="$LOGS/opus-runs.txt"
: > "$SUMMARY"
wins=0
for i in $(seq 1 "$RUNS"); do
  dotnet "$ROOT/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll" --brain opus --idle-exit 20 \
    > "$LOGS/opus-runner-$i.log" 2>&1 < /dev/null &
  RUNNER_PID=$!
  AGENTPLAY_AUTOPLAY_SECONDS=900 "$UNITY_EXE" -projectPath "$(cygpath -m "$PROJECT")" \
    -executeMethod HeistDemo.HeistAutoPlay.Run -logFile "$(cygpath -m "$LOGS/opus-autoplay-$i.log")"
  UNITY_CODE=$?
  wait "$RUNNER_PID"
  RUNNER_CODE=$?
  [ "$RUNNER_CODE" -eq 0 ] && wins=$((wins + 1))
  tokens=$(grep -o "model calls .*" "$LOGS/opus-runner-$i.log" | tail -1)
  echo "run $i: runner=$RUNNER_CODE unity=$UNITY_CODE $tokens" | tee -a "$SUMMARY"
done
echo "wins: $wins/$RUNS" | tee -a "$SUMMARY"
```

- [ ] **Step 3: Chạy năm ván**

Run (timeout 600000 ms mỗi lần gọi; chạy nền nếu cần): `cd /e/Unity-WorkSpace/AgentPlay && bash Tools/run-heist-opus.sh 5`
Expected: dòng cuối `wins: N/5` với N ≥ 3 (tiêu chí 2 của spec M2).

Nếu N < 3: đọc `opus-runner-*.log` và các RunLog để hiểu vì sao thua (Plan quá dài, không dùng luật, hiểu sai luật game). Được phép chỉnh `PromptBuilder.Instructions` (phần chung, không nhắc tên game) hoặc `Demos/Heist/AgentPlay/brief.md`, mỗi thay đổi một ruling, rồi chạy lại năm ván. Không đổi điều kiện thắng của game demo.

- [ ] **Step 4: Đồng bộ spec**

Trong `docs/superpowers/specs/2026-10-12-agentplay-m2-design.md`:
1. Mục 3, dòng `ModelClient`: "Gọi Opus qua SDK chính thức `Anthropic` trên NuGet (`client.Beta.Messages.Create`, structured outputs, cache phần system, `effort`, server-side fallback `"default"`); có bản giả cho test".
2. Mục 8, gạch đầu dòng **Đầu ra có cấu trúc**: thay bằng "Plan lấy qua structured outputs (`output_config.format`) với schema sinh từ `hello`: mỗi action là một biến thể `anyOf` có `args` đúng tham số; `until` là chuỗi, rỗng nghĩa là không có. Opus 5.5 không cho ép `tool_choice`, nên không dùng tool `submit_plan`."
3. Mục 8, **Prompt**: thêm "Mỗi lượt là một request độc lập (system cố định cộng một tin nhắn user); không giữ lịch sử hội thoại."
4. Mục 8, **Chế độ bộ não**: bỏ `--brain manual`; ghi "`--smoke` gọi model một lần với game giả để kiểm thông tin đăng nhập."
5. Mục 13, dòng thiếu `ANTHROPIC_API_KEY`: "Runner cảnh báo và để SDK thử các nguồn thông tin đăng nhập khác (profile của `ant auth login`); lượt gọi đầu thất bại vì xác thực thì dừng với thông báo rõ ràng."
6. Mục 16, rủi ro 1: thêm "Đã kiểm: SDK 12.55.0 hỗ trợ structured outputs, cache, effort và fallback."

- [ ] **Step 5: Ghi kết quả kiểm chứng**

Tạo `docs/notes/m2b-verification.md` từ số liệu thật:

```markdown
# M2b: kết quả kiểm chứng

Ngày chạy: <ngày>

## Test
- `dotnet test AgentPlay.sln`: <passed>/<tổng>, 0 failed
- `Tools/check-generic.sh`: OK
- Smoke test: <kết quả, token>

## Năm ván Heist với Opus 5.5 (effort high)
| Ván | Kết quả | Lượt gọi | Token vào (đọc cache) | Token ra |
| --- | --- | --- | --- | --- |
| 1 | ... | ... | ... | ... |

- Thắng: <N>/5 (yêu cầu ≥ 3)
- Tổng token và chi phí ước tính theo giá $4/$20/$0,20 mỗi triệu token: <...>
- Độ trễ trung vị mỗi lượt gọi: <... ms>
- Điều chỉnh prompt hoặc brief trong lúc chạy: <liệt kê, hoặc "không có">
```

- [ ] **Step 6: Commit**

```bash
git add Tools/run-heist-opus.sh docs Runner Demos/Heist/AgentPlay/brief.md
git commit -m "test: Opus plays the Heist demo; M2 spec synced with the M2b brain" -m "<trailers>"
```
