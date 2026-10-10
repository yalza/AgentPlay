# AgentPlay M1 (Nền) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Một game Unity gắn attribute AgentPlay gửi được `hello`, sự kiện và trạng thái qua TCP tới một runner .NET. Game ghi trace NDJSON, và chế độ theo lượt dừng hoặc chạy tiếp Editor theo lệnh của runner.

**Architecture:** Package UPM `com.agentplay.core` có ba assembly. `AgentPlay.Contract` là C# thuần, dùng chung với runner .NET. `AgentPlay.Runtime` chứa attribute, registry, sự kiện, trace và logic phiên. `AgentPlay.Editor` chứa TCP server, controller vòng đời Play mode và cửa sổ. Logic phiên (`SessionCore`) là lớp thuần nhận một `ISessionHost`, nên test được bằng EditMode mà không cần Play mode. Runner là console .NET 9 tham chiếu cùng mã nguồn Contract qua một `.csproj` liên kết file.

**Tech Stack:** Unity 6000.0.77f1 (C# 9), `com.unity.nuget.newtonsoft-json` 3.2.1, `com.unity.test-framework` 1.6.0 (NUnit), .NET SDK 9.0.315, xUnit (template `dotnet new xunit`), Newtonsoft.Json 13.0.3, Python 3.12 cho script kiểm tra E2E.

**Spec:** `docs/superpowers/specs/2026-10-11-agentplay-design.md` (mục 3, 4, 5, 8.1, 8.2, 9, 12 mốc M1, 13 rủi ro 1, 2, 3, 5)

## Global Constraints

- Code Unity phải biên dịch với C# 9: không dùng file-scoped namespace, `global using`, `record`, `required`.
- File trong `Packages/com.agentplay.core/Contract/` không được dùng `UnityEngine` (`noEngineReferences: true`) và phải build được bằng `dotnet build` với `netstandard2.1`, `LangVersion 9.0`.
- JSON chỉ dùng Newtonsoft.Json: Unity dùng package 3.2.1, .NET dùng NuGet 13.0.3.
- Bridge chỉ lắng nghe trên `127.0.0.1`, cổng 47800–47809 (lấy cổng trống đầu tiên). Mỗi message là một dòng JSON kết thúc bằng `\n`, có `type` (string) và `seq` (integer).
- Tên message: `hello`, `state`, `event`, `paused`, `error` (Unity gửi runner); `set_mode`, `resume`, `get_state` (runner gửi Unity). Mode: `turn_based`, `realtime`.
- Kiểu observe: `float`, `int`, `bool`, `string`, `enum`, `vector3`. Tham số action: `float`, `int`, `bool`, `string`.
- Trace nằm ở `<gốc project game>/AgentPlay/Traces/<yyyyMMdd-HHmmss>-<who>-<tag>.ndjson`, ngoài `Assets/`. M1 luôn ghi `who = human`. Nhãn phiên là `debug` (mặc định) hoặc `play`. Snapshot định kỳ mỗi 2 giây.
- Định dạng số hiển thị và log dùng `CultureInfo.InvariantCulture`, vì máy dev dùng locale vi-VN với dấu phẩy thập phân.
- Code, comment và tên biến bằng tiếng Anh.
- Mọi commit kết thúc bằng hai dòng:
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
  `Claude-Session: https://claude.ai/code/session_01XTqvn3xKuC12ugWwPzQueZ`
- Unity tạo `.meta` cho file mới trong package khi chạy `Tools/unity-tests.sh`. Phải commit `.meta` cùng file của nó, vì project dùng package qua git URL cần GUID cố định.
- `Testbed/` không được mở trong Editor khác khi chạy `Tools/unity-tests.sh` hoặc `Tools/e2e-m1.sh` (Unity khóa project).

## Review Focus

1. **Message hỏng hoặc lạ từ runner** (JSON sai, thiếu `type`/`seq`, sai kiểu trường, `type` không biết): Unity trả `error`, giữ kết nối, không ném exception ra Console. Test ở Task 1 và Task 8.
2. **Payload sự kiện không phải object thuần** (số, chuỗi, struct có `Vector3`, NaN): luôn ra JSON hợp lệ và không bao giờ ném exception vào code game. Test ở Task 5 và Task 6.
3. **Runner ngừng đọc** (treo, bị suspend trong debugger): main thread của Editor không bị chặn quá khoảng 2 giây, kết nối bị đóng. Test ở Task 9.
4. **Cổng 47800 đã bị chiếm** (một Editor khác đang chạy AgentPlay): server lấy cổng kế tiếp, runner quét cả dải cổng. Test ở Task 9 và Task 11.
5. **Pause trùng hoặc người dùng tự bấm bỏ pause trong chế độ theo lượt:** mỗi lần dừng chỉ gửi đúng một `paused`; sau khi người dùng tự bỏ pause, sự kiện quan trọng kế tiếp vẫn dừng được. Test ở Task 8.

---

## Cấu trúc file

```
AgentPlay/
  .gitignore
  AgentPlay.sln
  Contract/AgentPlay.Contract.csproj          liên kết *.cs từ package, netstandard2.1
  Packages/com.agentplay.core/
    package.json
    Contract/
      AgentPlay.Contract.asmdef
      Framing/LineFramer.cs                   tách byte stream thành dòng
      Protocol/Envelope.cs                    message đã parse
      Protocol/MessageCodec.cs                encode/decode một dòng
      Protocol/ProtocolException.cs
      Protocol/ContractJson.cs                JsonSerializer dùng chung
      Protocol/MessageTypes.cs                hằng số tên message, mode, kiểu giá trị
      Protocol/Messages.cs                    DTO cho từng message
      Protocol/SequenceCounter.cs
    Runtime/
      AgentPlay.Runtime.asmdef
      Attributes.cs                           AgentObserve, AgentAction, AgentGame, enum trạng thái
      Registry/AgentRegistry.cs               quét attribute, kiểm lỗi, tạo hello
      Registry/TypeScanner.cs                 tìm type có attribute trong AppDomain
      Registry/SnapshotBuilder.cs             đọc giá trị observe thành JObject phẳng
      Registry/UnityInstanceResolver.cs       tìm instance MonoBehaviour trong scene
      Json/UnityJson.cs                       serialize payload, converter Vector2/Vector3
      Events/AgentEvents.cs                   API Emit cho game
      Trace/TraceWriter.cs                    ghi NDJSON
      Trace/TracePaths.cs                     tên file trace
      Trace/IntervalTimer.cs
      Session/StepPolicy.cs                   logic theo lượt
      Session/SessionCore.cs                  xử lý message, sự kiện, pause
    Editor/
      AgentPlay.Editor.asmdef
      BridgeServer.cs                         TCP server
      AgentPlaySettings.cs                    EditorPrefs theo project
      AgentPlaySessionController.cs           vòng đời Play mode, nối mọi thứ
      AgentPlayWindow.cs                      Window → AgentPlay
    Tests/EditMode/
      AgentPlay.Tests.EditMode.asmdef
      ContractSmokeTests.cs, RegistryTests.cs, Fakes.cs, FakeObservedBehaviour.cs,
      SnapshotTests.cs, AgentEventsTests.cs, TraceTests.cs, SessionCoreTests.cs, BridgeServerTests.cs
  Runner/
    AgentPlay.Runner.csproj
    Program.cs, RunnerOptions.cs, RunnerSession.cs, BridgeClient.cs
  Runner.Tests/
    AgentPlay.Runner.Tests.csproj
    Contract/LineFramerTests.cs, Contract/MessageCodecTests.cs, Contract/MessagesTests.cs
    RunnerOptionsTests.cs, RunnerSessionTests.cs
  Testbed/                                     project Unity để chạy test package và E2E
    Packages/manifest.json
    Assets/Demo/DemoWalker.cs
    Assets/Editor/AutoPlay.cs
  Tools/
    unity-tests.sh                             chạy EditMode test của package trong Testbed
    e2e-m1.sh                                  chạy runner + Testbed, rồi kiểm tra
    verify_m1.py
  docs/notes/m1-verification.md
```

---

### Task 1: Khung repo, LineFramer và MessageCodec

**Files:**
- Create: `.gitignore`, `AgentPlay.sln`, `Contract/AgentPlay.Contract.csproj`
- Create: `Packages/com.agentplay.core/Contract/Framing/LineFramer.cs`
- Create: `Packages/com.agentplay.core/Contract/Protocol/Envelope.cs`, `MessageCodec.cs`, `ProtocolException.cs`, `ContractJson.cs`
- Test: `Runner.Tests/Contract/LineFramerTests.cs`, `Runner.Tests/Contract/MessageCodecTests.cs`

**Interfaces:**
- Produces:
  - `AgentPlay.Contract.LineFramer(int maxLineBytes = LineFramer.DefaultMaxLineBytes)`; `IReadOnlyList<string> Push(byte[] data, int offset, int count)`; ném `LineTooLongException` khi vượt giới hạn.
  - `AgentPlay.Contract.Envelope { string Type; long Seq; JObject Body; T As<T>(); }`
  - `AgentPlay.Contract.MessageCodec.Encode(string type, long seq, JObject body) : string`; `MessageCodec.Decode(string line) : Envelope`, ném `ProtocolException`.
  - `AgentPlay.Contract.ContractJson.Serializer : JsonSerializer`

- [ ] **Step 1: Tạo khung repo và project .NET**

`.gitignore`:

```gitignore
# .NET
bin/
obj/
*.user
# Unity testbed
Testbed/[Ll]ibrary/
Testbed/[Tt]emp/
Testbed/[Ll]ogs/
Testbed/[Oo]bj/
Testbed/[Uu]ser[Ss]ettings/
Testbed/AgentPlay/
Testbed/*.csproj
Testbed/*.sln
.vs/
.idea/
```

`Contract/AgentPlay.Contract.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <AssemblyName>AgentPlay.Contract</AssemblyName>
    <RootNamespace>AgentPlay.Contract</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <!-- Single source of truth: the Unity package compiles the same files. -->
    <Compile Include="../Packages/com.agentplay.core/Contract/**/*.cs" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
```

Run:

```bash
cd /e/Unity-WorkSpace/AgentPlay
mkdir -p Packages/com.agentplay.core/Contract/Framing Packages/com.agentplay.core/Contract/Protocol
dotnet new sln -n AgentPlay
dotnet new xunit -n AgentPlay.Runner.Tests -o Runner.Tests -f net9.0
rm Runner.Tests/UnitTest1.cs
mkdir -p Runner.Tests/Contract
dotnet sln AgentPlay.sln add Contract/AgentPlay.Contract.csproj Runner.Tests/AgentPlay.Runner.Tests.csproj
dotnet add Runner.Tests/AgentPlay.Runner.Tests.csproj reference Contract/AgentPlay.Contract.csproj
```

Expected: các lệnh kết thúc không lỗi; `AgentPlay.sln` có hai project.

- [ ] **Step 2: Viết test thất bại**

`Runner.Tests/Contract/LineFramerTests.cs`:

```csharp
using System.Text;
using AgentPlay.Contract;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class LineFramerTests
    {
        static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

        [Fact]
        public void SplitsCompleteLines()
        {
            var lines = new LineFramer().Push(B("a\nb\n"), 0, 4);
            Assert.Equal(new[] { "a", "b" }, lines);
        }

        [Fact]
        public void KeepsPartialLineUntilNewline()
        {
            var f = new LineFramer();
            Assert.Empty(f.Push(B("ab"), 0, 2));
            Assert.Equal(new[] { "abc" }, f.Push(B("c\n"), 0, 2));
        }

        [Fact]
        public void TrimsCarriageReturn()
        {
            Assert.Equal(new[] { "x" }, new LineFramer().Push(B("x\r\n"), 0, 3));
        }

        [Fact]
        public void ReassemblesMultiByteCharacterSplitAcrossChunks()
        {
            var bytes = B("đá\n");
            var f = new LineFramer();
            Assert.Empty(f.Push(bytes, 0, 1));
            Assert.Equal(new[] { "đá" }, f.Push(bytes, 1, bytes.Length - 1));
        }

        [Fact]
        public void RespectsOffsetAndCount()
        {
            Assert.Equal(new[] { "a" }, new LineFramer().Push(B("xxa\nyy"), 2, 2));
        }

        [Fact]
        public void ThrowsWhenLineExceedsLimit()
        {
            var f = new LineFramer(4);
            Assert.Throws<LineTooLongException>(() => f.Push(B("12345"), 0, 5));
        }
    }
}
```

`Runner.Tests/Contract/MessageCodecTests.cs`:

```csharp
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class MessageCodecTests
    {
        [Fact]
        public void EncodeProducesSingleLineWithTypeAndSeq()
        {
            var line = MessageCodec.Encode("hello", 7, new JObject { ["text"] = "a\nb" });
            Assert.DoesNotContain("\n", line);
            var o = JObject.Parse(line);
            Assert.Equal("hello", (string)o["type"]);
            Assert.Equal(7L, (long)o["seq"]);
            Assert.Equal("a\nb", (string)o["text"]);
        }

        [Fact]
        public void EncodeDoesNotMutateBody()
        {
            var body = new JObject { ["x"] = 1 };
            MessageCodec.Encode("t", 1, body);
            Assert.Null(body["type"]);
        }

        [Fact]
        public void EncodeAcceptsNullBody()
        {
            var env = MessageCodec.Decode(MessageCodec.Encode("get_state", 2, null));
            Assert.Equal("get_state", env.Type);
        }

        [Fact]
        public void DecodeKeepsIsoDatesAsStrings()
        {
            var env = MessageCodec.Decode("{\"type\":\"x\",\"seq\":1,\"at\":\"2026-10-11T01:02:03Z\"}");
            Assert.Equal(JTokenType.String, env.Body["at"].Type);
            Assert.Equal("2026-10-11T01:02:03Z", (string)env.Body["at"]);
        }

        [Fact]
        public void DecodeRoundTrips()
        {
            var env = MessageCodec.Decode(MessageCodec.Encode("event", 3, new JObject { ["name"] = "x" }));
            Assert.Equal("event", env.Type);
            Assert.Equal(3L, env.Seq);
            Assert.Equal("x", (string)env.Body["name"]);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[1,2]")]
        [InlineData("{\"seq\":1}")]
        [InlineData("{\"type\":5,\"seq\":1}")]
        [InlineData("{\"type\":\"x\"}")]
        [InlineData("{\"type\":\"x\",\"seq\":\"1\"}")]
        [InlineData("{\"type\":\"x\",\"seq\":1} {}")]
        public void DecodeRejectsMalformed(string line)
        {
            Assert.Throws<ProtocolException>(() => MessageCodec.Decode(line));
        }
    }
}
```

- [ ] **Step 3: Chạy test để thấy thất bại**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: build FAIL với lỗi `The type or namespace name 'LineFramer' could not be found`.

- [ ] **Step 4: Viết code**

`Packages/com.agentplay.core/Contract/Framing/LineFramer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text;

namespace AgentPlay.Contract
{
    /// <summary>Splits a UTF-8 byte stream into '\n'-terminated lines. Not thread-safe.</summary>
    public sealed class LineFramer
    {
        public const int DefaultMaxLineBytes = 1 << 20;

        readonly List<byte> _buffer = new List<byte>();
        readonly int _maxLineBytes;

        public LineFramer(int maxLineBytes = DefaultMaxLineBytes)
        {
            _maxLineBytes = maxLineBytes;
        }

        /// <summary>Appends bytes and returns every completed line, without the '\n' and a trailing '\r'.</summary>
        public IReadOnlyList<string> Push(byte[] data, int offset, int count)
        {
            var lines = new List<string>();
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                byte b = data[i];
                if (b == (byte)'\n')
                {
                    int len = _buffer.Count;
                    if (len > 0 && _buffer[len - 1] == (byte)'\r') len--;
                    lines.Add(Encoding.UTF8.GetString(_buffer.ToArray(), 0, len));
                    _buffer.Clear();
                    continue;
                }
                if (_buffer.Count >= _maxLineBytes) throw new LineTooLongException(_maxLineBytes);
                _buffer.Add(b);
            }
            return lines;
        }
    }

    public sealed class LineTooLongException : Exception
    {
        public LineTooLongException(int limit) : base("Line exceeds " + limit + " bytes") { }
    }
}
```

`Packages/com.agentplay.core/Contract/Protocol/ProtocolException.cs`:

```csharp
using System;

namespace AgentPlay.Contract
{
    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message) : base(message) { }
    }
}
```

`Packages/com.agentplay.core/Contract/Protocol/ContractJson.cs`:

```csharp
using Newtonsoft.Json;

namespace AgentPlay.Contract
{
    public static class ContractJson
    {
        public static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Include,
            FloatParseHandling = FloatParseHandling.Double,
        });
    }
}
```

`Packages/com.agentplay.core/Contract/Protocol/Envelope.cs`:

```csharp
using Newtonsoft.Json.Linq;

namespace AgentPlay.Contract
{
    /// <summary>A decoded message. Body is the whole JSON object, including "type" and "seq".</summary>
    public sealed class Envelope
    {
        public string Type { get; }
        public long Seq { get; }
        public JObject Body { get; }

        public Envelope(string type, long seq, JObject body)
        {
            Type = type;
            Seq = seq;
            Body = body;
        }

        public T As<T>() => Body.ToObject<T>(ContractJson.Serializer);
    }
}
```

`Packages/com.agentplay.core/Contract/Protocol/MessageCodec.cs`:

```csharp
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Contract
{
    public static class MessageCodec
    {
        /// <summary>Returns one line of JSON (no trailing newline). Does not modify <paramref name="body"/>.</summary>
        public static string Encode(string type, long seq, JObject body)
        {
            var o = body != null ? (JObject)body.DeepClone() : new JObject();
            o["type"] = type;
            o["seq"] = seq;
            return o.ToString(Formatting.None);
        }

        public static Envelope Decode(string line)
        {
            JObject o;
            try
            {
                // DateParseHandling.None keeps ISO strings as strings instead of turning them into DateTime.
                using (var reader = new JsonTextReader(new StringReader(line)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
                {
                    o = JObject.Load(reader);
                    if (reader.Read()) throw new ProtocolException("Unexpected content after the JSON object");
                }
            }
            catch (JsonException e)
            {
                throw new ProtocolException("Invalid JSON: " + e.Message);
            }
            var type = o["type"];
            if (type == null || type.Type != JTokenType.String)
                throw new ProtocolException("Missing string field 'type'");
            var seq = o["seq"];
            if (seq == null || seq.Type != JTokenType.Integer)
                throw new ProtocolException("Missing integer field 'seq'");
            return new Envelope((string)type, (long)seq, o);
        }
    }
}
```

- [ ] **Step 5: Chạy test để thấy pass**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: PASS, `Failed: 0`, `Passed: 18` (6 test LineFramer, 5 Fact và 7 case Theory của MessageCodec).

- [ ] **Step 6: Commit**

```bash
cd /e/Unity-WorkSpace/AgentPlay
git add .gitignore AgentPlay.sln Contract Packages Runner.Tests
git commit -m "feat(contract): line framing and message codec shared with the runner"
```

---

### Task 2: DTO của giao thức và SequenceCounter

**Files:**
- Create: `Packages/com.agentplay.core/Contract/Protocol/MessageTypes.cs`, `Messages.cs`, `SequenceCounter.cs`
- Modify: `Packages/com.agentplay.core/Contract/Protocol/MessageCodec.cs` (thêm overload generic)
- Test: `Runner.Tests/Contract/MessagesTests.cs`

**Interfaces:**
- Consumes: `MessageCodec.Encode(string, long, JObject)`, `Envelope.As<T>()`, `ContractJson.Serializer` (Task 1).
- Produces:
  - `MessageTypes.Hello/State/Event/Paused/Error/SetMode/Resume/GetState` (const string).
  - `Modes.TurnBased = "turn_based"`, `Modes.Realtime = "realtime"`, `Modes.IsKnown(string)`.
  - `ValueKinds.Float/Int/Bool/String/Enum/Vector3`.
  - `ProtocolVersion.Current = 1`.
  - DTO (field public): `HelloMessage { int Protocol; string Game; string Version; List<ObserveInfo> Observes; List<ActionInfo> Actions; List<string> Modes; }`, `ObserveInfo { Path; ValueType; Description; }`, `ActionInfo { Name; Description; bool Safe; List<ActionParamInfo> Params; }`, `ActionParamInfo { Name; ValueType; }`, `EventMessage { string Name; double T; long Frame; bool Important; string Outcome; JObject Payload; JObject State; }`, `StateMessage { double T; long Frame; JObject State; }`, `PausedMessage { string Reason; double T; }`, `SetModeMessage { string Mode; }`, `ResumeMessage { double MaxGameSeconds; }`, `ErrorMessage { string Message; }`.
  - `MessageCodec.Encode<T>(string type, long seq, T dto) : string`.
  - `SequenceCounter.Next() : long` (bắt đầu từ 1, thread-safe).

- [ ] **Step 1: Viết test thất bại**

`Runner.Tests/Contract/MessagesTests.cs`:

```csharp
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests.Contract
{
    public class MessagesTests
    {
        [Fact]
        public void HelloRoundTrips()
        {
            var hello = new HelloMessage
            {
                Protocol = ProtocolVersion.Current,
                Game = "G",
                Version = "v1",
            };
            hello.Observes.Add(new ObserveInfo { Path = "noise.value", ValueType = ValueKinds.Float, Description = "d" });
            hello.Actions.Add(new ActionInfo
            {
                Name = "goto",
                Safe = false,
                Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } },
            });
            hello.Modes.Add(Modes.TurnBased);

            var back = MessageCodec.Decode(MessageCodec.Encode(MessageTypes.Hello, 1, hello)).As<HelloMessage>();

            Assert.Equal(1, back.Protocol);
            Assert.Equal("G", back.Game);
            Assert.Equal("noise.value", back.Observes[0].Path);
            Assert.Equal("float", back.Observes[0].ValueType);
            Assert.Equal("zone", back.Actions[0].Params[0].Name);
            Assert.Equal(new[] { "turn_based" }, back.Modes);
        }

        [Fact]
        public void ResumeUsesSnakeCaseOnTheWire()
        {
            var line = MessageCodec.Encode(MessageTypes.Resume, 4, new ResumeMessage { MaxGameSeconds = 2.5 });
            Assert.Equal(2.5, (double)JObject.Parse(line)["max_game_seconds"]);
        }

        [Fact]
        public void EventWithNullPayloadRoundTrips()
        {
            var ev = new EventMessage { Name = "x", T = 1.5, Frame = 9, Important = true, Outcome = "bad", Payload = null, State = new JObject { ["a"] = 1 } };
            var back = MessageCodec.Decode(MessageCodec.Encode(MessageTypes.Event, 2, ev)).As<EventMessage>();
            Assert.Null(back.Payload);
            Assert.Equal(1, (int)back.State["a"]);
            Assert.True(back.Important);
            Assert.Equal(9L, back.Frame);
        }

        [Fact]
        public void ModesKnowsOnlyTwoModes()
        {
            Assert.True(Modes.IsKnown("turn_based"));
            Assert.True(Modes.IsKnown("realtime"));
            Assert.False(Modes.IsKnown("fast"));
            Assert.False(Modes.IsKnown(null));
        }

        [Fact]
        public void SequenceCounterStartsAtOneAndIsUniqueAcrossThreads()
        {
            var c = new SequenceCounter();
            Assert.Equal(1L, c.Next());
            var seen = new ConcurrentDictionary<long, bool>();
            Parallel.For(0, 1000, _ => Assert.True(seen.TryAdd(c.Next(), true)));
            Assert.Equal(1000, seen.Count);
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: build FAIL với `The type or namespace name 'HelloMessage' could not be found`.

- [ ] **Step 3: Viết code**

`Packages/com.agentplay.core/Contract/Protocol/MessageTypes.cs`:

```csharp
namespace AgentPlay.Contract
{
    public static class ProtocolVersion
    {
        public const int Current = 1;
    }

    public static class MessageTypes
    {
        // Unity -> runner
        public const string Hello = "hello";
        public const string State = "state";
        public const string Event = "event";
        public const string Paused = "paused";
        public const string Error = "error";
        // Runner -> Unity
        public const string SetMode = "set_mode";
        public const string Resume = "resume";
        public const string GetState = "get_state";
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
    }
}
```

`Packages/com.agentplay.core/Contract/Protocol/Messages.cs`:

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

    public sealed class HelloMessage
    {
        [JsonProperty("protocol")] public int Protocol;
        [JsonProperty("game")] public string Game;
        [JsonProperty("version")] public string Version;
        [JsonProperty("observes")] public List<ObserveInfo> Observes = new List<ObserveInfo>();
        [JsonProperty("actions")] public List<ActionInfo> Actions = new List<ActionInfo>();
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
        /// <summary>"mode", "event" or "budget".</summary>
        [JsonProperty("reason")] public string Reason;
        [JsonProperty("t")] public double T;
    }

    public sealed class SetModeMessage
    {
        [JsonProperty("mode")] public string Mode;
    }

    public sealed class ResumeMessage
    {
        /// <summary>Game seconds to run before pausing again; 0 or less means until the next important event.</summary>
        [JsonProperty("max_game_seconds")] public double MaxGameSeconds;
    }

    public sealed class ErrorMessage
    {
        [JsonProperty("message")] public string Message;
    }
}
```

`Packages/com.agentplay.core/Contract/Protocol/SequenceCounter.cs`:

```csharp
using System.Threading;

namespace AgentPlay.Contract
{
    public sealed class SequenceCounter
    {
        long _last;

        public long Next() => Interlocked.Increment(ref _last);
    }
}
```

Thêm vào `MessageCodec` (ngay dưới `Encode(string, long, JObject)`):

```csharp
        public static string Encode<T>(string type, long seq, T dto)
        {
            var body = dto == null ? null : JObject.FromObject(dto, ContractJson.Serializer);
            return Encode(type, seq, body);
        }
```

Lưu ý: `Encode(type, seq, (JObject)x)` vẫn chọn overload `JObject` vì nó cụ thể hơn. Test `EncodeAcceptsNullBody` của Task 1 truyền literal `null`, và C# chọn overload không generic. Không đổi test đó.

- [ ] **Step 4: Chạy test để thấy pass**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: PASS, `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add Packages Runner.Tests
git commit -m "feat(contract): protocol DTOs, mode and value-kind constants"
```

---

### Task 3: Khung package Unity, Testbed và script chạy test Unity

Kiểm chứng rủi ro 3 của spec: cùng mã nguồn Contract biên dịch được trong Unity.

**Files:**
- Create: `Packages/com.agentplay.core/package.json`
- Create: `Packages/com.agentplay.core/Contract/AgentPlay.Contract.asmdef`
- Create: `Packages/com.agentplay.core/Runtime/AgentPlay.Runtime.asmdef`
- Create: `Packages/com.agentplay.core/Editor/AgentPlay.Editor.asmdef`
- Create: `Packages/com.agentplay.core/Tests/EditMode/AgentPlay.Tests.EditMode.asmdef`
- Create: `Packages/com.agentplay.core/Tests/EditMode/ContractSmokeTests.cs`
- Create: `Testbed/` (project Unity), `Testbed/Packages/manifest.json`
- Create: `Tools/unity-tests.sh`

**Interfaces:**
- Consumes: Contract (Task 1–2).
- Produces: lệnh `bash Tools/unity-tests.sh` (exit 0 khi mọi EditMode test của package pass), các assembly `AgentPlay.Contract`, `AgentPlay.Runtime`, `AgentPlay.Editor`, `AgentPlay.Tests.EditMode`.

- [ ] **Step 1: Tạo package.json và các asmdef**

`Packages/com.agentplay.core/package.json`:

```json
{
  "name": "com.agentplay.core",
  "version": "0.1.0",
  "displayName": "AgentPlay",
  "description": "Lets an LLM agent observe and play a Unity game from the Editor.",
  "unity": "6000.0",
  "dependencies": {
    "com.unity.nuget.newtonsoft-json": "3.2.1"
  }
}
```

`Packages/com.agentplay.core/Contract/AgentPlay.Contract.asmdef`:

```json
{
  "name": "AgentPlay.Contract",
  "rootNamespace": "AgentPlay.Contract",
  "references": [],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["Newtonsoft.Json.dll"],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": true
}
```

`Packages/com.agentplay.core/Runtime/AgentPlay.Runtime.asmdef`:

```json
{
  "name": "AgentPlay.Runtime",
  "rootNamespace": "AgentPlay",
  "references": ["AgentPlay.Contract"],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["Newtonsoft.Json.dll"],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

`Packages/com.agentplay.core/Editor/AgentPlay.Editor.asmdef`:

```json
{
  "name": "AgentPlay.Editor",
  "rootNamespace": "AgentPlay.Editor",
  "references": ["AgentPlay.Contract", "AgentPlay.Runtime"],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["Newtonsoft.Json.dll"],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

`Packages/com.agentplay.core/Tests/EditMode/AgentPlay.Tests.EditMode.asmdef`:

```json
{
  "name": "AgentPlay.Tests.EditMode",
  "rootNamespace": "AgentPlay.Tests",
  "references": [
    "AgentPlay.Contract",
    "AgentPlay.Runtime",
    "AgentPlay.Editor",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner"
  ],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll", "Newtonsoft.Json.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

Runtime và Editor chưa có file `.cs`. Unity bỏ qua asmdef rỗng mà không báo lỗi. Các task sau sẽ thêm file.

- [ ] **Step 2: Viết test smoke**

`Packages/com.agentplay.core/Tests/EditMode/ContractSmokeTests.cs`:

```csharp
using AgentPlay.Contract;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class ContractSmokeTests
    {
        [Test]
        public void ContractCompilesAndRoundTripsInUnity()
        {
            var line = MessageCodec.Encode(MessageTypes.Resume, 5, new ResumeMessage { MaxGameSeconds = 1.25 });
            var env = MessageCodec.Decode(line);
            Assert.AreEqual(MessageTypes.Resume, env.Type);
            Assert.AreEqual(1.25, env.As<ResumeMessage>().MaxGameSeconds, 1e-9);
        }
    }
}
```

- [ ] **Step 3: Tạo Testbed**

Run (timeout 600000 ms, lần đầu Unity có thể mất vài phút):

```bash
cd /e/Unity-WorkSpace/AgentPlay
"/e/6000.0.77f1/Editor/Unity.exe" -batchmode -nographics -quit -createProject "$(cygpath -m Testbed)" -logFile -  | tail -5
ls Testbed
```

Expected: `Testbed` có `Assets`, `Packages`, `ProjectSettings`.

Ghi đè `Testbed/Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.agentplay.core": "file:../../Packages/com.agentplay.core",
    "com.unity.test-framework": "1.6.0",
    "com.unity.ugui": "2.0.0",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.screencapture": "1.0.0",
    "com.unity.modules.ui": "1.0.0"
  },
  "testables": ["com.agentplay.core"]
}
```

Đường dẫn `file:` tính từ thư mục `Testbed/Packages`, nên `../../Packages/com.agentplay.core` trỏ đúng về package ở gốc repo.

- [ ] **Step 4: Viết script chạy test Unity**

`Tools/unity-tests.sh`:

```bash
#!/usr/bin/env bash
# Runs the package EditMode tests inside Testbed. Testbed must not be open in another Editor.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
OUT="$ROOT/Testbed/Logs"
mkdir -p "$OUT"
rm -f "$OUT/editmode-results.xml"
"$UNITY_EXE" -batchmode -nographics -projectPath "$(cygpath -m "$ROOT/Testbed")" \
  -runTests -testPlatform EditMode \
  -testResults "$(cygpath -m "$OUT/editmode-results.xml")" \
  -logFile "$(cygpath -m "$OUT/editmode.log")"
