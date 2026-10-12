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

            if (session.Finished) Log("finished with exit code " + session.ExitCode);
            else if (session.ExitCodeOnStop != 0) Log("stopped before the run finished; exit code " + session.ExitCodeOnStop);
            return session.ExitCodeOnStop;
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
