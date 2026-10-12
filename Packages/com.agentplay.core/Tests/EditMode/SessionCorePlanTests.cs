using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class SessionCorePlanTests
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

        sealed class FakeExecutor : IPlanExecutor
        {
            public readonly List<Plan> Loaded = new List<Plan>();
            public int Stops;
            public void Load(Plan plan) => Loaded.Add(plan);
            public void Stop() => Stops++;
            public void SetReflexes(IReadOnlyList<Reflex> reflexes) { }
            public void RemoveReflex(string id) { }
        }

        FakeHost _host;
        FakeExecutor _executor;
        StringWriter _trace;
        SessionCore _core;

        static HelloMessage Hello()
        {
            var h = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G" };
            h.Observes.Add(new ObserveInfo { Path = "x", ValueType = ValueKinds.Float });
            h.Actions.Add(new ActionInfo { Name = "go", Params = new List<ActionParamInfo> { new ActionParamInfo { Name = "zone", ValueType = ValueKinds.String } } });
            h.Actions.Add(new ActionInfo { Name = "stop", Safe = true });
            return h;
        }

        [SetUp]
        public void SetUp()
        {
            _host = new FakeHost();
            _executor = new FakeExecutor();
            _trace = new StringWriter();
            _core = new SessionCore(Hello(), () => new JObject { ["x"] = 7 }, _host, new TraceWriter(_trace), 2.0, _executor);
        }

        static string Line(string type, object body) => MessageCodec.Encode(type, 1, body);

        const string ValidPlan = "{\"id\":\"p1\",\"steps\":[{\"do\":\"go\",\"args\":{\"zone\":\"a\"},\"until\":\"x > 1\"}]}";
        const string InvalidPlan = "{\"id\":\"p2\",\"steps\":[{\"do\":\"fly\"}]}";

        static string PlanLine(string planJson) => "{\"type\":\"plan\",\"seq\":1,\"plan\":" + planJson + "}";

        void TurnBasedRunning()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _core.OnLine(Line(MessageTypes.Resume, new ResumeMessage { MaxGameSeconds = 0 }));
            _host.Sent.Clear();
        }

        [Test]
        public void ImportantEventHandledByReflexDoesNotPause()
        {
            TurnBasedRunning();
            var outputs = new[] { new ExecutorOutput { Kind = ExecutorOutputKinds.ReflexFired, ReflexId = "R1", PlanId = "p" } };
            _core.OnFrameEnd(outputs, true, new List<string> { "guard.spotted" });
            Assert.IsFalse(_host.Paused);
            var reflex = _host.OfType(MessageTypes.Reflex).Single().As<ReflexMessage>();
            Assert.AreEqual("R1", reflex.Id);
            CollectionAssert.AreEqual(new[] { "guard.spotted" }, reflex.HandledEvents);
            StringAssert.Contains("\"type\":\"reflex\"", _trace.ToString());
        }

        [Test]
        public void ReflexEndIsSentAndTraced()
        {
            TurnBasedRunning();
            _core.OnFrameEnd(new[] { new ExecutorOutput { Kind = ExecutorOutputKinds.ReflexEnded, ReflexId = "R1", Reason = "done" } }, false);
            var end = _host.OfType(MessageTypes.ReflexEnd).Single().As<ReflexEndMessage>();
            Assert.AreEqual("done", end.Status);
            StringAssert.Contains("\"type\":\"reflex_end\"", _trace.ToString());
        }

        [Test]
        public void ImportantEventWithoutReflexStillPauses()
        {
            TurnBasedRunning();
            _core.OnFrameEnd(new ExecutorOutput[0], true, new List<string> { "guard.spotted" });
            Assert.IsTrue(_host.Paused);
        }

        [Test]
        public void ValidPlanIsLoadedAndTracedWithControl()
        {
            _core.OnLine(PlanLine(ValidPlan));
            Assert.AreEqual("p1", _executor.Loaded.Single().Id);
            var text = _trace.ToString();
            StringAssert.Contains("\"type\":\"control\"", text);
            StringAssert.Contains("\"who\":\"agent\"", text);
            StringAssert.Contains("\"type\":\"plan\"", text);
            Assert.AreEqual(0, _host.OfType(MessageTypes.Error).Count);
        }

        [Test]
        public void InvalidPlanWhilePausedIsRejectedAndResumeDoesNotRunTheGame()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _core.OnLine(PlanLine(InvalidPlan));
            var status = _host.OfType(MessageTypes.PlanStatus).Single().As<PlanStatusMessage>();
            Assert.AreEqual(PlanStatuses.PlanFailed, status.Status);
            StringAssert.StartsWith("invalid: ", status.Reason);
            StringAssert.Contains("unknown action 'fly'", status.Reason);
            CollectionAssert.IsEmpty(_executor.Loaded);

            _core.OnLine(Line(MessageTypes.Resume, new ResumeMessage { MaxGameSeconds = 0 }));
            Assert.IsTrue(_host.Paused, "resume after an invalid plan must not run the game");
            var paused = _host.OfType(MessageTypes.Paused);
            Assert.AreEqual(2, paused.Count);
            Assert.AreEqual("plan", paused[1].As<PausedMessage>().Reason);

            _core.OnLine(PlanLine(ValidPlan));
            _core.OnLine(Line(MessageTypes.Resume, new ResumeMessage { MaxGameSeconds = 0 }));
            Assert.IsFalse(_host.Paused, "a valid plan clears the re-pause");
        }

        [Test]
        public void InvalidPlanWhileRunningPausesTurnBasedPlay()
        {
            TurnBasedRunning();
            _core.OnLine(PlanLine(InvalidPlan));
            Assert.IsTrue(_host.Paused);
            Assert.AreEqual("plan", _host.OfType(MessageTypes.Paused).Single().As<PausedMessage>().Reason);
        }

        [Test]
        public void InvalidPlanWhileRunningStaysPausedThroughTheResumeThatFollows()
        {
            TurnBasedRunning();
            _core.OnLine(PlanLine(InvalidPlan));
            _core.OnLine(Line(MessageTypes.Resume, new ResumeMessage { MaxGameSeconds = 0 }));
            Assert.IsTrue(_host.Paused);
            Assert.AreEqual(2, _host.OfType(MessageTypes.Paused).Count);
        }

        [Test]
        public void ManualRunClearsAStaleRepause()
        {
            TurnBasedRunning();
            _core.OnLine(PlanLine(InvalidPlan));
            _host.Paused = false;          // user resumed the Editor by hand
            _core.Tick(true);
            _core.OnFrameEnd(new[] { new ExecutorOutput { Kind = PlanStatuses.PlanDone, PlanId = "x" } }, false);
            _core.OnLine(Line(MessageTypes.Resume, new ResumeMessage { MaxGameSeconds = 0 }));
            Assert.IsFalse(_host.Paused, "a resume after a later pause must run the game");
        }

        [Test]
        public void PlanEndSendsStatusWaitingThenPauses()
        {
            TurnBasedRunning();
            _core.OnFrameEnd(new[]
            {
                new ExecutorOutput { Kind = PlanStatuses.PlanDone, PlanId = "p1", Step = 1 },
                new ExecutorOutput { Kind = ExecutorOutputKinds.Waiting, PlanId = "p1" },
            }, false);
            CollectionAssert.AreEqual(new[] { MessageTypes.PlanStatus, MessageTypes.Waiting, MessageTypes.Paused },
                _host.Sent.Select(e => e.Type).ToArray());
            Assert.AreEqual("plan", _host.Sent[2].As<PausedMessage>().Reason);
            Assert.AreEqual(7, (int)_host.Sent[2].As<PausedMessage>().State["x"]);
            StringAssert.Contains("\"type\":\"plan_status\"", _trace.ToString());
        }

        [Test]
        public void PlanEndDoesNotPauseInRealtime()
        {
            _core.OnFrameEnd(new[] { new ExecutorOutput { Kind = PlanStatuses.PlanDone, PlanId = "p1" } }, false);
            Assert.IsFalse(_host.Paused);
        }

        [Test]
        public void RuleFiredInTheSameFrameSuppressesTheImportantPause()
        {
            TurnBasedRunning();
            _core.OnEvent(new AgentEventRecord("hit", null, true, AgentOutcome.Bad, 0, 0));
            _core.OnFrameEnd(new[] { new ExecutorOutput { Kind = PlanStatuses.RuleFired, PlanId = "p1", Step = 0, Reason = "resume" } }, true);
            Assert.IsFalse(_host.Paused);
            _core.OnFrameEnd(new ExecutorOutput[0], false);
            Assert.IsFalse(_host.Paused, "the important flag is cleared after the frame");
        }

        [Test]
        public void StopPlanStopsTheExecutorAndReportsWaiting()
        {
            _core.OnLine(Line(MessageTypes.StopPlan, null));
            Assert.AreEqual(1, _executor.Stops);
            Assert.AreEqual(1, _host.OfType(MessageTypes.Waiting).Count);
        }

        [Test]
        public void PausedCarriesTheSnapshot()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            Assert.AreEqual(7, (int)_host.OfType(MessageTypes.Paused).Single().As<PausedMessage>().State["x"]);
        }

        [Test]
        public void SessionWithoutExecutorRejectsPlans()
        {
            var core = new SessionCore(Hello(), () => new JObject(), _host, null, 2.0);
            core.OnLine(PlanLine(ValidPlan));
            StringAssert.Contains("no plan executor", _host.OfType(MessageTypes.Error).Single().As<ErrorMessage>().Message);
        }
    }
}
