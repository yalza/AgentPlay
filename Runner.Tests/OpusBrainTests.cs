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
        [InlineData(GoodPlan, "Refusal")]
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
        public async Task NonRetryableErrorStopsWithoutRetrying()
        {
            var client = new FakeClient();
            client.Replies.Enqueue(_ => throw new ModelCallException("authentication_error: x-api-key header is required", null, retryable: false));
            var ctx = Context();
            var brain = Brain(client);
            var plan = await brain.NextPlanAsync(ctx, CancellationToken.None);

            Assert.Null(plan);
            Assert.Equal(1, brain.Calls);
            Assert.Equal("model call failed: authentication_error: x-api-key header is required", ctx.StopReason);
        }

        [Fact]
        public async Task CallBudgetStopsTheBrain()
        {
            var client = new FakeClient();
            for (int i = 0; i < 5; i++) client.Replies.Enqueue(_ => throw new ModelCallException("down", null));
            var ctx = Context();
            var brain = Brain(client, maxCalls: 2);
            var plan = await brain.NextPlanAsync(ctx, CancellationToken.None);

            Assert.Null(plan);
            Assert.Equal(2, brain.Calls);
            Assert.Equal("call budget of 2 reached", ctx.StopReason);
        }

        [Fact]
        public async Task RepeatedTransientFailuresStopBeforeTheBudget()
        {
            var client = new FakeClient();
            for (int i = 0; i < 10; i++) client.Replies.Enqueue(_ => throw new ModelCallException("down", null));
            var ctx = Context();
            var brain = Brain(client, maxCalls: 40);
            var plan = await brain.NextPlanAsync(ctx, CancellationToken.None);

            Assert.Null(plan);
            Assert.Equal(OpusBrain.MaxFailuresInARow, brain.Calls);
            Assert.Equal("model call failed " + OpusBrain.MaxFailuresInARow + " times in a row: down", ctx.StopReason);
        }

        const string FourSteps = "{\"id\":\"x\",\"intent\":\"long\",\"steps\":[" +
            "{\"do\":\"goto\",\"args\":{\"zone\":\"a\"},\"until\":\"\"},{\"do\":\"goto\",\"args\":{\"zone\":\"b\"},\"until\":\"\"}," +
            "{\"do\":\"goto\",\"args\":{\"zone\":\"c\"},\"until\":\"\"},{\"do\":\"goto\",\"args\":{\"zone\":\"d\"},\"until\":\"\"}],\"rules\":[],\"timeout_s\":30}";

        static RouteDecision Fast() => new RouteDecision { Role = "fast", Model = "claude-haiku-5-5", Effort = "low", MaxSteps = 3 };

        [Fact]
        public async Task QuickPlanTooLongIsRetried()
        {
            var client = new FakeClient();
            client.Replies.Enqueue(_ => Reply(FourSteps));
            client.Replies.Enqueue(_ => Reply(GoodPlan));
            var ctx = Context();
            ctx.Route = Fast();
            var plan = await Brain(client).NextPlanAsync(ctx, CancellationToken.None);
            Assert.NotNull(plan);
            Assert.Equal(2, client.Requests.Count);
            Assert.Contains("at most 3 steps", client.Requests[1].User);
        }

        [Fact]
        public async Task RouteSetsModelEffortAndRole()
        {
            var client = new FakeClient();
            client.Replies.Enqueue(_ => Reply(GoodPlan));
            var ctx = Context();
            ctx.Route = Fast();
            await Brain(client).NextPlanAsync(ctx, CancellationToken.None);
            Assert.Equal("claude-haiku-5-5", client.Requests[0].Model);
            Assert.Equal("low", client.Requests[0].Effort);
            Assert.Equal("fast", _calls[0].Role);
        }

        [Fact]
        public async Task NoRouteKeepsTheConfiguredModel()
        {
            var client = new FakeClient();
            client.Replies.Enqueue(_ => Reply(GoodPlan));
            await Brain(client).NextPlanAsync(Context(), CancellationToken.None);
            Assert.Null(client.Requests[0].Model);
            Assert.Equal("strategy", _calls[0].Role);
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