code=$?
if [ -f "$OUT/editmode-results.xml" ]; then
  grep -o '<test-run [^>]*>' "$OUT/editmode-results.xml" | grep -oE '(result|total|passed|failed)="[^"]*"' | tr '\n' ' '
  echo
  grep -B2 -A6 '<failure>' "$OUT/editmode-results.xml" | head -80
else
  echo "No results file. Compile errors:"
  grep -E 'error CS[0-9]+' "$OUT/editmode.log" | sort -u | head -40
fi
echo "Unity exit code: $code"
exit $code
```

- [ ] **Step 5: Chạy test Unity**

Run (timeout 600000 ms): `cd /e/Unity-WorkSpace/AgentPlay && bash Tools/unity-tests.sh`
Expected: `result="Passed" total="1" passed="1" failed="0"`, `Unity exit code: 0`.

Nếu có `error CS` trong Contract (ví dụ cú pháp mới hơn C# 9), sửa file Contract rồi chạy lại cả `dotnet test AgentPlay.sln` và script này.

- [ ] **Step 6: Commit (gồm `.meta` Unity vừa sinh trong package)**

```bash
cd /e/Unity-WorkSpace/AgentPlay
git status --short
git add Packages Testbed/Assets Testbed/Packages Testbed/ProjectSettings Tools/unity-tests.sh
git commit -m "chore: Unity package skeleton, Testbed project and EditMode test script"
```

Expected trong `git status`: có `.meta` cho từng thư mục và file trong `Packages/com.agentplay.core`, không có gì trong `Testbed/Library`.

---

### Task 4: Attribute, AgentRegistry và TypeScanner

**Files:**
- Create: `Packages/com.agentplay.core/Runtime/Attributes.cs`
- Create: `Packages/com.agentplay.core/Runtime/Registry/AgentRegistry.cs`
- Create: `Packages/com.agentplay.core/Runtime/Registry/TypeScanner.cs`
- Test: `Packages/com.agentplay.core/Tests/EditMode/Fakes.cs`, `RegistryTests.cs`

**Interfaces:**
- Consumes: `ValueKinds`, `Modes`, `ProtocolVersion`, `HelloMessage`, `ObserveInfo`, `ActionInfo`, `ActionParamInfo` (Task 2).
- Produces (namespace `AgentPlay`):
  - `AgentObserveAttribute(string path, string description = "")` với `Path`, `Description`.
  - `AgentActionAttribute(string name, string description = "")` với `Name`, `Description`, `bool Safe { get; set; }`.
  - `AgentGameAttribute(string name)` với `Name`, `string Version { get; set; }`, `string VersionMember { get; set; }`.
  - `enum AgentActionStatus { Running, Done, Failed }`, `enum AgentOutcome { Neutral, Good, Bad }`.
  - `ObserveEntry { string Path; string Description; Type DeclaringType; MemberInfo Member; string ValueKind; bool IsStatic; }`
  - `ActionEntry { string Name; string Description; bool Safe; Type DeclaringType; MethodInfo Method; List<ActionParamInfo> Params; }`
  - `RegistryModel { List<ObserveEntry> Observes; List<ActionEntry> Actions; List<string> Errors; string GameName; string GameVersion; }`
  - `AgentRegistry.Build(IEnumerable<Type> types) : RegistryModel`; `AgentRegistry.ToHello(RegistryModel model, string fallbackGameName) : HelloMessage`; `AgentRegistry.KindOf(Type t) : string` (null nếu không hỗ trợ).
  - `TypeScanner.FindAnnotatedTypes() : List<Type>`; `TypeScanner.ShouldScan(Assembly asm) : bool`; `TypeScanner.IsAnnotated(Type t) : bool`.

Ghi chú so với spec:
- Tên property là `Safe` (PascalCase theo quy ước C#). Ví dụ `safe = true` trong spec mục 4 sẽ được sửa ở Task 12.
- `[AgentObserveList]` (spec mục 4) để sang M2, khi adapter TileThieves cần danh sách bảo vệ. M1 chỉ hỗ trợ observe đơn, mỗi type một instance.

- [ ] **Step 1: Viết test thất bại**

`Packages/com.agentplay.core/Tests/EditMode/Fakes.cs`:

```csharp
using System;
using UnityEngine;

