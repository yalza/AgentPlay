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
            Console.OutputEncoding = System.Text.Encoding.UTF8;

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

            OpusBrain opus = null;
            RunLog runLog = null;
            if (options.Brain == "opus" || options.Smoke)
            {
                var config = RunnerConfig.Load(Path.Combine(AppContext.BaseDirectory, "runner.json"));
                if (options.Model != null) config.Model = options.Model;
                if (options.Effort != null) config.Effort = options.Effort;
                if (options.MaxCalls.HasValue) config.MaxCalls = options.MaxCalls.Value;
                if (options.Backend != null) config.Backend = options.Backend;
                try
                {
                    config.Validate();
                }
                catch (ArgumentException e)
                {
                    Console.Error.WriteLine(e.Message);
                    return 2;
                }
                IModelClient client;
                if (config.Backend == "api")
                {
                    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
                        Log("WARNING: ANTHROPIC_API_KEY is not set; the SDK will try other credentials (ant auth login profile)");
                    client = new AnthropicModelClient(config);
                }
                else
                {
                    client = new ClaudeCodeModelClient(config);
                }
                opus = new OpusBrain(client, config.MaxCalls, Log);
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
                Log("opus brain: model " + config.Model + " via " + config.Backend + ", effort " + config.Effort + ", at most " + config.MaxCalls + " calls");
                if (options.Smoke) return await Smoke(opus);
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
        }

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
