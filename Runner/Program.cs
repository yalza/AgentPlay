using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AgentPlay.Contract;
using AgentPlay.Runner.Brains;
using AgentPlay.Runner.Learning;

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

            if (options.Command == "reflect")
            {
                var reflectConfig = LoadConfig(options);
                if (reflectConfig == null) return 2;
                return await new Reflector(MakeClient(reflectConfig), reflectConfig, Log).RunAsync(options.ProjectDir, options.Traces, options.Force, CancellationToken.None);
            }
            if (options.ReflectorSmoke)
            {
                var smokeConfig = LoadConfig(options);
                if (smokeConfig == null) return 2;
                return await ReflectorSmoke(MakeClient(smokeConfig), smokeConfig);
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
            ModelRouter router = null;
            RunLog runLog = null;
            var telemetry = options.Brain != null ? new TelemetryRecorder { Mode = options.Mode } : null;
            if (options.Brain == "opus" || options.Smoke)
            {
                var config = LoadConfig(options);
                if (config == null) return 2;
                opus = new OpusBrain(MakeClient(config), config.MaxCalls, Log);
                if (options.Router) router = new ModelRouter(config);
                opus.Called += (ctx, record) =>
                {
                    telemetry?.OnCall(record);
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

            var session = new RunnerSession(options, Log, telemetry, LoadPlaybookSafe);
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
                    await RunConnectionAsync(client, session, brain, router, options, telemetry, cts.Token);
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
            if (telemetry != null && session.Hello?.ProjectRoot != null)
            {
                var report = telemetry.Build(session.GameResult ?? (session.Finished ? "stopped" : "unfinished"));
                string telemetryPath = TelemetryRecorder.Write(session.Hello.ProjectRoot, DateTime.Now, report);
                Log("telemetry: " + telemetryPath + " (model calls " + report.ModelCalls + ", important events " + report.ImportantEvents
                    + ", handled by reflex " + report.HandledByReflex + ")");
            }
            if (brain != null && !options.NoReflect && session.Hello?.SessionTag == "play" && session.Hello.ProjectRoot != null)
            {
                Log("session tag is play: learning from it");
                var reflectConfig = LoadConfig(options);
                if (reflectConfig != null)
                    await new Reflector(MakeClient(reflectConfig), reflectConfig, Log).RunAsync(session.Hello.ProjectRoot, null, false, CancellationToken.None);
            }
            return session.ExitCodeOnStop;
        }

        /// <summary>
        /// One Unity connection: reads messages while brain calls run in the background, so the game can keep
        /// running (realtime) and plans can be asked for ahead of time (spec M4a section 3).
        /// </summary>
        static async Task RunConnectionAsync(BridgeClient client, RunnerSession session, IBrain brain, ModelRouter router,
            RunnerOptions options, TelemetryRecorder telemetry, CancellationToken ct)
        {
            var coordinator = new DecisionCoordinator(options.Prefetch && session.Realtime);
            var calls = new Dictionary<Task<Plan>, (CallKind Kind, CancellationTokenSource Cts)>();
            var lines = client.ReadLinesAsync(ct).GetAsyncEnumerator(ct);

            async Task Execute(List<CoordinatorCommand> commands)
            {
                foreach (var cmd in commands)
                {
                    switch (cmd.Kind)
                    {
                        case CoordinatorCommand.Start:
                            var callCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            calls[brain.NextPlanAsync(cmd.Context, callCts.Token)] = (cmd.Call, callCts);
                            break;
                        case CoordinatorCommand.Send:
                            if (session.Finished) break;
                            if (cmd.FromPrefetch) Log("using the plan asked for ahead of time");
                            foreach (var msg in session.OnPlan(cmd.Plan)) await Send(client, msg, ct);
                            telemetry?.OnPlanSent(cmd.FromPrefetch);
                            break;
                        case CoordinatorCommand.CancelPrefetch:
                            foreach (var entry in calls.Values)
                                if (entry.Kind == CallKind.Prefetch) entry.Cts.Cancel();
                            break;
                    }
                }
                telemetry?.SetPrefetchCounts(coordinator.PrefetchRequested, coordinator.PrefetchUsed, coordinator.PrefetchDiscarded);
            }

            // A plan just sent or a plan entering its last step: ask for the next plan ahead of time.
            async Task LookAhead()
            {
                string planId = session.TakePrefetchOpportunity();
                if (planId == null || !session.Realtime || brain == null || session.Finished) return;
                var ahead = session.PeekContext().Clone();
                var current = ahead.LastPlan;
                ahead.State = session.LatestState ?? ahead.State;
                ahead.PauseT = session.LatestT;
                ahead.PauseReason = "prefetch";
                ahead.Route = router?.Prefetch();
                ahead.PrefetchNote = "Plan " + planId + " (" + current.Intent + ") is running; its last step is "
                    + current.Steps[current.Steps.Count - 1].Do + ". Plan what to do after it finishes, assuming it succeeds.";
                await Execute(coordinator.OnPrefetchOpportunity(ahead, planId));
            }

            try
            {
                var read = lines.MoveNextAsync().AsTask();
                while (!session.Finished)
                {
                    var waitOn = new List<Task>(calls.Keys) { read };
                    var done = await Task.WhenAny(waitOn);
                    if (done != read)
                    {
                        var task = (Task<Plan>)done;
                        var (kind, callCts) = calls[task];
                        calls.Remove(task);
                        callCts.Dispose();
                        if (task.IsCanceled) continue;
                        await Execute(coordinator.OnCallCompleted(kind, await task));
                        await LookAhead();
                        continue;
                    }

                    if (!await read) break;   // Unity closed the connection
                    string line = lines.Current;
                    read = lines.MoveNextAsync().AsTask();
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
                    foreach (var msg in session.Handle(env)) await Send(client, msg, ct);
                    if (session.Finished || brain == null) continue;

                    if (session.TakeInvalidated()) await Execute(coordinator.OnInvalidated());
                    if (session.DecisionPending)
                    {
                        var ctx = session.TakeDecisionContext().Clone();
                        if (router != null) ctx.Route = router.Route(ctx);
                        telemetry?.OnDecisionStarted();
                        await Execute(coordinator.OnDecisionNeeded(ctx, session.LastEndedPlanId, session.LastEndedDone));
                    }
                    await LookAhead();
                }
            }
            finally
            {
                // Game over or Unity gone: no answer is needed any more; cancelling kills the CLI processes.
                foreach (var entry in calls.Values) entry.Cts.Cancel();
                // The reader is not disposed here: a read is still pending, and an async iterator cannot be disposed
                // mid-read. The caller disposes the client, which closes the socket and ends that read.
            }
        }

        /// <summary>runner.json next to the runner plus command-line overrides; null (after printing why) when invalid.</summary>
        static RunnerConfig LoadConfig(RunnerOptions options)
        {
            var config = RunnerConfig.Load(Path.Combine(AppContext.BaseDirectory, "runner.json"));
            if (options.Model != null) config.Model = options.Model;
            if (options.Effort != null) config.Effort = options.Effort;
            if (options.MaxCalls.HasValue) config.MaxCalls = options.MaxCalls.Value;
            if (options.Backend != null) config.Backend = options.Backend;
            try
            {
                config.Validate();
                return config;
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e.Message);
                return null;
            }
        }

        static IModelClient MakeClient(RunnerConfig config)
        {
            if (config.Backend != "api") return new ClaudeCodeModelClient(config);
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
                Log("WARNING: ANTHROPIC_API_KEY is not set; the SDK will try other credentials (ant auth login profile)");
            return new AnthropicModelClient(config);
        }

        /// <summary>One reflector call against a made-up session, to check the schema with the real backend.</summary>
        static async Task<int> ReflectorSmoke(IModelClient client, RunnerConfig config)
        {
            var hello = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "Smoke test", Version = "1", SessionTag = "play", Brief = "Reach the exit without being seen by the guard." };
            hello.Observes.Add(new ObserveInfo { Path = "player.hidden", ValueType = ValueKinds.Bool });
            hello.Events.Add(new EventInfo { Name = "guard.spotted", Important = true, Outcome = "bad" });
            hello.Actions.Add(new ActionInfo { Name = "hide_nearest", Description = "Crouch behind the nearest cover" });
            hello.Actions.Add(new ActionInfo { Name = "goto", Params = new System.Collections.Generic.List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } } });
            hello.Actions.Add(new ActionInfo { Name = "stop", Safe = true });
            var request = new ModelRequest
            {
                System = Reflector.Instructions + "\n\n" + PromptBuilder.BuildGameSection(hello),
                User = "# Current lessons\n(no lessons yet)\n\n# Session\nGame: Smoke test (version 1). Player: human. Session tag: play. Length: 9.0 game seconds.\n" +
                       "t=0.00 state player.hidden=false\nt=3.00 event guard.spotted {}\nt=3.50 state player.hidden=true\nt=9.00 event game.won {}\n\nAnswer with the lessons this session supports or contradicts.",
                Schema = ReflectorSchema.Build(hello),
                Effort = config.Reflector.Effort,
            };
            try
            {
                var reply = await client.CompleteAsync(request, CancellationToken.None);
                Log("reflector smoke: " + reply.LatencyMs + " ms; answer: " + reply.Text);
                var output = ReflectorOutput.Parse(reply.Text);
                var errors = output.Validate();
                Log(errors.Count == 0 ? "reflector smoke ok: " + output.Lessons.Count + " lesson(s)" : "reflector smoke invalid: " + string.Join("; ", errors));
                return errors.Count == 0 ? 0 : 1;
            }
            catch (Exception e) when (e is ModelCallException || e is Newtonsoft.Json.JsonException)
            {
                Log("reflector smoke failed: " + e.Message);
                return 1;
            }
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

        static Playbook LoadPlaybookSafe(HelloMessage hello)
        {
            if (string.IsNullOrEmpty(hello.ProjectRoot)) return null;
            try
            {
                var pb = PlaybookStore.Load(hello.ProjectRoot);
                Log("playbook: " + pb.Lessons.Count + " lesson(s); reflexes active in game: " + hello.Reflexes.Count);
                return pb;
            }
            catch (InvalidDataException e)
            {
                Log("WARNING: " + e.Message + " Playing without the playbook.");
                return null;
            }
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