namespace AgentPlay.Tests
{
    public enum FakeLevel { Calm, Alert }

    [AgentGame("FakeGame", Version = "7")]
    public class FakeGame
    {
        [AgentObserve("noise.value", "Noise 0-100")] public float Noise = 12.3456f;
        [AgentObserve("sack.tiles")] public int Tiles { get; set; } = 4;
        [AgentObserve("sack.full")] public bool Full => false;
        [AgentObserve("player.name")] public string Name = "thief";
        [AgentObserve("noise.level")] public FakeLevel Level = FakeLevel.Alert;
        [AgentObserve("player.pos")] public Vector3 Pos = new Vector3(1f, 2f, 3.00049f);
        [AgentObserve("game.round")] public static int Round = 2;

        [AgentAction("goto", "Walk to a zone")] public AgentActionStatus GoTo(string zone, float speed) => AgentActionStatus.Done;
        [AgentAction("hide", Safe = true)] public AgentActionStatus Hide() => AgentActionStatus.Done;
    }

    public class FakeBroken
    {
        [AgentObserve("noise.value")] public float Duplicate;                 // duplicate path
        [AgentObserve("")] public float Empty;                                 // empty path
        [AgentObserve("bad.type")] public object Unsupported;                  // unsupported type
        [AgentObserve("write.only")] public int WriteOnly { set { } }          // no getter
        [AgentAction("wrong.return")] public void WrongReturn() { }             // must return AgentActionStatus
        [AgentAction("bad.param")] public AgentActionStatus BadParam(Vector3 v) => AgentActionStatus.Done;
        [AgentAction("goto")] public AgentActionStatus DuplicateAction() => AgentActionStatus.Done;
        [AgentAction("rest", Safe = true)] public AgentActionStatus SecondSafe() => AgentActionStatus.Done;
    }

    [AgentGame("Dynamic", VersionMember = nameof(ComputeVersion))]
    public class FakeDynamicVersion
    {
        public static string ComputeVersion() => "hash-abc";
    }

    [AgentGame("Throwing", VersionMember = nameof(Boom))]
    public class FakeThrowingVersion
    {
        public static string Boom => throw new InvalidOperationException("no config");
    }

    [AgentGame("Second")]
    public class FakeSecondGame { }

    public class FakeThrowingGetter
    {
        [AgentObserve("explodes")] public float Explodes => throw new InvalidOperationException("kaboom");
        [AgentObserve("not.a.number")] public float NaN = float.NaN;
    }
}
```

`Packages/com.agentplay.core/Tests/EditMode/RegistryTests.cs`:

```csharp
using System.Linq;
using AgentPlay.Contract;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class RegistryTests
    {
        [Test]
        public void CollectsObservesSortedByPathWithKinds()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeGame) });
            CollectionAssert.IsEmpty(m.Errors);
            CollectionAssert.AreEqual(
                new[] { "game.round", "noise.level", "noise.value", "player.name", "player.pos", "sack.full", "sack.tiles" },
                m.Observes.Select(o => o.Path).ToArray());
            var kinds = m.Observes.ToDictionary(o => o.Path, o => o.ValueKind);
            Assert.AreEqual(ValueKinds.Float, kinds["noise.value"]);
            Assert.AreEqual(ValueKinds.Int, kinds["sack.tiles"]);
            Assert.AreEqual(ValueKinds.Bool, kinds["sack.full"]);
            Assert.AreEqual(ValueKinds.String, kinds["player.name"]);
            Assert.AreEqual(ValueKinds.Enum, kinds["noise.level"]);
            Assert.AreEqual(ValueKinds.Vector3, kinds["player.pos"]);
            Assert.IsTrue(m.Observes.Single(o => o.Path == "game.round").IsStatic);
        }

        [Test]
        public void CollectsActionsWithParamsAndSafeFlag()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeGame) });
            CollectionAssert.AreEqual(new[] { "goto", "hide" }, m.Actions.Select(a => a.Name).ToArray());
            var go = m.Actions[0];
            CollectionAssert.AreEqual(new[] { "zone:string", "speed:float" }, go.Params.Select(p => p.Name + ":" + p.ValueType).ToArray());
            Assert.IsFalse(go.Safe);
            Assert.IsTrue(m.Actions[1].Safe);
        }

        [Test]
        public void ReadsGameNameAndStaticVersion()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeGame) });
            Assert.AreEqual("FakeGame", m.GameName);
            Assert.AreEqual("7", m.GameVersion);
        }

        [Test]
        public void ResolvesVersionFromStaticMethod()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeDynamicVersion) });
            Assert.AreEqual("hash-abc", m.GameVersion);
            CollectionAssert.IsEmpty(m.Errors);
        }

        [Test]
        public void ReportsThrowingVersionMemberWithoutThrowing()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeThrowingVersion) });
            Assert.AreEqual("", m.GameVersion);
            Assert.IsTrue(m.Errors.Any(e => e.Contains("no config")), string.Join("\n", m.Errors));
        }

        [Test]
        public void ReportsEveryInvalidDeclarationAndSkipsIt()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeGame), typeof(FakeBroken) });
            string all = string.Join("\n", m.Errors);
            StringAssert.Contains("Duplicate observe path 'noise.value'", all);
            StringAssert.Contains("empty path", all);
            StringAssert.Contains("unsupported type Object", all);
            StringAssert.Contains("'write.only'", all);
            StringAssert.Contains("'wrong.return'", all);
            StringAssert.Contains("parameter 'v'", all);
            StringAssert.Contains("Duplicate action 'goto'", all);
            StringAssert.Contains("Only one safe action", all);
            Assert.AreEqual(7, m.Observes.Count, "broken observes must be skipped");
            Assert.AreEqual(2, m.Actions.Count, "broken actions must be skipped");
        }

        [Test]
        public void ReportsSecondAgentGame()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeGame), typeof(FakeSecondGame) });
            Assert.AreEqual("FakeGame", m.GameName);
            Assert.IsTrue(m.Errors.Any(e => e.Contains("More than one [AgentGame]")));
        }

        [Test]
        public void ToHelloUsesFallbackNameWhenNoAgentGame()
        {
            var m = AgentRegistry.Build(new[] { typeof(FakeThrowingGetter) });
            var hello = AgentRegistry.ToHello(m, "ProductName");
            Assert.AreEqual(ProtocolVersion.Current, hello.Protocol);
            Assert.AreEqual("ProductName", hello.Game);
            Assert.AreEqual("", hello.Version);
            CollectionAssert.AreEqual(new[] { Modes.TurnBased, Modes.Realtime }, hello.Modes);
            Assert.AreEqual(2, hello.Observes.Count);
        }

        [Test]
        public void ScannerSkipsTestAssembliesAndRuntimeItself()
        {
            Assert.IsFalse(TypeScanner.ShouldScan(typeof(FakeGame).Assembly), "test assembly references nunit");
            Assert.IsFalse(TypeScanner.ShouldScan(typeof(AgentObserveAttribute).Assembly), "runtime assembly");
            CollectionAssert.DoesNotContain(TypeScanner.FindAnnotatedTypes(), typeof(FakeGame));
        }

        [Test]
        public void IsAnnotatedDetectsAnyAgentAttribute()
        {
            Assert.IsTrue(TypeScanner.IsAnnotated(typeof(FakeGame)));
            Assert.IsTrue(TypeScanner.IsAnnotated(typeof(FakeSecondGame)));
            Assert.IsTrue(TypeScanner.IsAnnotated(typeof(FakeThrowingGetter)));
            Assert.IsFalse(TypeScanner.IsAnnotated(typeof(string)));
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `bash Tools/unity-tests.sh`
Expected: không có file kết quả, có `error CS0246: The type or namespace name 'AgentGameAttribute' could not be found`.

- [ ] **Step 3: Viết code**

`Packages/com.agentplay.core/Runtime/Attributes.cs`:

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

    /// <summary>Marks a method the agent may call. It must return AgentActionStatus and take only float/int/bool/string.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class AgentActionAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }
        /// <summary>The single default action run while no plan is available.</summary>
        public bool Safe { get; set; }

        public AgentActionAttribute(string name, string description = "")
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

    public enum AgentActionStatus { Running, Done, Failed }

    public enum AgentOutcome { Neutral, Good, Bad }
}
```

`Packages/com.agentplay.core/Runtime/Registry/AgentRegistry.cs`:

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
    }

    public sealed class ActionEntry
    {
        public string Name;
        public string Description;
        public bool Safe;
        public Type DeclaringType;
        public MethodInfo Method;
        public List<ActionParamInfo> Params = new List<ActionParamInfo>();
    }

    public sealed class RegistryModel
    {
        public readonly List<ObserveEntry> Observes = new List<ObserveEntry>();
        public readonly List<ActionEntry> Actions = new List<ActionEntry>();
        public readonly List<string> Errors = new List<string>();
        /// <summary>Null when no type carries [AgentGame].</summary>
        public string GameName;
        public string GameVersion = "";
    }

    public static class AgentRegistry
    {
        const BindingFlags MemberFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        public static RegistryModel Build(IEnumerable<Type> types)
        {
            var model = new RegistryModel();
            var observeOwners = new Dictionary<string, string>();
            var actionOwners = new Dictionary<string, string>();
            string safeAction = null;

            foreach (var type in types.Distinct().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                ReadGame(type, model);
                foreach (var member in type.GetMembers(MemberFlags).OrderBy(m => m.Name, StringComparer.Ordinal))
                {
                    var observe = member.GetCustomAttribute<AgentObserveAttribute>(false);
                    if (observe != null && (member is FieldInfo || member is PropertyInfo))
                        AddObserve(type, member, observe, model, observeOwners);

                    var action = member.GetCustomAttribute<AgentActionAttribute>(false);
                    if (action != null && member is MethodInfo method)
                        AddAction(type, method, action, model, actionOwners, ref safeAction);
                }
            }

            model.Observes.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            model.Actions.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
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
            foreach (var o in model.Observes)
                hello.Observes.Add(new ObserveInfo { Path = o.Path, ValueType = o.ValueKind, Description = o.Description });
            foreach (var a in model.Actions)
                hello.Actions.Add(new ActionInfo { Name = a.Name, Description = a.Description, Safe = a.Safe, Params = new List<ActionParamInfo>(a.Params) });
            hello.Modes.Add(Modes.TurnBased);
            hello.Modes.Add(Modes.Realtime);
            return hello;
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

        static void AddObserve(Type type, MemberInfo member, AgentObserveAttribute attr, RegistryModel model, Dictionary<string, string> owners)
        {
            string where = type.Name + "." + member.Name;
            if (string.IsNullOrWhiteSpace(attr.Path))
            {
                model.Errors.Add("Observe on " + where + " has an empty path");
                return;
            }

            Type valueType;
            bool isStatic;
            if (member is FieldInfo field)
            {
                valueType = field.FieldType;
                isStatic = field.IsStatic;
            }
            else
            {
                var prop = (PropertyInfo)member;
                var getter = prop.GetGetMethod(true);
                if (getter == null || prop.GetIndexParameters().Length > 0)
                {
                    model.Errors.Add("Observe '" + attr.Path + "' on " + where + " needs a readable, non-indexed property");
                    return;
                }
                valueType = prop.PropertyType;
                isStatic = getter.IsStatic;
            }

            string kind = KindOf(valueType);
            if (kind == null)
            {
                model.Errors.Add("Observe '" + attr.Path + "' on " + where + " has unsupported type " + valueType.Name);
                return;
            }
            if (owners.TryGetValue(attr.Path, out var first))
            {
                model.Errors.Add("Duplicate observe path '" + attr.Path + "' on " + first + " and " + where);
                return;
            }
            owners[attr.Path] = where;
            model.Observes.Add(new ObserveEntry
            {
                Path = attr.Path,
                Description = attr.Description,
                DeclaringType = type,
                Member = member,
                ValueKind = kind,
                IsStatic = isStatic,
            });
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

            var ps = new List<ActionParamInfo>();
            foreach (var p in method.GetParameters())
            {
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
                Params = ps,
            });
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

`Packages/com.agentplay.core/Runtime/Registry/TypeScanner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AgentPlay
{
    public static class TypeScanner
    {
        const BindingFlags MemberFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        static readonly Assembly RuntimeAssembly = typeof(AgentObserveAttribute).Assembly;

        /// <summary>Types carrying AgentPlay attributes in game assemblies. Test assemblies are skipped.</summary>
        public static List<Type> FindAnnotatedTypes()
        {
            var result = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!ShouldScan(asm)) continue;
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }
                foreach (var t in types)
                    if (IsAnnotated(t)) result.Add(t);
            }
            return result;
        }

        public static bool ShouldScan(Assembly asm)
        {
            if (asm == RuntimeAssembly || asm.IsDynamic) return false;
            string runtimeName = RuntimeAssembly.GetName().Name;
            bool referencesRuntime = false;
            foreach (var r in asm.GetReferencedAssemblies())
            {
                if (r.Name == "nunit.framework") return false;
                if (r.Name == runtimeName) referencesRuntime = true;
            }
            return referencesRuntime;
        }

        public static bool IsAnnotated(Type t)
        {
            if (t.IsDefined(typeof(AgentGameAttribute), false)) return true;
            foreach (var m in t.GetMembers(MemberFlags))
                if (m.IsDefined(typeof(AgentObserveAttribute), false) || m.IsDefined(typeof(AgentActionAttribute), false))
                    return true;
            return false;
        }
    }
}
```

- [ ] **Step 4: Chạy test để thấy pass**

Run: `bash Tools/unity-tests.sh`
Expected: `result="Passed"`, `failed="0"`, tổng 11 test (1 smoke + 10 registry).

- [ ] **Step 5: Commit**

```bash
git add Packages
git commit -m "feat(runtime): agent attributes, registry validation and type scanner"
```

---

### Task 5: UnityJson, SnapshotBuilder và UnityInstanceResolver

**Files:**
- Create: `Packages/com.agentplay.core/Runtime/Json/UnityJson.cs`
- Create: `Packages/com.agentplay.core/Runtime/Registry/SnapshotBuilder.cs`
- Create: `Packages/com.agentplay.core/Runtime/Registry/UnityInstanceResolver.cs`
- Test: `Packages/com.agentplay.core/Tests/EditMode/FakeObservedBehaviour.cs`, `SnapshotTests.cs`

**Interfaces:**
- Consumes: `RegistryModel`, `ObserveEntry`, `AgentRegistry.Build` (Task 4); `ValueKinds` (Task 2).
- Produces:
  - `UnityJson.Serializer : JsonSerializer` (có converter `Vector3`, `Vector2`, enum thành chuỗi, bỏ qua vòng tham chiếu).
  - `UnityJson.ToPayload(object payload) : JObject` (null vào thì null ra; không bao giờ ném exception).
  - `UnityJson.Clean(double d) : double?` (làm tròn 3 chữ số; NaN hoặc vô cực thành null).
  - `SnapshotBuilder.Build(RegistryModel model, Func<Type, object> resolveInstance, Action<string> warn = null) : JObject`.
  - `UnityInstanceResolver.Resolve(Type type) : object`.

- [ ] **Step 1: Viết test thất bại**

`Packages/com.agentplay.core/Tests/EditMode/FakeObservedBehaviour.cs`:

```csharp
using UnityEngine;

