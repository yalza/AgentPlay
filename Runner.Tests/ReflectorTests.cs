using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AgentPlay.Contract;
using AgentPlay.Runner.Brains;
using AgentPlay.Runner.Learning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class ReflectorTests
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

        const string PlayTrace = "20261013-100000-human-play.ndjson";
        const string DebugTrace = "20261013-100100-human-debug.ndjson";
        const string Answer =
            "{\"summary\":\"The thief hid after being spotted.\",\"lessons\":[{\"id\":\"\",\"verdict\":\"new\",\"text\":\"Hide when spotted\",\"evidence\":\"t=2\"," +
            "\"reflex\":{\"when\":\"event:guard.spotted\",\"do\":\"hide_nearest\",\"args\":{}}}]}";

        readonly List<string> _log = new List<string>();
        string _root;

        static ModelReply Reply(string text) => new ModelReply { Text = text, StopReason = "end_turn", Model = "m", LatencyMs = 10 };

        static string Trace(string tag)
        {
            var hello = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G", Version = "m3", SessionTag = tag };
            hello.Events.Add(new EventInfo { Name = "guard.spotted", Important = true, Outcome = "bad" });
            hello.Actions.Add(new ActionInfo { Name = "hide_nearest" });
            hello.Actions.Add(new ActionInfo { Name = "stop", Safe = true });
            var lines = new[]
            {
                new JObject { ["type"] = "header", ["game"] = "G", ["version"] = "m3", ["who"] = "human", ["tag"] = tag, ["hello"] = JObject.FromObject(hello, ContractJson.Serializer) },
                new JObject { ["type"] = "snapshot", ["t"] = 0.0, ["state"] = new JObject { ["player.hidden"] = false } },
                new JObject { ["type"] = "event", ["t"] = 2.0, ["name"] = "guard.spotted", ["payload"] = new JObject(), ["state"] = new JObject { ["player.hidden"] = false } },
                new JObject { ["type"] = "snapshot", ["t"] = 2.5, ["state"] = new JObject { ["player.hidden"] = true } },
            };
            return string.Join("\n", Array.ConvertAll(lines, l => l.ToString(Formatting.None))) + "\n";
        }

        string MakeProject()
        {
            _root = Path.Combine(Path.GetTempPath(), "agentplay-reflect-" + Guid.NewGuid().ToString("N"));
            var traces = Path.Combine(_root, "AgentPlay", "Traces");
            Directory.CreateDirectory(traces);
            File.WriteAllText(Path.Combine(traces, PlayTrace), Trace("play"));
            File.WriteAllText(Path.Combine(traces, DebugTrace), Trace("debug"));
            return _root;
        }

        Reflector Make(FakeClient client, int minConfirmations = 1)
        {
            var config = new RunnerConfig();
            config.Reflector.MinConfirmations = minConfirmations;
            return new Reflector(client, config, _log.Add) { RetryDelayMs = 0 };
        }

        [Fact]
        public async Task ReflectsPlayTracesOnlyAndWritesPlaybookLedgerAndInbox()
        {
            var root = MakeProject();
            var client = new FakeClient();
            client.Replies.Enqueue(_ => Reply(Answer));
            var reflector = Make(client);

            Assert.Equal(0, await reflector.RunAsync(root, null, false, CancellationToken.None));
            var request = Assert.Single(client.Requests);
            Assert.Contains("# Session", request.User);
            Assert.Contains("Game: G (version m3). Player: human. Session tag: play.", request.User);
            Assert.Contains("(no lessons yet)", request.User);
            Assert.Contains("# Game: G", request.System);
            Assert.Contains("keep the game's playbook", request.System);
            Assert.Equal("object", (string)request.Schema["type"]);

            var lesson = Assert.Single(PlaybookStore.Load(root).Lessons);
            Assert.Equal("L1", lesson.Id);
            Assert.Equal(new[] { PlayTrace }, lesson.Sources);
            Assert.Contains(PlayTrace, ReflectedLedger.Load(root));
            Assert.DoesNotContain(DebugTrace, ReflectedLedger.Load(root));
            var inbox = Assert.Single(Directory.GetFiles(Path.Combine(root, "AgentPlay", "Inbox"), "reflex-L1-*.json"));
            var proposal = JsonConvert.DeserializeObject<Reflex>(File.ReadAllText(inbox), ContractJson.Settings);
            Assert.Equal("R-L1", proposal.Id);
            Assert.Contains(_log, l => l.StartsWith("reflect done: 1 trace(s), 1 proposal(s)"));

            Assert.Equal(0, await reflector.RunAsync(root, null, false, CancellationToken.None));
            Assert.Single(client.Requests);
        }

        [Fact]
        public async Task SecondRunShowsExistingLessons()
        {
            var root = MakeProject();
            var client = new FakeClient();
            client.Replies.Enqueue(_ => Reply(Answer));
            client.Replies.Enqueue(_ => Reply("{\"summary\":\"s\",\"lessons\":[]}"));
            var reflector = Make(client, minConfirmations: 3);
            await reflector.RunAsync(root, null, false, CancellationToken.None);
            Assert.Equal(0, await reflector.RunAsync(root, null, true, CancellationToken.None));
            Assert.Contains("- L1 [confirmed 1x, contradicted 0x] Hide when spotted | reflex: when event:guard.spotted do hide_nearest()", client.Requests[1].User);
        }

        [Fact]
        public async Task InvalidAnswerIsRetriedOnceThenFails()
        {
            var root = MakeProject();
            var client = new FakeClient();
            client.Replies.Enqueue(_ => Reply("not json"));
            client.Replies.Enqueue(_ => Reply("{\"summary\":\"s\",\"lessons\":[{\"id\":\"\",\"verdict\":\"maybe\",\"text\":\"x\",\"evidence\":\"\",\"reflex\":null}]}"));
            Assert.Equal(4, await Make(client).RunAsync(root, null, false, CancellationToken.None));
            Assert.Equal(2, client.Requests.Count);
            Assert.Contains("Your previous answer was rejected", client.Requests[1].User);
            Assert.Empty(ReflectedLedger.Load(root));
        }

        [Fact]
        public async Task TransientErrorsAreRetried()
        {
            var root = MakeProject();
            var client = new FakeClient();
            client.Replies.Enqueue(_ => throw new ModelCallException("overloaded", null));
            client.Replies.Enqueue(_ => Reply(Answer));
            Assert.Equal(0, await Make(client).RunAsync(root, null, false, CancellationToken.None));
            Assert.Equal(2, client.Requests.Count);
        }

        [Fact]
        public async Task NonRetryableErrorStops()
        {
            var root = MakeProject();
            var client = new FakeClient();
            client.Replies.Enqueue(_ => throw new ModelCallException("You've hit your limit", null, retryable: false));
            Assert.Equal(4, await Make(client).RunAsync(root, null, false, CancellationToken.None));
            Assert.Single(client.Requests);
            Assert.Contains(_log, l => l.Contains("hit your limit"));
        }

        [Fact]
        public async Task CorruptPlaybookExitsTwo()
        {
            var root = MakeProject();
            File.WriteAllText(Path.Combine(root, "AgentPlay", "playbook.json"), "{");
            var client = new FakeClient();
            Assert.Equal(2, await Make(client).RunAsync(root, null, false, CancellationToken.None));
            Assert.Empty(client.Requests);
        }

        [Fact]
        public async Task MissingProjectFolderExitsTwo()
        {
            var client = new FakeClient();
            Assert.Equal(2, await Make(client).RunAsync(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N")), null, false, CancellationToken.None));
        }

        [Fact]
        public async Task ExplicitDebugTraceIsNotLearned()
        {
            var root = MakeProject();
            var client = new FakeClient();
            var path = Path.Combine(root, "AgentPlay", "Traces", DebugTrace);
            Assert.Equal(0, await Make(client).RunAsync(root, new[] { path }, false, CancellationToken.None));
            Assert.Empty(client.Requests);
            Assert.Contains(_log, l => l.Contains("skipped (tag debug)"));
        }

        [Fact]
        public async Task TraceWithoutHelloIsMarkedAndSkipped()
        {
            var root = MakeProject();
            File.WriteAllText(Path.Combine(root, "AgentPlay", "Traces", PlayTrace),
                "{\"type\":\"header\",\"game\":\"G\",\"version\":\"m2\",\"who\":\"human\",\"tag\":\"play\"}\n");
            var client = new FakeClient();
            Assert.Equal(0, await Make(client).RunAsync(root, null, false, CancellationToken.None));
            Assert.Empty(client.Requests);
            Assert.Contains(PlayTrace, ReflectedLedger.Load(root));
        }

        [Fact]
        public async Task ConcurrentRunIsRefused()
        {
            var root = MakeProject();
            var client = new FakeClient();
            using (var held = ReflectLock.TryAcquire(root))
            {
                Assert.NotNull(held);
                Assert.Equal(Reflector.ExitBusy, await Make(client).RunAsync(root, null, false, CancellationToken.None));
            }
            Assert.Empty(client.Requests);
            Assert.Contains(_log, l => l.Contains("another reflect run"));
            client.Replies.Enqueue(_ => Reply(Answer));
            Assert.Equal(0, await Make(client).RunAsync(root, null, false, CancellationToken.None));
        }

        [Fact]
        public async Task BadLedgerExitsTwo()
        {
            var root = MakeProject();
            File.WriteAllText(Path.Combine(root, "AgentPlay", "reflected.json"), "{ not a list");
            var client = new FakeClient();
            Assert.Equal(2, await Make(client).RunAsync(root, null, false, CancellationToken.None));
            Assert.Empty(client.Requests);
        }
    }
}
