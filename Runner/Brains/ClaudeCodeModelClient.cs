using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner.Brains
{
    /// <summary>
    /// Asks the model through the Claude Code CLI in headless mode (claude -p), which uses the signed-in
    /// Claude subscription instead of an API key. One process per call, no tools, no saved session; the plan
    /// comes back as structured output validated against the plan schema.
    /// </summary>
    public sealed class ClaudeCodeModelClient : IModelClient
    {
        public const int TimeoutSeconds = 300;

        readonly RunnerConfig _config;
        readonly string _executable;
        readonly string _workingDirectory;

        public ClaudeCodeModelClient(RunnerConfig config, string executable = "claude")
        {
            _config = config;
            _executable = executable;
            // An empty folder, so no project CLAUDE.md or settings are picked up.
            _workingDirectory = Path.Combine(Path.GetTempPath(), "agentplay-claude-code");
            Directory.CreateDirectory(_workingDirectory);
        }

        public static IReadOnlyList<string> BuildArguments(ModelRequest request, RunnerConfig config) => new[]
        {
            "-p",
            "--model", config.Model,
            "--effort", config.Effort,
            "--output-format", "json",
            "--json-schema", request.Schema.ToString(Formatting.None),
            "--system-prompt", request.System,
            "--tools", "",
            "--strict-mcp-config",
            "--disable-slash-commands",
            "--no-session-persistence",
        };

        public async Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct)
        {
            var info = new ProcessStartInfo(_executable)
            {
                WorkingDirectory = _workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var argument in BuildArguments(request, _config)) info.ArgumentList.Add(argument);

            var watch = Stopwatch.StartNew();
            Process process;
            try
            {
                process = Process.Start(info);
            }
            catch (Exception e)
            {
                throw new ModelCallException("cannot start '" + _executable + "' (is Claude Code installed and on PATH?): " + e.Message, e, retryable: false);
            }

            using (process)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                await process.StandardInput.WriteAsync(request.User);
                process.StandardInput.Close();
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(true); } catch (InvalidOperationException) { }
                    if (ct.IsCancellationRequested) throw;
                    throw new ModelCallException("claude -p timed out after " + TimeoutSeconds + " s", null);
                }
                string stdout = await stdoutTask;
                string stderr = await stderrTask;
                watch.Stop();
                if (string.IsNullOrWhiteSpace(stdout))
                    throw new ModelCallException("claude -p exited with " + process.ExitCode + " and no output: " + Trim(stderr), null);
                return ParseOutput(stdout, watch.ElapsedMilliseconds);
            }
        }

        /// <summary>Turns the CLI's JSON result into a reply, or throws ModelCallException with the right retry flag.</summary>
        public static ModelReply ParseOutput(string stdout, long latencyMs)
        {
            JObject o;
            try
            {
                o = JObject.Parse(stdout);
            }
            catch (JsonException e)
            {
                throw new ModelCallException("claude -p printed something that is not JSON: " + Trim(stdout), e);
            }

            string result = (string)o["result"] ?? "";
            if ((bool?)o["is_error"] == true)
            {
                int? status = (int?)o["api_error_status"];
                string message = "claude -p failed" + (status.HasValue ? " (" + status + ")" : "") + ": " + Trim(result);
                // Usage limits reset hours later and auth problems need the user; retrying only burns the call budget.
                bool limit = result.IndexOf("limit", StringComparison.OrdinalIgnoreCase) >= 0 && status == 429;
                bool clientError = status.HasValue && status >= 400 && status < 500 && status != 429;
                throw new ModelCallException(message, null, retryable: !limit && !clientError);
            }

            var usage = o["usage"] as JObject ?? new JObject();
            var structured = o["structured_output"];
            string model = null;
            if (o["modelUsage"] is JObject models)
                foreach (var p in models.Properties())
                {
                    model = p.Name;
                    break;
                }

            return new ModelReply
            {
                Text = structured != null && structured.Type == JTokenType.Object ? structured.ToString(Formatting.None) : result,
                // Structured output is delivered through an internal tool call; report it as a normal finish.
                StopReason = (string)o["stop_reason"] == "refusal" ? "refusal" : "end_turn",
                Model = model ?? "claude-code",
                InputTokens = (long?)usage["input_tokens"] ?? 0,
                OutputTokens = (long?)usage["output_tokens"] ?? 0,
                CacheReadTokens = (long?)usage["cache_read_input_tokens"] ?? 0,
                CacheWriteTokens = (long?)usage["cache_creation_input_tokens"] ?? 0,
                LatencyMs = latencyMs,
            };
        }

        static string Trim(string text)
        {
            text = (text ?? "").Trim();
            return text.Length > 300 ? text.Substring(0, 300) + "..." : text;
        }
    }
}