namespace AgentPlay.Tests
{
    public class FakeObservedBehaviour : MonoBehaviour
    {
        [AgentObserve("fake.hp")] public int Hp = 10;
    }
}
```

`Packages/com.agentplay.core/Tests/EditMode/SnapshotTests.cs`:

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AgentPlay.Tests
{
    public class SnapshotTests
    {
        [Test]
        public void BuildsFlatSnapshotWithRoundedValues()
        {
            var model = AgentRegistry.Build(new[] { typeof(FakeGame) });
            var game = new FakeGame();
            var s = SnapshotBuilder.Build(model, t => t == typeof(FakeGame) ? game : null);

            Assert.AreEqual(12.346, (double)s["noise.value"], 1e-9);
            Assert.AreEqual(4, (int)s["sack.tiles"]);
            Assert.AreEqual(false, (bool)s["sack.full"]);
            Assert.AreEqual("thief", (string)s["player.name"]);
            Assert.AreEqual("Alert", (string)s["noise.level"]);
            Assert.AreEqual("[1.0,2.0,3.0]", s["player.pos"].ToString(Formatting.None));
            Assert.AreEqual(2, (int)s["game.round"]);
        }

        [Test]
        public void MissingInstanceGivesNullButStaticStillReads()
        {
            var model = AgentRegistry.Build(new[] { typeof(FakeGame) });
            var s = SnapshotBuilder.Build(model, t => null);
            Assert.AreEqual(JTokenType.Null, s["noise.value"].Type);
            Assert.AreEqual(2, (int)s["game.round"]);
        }

        [Test]
        public void ThrowingGetterAndNaNBecomeNullAndWarn()
        {
            var model = AgentRegistry.Build(new[] { typeof(FakeThrowingGetter) });
            var warnings = new List<string>();
            var s = SnapshotBuilder.Build(model, t => new FakeThrowingGetter(), warnings.Add);
            Assert.AreEqual(JTokenType.Null, s["explodes"].Type);
            Assert.AreEqual(JTokenType.Null, s["not.a.number"].Type);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("kaboom", warnings[0]);
        }

        [Test]
        public void PayloadSerializesVector3AndWrapsNonObjects()
        {
            Assert.IsNull(UnityJson.ToPayload(null));
            Assert.AreEqual("{\"at\":[1.0,2.0,3.0],\"n\":5}",
                UnityJson.ToPayload(new { at = new Vector3(1, 2, 3), n = 5 }).ToString(Formatting.None));
            Assert.AreEqual("{\"value\":5}", UnityJson.ToPayload(5).ToString(Formatting.None));
            Assert.AreEqual("{\"value\":\"hi\"}", UnityJson.ToPayload("hi").ToString(Formatting.None));
            Assert.AreEqual("{\"level\":\"Alert\"}", UnityJson.ToPayload(new { level = FakeLevel.Alert }).ToString(Formatting.None));
            var jo = new JObject { ["k"] = 1 };
            Assert.AreSame(jo, UnityJson.ToPayload(jo));
        }

        [Test]
        public void PayloadThatCannotSerializeBecomesErrorObject()
        {
            var p = UnityJson.ToPayload(new { bad = new ThrowsOnGet() });
            StringAssert.Contains("could not be serialized", (string)p["error"]);
        }

        [Test]
        public void ResolverFindsSceneInstanceAndForgetsDestroyedOne()
        {
            var go = new GameObject("fake");
            try
            {
                var c = go.AddComponent<FakeObservedBehaviour>();
                var r = new UnityInstanceResolver();
                Assert.AreSame(c, r.Resolve(typeof(FakeObservedBehaviour)));
                Object.DestroyImmediate(go);
                go = null;
                Assert.IsNull(r.Resolve(typeof(FakeObservedBehaviour)));
                Assert.IsNull(r.Resolve(typeof(FakeGame)), "plain C# types cannot be resolved");
            }
            finally
            {
                if (go != null) Object.DestroyImmediate(go);
            }
        }

        class ThrowsOnGet
        {
            public int Value => throw new System.InvalidOperationException("nope");
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `bash Tools/unity-tests.sh`
Expected: `error CS0103: The name 'SnapshotBuilder' does not exist in the current context`.

- [ ] **Step 3: Viết code**

`Packages/com.agentplay.core/Runtime/Json/UnityJson.cs`:

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AgentPlay
{
    public static class UnityJson
    {
        public static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Converters = new List<JsonConverter> { new Vector3Converter(), new Vector2Converter(), new StringEnumConverter() },
        });

        /// <summary>Turns any payload into a JSON object. Never throws: failures become {"error": ...}.</summary>
        public static JObject ToPayload(object payload)
        {
            if (payload == null) return null;
            if (payload is JObject jo) return jo;
            JToken token;
            try
            {
                token = JToken.FromObject(payload, Serializer);
            }
            catch (Exception e)
            {
                return new JObject { ["error"] = "Payload could not be serialized: " + e.Message };
            }
            return token as JObject ?? new JObject { ["value"] = token };
        }

        /// <summary>Rounds to 3 decimals; NaN and infinity become null so the JSON stays valid.</summary>
        public static double? Clean(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return null;
            return Math.Round(d, 3);
        }

        internal static void WriteNumber(JsonWriter writer, float f)
        {
            var c = Clean(f);
            if (c.HasValue) writer.WriteValue(c.Value);
            else writer.WriteNull();
        }

        sealed class Vector3Converter : JsonConverter<Vector3>
        {
            public override bool CanRead => false;

            public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
            {
                writer.WriteStartArray();
                WriteNumber(writer, value.x);
                WriteNumber(writer, value.y);
                WriteNumber(writer, value.z);
                writer.WriteEndArray();
            }

            public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue, bool hasExistingValue, JsonSerializer serializer)
                => throw new NotSupportedException();
        }

        sealed class Vector2Converter : JsonConverter<Vector2>
        {
            public override bool CanRead => false;

            public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer)
            {
                writer.WriteStartArray();
                WriteNumber(writer, value.x);
                WriteNumber(writer, value.y);
                writer.WriteEndArray();
            }

            public override Vector2 ReadJson(JsonReader reader, Type objectType, Vector2 existingValue, bool hasExistingValue, JsonSerializer serializer)
                => throw new NotSupportedException();
        }
    }
}
```

`Packages/com.agentplay.core/Runtime/Registry/SnapshotBuilder.cs`:

