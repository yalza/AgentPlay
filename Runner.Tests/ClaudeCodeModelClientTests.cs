using System.Linq;
using AgentPlay.Runner.Brains;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentPlay.Runner.Tests
{
    public class ClaudeCodeModelClientTests
    {
        static RunnerConfig Config() => new RunnerConfig { Model = "claude-opus-5-5", Effort = "high" };

        static ModelRequest Request() => new ModelRequest
        {
            System = "You play a game.",
            User = "Paused because: mode",
            Schema = new JObject { ["type"] = "object", ["additionalProperties"] = false },
        };

        const string Success =
            "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"api_error_status\":null,\"stop_reason\":\"tool_use\"," +
            "\"result\":\"{\\\"id\\\":\\\"x\\\"}\",\"structured_output\":{\"id\":\"x\",\"steps\":[]}," +
            "\"usage\":{\"input_tokens\":2,\"cache_creation_input_tokens\":4996,\"cache_read_input_tokens\":120,\"output_tokens\":80}," +
            "\"modelUsage\":{\"claude-opus-5-5\":{\"inputTokens\":2}}}";

        [Fact]
        public void ArgumentsRunHeadlessWithSchemaModelAndNoTools()
        {
            var args = ClaudeCodeModelClient.BuildArguments(Request(), Config()).ToList();
            Assert.Equal("-p", args[0]);
            Assert.Equal("claude-opus-5-5", args[args.IndexOf("--model") + 1]);
            Assert.Equal("high", args[args.IndexOf("--effort") + 1]);
            Assert.Equal("json", args[args.IndexOf("--output-format") + 1]);
            Assert.Equal("{\"type\":\"object\",\"additionalProperties\":false}", args[args.IndexOf("--json-schema") + 1]);
            Assert.Equal("You play a game.", args[args.IndexOf("--system-prompt") + 1]);
            Assert.Equal("", args[args.IndexOf("--tools") + 1]);
            Assert.Contains("--strict-mcp-config", args);
            Assert.Contains("--no-session-persistence", args);
            Assert.DoesNotContain("Paused because: mode", args);
        }

        [Fact]
        public void SuccessfulOutputGivesTheStructuredPlanAndUsage()
        {
            var reply = ClaudeCodeModelClient.ParseOutput(Success, 4200);
            Assert.Equal("{\"id\":\"x\",\"steps\":[]}", reply.Text);
            Assert.Equal("claude-opus-5-5", reply.Model);
            Assert.Equal(2, reply.InputTokens);
            Assert.Equal(80, reply.OutputTokens);
            Assert.Equal(120, reply.CacheReadTokens);
            Assert.Equal(4996, reply.CacheWriteTokens);
            Assert.Equal(4200, reply.LatencyMs);
            Assert.Equal("end_turn", reply.StopReason);
        }

        [Fact]
        public void MissingStructuredOutputFallsBackToTheResultText()
        {
            var reply = ClaudeCodeModelClient.ParseOutput(
                "{\"subtype\":\"success\",\"is_error\":false,\"result\":\"{\\\"id\\\":\\\"y\\\"}\",\"usage\":{}}", 10);
            Assert.Equal("{\"id\":\"y\"}", reply.Text);
        }

        [Fact]
        public void UsageLimitIsNotRetryable()
        {
            var e = Assert.Throws<ModelCallException>(() => ClaudeCodeModelClient.ParseOutput(
                "{\"subtype\":\"success\",\"is_error\":true,\"api_error_status\":429,\"result\":\"You've hit your session limit · resets 1am\"}", 10));
            Assert.False(e.Retryable);
            Assert.Contains("session limit", e.Message);
        }

        [Fact]
        public void ServerErrorIsRetryable()
        {
            var e = Assert.Throws<ModelCallException>(() => ClaudeCodeModelClient.ParseOutput(
                "{\"subtype\":\"error_during_execution\",\"is_error\":true,\"api_error_status\":529,\"result\":\"Overloaded\"}", 10));
            Assert.True(e.Retryable);
        }

        [Fact]
        public void AuthenticationErrorIsNotRetryable()
        {
            var e = Assert.Throws<ModelCallException>(() => ClaudeCodeModelClient.ParseOutput(
                "{\"is_error\":true,\"api_error_status\":401,\"result\":\"Invalid API key · Please run /login\"}", 10));
            Assert.False(e.Retryable);
        }

        [Fact]
        public void GarbageOutputIsRetryable()
        {
            var e = Assert.Throws<ModelCallException>(() => ClaudeCodeModelClient.ParseOutput("not json", 10));
            Assert.True(e.Retryable);
        }

        [Fact]
        public void BackendConfigDefaultsToClaudeCodeAndValidates()
        {
            Assert.Equal("claude-code", new RunnerConfig().Backend);
            var bad = new RunnerConfig { Backend = "carrier-pigeon" };
            Assert.Throws<System.ArgumentException>(() => bad.Validate());
            var o = RunnerOptions.Parse(new[] { "--brain", "opus", "--backend", "api" });
            Assert.Equal("api", o.Backend);
        }
    }
}
