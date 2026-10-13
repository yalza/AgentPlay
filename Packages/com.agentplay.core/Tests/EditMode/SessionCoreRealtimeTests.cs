using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class SessionCoreRealtimeTests
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
            public TimeSplit TimeSplit { get; } = new TimeSplit { Safe = 1, Plan = 2, Reflex = 0.5 };
            public void Load(Plan plan) { }
            public void Stop() { }
            public void SetReflexes(IReadOnlyList<Reflex> reflexes) { }
            public void RemoveReflex(string id) { }
        }

        FakeHost _host;
        SessionCore _core;

        [SetUp]
        public void SetUp()
        {
            _host = new FakeHost { GameTime = 7 };
            var hello = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G" };
            _core = new SessionCore(hello, () => new JObject { ["x"] = 3 }, _host, new TraceWriter(new StringWriter()), 2.0, new FakeExecutor());
        }

        static string Line(string type, object body) => MessageCodec.Encode(type, 1, body);

        void Realtime()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.Realtime }));
        }

        [Test]
        public void SetModeRealtimeSendsAttentionMode()
        {
            Realtime();
            var a = _host.OfType(MessageTypes.Attention).Single().As<AttentionMessage>();
            Assert.AreEqual("mode", a.Reason);
            Assert.AreEqual(7, a.T);
            Assert.AreEqual(3, (int)a.State["x"]);
            Assert.AreEqual(2, a.TimeSplit.Plan);
            Assert.IsFalse(_host.Paused);
        }

        [Test]
        public void PlanEndInRealtimeSendsAttentionPlan()
        {
            Realtime();
            _host.Sent.Clear();
            _core.OnFrameEnd(new[] { new ExecutorOutput { Kind = PlanStatuses.PlanDone, PlanId = "p1" } }, false);
            Assert.AreEqual("plan", _host.OfType(MessageTypes.Attention).Single().As<AttentionMessage>().Reason);
            Assert.IsFalse(_host.Paused);
            Assert.IsEmpty(_host.OfType(MessageTypes.Paused));
        }

        [Test]
        public void UnhandledImportantEventInRealtimeSendsAttention()
        {
            Realtime();
            _host.Sent.Clear();
            _core.OnFrameEnd(new ExecutorOutput[0], true, new List<string> { "guard.spotted" });
            Assert.AreEqual("event", _host.OfType(MessageTypes.Attention).Single().As<AttentionMessage>().Reason);
        }

        [Test]
        public void HandledEventInRealtimeSendsNothing()
        {
            Realtime();
            _host.Sent.Clear();
            _core.OnFrameEnd(new[] { new ExecutorOutput { Kind = PlanStatuses.RuleFired, PlanId = "p1", Reason = "resume" } }, true, new List<string> { "guard.spotted" });
            Assert.IsEmpty(_host.OfType(MessageTypes.Attention));
        }

        [Test]
        public void TurnBasedStillPausesWithTimeSplit()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            Assert.IsTrue(_host.Paused);
            var p = _host.OfType(MessageTypes.Paused).Single().As<PausedMessage>();
            Assert.AreEqual(0.5, p.TimeSplit.Reflex);
            Assert.IsEmpty(_host.OfType(MessageTypes.Attention));
        }
    }
}