```csharp
using System;
using System.Reflection;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AgentPlay
{
    public static class SnapshotBuilder
    {
        /// <summary>Reads every observe into a flat object keyed by path. Unreadable values become null.</summary>
        public static JObject Build(RegistryModel model, Func<Type, object> resolveInstance, Action<string> warn = null)
        {
            var o = new JObject();
            foreach (var e in model.Observes)
                o[e.Path] = Read(e, resolveInstance, warn);
            return o;
        }

        static JToken Read(ObserveEntry e, Func<Type, object> resolveInstance, Action<string> warn)
        {
            object target = null;
            if (!e.IsStatic)
            {
                target = resolveInstance(e.DeclaringType);
                if (target == null) return JValue.CreateNull();
            }

            object value;
            try
            {
                value = e.Member is FieldInfo f ? f.GetValue(target) : ((PropertyInfo)e.Member).GetValue(target);
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                warn?.Invoke("Observe '" + e.Path + "' threw: " + inner.Message);
                return JValue.CreateNull();
            }
            return ToToken(e.ValueKind, value);
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

`Packages/com.agentplay.core/Runtime/Registry/UnityInstanceResolver.cs`:

```csharp
using System;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace AgentPlay
{
    /// <summary>Finds the first live scene instance of a UnityEngine.Object type, caching it until destroyed.</summary>
    public sealed class UnityInstanceResolver
    {
        readonly Dictionary<Type, Object> _cache = new Dictionary<Type, Object>();

        public object Resolve(Type type)
        {
            if (!typeof(Object).IsAssignableFrom(type)) return null;
            if (_cache.TryGetValue(type, out var cached) && cached != null) return cached;
            var found = Object.FindFirstObjectByType(type);
            _cache[type] = found;
            return found != null ? found : null;
        }
    }
}
```

Dòng `return found != null ? found : null;` là cố ý: object Unity đã hủy so sánh `== null` là true nhưng không phải C# null. Trả về C# null thì `SnapshotBuilder` mới nhận ra là thiếu instance.

- [ ] **Step 4: Chạy test để thấy pass**

Run: `bash Tools/unity-tests.sh`
Expected: `result="Passed"`, `failed="0"`.

- [ ] **Step 5: Commit**

```bash
git add Packages
git commit -m "feat(runtime): snapshot builder, safe payload JSON and scene instance resolver"
```

---

### Task 6: AgentEvents

**Files:**
- Create: `Packages/com.agentplay.core/Runtime/Events/AgentEvents.cs`
- Test: `Packages/com.agentplay.core/Tests/EditMode/AgentEventsTests.cs`

**Interfaces:**
- Consumes: `UnityJson.ToPayload` (Task 5), `AgentOutcome` (Task 4).
- Produces:
  - `struct AgentEventRecord { string Name; JObject Payload; bool Important; AgentOutcome Outcome; double Time; long Frame; }` (readonly, có constructor đủ tham số).
  - `AgentEvents.Emit(string name, object payload = null, bool important = false, AgentOutcome outcome = AgentOutcome.Neutral)`.
  - `static event Action<AgentEventRecord> AgentEvents.Emitted`.
  - Test seam: `static Func<double> AgentEvents.TimeSource`, `static Func<long> AgentEvents.FrameSource`.

- [ ] **Step 1: Viết test thất bại**

`Packages/com.agentplay.core/Tests/EditMode/AgentEventsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AgentPlay.Tests
{
    public class AgentEventsTests
    {
        readonly List<AgentEventRecord> _got = new List<AgentEventRecord>();
        Func<double> _oldTime;
        Func<long> _oldFrame;

        void Collect(AgentEventRecord r) => _got.Add(r);

        [SetUp]
        public void SetUp()
        {
            _got.Clear();
            _oldTime = AgentEvents.TimeSource;
            _oldFrame = AgentEvents.FrameSource;
            AgentEvents.TimeSource = () => 12.5;
            AgentEvents.FrameSource = () => 300;
        }

        [TearDown]
        public void TearDown()
        {
            AgentEvents.Emitted -= Collect;
            AgentEvents.TimeSource = _oldTime;
            AgentEvents.FrameSource = _oldFrame;
        }

        [Test]
        public void DeliversRecordWithAllFields()
        {
            AgentEvents.Emitted += Collect;
            AgentEvents.Emit("guard.spotted", new { at = new Vector3(1, 0, 2) }, important: true, outcome: AgentOutcome.Bad);

            Assert.AreEqual(1, _got.Count);
            var r = _got[0];
            Assert.AreEqual("guard.spotted", r.Name);
            Assert.IsTrue(r.Important);
            Assert.AreEqual(AgentOutcome.Bad, r.Outcome);
            Assert.AreEqual(12.5, r.Time);
            Assert.AreEqual(300L, r.Frame);
            Assert.AreEqual(3, ((Newtonsoft.Json.Linq.JArray)r.Payload["at"]).Count);
        }

        [Test]
        public void DoesNotTouchPayloadWhenNobodyListens()
        {
            var probe = new CountingPayload();
            AgentEvents.Emit("quiet", probe);
            Assert.AreEqual(0, probe.Reads);
        }

        [Test]
        public void NonObjectPayloadIsWrapped()
        {
            AgentEvents.Emitted += Collect;
            AgentEvents.Emit("count", 3);
            Assert.AreEqual(3, (int)_got[0].Payload["value"]);
        }

        [Test]
        public void EmptyNameIsIgnoredWithWarning()
        {
            AgentEvents.Emitted += Collect;
            LogAssert.Expect(LogType.Warning, new Regex("empty event name"));
            AgentEvents.Emit("");
            Assert.AreEqual(0, _got.Count);
        }

        [Test]
        public void ThrowingSubscriberDoesNotStopOthersOrTheGame()
        {
            Action<AgentEventRecord> boom = _ => throw new InvalidOperationException("boom");
            AgentEvents.Emitted += boom;
            AgentEvents.Emitted += Collect;
            try
            {
                LogAssert.Expect(LogType.Exception, new Regex("boom"));
                Assert.DoesNotThrow(() => AgentEvents.Emit("x"));
                Assert.AreEqual(1, _got.Count);
            }
            finally
            {
                AgentEvents.Emitted -= boom;
            }
        }

        class CountingPayload
        {
            public int Reads;
            public int Value { get { Reads++; return 1; } }
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `bash Tools/unity-tests.sh`
Expected: `error CS0246: The type or namespace name 'AgentEventRecord' could not be found`.

- [ ] **Step 3: Viết code**

`Packages/com.agentplay.core/Runtime/Events/AgentEvents.cs`:

```csharp
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AgentPlay
{
    public readonly struct AgentEventRecord
    {
        public readonly string Name;
        public readonly JObject Payload;
        public readonly bool Important;
        public readonly AgentOutcome Outcome;
        public readonly double Time;
        public readonly long Frame;

        public AgentEventRecord(string name, JObject payload, bool important, AgentOutcome outcome, double time, long frame)
        {
            Name = name;
            Payload = payload;
            Important = important;
            Outcome = outcome;
            Time = time;
            Frame = frame;
        }
    }

    /// <summary>Game-facing API. Costs nothing when AgentPlay is not listening.</summary>
    public static class AgentEvents
    {
        public static event Action<AgentEventRecord> Emitted;

        public static Func<double> TimeSource = () => UnityEngine.Time.timeAsDouble;
        public static Func<long> FrameSource = () => UnityEngine.Time.frameCount;

        /// <param name="important">Pauses turn-based play and must reach the runner when no rule handles it.</param>
        /// <param name="outcome">Marks the event as good or bad for scoring reflexes.</param>
        public static void Emit(string name, object payload = null, bool important = false, AgentOutcome outcome = AgentOutcome.Neutral)
        {
            var handlers = Emitted;
            if (handlers == null) return;
            if (string.IsNullOrEmpty(name))
            {
                Debug.LogWarning("[AgentPlay] Ignored an event with an empty event name");
                return;
            }

            var record = new AgentEventRecord(name, UnityJson.ToPayload(payload), important, outcome, TimeSource(), FrameSource());
            foreach (Action<AgentEventRecord> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(record);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }
}
```

Giới hạn đã biết, ghi lại trong `docs/notes/m1-verification.md` ở Task 12: sự kiện phát ra trước khi controller của Editor đăng ký (ví dụ trong `Awake` của scene đầu tiên) sẽ bị mất.

- [ ] **Step 4: Chạy test để thấy pass**

Run: `bash Tools/unity-tests.sh`
Expected: `result="Passed"`, `failed="0"`.

- [ ] **Step 5: Commit**

```bash
git add Packages
git commit -m "feat(runtime): AgentEvents.Emit with isolated subscribers and safe payloads"
```

---

### Task 7: Trace: TraceWriter, TracePaths, IntervalTimer

**Files:**
- Create: `Packages/com.agentplay.core/Runtime/Trace/TraceWriter.cs`, `TracePaths.cs`, `IntervalTimer.cs`
- Test: `Packages/com.agentplay.core/Tests/EditMode/TraceTests.cs`

**Interfaces:**
- Consumes: `AgentEventRecord` (Task 6).
- Produces:
  - `TraceWriter(TextWriter output)`: `WriteHeader(string game, string version, string who, string tag, DateTime startedUtc)`, `WriteEvent(AgentEventRecord e, JObject state)`, `WriteSnapshot(double t, long frame, JObject state)`, `Dispose()`.
  - `TracePaths.Build(string projectRoot, DateTime localNow, string who, string tag, Func<string, bool> exists) : string`.
  - `TracePaths.Open(string path) : TraceWriter` (tạo thư mục, UTF-8 không BOM, AutoFlush).
  - `IntervalTimer(double intervalSeconds)`: `bool Tick(double now)` (true ở lần gọi đầu tiên, rồi mỗi `interval`).

Định dạng dòng trace:
- Header: `{"type":"header","game":..,"version":..,"who":..,"tag":..,"started":"<ISO 8601 UTC>"}`
- Sự kiện: `{"type":"event","t":..,"frame":..,"name":..,"important":..,"outcome":"neutral|good|bad","payload":{..}|null,"state":{..}}`
- Snapshot: `{"type":"snapshot","t":..,"frame":..,"state":{..}}`

- [ ] **Step 1: Viết test thất bại**

`Packages/com.agentplay.core/Tests/EditMode/TraceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class TraceTests
    {
        static List<JObject> Lines(StringWriter sw)
        {
            var settings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };
            var result = new List<JObject>();
            foreach (var line in sw.ToString().Split('\n'))
                if (line.Length > 0) result.Add(JsonConvert.DeserializeObject<JObject>(line, settings));
            return result;
        }

        [Test]
        public void WritesHeaderEventAndSnapshotAsOneJsonPerLine()
        {
            var sw = new StringWriter();
            using (var w = new TraceWriter(sw))
            {
                w.WriteHeader("G", "v1", "human", "play", new DateTime(2026, 10, 11, 1, 2, 3, DateTimeKind.Utc));
                w.WriteEvent(new AgentEventRecord("tile.pried", new JObject { ["n"] = 1 }, true, AgentOutcome.Good, 3.5, 210),
                    new JObject { ["noise.value"] = 40.0 });
                w.WriteSnapshot(4.0, 240, new JObject { ["noise.value"] = 41.0 });
            }
            var lines = Lines(sw);
            Assert.AreEqual(3, lines.Count);

            Assert.AreEqual("header", (string)lines[0]["type"]);
            Assert.AreEqual("human", (string)lines[0]["who"]);
            Assert.AreEqual("play", (string)lines[0]["tag"]);
            StringAssert.StartsWith("2026-10-11T01:02:03", (string)lines[0]["started"]);

            Assert.AreEqual("event", (string)lines[1]["type"]);
            Assert.AreEqual("tile.pried", (string)lines[1]["name"]);
            Assert.AreEqual("good", (string)lines[1]["outcome"]);
            Assert.AreEqual(true, (bool)lines[1]["important"]);
            Assert.AreEqual(210, (long)lines[1]["frame"]);
            Assert.AreEqual(1, (int)lines[1]["payload"]["n"]);
            Assert.AreEqual(40.0, (double)lines[1]["state"]["noise.value"]);

            Assert.AreEqual("snapshot", (string)lines[2]["type"]);
            Assert.AreEqual(4.0, (double)lines[2]["t"]);
        }

        [Test]
        public void EventWithoutPayloadWritesNull()
        {
            var sw = new StringWriter();
            using (var w = new TraceWriter(sw))
                w.WriteEvent(new AgentEventRecord("x", null, false, AgentOutcome.Neutral, 0, 0), new JObject());
            Assert.AreEqual(JTokenType.Null, Lines(sw)[0]["payload"].Type);
        }

        [Test]
        public void PathUsesTimestampWhoAndTagAndAvoidsCollisions()
        {
            var now = new DateTime(2026, 10, 11, 9, 5, 7);
            string root = Path.Combine(Path.GetTempPath(), "Game");
            string expected = Path.Combine(root, "AgentPlay", "Traces", "20261011-090507-human-debug.ndjson");
            Assert.AreEqual(expected, TracePaths.Build(root, now, "human", "debug", _ => false));

            var taken = new HashSet<string> { expected };
            string second = TracePaths.Build(root, now, "human", "debug", taken.Contains);
            Assert.AreEqual(Path.Combine(root, "AgentPlay", "Traces", "20261011-090507-human-debug-2.ndjson"), second);
        }

        [Test]
        public void OpenCreatesDirectoryAndWritesImmediately()
        {
            string dir = Path.Combine(Path.GetTempPath(), "agentplay-trace-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "a", "t.ndjson");
            try
            {
                var w = TracePaths.Open(path);
                w.WriteSnapshot(1, 1, new JObject());
                StringAssert.Contains("\"snapshot\"", File.ReadAllText(path), "AutoFlush must write without Dispose");
                w.Dispose();
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void IntervalTimerFiresFirstTickThenEveryInterval()
        {
            var t = new IntervalTimer(2.0);
            Assert.IsTrue(t.Tick(0.5));
            Assert.IsFalse(t.Tick(1.0));
            Assert.IsFalse(t.Tick(2.4));
            Assert.IsTrue(t.Tick(2.5));
            Assert.IsFalse(t.Tick(3.0));
            Assert.IsTrue(t.Tick(10.0));
            Assert.IsFalse(t.Tick(11.9));
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `bash Tools/unity-tests.sh`
Expected: `error CS0246: The type or namespace name 'TraceWriter' could not be found`.

- [ ] **Step 3: Viết code**

`Packages/com.agentplay.core/Runtime/Trace/TraceWriter.cs`:

```csharp
using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay
{
    /// <summary>Writes one JSON object per line. Not thread-safe; call from the main thread.</summary>
    public sealed class TraceWriter : IDisposable
    {
        readonly TextWriter _out;

        public TraceWriter(TextWriter output)
        {
            _out = output;
        }

        public void WriteHeader(string game, string version, string who, string tag, DateTime startedUtc)
        {
            WriteLine(new JObject
            {
                ["type"] = "header",
                ["game"] = game,
                ["version"] = version,
                ["who"] = who,
                ["tag"] = tag,
                ["started"] = startedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            });
        }

        public void WriteEvent(AgentEventRecord e, JObject state)
        {
            WriteLine(new JObject
            {
                ["type"] = "event",
                ["t"] = e.Time,
                ["frame"] = e.Frame,
                ["name"] = e.Name,
                ["important"] = e.Important,
                ["outcome"] = e.Outcome.ToString().ToLowerInvariant(),
                ["payload"] = e.Payload != null ? (JToken)e.Payload : JValue.CreateNull(),
                ["state"] = state,
            });
        }

        public void WriteSnapshot(double t, long frame, JObject state)
        {
            WriteLine(new JObject
            {
                ["type"] = "snapshot",
                ["t"] = t,
                ["frame"] = frame,
                ["state"] = state,
            });
        }

        void WriteLine(JObject o)
        {
            _out.Write(o.ToString(Formatting.None));
            _out.Write('\n');
        }

        public void Dispose()
        {
            _out.Dispose();
        }
    }
}
```

`Packages/com.agentplay.core/Runtime/Trace/TracePaths.cs`:

```csharp
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AgentPlay
{
    public static class TracePaths
    {
        /// <summary>&lt;root&gt;/AgentPlay/Traces/yyyyMMdd-HHmmss-who-tag[-n].ndjson</summary>
        public static string Build(string projectRoot, DateTime localNow, string who, string tag, Func<string, bool> exists)
        {
            string dir = Path.Combine(projectRoot, "AgentPlay", "Traces");
            string stem = localNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + who + "-" + tag;
            string path = Path.Combine(dir, stem + ".ndjson");
            for (int n = 2; exists(path); n++)
                path = Path.Combine(dir, stem + "-" + n + ".ndjson");
            return path;
        }

        public static TraceWriter Open(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var stream = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
            return new TraceWriter(stream);
        }
    }
}
```

`Packages/com.agentplay.core/Runtime/Trace/IntervalTimer.cs`:

```csharp
namespace AgentPlay
{
    public sealed class IntervalTimer
    {
        readonly double _interval;
        double _next = double.NegativeInfinity;

        public IntervalTimer(double intervalSeconds)
        {
            _interval = intervalSeconds;
        }

        /// <summary>True on the first call, then once each time <paramref name="now"/> passes the next deadline.</summary>
        public bool Tick(double now)
        {
            if (now < _next) return false;
            _next = now + _interval;
            return true;
        }
    }
}
```

- [ ] **Step 4: Chạy test để thấy pass**

Run: `bash Tools/unity-tests.sh`
Expected: `result="Passed"`, `failed="0"`.

- [ ] **Step 5: Commit**

```bash
git add Packages
git commit -m "feat(runtime): NDJSON trace writer, trace paths and interval timer"
```

---

### Task 8: StepPolicy và SessionCore

**Files:**
- Create: `Packages/com.agentplay.core/Runtime/Session/StepPolicy.cs`, `SessionCore.cs`
- Test: `Packages/com.agentplay.core/Tests/EditMode/SessionCoreTests.cs`

**Interfaces:**
- Consumes: `MessageCodec`, `MessageTypes`, `Modes`, DTO (Task 1–2); `AgentEventRecord` (Task 6); `TraceWriter`, `IntervalTimer` (Task 7).
- Produces:
  - `enum PauseReason { None, Mode, Event, Budget }`.
  - `StepPolicy`: `string Mode`, `bool SetMode(string mode)`, `PauseReason OnImportantEvent()`, `PauseReason OnTick(double gameTime)`, `void OnResume(double gameTime, double maxGameSeconds)`.
  - `interface ISessionHost { double GameTime { get; } long Frame { get; } void SetPaused(bool paused); void Send(string line); }`.
  - `SessionCore(HelloMessage hello, Func<JObject> snapshot, ISessionHost host, TraceWriter trace, double snapshotIntervalSeconds)`: `OnConnected()`, `OnLine(string line)`, `OnEvent(AgentEventRecord rec)`, `Tick(bool gameRunning)`, `string Mode`, `int EventCount`, `IReadOnlyList<string> RecentEvents`, `bool AwaitingResume`.

Hành vi:
- `set_mode turn_based`: dừng ngay, gửi `paused` với lý do `mode`. `set_mode realtime`: bỏ dừng. Mode lạ thì trả `error`.
- `resume`: đặt ngân sách thời gian game (`max_game_seconds` ≤ 0 nghĩa là chạy tới sự kiện quan trọng kế tiếp) rồi bỏ dừng.
- Sự kiện quan trọng trong chế độ theo lượt: gửi `event` trước, rồi dừng và gửi `paused` với lý do `event`.
- Hết ngân sách: dừng, lý do `budget`.
- Đang chờ `resume` thì không gửi `paused` lần nữa. Nếu `Tick(true)` thấy game đang chạy trong lúc chờ (người dùng tự bỏ pause), trạng thái chờ bị xóa.

- [ ] **Step 1: Viết test thất bại**

`Packages/com.agentplay.core/Tests/EditMode/SessionCoreTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class SessionCoreTests
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

        FakeHost _host;
        StringWriter _traceText;
        SessionCore _core;

        [SetUp]
        public void SetUp()
        {
            _host = new FakeHost();
            _traceText = new StringWriter();
            var hello = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G", Version = "1" };
            _core = new SessionCore(hello, () => new JObject { ["x"] = _host.GameTime }, _host, new TraceWriter(_traceText), 2.0);
        }

        static string Line(string type, object body) => MessageCodec.Encode(type, 1, body);
        static AgentEventRecord Ev(string name, bool important) => new AgentEventRecord(name, null, important, AgentOutcome.Neutral, 0, 0);

        [Test]
        public void SendsHelloOnConnect()
        {
            _core.OnConnected();
            Assert.AreEqual("G", _host.OfType(MessageTypes.Hello).Single().As<HelloMessage>().Game);
        }

        [Test]
        public void SeqIncreasesAcrossMessages()
        {
            _core.OnConnected();
            _core.OnLine(Line(MessageTypes.GetState, null));
            Assert.Less(_host.Sent[0].Seq, _host.Sent[1].Seq);
        }

        [Test]
        public void TurnBasedPausesImmediatelyWithReasonMode()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            Assert.IsTrue(_host.Paused);
            Assert.AreEqual("mode", _host.OfType(MessageTypes.Paused).Single().As<PausedMessage>().Reason);
            Assert.AreEqual(Modes.TurnBased, _core.Mode);
        }

        [Test]
        public void ResumeRunsUntilBudgetThenPausesOnce()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _host.GameTime = 10;
            _core.OnLine(Line(MessageTypes.Resume, new ResumeMessage { MaxGameSeconds = 2 }));
            Assert.IsFalse(_host.Paused);

            _host.GameTime = 11.9; _core.Tick(true);
            Assert.IsFalse(_host.Paused);
            _host.GameTime = 12.0; _core.Tick(true);
            Assert.IsTrue(_host.Paused);
            _core.Tick(false);

            var paused = _host.OfType(MessageTypes.Paused);
            Assert.AreEqual(2, paused.Count);
            Assert.AreEqual("budget", paused[1].As<PausedMessage>().Reason);
        }

        [Test]
        public void ImportantEventPausesOnlyInTurnBasedAndAfterSendingTheEvent()
        {
            _core.OnEvent(Ev("e1", true));
            Assert.IsFalse(_host.Paused, "realtime never pauses");

            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _core.OnLine(Line(MessageTypes.Resume, new ResumeMessage { MaxGameSeconds = 0 }));
            _host.Sent.Clear();

            _core.OnEvent(Ev("minor", false));
            Assert.IsFalse(_host.Paused);
            _core.OnEvent(Ev("e2", true));
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
            _core.OnEvent(Ev("b", true));
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
            Assert.IsTrue(_host.Paused);
            Assert.AreEqual(2, _host.OfType(MessageTypes.Paused).Count);
        }

        [Test]
        public void RealtimeModeUnpauses()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.Realtime }));
            Assert.IsFalse(_host.Paused);
            Assert.IsFalse(_core.AwaitingResume);
        }

        [Test]
        public void GetStateReturnsSnapshot()
        {
            _host.GameTime = 3; _host.Frame = 99;
            _core.OnLine(Line(MessageTypes.GetState, null));
            var s = _host.OfType(MessageTypes.State).Single().As<StateMessage>();
            Assert.AreEqual(3.0, s.T);
            Assert.AreEqual(99L, s.Frame);
            Assert.AreEqual(3.0, (double)s.State["x"]);
        }

        [TestCase("not json")]
        [TestCase("{\"type\":\"teleport\",\"seq\":1}")]
        [TestCase("{\"type\":\"set_mode\",\"seq\":1,\"mode\":\"fast\"}")]
        [TestCase("{\"type\":\"resume\",\"seq\":1,\"max_game_seconds\":\"abc\"}")]
        public void BadMessagesGetAnErrorReplyAndNoException(string line)
        {
            Assert.DoesNotThrow(() => _core.OnLine(line));
            Assert.AreEqual(1, _host.OfType(MessageTypes.Error).Count);
            Assert.IsFalse(_host.Paused);
        }

        [Test]
        public void BlankLinesAreIgnored()
        {
            _core.OnLine("   ");
            Assert.AreEqual(0, _host.Sent.Count);
        }

        [Test]
        public void EventsAreTracedCountedAndKeptInRecentList()
        {
            for (int i = 0; i < SessionCore.RecentLimit + 3; i++) _core.OnEvent(Ev("e" + i, false));
            Assert.AreEqual(SessionCore.RecentLimit + 3, _core.EventCount);
            Assert.AreEqual(SessionCore.RecentLimit, _core.RecentEvents.Count);
            StringAssert.Contains("e" + (SessionCore.RecentLimit + 2), _core.RecentEvents[SessionCore.RecentLimit - 1]);
            StringAssert.Contains("\"type\":\"event\"", _traceText.ToString());
        }

        [Test]
        public void TickWritesSnapshotsOnIntervalOnlyWhileRunning()
        {
            _host.GameTime = 0; _core.Tick(true);
            _host.GameTime = 1; _core.Tick(true);
            _host.GameTime = 5; _core.Tick(false);
            _host.GameTime = 2; _core.Tick(true);
            int snapshots = _traceText.ToString().Split('\n').Count(l => l.Contains("\"type\":\"snapshot\""));
            Assert.AreEqual(2, snapshots);
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `bash Tools/unity-tests.sh`
Expected: `error CS0246: The type or namespace name 'ISessionHost' could not be found`.

- [ ] **Step 3: Viết code**

`Packages/com.agentplay.core/Runtime/Session/StepPolicy.cs`:

```csharp
using AgentPlay.Contract;

namespace AgentPlay
{
    public enum PauseReason { None, Mode, Event, Budget }

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

        public PauseReason OnImportantEvent()
        {
            if (Mode != Modes.TurnBased) return PauseReason.None;
            _budgetEnd = double.PositiveInfinity;
            return PauseReason.Event;
        }

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
    }
}
```

`Packages/com.agentplay.core/Runtime/Session/SessionCore.cs`:

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
        readonly StepPolicy _step = new StepPolicy();
        readonly SequenceCounter _seq = new SequenceCounter();
        readonly List<string> _recent = new List<string>();

        public string Mode => _step.Mode;
        public int EventCount { get; private set; }
        public IReadOnlyList<string> RecentEvents => _recent;
        public bool AwaitingResume { get; private set; }

        public SessionCore(HelloMessage hello, Func<JObject> snapshot, ISessionHost host, TraceWriter trace, double snapshotIntervalSeconds)
        {
            _hello = hello;
            _snapshot = snapshot;
            _host = host;
            _trace = trace;
            _snapshotTimer = new IntervalTimer(snapshotIntervalSeconds);
        }

        public void OnConnected() => Send(MessageTypes.Hello, _hello);

        public void OnLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            Envelope env;
            try
            {
                env = MessageCodec.Decode(line);
            }
            catch (ProtocolException e)
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

            if (rec.Important)
            {
                var reason = _step.OnImportantEvent();
                if (reason != PauseReason.None) Pause(reason);
            }
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
                        Pause(PauseReason.Mode);
                    }
                    else
                    {
                        AwaitingResume = false;
                        _host.SetPaused(false);
                    }
                    return;

                case MessageTypes.Resume:
                    var resume = env.As<ResumeMessage>();
                    _step.OnResume(_host.GameTime, resume.MaxGameSeconds);
                    AwaitingResume = false;
                    _host.SetPaused(false);
                    return;

                case MessageTypes.GetState:
                    Send(MessageTypes.State, new StateMessage { T = _host.GameTime, Frame = _host.Frame, State = _snapshot() });
                    return;

                default:
                    SendError("Unknown message type '" + env.Type + "'");
                    return;
            }
        }

        void Pause(PauseReason reason)
        {
            if (AwaitingResume) return;
            AwaitingResume = true;
            _host.SetPaused(true);
            Send(MessageTypes.Paused, new PausedMessage { Reason = reason.ToString().ToLowerInvariant(), T = _host.GameTime });
        }

        void SendError(string message) => Send(MessageTypes.Error, new ErrorMessage { Message = message });

        void Send<T>(string type, T dto) => _host.Send(MessageCodec.Encode(type, _seq.Next(), dto));
    }
}
```

- [ ] **Step 4: Chạy test để thấy pass**

Run: `bash Tools/unity-tests.sh`
Expected: `result="Passed"`, `failed="0"`.

Trong `TickWritesSnapshotsOnIntervalOnlyWhileRunning`, snapshot ghi ở t=0 và t=2. Lần gọi t=5 bị bỏ qua vì game không chạy, nên tổng là 2.

- [ ] **Step 5: Commit**

```bash
git add Packages
git commit -m "feat(runtime): SessionCore protocol handling and turn-based step policy"
```

---

### Task 9: BridgeServer

Kiểm chứng một phần rủi ro 2 của spec: dừng server thì cổng được giải phóng.

**Files:**
- Create: `Packages/com.agentplay.core/Editor/BridgeServer.cs`
- Test: `Packages/com.agentplay.core/Tests/EditMode/BridgeServerTests.cs`

**Interfaces:**
- Consumes: `LineFramer` (Task 1).
- Produces (namespace `AgentPlay.Editor`):
  - `enum BridgeSignal { Connected, Disconnected }`.
  - `BridgeServer`: hằng `FirstPort = 47800`, `LastPort = 47809`, `SendTimeoutMs = 2000`; `bool Start(int firstPort = FirstPort, int lastPort = LastPort)`; `int Port`; `bool IsClientConnected`; `bool TrySend(string line)`; `bool TryReceive(out string line)`; `bool TryGetSignal(out BridgeSignal signal)`; `void Stop()`; `Dispose()`.

Test dùng dải cổng 47850–47859 để không đụng một phiên AgentPlay thật đang chạy.

- [ ] **Step 1: Viết test thất bại**

`Packages/com.agentplay.core/Tests/EditMode/BridgeServerTests.cs`:

```csharp
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using AgentPlay.Editor;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class BridgeServerTests
    {
        const int First = 47850, Last = 47859;
        BridgeServer _server;
        TcpClient _client;
        TcpListener _blocker;

        [TearDown]
        public void TearDown()
        {
            _client?.Close();
            _server?.Dispose();
            _blocker?.Stop();
        }

        static bool WaitFor(System.Func<bool> condition, int timeoutMs = 3000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                Thread.Sleep(10);
            }
            return condition();
        }

        void StartAndConnect()
        {
            _server = new BridgeServer();
            Assert.IsTrue(_server.Start(First, Last));
            _client = new TcpClient();
            _client.Connect(IPAddress.Loopback, _server.Port);
            BridgeSignal s = default;
            Assert.IsTrue(WaitFor(() => _server.TryGetSignal(out s)), "no Connected signal");
            Assert.AreEqual(BridgeSignal.Connected, s);
        }

        [Test]
        public void ExchangesLinesBothWays()
        {
            StartAndConnect();
            var bytes = Encoding.UTF8.GetBytes("{\"a\":1}\n{\"b\":");
            _client.GetStream().Write(bytes, 0, bytes.Length);
            string line = null;
            Assert.IsTrue(WaitFor(() => _server.TryReceive(out line)));
            Assert.AreEqual("{\"a\":1}", line);
            Assert.IsFalse(_server.TryReceive(out _), "partial line must wait for its newline");

            Assert.IsTrue(_server.TrySend("{\"c\":2}"));
            var reader = new StreamReader(_client.GetStream(), Encoding.UTF8);
            Assert.AreEqual("{\"c\":2}", reader.ReadLine());
        }

        [Test]
        public void ReportsDisconnectAndRefusesSendAfterward()
        {
            StartAndConnect();
            _client.Close();
            BridgeSignal s = default;
            Assert.IsTrue(WaitFor(() => _server.TryGetSignal(out s)));
            Assert.AreEqual(BridgeSignal.Disconnected, s);
            Assert.IsFalse(_server.IsClientConnected);
            Assert.IsFalse(_server.TrySend("x"));
        }

        [Test]
        public void SkipsPortThatIsAlreadyTaken()
        {
            _blocker = new TcpListener(IPAddress.Loopback, First);
            _blocker.Start();
            _server = new BridgeServer();
            Assert.IsTrue(_server.Start(First, Last));
            Assert.AreEqual(First + 1, _server.Port);
        }

        [Test]
        public void StopReleasesThePort()
        {
            StartAndConnect();
            int port = _server.Port;
            _server.Stop();
            var again = new TcpListener(IPAddress.Loopback, port);
            Assert.DoesNotThrow(() => again.Start());
            again.Stop();
        }

        [Test]
        public void ClientThatNeverReadsCannotFreezeTheSender()
        {
            StartAndConnect();
            _client.ReceiveBufferSize = 4096;
            string chunk = new string('x', 64 * 1024);
            var sw = Stopwatch.StartNew();
            bool failed = false;
            for (int i = 0; i < 4000 && !failed; i++)
                failed = !_server.TrySend(chunk);
            sw.Stop();
            Assert.IsTrue(failed, "sending to a stalled client must eventually fail");
            Assert.Less(sw.ElapsedMilliseconds, 10000);
            Assert.IsFalse(_server.IsClientConnected);
        }
    }
}
```

- [ ] **Step 2: Chạy test để thấy thất bại**

Run: `bash Tools/unity-tests.sh`
Expected: `error CS0246: The type or namespace name 'BridgeServer' could not be found`.

- [ ] **Step 3: Viết code**

`Packages/com.agentplay.core/Editor/BridgeServer.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using AgentPlay.Contract;

