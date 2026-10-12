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

        /// <summary>
        /// The system prompt goes through a file because Windows caps a command line at 32,767 characters;
        /// the schema stays inline since the CLI has no file option for it.
        /// </summary>
        public static IReadOnlyList<string> BuildArguments(ModelRequest request, RunnerConfig config, string systemPromptFile) => new[]
        {
            "-p",
            "--model", config.Model,
            "--effort", request.Effort ?? config.Effort,
            "--output-format", "json",
            "--json-schema", request.Schema.ToString(Formatting.None),
            "--system-prompt-file", systemPromptFile,
            "--tools", "",
            "--strict-mcp-config",
            "--disable-slash-commands",
            "--no-session-persistence",
            // No CLAUDE.md, skills, plugins or hooks of the user's setup: the game agent sees only its own prompt.
            "--safe-mode",
            // Only the (empty) working folder's settings: the user's language and other preferences stay out of game prompts.
            "--setting-sources", "project",
        };

        public async Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken ct)
        {
            string systemPromptFile = Path.Combine(_workingDirectory, "system-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(systemPromptFile, request.System, new UTF8Encoding(false));
            try
            {
                return await RunAsync(request, systemPromptFile, ct);
            }
            finally
            {
                try { File.Delete(systemPromptFile); } catch (IOException) { }
            }
        }

        async Task<ModelReply> RunAsync(ModelRequest request, string systemPromptFile, CancellationToken ct)
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
            foreach (var argument in BuildArguments(request, _config, systemPromptFile)) info.ArgumentList.Add(argument);

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
                try
                {
                    try
                    {
                        await process.StandardInput.WriteAsync(request.User.AsMemory(), timeout.Token);
                        process.StandardInput.Close();
                    }
                    catch (IOException)
                    {
                        // The CLI exited before reading its input (bad flag, not logged in); its output says why.
                    }
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    Kill(process);
                    if (ct.IsCancellationRequested) throw;
                    string partial = await StderrAfterKill(stderrTask);
                    throw new ModelCallException("claude -p timed out after " + TimeoutSeconds + " s" + (partial.Length > 0 ? ": " + partial : ""), null);
                }
                catch
                {
                    Kill(process);
                    throw;
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

            string result = o["result"]?.Type == JTokenType.String ? (string)o["result"] : "";
            if (o["is_error"]?.Type == JTokenType.Boolean && (bool)o["is_error"])
            {
                int? status = o["api_error_status"]?.Type == JTokenType.Integer ? (int?)o["api_error_status"] : null;
                string message = "claude -p failed" + (status.HasValue ? " (" + status + ")" : "") + ": " + Trim(result);
                throw new ModelCallException(message, null, retryable: IsRetryable(status, result));
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

        /// <summary>
        /// Usage limits reset hours later and login or billing problems need the user, so retrying only burns the
        /// call budget. The CLI reports some of them without an HTTP status, so the text is checked too.
        /// </summary>
        static bool IsRetryable(int? status, string result)
        {
            if (status == 408 || status == 409) return true;
            if (status.HasValue && status >= 400 && status < 500 && status != 429) return false;
            if (status.HasValue && status >= 500) return true;
            foreach (var hint in PermanentErrorHints)
                if (result.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return true;
        }

        static readonly string[] PermanentErrorHints = { "limit", "login", "logged in", "credit", "api key", "unauthorized", "forbidden" };

        static void Kill(Process process)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        }

        static async Task<string> StderrAfterKill(Task<string> stderrTask)
        {
            try
            {
                return Trim(await stderrTask.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            catch (Exception)
            {
                return "";
            }
        }

        static string Trim(string text)
        {
            text = (text ?? "").Trim();
            return text.Length > 300 ? text.Substring(0, 300) + "..." : text;
        }
    }
}
