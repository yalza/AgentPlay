using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner
{
    /// <summary>
    /// `AgentPlay.Runner check`: connects to the game, reads hello, asks for the state, lets the game run about two
    /// seconds, asks again, then prints AdapterCheck's verdict. Sends no plan and calls no model.
    /// </summary>
    public static class CheckCommand
    {
        public const int ExitPassed = 0, ExitFailed = 1, ExitNoGame = 6;

        static readonly SequenceCounter Seq = new SequenceCounter();

        public static async Task<int> RunAsync(RunnerOptions options, Action<string> log)
        {
            var clock = Stopwatch.StartNew();
            var limit = TimeSpan.FromSeconds(options.IdleExitSeconds);
            BridgeClient client = null;
            while (client == null)
            {
                if (clock.Elapsed > limit)
                {
                    log("check: no Unity connection within " + options.IdleExitSeconds + " s");
                    return ExitNoGame;
                }
                client = await BridgeClient.TryConnectAsync(options.Host, options.FirstPort, options.LastPort, CancellationToken.None);
                if (client == null) await Task.Delay(500);
            }
            log("check: connected to " + options.Host + ":" + client.Port);

            using (client)
            using (var cts = new CancellationTokenSource(limit))
            {
                HelloMessage hello = null;
                var snapshots = new List<JObject>();
                var errors = new List<string>();
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
                            errors.Add("bad message from Unity: " + e.Message);
                            continue;
                        }
                        if (env.Type == MessageTypes.Hello)
                        {
                            hello = env.As<HelloMessage>();
                            log("check: hello from " + hello.Game + " " + hello.Version);
                            await Send(client, MessageTypes.GetState, null, cts.Token);
                        }
                        else if (env.Type == MessageTypes.Error)
                        {
                            errors.Add(env.As<ErrorMessage>().Message);
                        }
                        else if (env.Type == MessageTypes.State && hello != null)
                        {
                            snapshots.Add(env.As<StateMessage>().State ?? new JObject());
                            if (snapshots.Count > 1) break;
                            // Let the game run: resume is harmless when it is not paused.
                            await Send(client, MessageTypes.Resume, new ResumeMessage { MaxGameSeconds = 2 }, cts.Token);
                            await Task.Delay(2500, cts.Token);
                            await Send(client, MessageTypes.GetState, null, cts.Token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (System.IO.IOException e)
                {
                    errors.Add("connection lost: " + e.Message);
                }
                if (hello == null)
                {
                    log("check: connected but no hello within " + options.IdleExitSeconds + " s");
                    return ExitNoGame;
                }
                var items = AdapterCheck.Run(hello, snapshots, errors);
                foreach (var i in items) log((i.Passed ? "PASS " : "FAIL ") + i.Name + (i.Detail.Length > 0 ? " (" + i.Detail + ")" : ""));
                bool ok = items.All(i => i.Passed);
                log(ok ? "check: adapter OK" : "check: adapter has problems");
                return ok ? ExitPassed : ExitFailed;
            }
        }

        static Task Send(BridgeClient client, string type, object body, CancellationToken ct) =>
            client.SendAsync(MessageCodec.Encode(type, Seq.Next(), body), ct);
    }
}