namespace AgentPlay.Editor
{
    public enum BridgeSignal { Connected, Disconnected }

    /// <summary>
    /// Loopback TCP server for one runner at a time. A background thread accepts and reads;
    /// the main thread drains TryReceive/TryGetSignal and calls TrySend.
    /// </summary>
    public sealed class BridgeServer : IDisposable
    {
        public const int FirstPort = 47800;
        public const int LastPort = 47809;
        public const int SendTimeoutMs = 2000;

        readonly ConcurrentQueue<string> _inbound = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<BridgeSignal> _signals = new ConcurrentQueue<BridgeSignal>();
        readonly object _clientLock = new object();

        TcpListener _listener;
        Thread _thread;
        TcpClient _client;
        NetworkStream _stream;
        volatile bool _running;

        public int Port { get; private set; }

        public bool IsClientConnected
        {
            get { lock (_clientLock) return _stream != null; }
        }

        public bool Start(int firstPort = FirstPort, int lastPort = LastPort)
        {
            for (int port = firstPort; port <= lastPort; port++)
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                try
                {
                    listener.Start();
                }
                catch (SocketException)
                {
                    continue;
                }
                _listener = listener;
                Port = port;
                _running = true;
                _thread = new Thread(Serve) { IsBackground = true, Name = "AgentPlay.Bridge" };
                _thread.Start();
                return true;
            }
            return false;
        }

        public bool TryReceive(out string line) => _inbound.TryDequeue(out line);

        public bool TryGetSignal(out BridgeSignal signal) => _signals.TryDequeue(out signal);

        public bool TrySend(string line)
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            lock (_clientLock)
            {
                if (_stream == null) return false;
                try
                {
                    _stream.Write(bytes, 0, bytes.Length);
                    return true;
                }
                catch (Exception)
                {
                    // Timeout or broken pipe: drop the client so the main thread is never blocked again.
                    CloseClientLocked();
                    return false;
                }
            }
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch (Exception) { }
            lock (_clientLock) CloseClientLocked();
            if (_thread != null && _thread != Thread.CurrentThread) _thread.Join(1000);
            _thread = null;
            _listener = null;
        }

        public void Dispose() => Stop();

        void Serve()
        {
            var buffer = new byte[8192];
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    return;   // listener stopped
                }

                client.NoDelay = true;
                client.SendTimeout = SendTimeoutMs;
                NetworkStream stream;
                lock (_clientLock)
                {
                    _client = client;
                    _stream = stream = client.GetStream();
                }
                _signals.Enqueue(BridgeSignal.Connected);

                var framer = new LineFramer();
                try
                {
                    int n;
                    while (_running && (n = stream.Read(buffer, 0, buffer.Length)) > 0)
                        foreach (var line in framer.Push(buffer, 0, n))
                            _inbound.Enqueue(line);
                }
                catch (Exception)
                {
                    // Closed by Stop/TrySend, reset by the runner, or a line over the size limit.
                }

                lock (_clientLock)
                {
                    if (_client == client) CloseClientLocked();
                }
                _signals.Enqueue(BridgeSignal.Disconnected);
            }
        }

        void CloseClientLocked()
        {
            try { _client?.Close(); } catch (Exception) { }
            _client = null;
            _stream = null;
        }
    }
}
```

- [ ] **Step 4: Chạy test để thấy pass**

Run: `bash Tools/unity-tests.sh`
Expected: `result="Passed"`, `failed="0"`. `ClientThatNeverReadsCannotFreezeTheSender` mất khoảng 2–4 giây.

Nếu test đó fail vì vòng lặp gửi hết 4000 lần mà không lỗi (bộ đệm loopback quá lớn), tăng giới hạn vòng lặp lên 20000 chứ không bỏ test. Đây là điều kiện bắt buộc trong Review Focus mục 3.

- [ ] **Step 5: Commit**

```bash
git add Packages
git commit -m "feat(editor): loopback TCP bridge with send timeout and port fallback"
```

---

### Task 10: Controller Play mode, settings và cửa sổ AgentPlay

Phần này là code nối vào API Editor, không có unit test riêng. Hành vi được kiểm bằng E2E ở Task 12. Task này chỉ cần biên dịch sạch và giữ toàn bộ test cũ pass.

**Files:**
- Create: `Packages/com.agentplay.core/Editor/AgentPlaySettings.cs`
- Create: `Packages/com.agentplay.core/Editor/AgentPlaySessionController.cs`
- Create: `Packages/com.agentplay.core/Editor/AgentPlayWindow.cs`

**Interfaces:**
- Consumes: `TypeScanner`, `AgentRegistry`, `SnapshotBuilder`, `UnityInstanceResolver` (Task 4–5); `AgentEvents` (Task 6); `TracePaths`, `TraceWriter` (Task 7); `SessionCore`, `ISessionHost` (Task 8); `BridgeServer` (Task 9).
- Produces:
  - `AgentPlaySettings.Enabled : bool` (mặc định true), `AgentPlaySettings.SessionTag : string` (`debug` hoặc `play`, mặc định `debug`), `AgentPlaySettings.Tags : string[]`.
  - `AgentPlaySessionController` (static, `[InitializeOnLoad]`) với trạng thái đọc được: `IsActive`, `Port`, `IsRunnerConnected`, `Mode`, `EventCount`, `RecentEvents`, `TracePath`, `RegistryErrors`.
  - Log Console cố định mà Task 12 kiểm: `[AgentPlay] Bridge listening on 127.0.0.1:<port>` và `[AgentPlay] Registry: <n> observes, <m> actions in <ms> ms`.

- [ ] **Step 1: Viết AgentPlaySettings**

`Packages/com.agentplay.core/Editor/AgentPlaySettings.cs`:

```csharp
using UnityEditor;
using UnityEngine;

namespace AgentPlay.Editor
{
    /// <summary>Per-project Editor preferences.</summary>
    public static class AgentPlaySettings
    {
        public static readonly string[] Tags = { "debug", "play" };

        static string Key(string name) => "AgentPlay:" + Application.dataPath + ":" + name;

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(Key("Enabled"), true);
            set => EditorPrefs.SetBool(Key("Enabled"), value);
        }

        public static string SessionTag
        {
            get
            {
                string tag = EditorPrefs.GetString(Key("SessionTag"), "debug");
                return System.Array.IndexOf(Tags, tag) >= 0 ? tag : "debug";
            }
            set => EditorPrefs.SetString(Key("SessionTag"), value);
        }
    }
}
```

- [ ] **Step 2: Viết AgentPlaySessionController**

`Packages/com.agentplay.core/Editor/AgentPlaySessionController.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AgentPlay.Editor
{
    /// <summary>Starts a session when Play mode begins and tears it down when it ends or the domain reloads.</summary>
    [InitializeOnLoad]
    public static class AgentPlaySessionController
    {
        const double SnapshotIntervalSeconds = 2.0;
        const int SnapshotWarnBytes = 4096;
        const string Who = "human";   // M1: the agent does not play yet

        static BridgeServer _server;
        static SessionCore _core;
        static TraceWriter _trace;
        static RegistryModel _model;
        static UnityInstanceResolver _resolver;
        static readonly HashSet<string> Warned = new HashSet<string>();
        static readonly UnityHost Host = new UnityHost();

        public static bool IsActive { get; private set; }
        public static int Port => _server?.Port ?? 0;
        public static bool IsRunnerConnected => _server != null && _server.IsClientConnected;
        public static string Mode => _core?.Mode;
        public static int EventCount => _core?.EventCount ?? 0;
        public static IReadOnlyList<string> RecentEvents => _core?.RecentEvents ?? (IReadOnlyList<string>)Array.Empty<string>();
        public static string TracePath { get; private set; }
        public static IReadOnlyList<string> RegistryErrors => _model?.Errors ?? (IReadOnlyList<string>)Array.Empty<string>();

        static AgentPlaySessionController()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += End;
            EditorApplication.quitting += End;
            EditorApplication.update += Tick;
            if (EditorApplication.isPlaying) Begin();   // domain reloaded while playing
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode) Begin();
            else if (change == PlayModeStateChange.ExitingPlayMode) End();
        }

        static void Begin()
        {
            if (IsActive || !AgentPlaySettings.Enabled) return;

            var sw = Stopwatch.StartNew();
            _model = AgentRegistry.Build(TypeScanner.FindAnnotatedTypes());
            sw.Stop();
            Debug.Log("[AgentPlay] Registry: " + _model.Observes.Count + " observes, " + _model.Actions.Count + " actions in " + sw.ElapsedMilliseconds + " ms");
            foreach (var error in _model.Errors) Debug.LogError("[AgentPlay] " + error);

            _resolver = new UnityInstanceResolver();
            Warned.Clear();

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string tag = AgentPlaySettings.SessionTag;
            TracePath = TracePaths.Build(projectRoot, DateTime.Now, Who, tag, File.Exists);
            _trace = TracePaths.Open(TracePath);
            var hello = AgentRegistry.ToHello(_model, Application.productName);
            _trace.WriteHeader(hello.Game, hello.Version, Who, tag, DateTime.UtcNow);

            _server = new BridgeServer();
            Host.Server = _server;
            _core = new SessionCore(hello, Snapshot, Host, _trace, SnapshotIntervalSeconds);

            if (_server.Start())
                Debug.Log("[AgentPlay] Bridge listening on 127.0.0.1:" + _server.Port);
            else
                Debug.LogError("[AgentPlay] No free port in " + BridgeServer.FirstPort + "-" + BridgeServer.LastPort + "; runner cannot connect");

            AgentEvents.Emitted -= OnEvent;
            AgentEvents.Emitted += OnEvent;
            IsActive = true;
        }

        static void End()
        {
            if (!IsActive) return;
            AgentEvents.Emitted -= OnEvent;
            _server?.Dispose();
            _trace?.Dispose();
            _server = null;
            Host.Server = null;
            _trace = null;
            _core = null;
            IsActive = false;
        }

        static void Tick()
        {
            if (!IsActive) return;
            while (_server.TryGetSignal(out var signal))
                if (signal == BridgeSignal.Connected) _core.OnConnected();
            while (_server.TryReceive(out var line))
                _core.OnLine(line);
            _core.Tick(EditorApplication.isPlaying && !EditorApplication.isPaused);
        }

        static void OnEvent(AgentEventRecord record) => _core?.OnEvent(record);

        static JObject Snapshot()
        {
            var s = SnapshotBuilder.Build(_model, _resolver.Resolve, WarnOnce);
            if (!Warned.Contains("size") && s.ToString(Formatting.None).Length > SnapshotWarnBytes)
            {
                Warned.Add("size");
                Debug.LogWarning("[AgentPlay] Snapshot is larger than " + SnapshotWarnBytes + " bytes; consider observing fewer values");
            }
            return s;
        }

        static void WarnOnce(string message)
        {
            if (Warned.Add(message)) Debug.LogWarning("[AgentPlay] " + message);
        }

        sealed class UnityHost : ISessionHost
        {
            public BridgeServer Server;
            public double GameTime => Time.timeAsDouble;
            public long Frame => Time.frameCount;
            public void SetPaused(bool paused) => EditorApplication.isPaused = paused;
            public void Send(string line) => Server?.TrySend(line);
        }
    }
}
```

- [ ] **Step 3: Viết cửa sổ AgentPlay**

`Packages/com.agentplay.core/Editor/AgentPlayWindow.cs`:

```csharp
using UnityEditor;
using UnityEngine;

namespace AgentPlay.Editor
{
    public sealed class AgentPlayWindow : EditorWindow
    {
        Vector2 _scroll;

        [MenuItem("Window/AgentPlay")]
        static void Open() => GetWindow<AgentPlayWindow>("AgentPlay");

        void OnInspectorUpdate() => Repaint();

        void OnGUI()
        {
            bool playing = EditorApplication.isPlaying;

            using (new EditorGUI.DisabledScope(playing))
            {
                AgentPlaySettings.Enabled = EditorGUILayout.Toggle("Enabled", AgentPlaySettings.Enabled);
                int index = System.Array.IndexOf(AgentPlaySettings.Tags, AgentPlaySettings.SessionTag);
                int picked = EditorGUILayout.Popup(new GUIContent("Session tag", "play = learn from this session, debug = do not learn"),
                    index, AgentPlaySettings.Tags);
                if (picked != index) AgentPlaySettings.SessionTag = AgentPlaySettings.Tags[picked];
            }
            if (playing) EditorGUILayout.HelpBox("Settings apply from the next Play session.", MessageType.None);

            EditorGUILayout.Space();
            string status = !playing ? "Not playing"
                : !AgentPlaySessionController.IsActive ? "Disabled"
                : AgentPlaySessionController.IsRunnerConnected ? "Runner connected on port " + AgentPlaySessionController.Port
                : "Listening on port " + AgentPlaySessionController.Port;
            EditorGUILayout.LabelField("Status", status);
            EditorGUILayout.LabelField("Mode", AgentPlaySessionController.Mode ?? "-");
            EditorGUILayout.LabelField("Events", AgentPlaySessionController.EventCount.ToString());
            EditorGUILayout.LabelField("Trace", AgentPlaySessionController.TracePath ?? "-");

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var error in AgentPlaySessionController.RegistryErrors)
                EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.LabelField("Recent events", EditorStyles.boldLabel);
            foreach (var line in AgentPlaySessionController.RecentEvents)
                EditorGUILayout.LabelField(line);
            EditorGUILayout.EndScrollView();
        }
    }
}
```

- [ ] **Step 4: Biên dịch và chạy lại toàn bộ test**

Run: `bash Tools/unity-tests.sh`
Expected: `result="Passed"`, `failed="0"`, không có `error CS` trong `Testbed/Logs/editmode.log`.

- [ ] **Step 5: Commit**

```bash
git add Packages
git commit -m "feat(editor): Play-mode session controller, per-project settings and AgentPlay window"
```

---

### Task 11: Runner .NET

**Files:**
- Create: `Runner/AgentPlay.Runner.csproj` (qua `dotnet new console`), `Runner/Program.cs`, `Runner/RunnerOptions.cs`, `Runner/RunnerSession.cs`, `Runner/BridgeClient.cs`
- Test: `Runner.Tests/RunnerOptionsTests.cs`, `Runner.Tests/RunnerSessionTests.cs`

**Interfaces:**
- Consumes: toàn bộ Contract (Task 1–2).
- Produces:
  - `RunnerOptions { string Host; int FirstPort; int LastPort; string Mode; bool AutoResume; double ResumeSeconds; int ThinkMs; double IdleExitSeconds; }`; `RunnerOptions.Parse(string[] args)` ném `ArgumentException`; `RunnerOptions.Usage`.
  - `Outgoing { string Type; object Body; int DelayMs; }`.
  - `RunnerSession(RunnerOptions options, Action<string> print)`: `IReadOnlyList<Outgoing> Handle(Envelope env)`, `bool HasHello`, `HelloMessage Hello`, `void Reset()`.
  - `BridgeClient.TryConnectAsync(string host, int firstPort, int lastPort, CancellationToken ct) : Task<BridgeClient>` (null nếu không cổng nào nhận), `ReadLinesAsync(CancellationToken)`, `SendAsync(string line, CancellationToken)`, `Port`.
  - Định dạng log mà Task 12 kiểm: mỗi dòng `[HH:mm:ss.fff] <message>`. Các message: `connected to 127.0.0.1:<port>`, `hello: game=<g> version=<v> observes=<n> actions=<m>`, `  observe <path> : <type>`, `  action <name>(<p:type, ...>)[ [safe]]`, `event t=<0.00> frame=<n> <name>[ (important)] payload=<json>`, `paused t=<0.00> reason=<r>`, `state t=<0.00> <json>`, `unity error: <msg>`.

Tham số dòng lệnh:
- `--mode turn_based|realtime`: gửi `set_mode` sau khi nhận `hello`.
- `--auto-resume`: trả `resume` sau mỗi `paused`.
- `--resume-seconds <n>`: mặc định 3.
- `--think-ms <n>`: mặc định 0, mô phỏng thời gian model suy nghĩ trước khi `resume`.
- `--ports <a>-<b>`: mặc định 47800-47809.
- `--idle-exit <seconds>`: thoát khi đã từng kết nối rồi mất kết nối liên tục quá N giây. 0 là không bao giờ.
- Bàn phím khi chạy: `s` Enter gửi `get_state`, `r` Enter gửi `resume` (budget 0), `q` Enter thoát.

- [ ] **Step 1: Tạo project**

```bash
cd /e/Unity-WorkSpace/AgentPlay
dotnet new console -n AgentPlay.Runner -o Runner -f net9.0
dotnet sln AgentPlay.sln add Runner/AgentPlay.Runner.csproj
dotnet add Runner/AgentPlay.Runner.csproj reference Contract/AgentPlay.Contract.csproj
dotnet add Runner.Tests/AgentPlay.Runner.Tests.csproj reference Runner/AgentPlay.Runner.csproj
```

Mở `Runner/AgentPlay.Runner.csproj`. Nếu template có `<ImplicitUsings>enable</ImplicitUsings>` hay `<Nullable>enable</Nullable>` thì giữ nguyên, code dưới đây không xung đột.

- [ ] **Step 2: Viết test thất bại**

`Runner.Tests/RunnerOptionsTests.cs`:

```csharp
using System;
using AgentPlay.Runner;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class RunnerOptionsTests
    {
        [Fact]
        public void DefaultsWithoutArgs()
        {
            var o = RunnerOptions.Parse(Array.Empty<string>());
            Assert.Equal("127.0.0.1", o.Host);
            Assert.Equal(47800, o.FirstPort);
            Assert.Equal(47809, o.LastPort);
            Assert.Null(o.Mode);
            Assert.False(o.AutoResume);
            Assert.Equal(3.0, o.ResumeSeconds);
            Assert.Equal(0, o.ThinkMs);
            Assert.Equal(0.0, o.IdleExitSeconds);
        }

        [Fact]
        public void ParsesEveryOption()
        {
            var o = RunnerOptions.Parse(new[]
            {
                "--mode", "turn_based", "--auto-resume", "--resume-seconds", "2.5",
                "--think-ms", "3000", "--ports", "47850-47852", "--idle-exit", "15",
            });
            Assert.Equal("turn_based", o.Mode);
            Assert.True(o.AutoResume);
            Assert.Equal(2.5, o.ResumeSeconds);
            Assert.Equal(3000, o.ThinkMs);
            Assert.Equal(47850, o.FirstPort);
            Assert.Equal(47852, o.LastPort);
            Assert.Equal(15.0, o.IdleExitSeconds);
        }

        [Theory]
        [InlineData("--mode fast")]
        [InlineData("--think-ms -1")]
        [InlineData("--ports 47809-47800")]
        [InlineData("--ports abc")]
        [InlineData("--resume-seconds")]
        [InlineData("--unknown")]
        public void RejectsInvalidArguments(string commandLine)
        {
            Assert.Throws<ArgumentException>(() => RunnerOptions.Parse(commandLine.Split(' ')));
        }
    }
}
```

`Runner.Tests/RunnerSessionTests.cs`:

```csharp
using System.Collections.Generic;
using AgentPlay.Contract;
using AgentPlay.Runner;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class RunnerSessionTests
    {
        readonly List<string> _printed = new List<string>();

        RunnerSession Session(params string[] args) => new RunnerSession(RunnerOptions.Parse(args), _printed.Add);

        static Envelope Env<T>(string type, T body) => MessageCodec.Decode(MessageCodec.Encode(type, 1, body));

        static HelloMessage Hello()
        {
            var h = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "AgentPlay Testbed", Version = "m1" };
            h.Observes.Add(new ObserveInfo { Path = "walker.x", ValueType = "float" });
            h.Actions.Add(new ActionInfo { Name = "wait", Safe = true });
            h.Actions.Add(new ActionInfo { Name = "goto", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = "string" } } });
            return h;
        }

        [Fact]
        public void HelloIsPrintedAndTriggersSetModeWhenRequested()
        {
            var s = Session("--mode", "turn_based");
            var outgoing = s.Handle(Env(MessageTypes.Hello, Hello()));

            Assert.True(s.HasHello);
            Assert.Contains("hello: game=AgentPlay Testbed version=m1 observes=1 actions=2", _printed);
            Assert.Contains("  observe walker.x : float", _printed);
            Assert.Contains("  action wait() [safe]", _printed);
            Assert.Contains("  action goto(zone:string)", _printed);
            var msg = Assert.Single(outgoing);
            Assert.Equal(MessageTypes.SetMode, msg.Type);
            Assert.Equal("turn_based", ((SetModeMessage)msg.Body).Mode);
        }

        [Fact]
        public void HelloWithoutModeSendsNothing()
        {
            Assert.Empty(Session().Handle(Env(MessageTypes.Hello, Hello())));
        }

        [Fact]
        public void PausedTriggersDelayedResumeWhenAutoResumeIsOn()
        {
            var s = Session("--auto-resume", "--resume-seconds", "2", "--think-ms", "3000");
            var outgoing = s.Handle(Env(MessageTypes.Paused, new PausedMessage { Reason = "budget", T = 12.346 }));

            Assert.Contains("paused t=12.35 reason=budget", _printed);
            var msg = Assert.Single(outgoing);
            Assert.Equal(MessageTypes.Resume, msg.Type);
            Assert.Equal(2.0, ((ResumeMessage)msg.Body).MaxGameSeconds);
            Assert.Equal(3000, msg.DelayMs);
        }

        [Fact]
        public void PausedWithoutAutoResumeOnlyPrints()
        {
            Assert.Empty(Session().Handle(Env(MessageTypes.Paused, new PausedMessage { Reason = "event", T = 1 })));
        }

        [Fact]
        public void EventStateAndErrorArePrinted()
        {
            var s = Session();
            s.Handle(Env(MessageTypes.Event, new EventMessage { Name = "walker.turned", T = 2.5, Frame = 150, Important = true, Payload = new JObject { ["laps"] = 1 } }));
            s.Handle(Env(MessageTypes.State, new StateMessage { T = 3, State = new JObject { ["walker.x"] = 1.5 } }));
            s.Handle(Env(MessageTypes.Error, new ErrorMessage { Message = "Unknown mode 'fast'" }));

            Assert.Contains("event t=2.50 frame=150 walker.turned (important) payload={\"laps\":1}", _printed);
            Assert.Contains("state t=3.00 {\"walker.x\":1.5}", _printed);
            Assert.Contains("unity error: Unknown mode 'fast'", _printed);
        }

        [Fact]
        public void ResetForgetsHello()
        {
            var s = Session();
            s.Handle(Env(MessageTypes.Hello, Hello()));
            s.Reset();
            Assert.False(s.HasHello);
            Assert.Null(s.Hello);
        }
    }
}
```

- [ ] **Step 3: Chạy test để thấy thất bại**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: build FAIL với `The type or namespace name 'RunnerOptions' could not be found`.

- [ ] **Step 4: Viết code**

`Runner/RunnerOptions.cs`:

```csharp
using System;
using System.Globalization;
using AgentPlay.Contract;

namespace AgentPlay.Runner
{
    public sealed class RunnerOptions
    {
        public const string Usage =
            "Usage: AgentPlay.Runner [--mode turn_based|realtime] [--auto-resume] [--resume-seconds N] " +
            "[--think-ms N] [--ports A-B] [--idle-exit SECONDS]";

        public string Host = "127.0.0.1";
        public int FirstPort = 47800;
        public int LastPort = 47809;
        public string Mode;
        public bool AutoResume;
        public double ResumeSeconds = 3;
        public int ThinkMs;
        public double IdleExitSeconds;

        public static RunnerOptions Parse(string[] args)
        {
            var o = new RunnerOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
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

`Runner/RunnerSession.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AgentPlay.Contract;
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

    /// <summary>Reacts to Unity messages. Pure: printing and sending are done by the caller.</summary>
    public sealed class RunnerSession
    {
        readonly RunnerOptions _options;
        readonly Action<string> _print;

        public bool HasHello { get; private set; }
        public HelloMessage Hello { get; private set; }

        public RunnerSession(RunnerOptions options, Action<string> print)
        {
            _options = options;
            _print = print;
        }

        public void Reset()
        {
            HasHello = false;
            Hello = null;
        }

        public IReadOnlyList<Outgoing> Handle(Envelope env)
        {
            var outgoing = new List<Outgoing>();
            switch (env.Type)
            {
                case MessageTypes.Hello:
                    Hello = env.As<HelloMessage>();
                    HasHello = true;
                    if (Hello.Protocol != ProtocolVersion.Current)
                        _print("WARNING: Unity speaks protocol " + Hello.Protocol + ", runner speaks " + ProtocolVersion.Current);
                    _print("hello: game=" + Hello.Game + " version=" + Hello.Version + " observes=" + Hello.Observes.Count + " actions=" + Hello.Actions.Count);
                    foreach (var o in Hello.Observes)
                        _print("  observe " + o.Path + " : " + o.ValueType);
                    foreach (var a in Hello.Actions)
                        _print("  action " + a.Name + "(" + string.Join(", ", a.Params.Select(p => p.Name + ":" + p.ValueType)) + ")" + (a.Safe ? " [safe]" : ""));
                    if (_options.Mode != null)
                        outgoing.Add(new Outgoing { Type = MessageTypes.SetMode, Body = new SetModeMessage { Mode = _options.Mode } });
                    break;

                case MessageTypes.Event:
                    var e = env.As<EventMessage>();
                    _print("event t=" + Fmt(e.T) + " frame=" + e.Frame + " " + e.Name + (e.Important ? " (important)" : "") + " payload=" + Compact(e.Payload));
                    break;

                case MessageTypes.Paused:
                    var p = env.As<PausedMessage>();
                    _print("paused t=" + Fmt(p.T) + " reason=" + p.Reason);
                    if (_options.AutoResume)
                        outgoing.Add(new Outgoing
                        {
                            Type = MessageTypes.Resume,
                            Body = new ResumeMessage { MaxGameSeconds = _options.ResumeSeconds },
                            DelayMs = _options.ThinkMs,
                        });
                    break;

                case MessageTypes.State:
                    var s = env.As<StateMessage>();
                    _print("state t=" + Fmt(s.T) + " " + Compact(s.State));
                    break;

                case MessageTypes.Error:
                    _print("unity error: " + env.As<ErrorMessage>().Message);
                    break;

                default:
                    _print("unknown message '" + env.Type + "'");
                    break;
            }
            return outgoing;
        }

        static string Fmt(double d) => d.ToString("0.00", CultureInfo.InvariantCulture);

        static string Compact(JObject o) => o == null ? "null" : o.ToString(Formatting.None);
    }
}
```

`Runner/BridgeClient.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgentPlay.Contract;

namespace AgentPlay.Runner
{
    public sealed class BridgeClient : IDisposable
    {
        readonly TcpClient _client;
        readonly NetworkStream _stream;
        readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        public int Port { get; }

        BridgeClient(TcpClient client, int port)
        {
            _client = client;
            _client.NoDelay = true;
            _stream = client.GetStream();
            Port = port;
        }

        /// <summary>Connects to the first port in the range that accepts; null when none does.</summary>
        public static async Task<BridgeClient> TryConnectAsync(string host, int firstPort, int lastPort, CancellationToken ct)
        {
            for (int port = firstPort; port <= lastPort; port++)
            {
                var client = new TcpClient();
                try
                {
                    await client.ConnectAsync(host, port, ct);
                    return new BridgeClient(client, port);
                }
                catch (SocketException)
                {
                    client.Dispose();
                }
            }
            return null;
        }

        public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken ct)
        {
            var framer = new LineFramer();
            var buffer = new byte[8192];
            while (true)
            {
                int n = 0;
                bool broken = false;
                try
                {
                    n = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                }
                catch (IOException)
                {
                    broken = true;
                }
                catch (ObjectDisposedException)
                {
                    broken = true;
                }
                if (broken || n == 0) yield break;
                foreach (var line in framer.Push(buffer, 0, n))
                    yield return line;
            }
        }

        public async Task SendAsync(string line, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            await _writeLock.WaitAsync(ct);
            try
            {
                await _stream.WriteAsync(bytes.AsMemory(0, bytes.Length), ct);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Dispose()
        {
            _client.Dispose();
            _writeLock.Dispose();
        }
    }
}
```

Ghi đè `Runner/Program.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AgentPlay.Contract;

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

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            _ = Task.Run(() => ReadKeyboard(cts));

            var session = new RunnerSession(options, Log);
            var idle = Stopwatch.StartNew();
            bool everConnected = false;
            Log("runner started; looking for Unity on " + options.Host + ":" + options.FirstPort + "-" + options.LastPort);

            while (!cts.IsCancellationRequested)
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
                        foreach (var msg in session.Handle(env))
                        {
                            if (msg.DelayMs > 0) await Task.Delay(msg.DelayMs, cts.Token);
                            await client.SendAsync(MessageCodec.Encode(msg.Type, Seq.Next(), msg.Body), cts.Token);
                        }
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
                if (!cts.IsCancellationRequested) Log("disconnected; waiting for Unity Play mode");
            }
            return 0;
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
        }

        static void Log(string message)
        {
            lock (PrintLock)
                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "] " + message);
        }
    }
}
```

Ghi chú: `MessageCodec.Encode(type, seq, (object)null)` ép kiểu để gọi overload generic. Với body null, overload đó trả về object chỉ có `type` và `seq`.

- [ ] **Step 5: Chạy test để thấy pass**

Run: `cd /e/Unity-WorkSpace/AgentPlay && dotnet test AgentPlay.sln`
Expected: PASS, `Failed: 0`.

- [ ] **Step 6: Kiểm tay runner không có Unity**

Run: `cd /e/Unity-WorkSpace/AgentPlay && timeout 4 dotnet run --project Runner -- --mode fast; echo "exit=$?"`
Expected: in `Unknown mode: fast` và dòng `Usage`, `exit=2`.

Run: `timeout 3 dotnet run --project Runner -- --idle-exit 1; echo "exit=$?"`
Expected: in `runner started; looking for Unity on 127.0.0.1:47800-47809`. Vì chưa từng kết nối nên không thoát theo `--idle-exit`, bị `timeout` dừng (`exit=124`).

- [ ] **Step 7: Commit**

```bash
git add AgentPlay.sln Runner Runner.Tests
git commit -m "feat(runner): console runner with reconnect, auto-resume and think-time simulation"
```

---

### Task 12: Demo Testbed, E2E tự động, cập nhật spec và ghi kết quả kiểm chứng

Kiểm chứng các tiêu chí xong của M1 và rủi ro 1, 2, 5 của spec.

**Files:**
- Create: `Testbed/Assets/Demo/DemoWalker.cs`
- Create: `Testbed/Assets/Editor/AutoPlay.cs`
- Create: `Tools/e2e-m1.sh`, `Tools/verify_m1.py`
- Create: `docs/notes/m1-verification.md`
- Modify: `docs/superpowers/specs/2026-10-11-agentplay-design.md` (mục 3.1, 4, 5)

**Interfaces:**
- Consumes: toàn bộ package (Task 3–10), runner (Task 11) và các dòng log cố định đã nêu ở Task 10 và Task 11.
- Produces: `bash Tools/e2e-m1.sh` trả exit 0 khi mọi kiểm tra M1 pass.

- [ ] **Step 1: Viết demo**

`Testbed/Assets/Demo/DemoWalker.cs`:

```csharp
using AgentPlay;
using UnityEngine;

/// <summary>A cube walking back and forth. Every turn emits an event; every second turn is important.</summary>
[AgentGame("AgentPlay Testbed", Version = "m1")]
public class DemoWalker : MonoBehaviour
{
    public float speed = 2f;
    public float halfWidth = 5f;
    int _direction = 1;

    [AgentObserve("walker.x", "Walker x position")] public float X => transform.position.x;
    [AgentObserve("walker.laps", "Number of turns so far")] public int Laps { get; private set; }
    [AgentObserve("walker.pos")] public Vector3 Pos => transform.position;

    [AgentAction("wait", "Do nothing", Safe = true)]
    public AgentActionStatus Wait() => AgentActionStatus.Done;

    void Update()
    {
        var p = transform.position;
        p.x += _direction * speed * Time.deltaTime;
        if (Mathf.Abs(p.x) >= halfWidth)
        {
            p.x = Mathf.Clamp(p.x, -halfWidth, halfWidth);
            _direction = -_direction;
            Laps++;
            AgentEvents.Emit("walker.turned", new { laps = Laps, at = p }, important: Laps % 2 == 0);
        }
        transform.position = p;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        if (FindFirstObjectByType<DemoWalker>() != null) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "DemoWalker";
        go.AddComponent<DemoWalker>();
    }
}
```

`Testbed/Assets/Editor/AutoPlay.cs`:

```csharp
using System;
using UnityEditor;
using UnityEngine;

namespace AgentPlayTestbed
{
    /// <summary>
    /// Run with: Unity.exe -projectPath Testbed -executeMethod AgentPlayTestbed.AutoPlay.Run
    /// Enters Play mode, forces one domain reload while playing, then quits after AGENTPLAY_AUTOPLAY_SECONDS.
    /// </summary>
    [InitializeOnLoad]
    public static class AutoPlay
    {
        const string DeadlineKey = "AgentPlayTestbed.AutoPlay.Deadline";
        const string ReloadKey = "AgentPlayTestbed.AutoPlay.ReloadAt";

        static AutoPlay()
        {
            EditorApplication.update += Watch;
        }

        public static void Run()
        {
            int seconds = 45;
            int.TryParse(Environment.GetEnvironmentVariable("AGENTPLAY_AUTOPLAY_SECONDS"), out seconds);
            if (seconds <= 0) seconds = 45;
            double now = EditorApplication.timeSinceStartup;
            SessionState.SetFloat(DeadlineKey, (float)(now + seconds));
            SessionState.SetFloat(ReloadKey, (float)(now + seconds / 2.0));
            Debug.Log("[Testbed] AutoPlay for " + seconds + " s");
            EditorApplication.EnterPlaymode();
        }

        static void Watch()
        {
            double now = EditorApplication.timeSinceStartup;

            float reloadAt = SessionState.GetFloat(ReloadKey, 0f);
            if (reloadAt > 0f && now >= reloadAt && EditorApplication.isPlaying)
            {
                SessionState.EraseFloat(ReloadKey);
                Debug.Log("[Testbed] Requesting a domain reload during Play mode");
                EditorUtility.RequestScriptReload();
            }

            float deadline = SessionState.GetFloat(DeadlineKey, 0f);
            if (deadline <= 0f || now < deadline) return;
            SessionState.EraseFloat(DeadlineKey);
            Debug.Log("[Testbed] AutoPlay deadline reached; quitting");
            EditorApplication.isPaused = false;
            EditorApplication.ExitPlaymode();
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
    }
}
```

- [ ] **Step 2: Viết script E2E và kiểm tra**

`Tools/e2e-m1.sh`:

```bash
#!/usr/bin/env bash
# End-to-end check for M1: runner + Testbed in turn-based mode with simulated think time.
# Testbed must not be open in another Editor. Opens a Unity window for about a minute.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/e/6000.0.77f1/Editor/Unity.exe}"
LOGS="$ROOT/Testbed/Logs"
RESUME_SECONDS=2
THINK_MS=3000
mkdir -p "$LOGS"
rm -rf "$ROOT/Testbed/AgentPlay/Traces" "$LOGS/runner.log" "$LOGS/autoplay.log"

dotnet build "$ROOT/Runner/AgentPlay.Runner.csproj" -c Debug -v q || exit 1
dotnet "$ROOT/Runner/bin/Debug/net9.0/AgentPlay.Runner.dll" \
  --mode turn_based --auto-resume --resume-seconds "$RESUME_SECONDS" --think-ms "$THINK_MS" --idle-exit 15 \
  > "$LOGS/runner.log" 2>&1 < /dev/null &
RUNNER_PID=$!

AGENTPLAY_AUTOPLAY_SECONDS=45 "$UNITY_EXE" -projectPath "$(cygpath -m "$ROOT/Testbed")" \
  -executeMethod AgentPlayTestbed.AutoPlay.Run -logFile "$(cygpath -m "$LOGS/autoplay.log")"
echo "Unity exited with $?"

wait "$RUNNER_PID"
echo "Runner exited with $?"
python -I "$ROOT/Tools/verify_m1.py" "$ROOT" "$RESUME_SECONDS" "$THINK_MS"
```

`Tools/verify_m1.py`:

```python
"""Checks the logs and traces produced by Tools/e2e-m1.sh. Usage: verify_m1.py ROOT RESUME_SECONDS THINK_MS"""
import json
import pathlib
import re
import sys

root = pathlib.Path(sys.argv[1])
resume_seconds = float(sys.argv[2])
think_seconds = int(sys.argv[3]) / 1000.0
logs = root / "Testbed" / "Logs"
runner_log = (logs / "runner.log").read_text(encoding="utf-8", errors="replace")
unity_log = (logs / "autoplay.log").read_text(encoding="utf-8", errors="replace")
failures = []


def check(ok, message):
    print(("PASS " if ok else "FAIL ") + message)
    if not ok:
        failures.append(message)


# Handshake and events
check("hello: game=AgentPlay Testbed version=m1" in runner_log, "runner received hello from the Testbed")
check("  observe walker.x : float" in runner_log, "hello lists observe walker.x")
check("  observe walker.pos : vector3" in runner_log, "hello lists observe walker.pos")
check("  action wait() [safe]" in runner_log, "hello lists the safe action")
check(re.search(r"event t=\S+ frame=\d+ walker\.turned", runner_log) is not None, "runner received walker.turned events")
check('"at":[' in runner_log, "event payload serialized Vector3 as an array")
check("unity error:" not in runner_log, "Unity reported no protocol errors")

# Bridge lifecycle (spec risks 1 and 2)
ports = re.findall(r"\[AgentPlay\] Bridge listening on 127\.0\.0\.1:(\d+)", unity_log)
check(len(ports) >= 1 and all(p == "47800" for p in ports), "bridge always listened on 47800 (saw %s)" % ports)
check("SocketException" not in unity_log, "no SocketException in the Unity log")
check("[AgentPlay] Registry: 3 observes, 1 actions" in unity_log, "registry found the demo's 3 observes and 1 action")
reload_requested = "[Testbed] Requesting a domain reload during Play mode" in unity_log
print("INFO domain reload requested during Play: %s; bridge starts seen: %d" % (reload_requested, len(ports)))

# Turn-based: game time between pauses stays within the budget while wall time covers the think time
# No "$" anchor: the runner log may end lines with \r\n on Windows.
pattern = re.compile(r"^\[(\d\d):(\d\d):(\d\d)\.(\d{3})\] (connected to \S+|paused t=([\d.]+) reason=(\w+))", re.M)
gaps = []
previous = None
for m in pattern.finditer(runner_log):
    wall = int(m[1]) * 3600 + int(m[2]) * 60 + int(m[3]) + int(m[4]) / 1000.0
    if m[5].startswith("connected"):
        previous = None
        continue
    t = float(m[6])
    if previous is not None:
        gaps.append((t - previous[0], wall - previous[1]))
    previous = (t, wall)
game_gaps = [round(g, 2) for g, _ in gaps]
wall_gaps = [round(w, 2) for _, w in gaps]
check(len(gaps) >= 3, "at least 3 pause-to-pause intervals (saw %d)" % len(gaps))
check(all(g <= resume_seconds + 0.5 for g, _ in gaps), "game time between pauses <= %.1f s: %s" % (resume_seconds + 0.5, game_gaps))
check(all(w >= think_seconds - 0.1 for _, w in gaps), "wall time between pauses >= think time %.1f s: %s" % (think_seconds, wall_gaps))

# Traces
traces = sorted((root / "Testbed" / "AgentPlay" / "Traces").glob("*.ndjson"))
check(len(traces) >= 1, "trace file written (%d files)" % len(traces))
records = []
for path in traces:
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            records.append(json.loads(line))
check(any(r.get("type") == "header" and r.get("game") == "AgentPlay Testbed" and r.get("who") == "human" and r.get("tag") in ("debug", "play") for r in records),
      "trace header names the game, who and tag")
check(any(r.get("type") == "event" and isinstance((r.get("payload") or {}).get("at"), list) for r in records), "trace has an event with a Vector3 payload")
check(any(r.get("type") == "snapshot" and "walker.x" in (r.get("state") or {}) for r in records), "trace has a snapshot with walker.x")

print("%d check(s) failed" % len(failures) if failures else "ALL CHECKS PASSED")
sys.exit(1 if failures else 0)
```

- [ ] **Step 3: Chạy E2E**

Run (timeout 600000 ms; một cửa sổ Unity sẽ mở khoảng một phút): `cd /e/Unity-WorkSpace/AgentPlay && bash Tools/e2e-m1.sh`
Expected: dòng cuối `ALL CHECKS PASSED`.

Nếu fail, đọc `Testbed/Logs/autoplay.log` và `Testbed/Logs/runner.log`, sửa nguyên nhân ở task sở hữu đoạn code liên quan, rồi chạy lại cả `bash Tools/unity-tests.sh` và E2E. Không nới điều kiện trong `verify_m1.py` để cho pass. Ngoại lệ duy nhất: nếu dòng `INFO` cho thấy Unity hoãn domain reload tới khi thoát Play mode, thì chỉ có một dòng `Bridge listening`. Đó là hành vi hợp lệ, và check port đã chấp nhận trường hợp này.

- [ ] **Step 4: Cập nhật spec cho khớp code**

Trong `docs/superpowers/specs/2026-10-11-agentplay-design.md`:

1. Mục 3.1: thay đoạn mô tả `Contract/` bằng:
   > Mã nguồn Contract nằm trong package, ở `Packages/com.agentplay.core/Contract/` (asmdef `AgentPlay.Contract`, `noEngineReferences`). Thư mục `Contract/` ở gốc repo chỉ chứa `AgentPlay.Contract.csproj`, file này biên dịch cùng các file `.cs` đó cho runner. Hai bên luôn dùng chung một mã nguồn.
2. Mục 4: đổi `safe = false` thành `Safe = false` và `safe = true` thành `Safe = true` trong ví dụ code và trong câu quy tắc về `[AgentAction]`. Đổi `[AgentGame(name, version)]` thành `[AgentGame(name, Version = ..., VersionMember = ...)]`.
3. Mục 5, bảng giao thức: thêm hai dòng sau dòng `waiting`:
   - `| Unity → Runner | paused | Editor vừa dừng ở chế độ theo lượt; reason là mode, event hoặc budget |`
   - `| Unity → Runner | error | Message từ runner không hợp lệ hoặc không xử lý được, kèm lý do |`
4. Mục 5, sửa mô tả `resume` thành: `Chế độ theo lượt: chạy tiếp tối đa max_game_seconds giây game (0 là tới sự kiện quan trọng kế tiếp)`.
5. Mục 12, dòng M2: thêm `[AgentObserveList]` vào cột Nội dung.

- [ ] **Step 5: Ghi kết quả kiểm chứng**

Tạo `docs/notes/m1-verification.md` với nội dung lấy từ output thật của Step 3 và các lần chạy `Tools/unity-tests.sh`:

```markdown
# M1: kết quả kiểm chứng

Ngày chạy: <ngày chạy thật>

## Test
- `dotnet test AgentPlay.sln`: <số passed>/<tổng>, 0 failed
- `Tools/unity-tests.sh`: <số passed>/<tổng>, 0 failed
- `Tools/e2e-m1.sh`: ALL CHECKS PASSED

## Rủi ro trong spec mục 13
1. Pause + EditorApplication.update: <kết quả, trích dòng game_gaps và wall_gaps từ verify_m1.py>
2. Socket và domain reload: <số dòng "Bridge listening", có reload trong Play hay không, có SocketException không>
3. Nhúng Contract vào package: Contract biên dịch trong Unity và .NET từ cùng mã nguồn (Task 3)
4. SDK C# của Anthropic: chưa kiểm, thuộc M2
5. Thời gian quét attribute: <trích dòng "[AgentPlay] Registry: ... in N ms" trong autoplay.log>

## Giới hạn đã biết
- Sự kiện phát trước khi controller Editor đăng ký (ví dụ trong Awake của scene đầu tiên) bị mất.
- Runner thứ hai kết nối vào cùng cổng sẽ chờ tới khi runner đầu ngắt, và in cảnh báo sau 5 giây không nhận được hello.
- Kiểm tra hết ngân sách thời gian chạy theo EditorApplication.update, nên có thể vượt ngân sách vài frame.
```

Mỗi chỗ `<...>` phải thay bằng số liệu thật đọc từ log của lần chạy vừa rồi. Không được để nguyên dấu ngoặc nhọn khi commit.

- [ ] **Step 6: Chạy lại toàn bộ kiểm tra lần cuối**

Run:

```bash
cd /e/Unity-WorkSpace/AgentPlay
dotnet test AgentPlay.sln
bash Tools/unity-tests.sh
bash Tools/e2e-m1.sh
```

Expected: cả ba pass, dòng cuối `ALL CHECKS PASSED`.

- [ ] **Step 7: Commit**

```bash
git add Testbed/Assets Tools docs Packages
git status --short
git commit -m "test: M1 end-to-end run with Testbed demo, verification notes and spec sync"
```

Expected trong `git status` trước khi commit: không có file nào trong `Testbed/Library`, `Testbed/Logs`, `Testbed/AgentPlay`.
