using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class SessionCoreTests
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

        FakeHost _host;
        StringWriter _traceText;
        SessionCore _core;

        [SetUp]
        public void SetUp()
        {
            _host = new FakeHost();
            _traceText = new StringWriter();
            var hello = new HelloMessage { Protocol = ProtocolVersion.Current, Game = "G", Version = "1" };
            _core = new SessionCore(hello, () => new JObject { ["x"] = _host.GameTime }, _host, new TraceWriter(_traceText), 2.0);
        }

        static string Line(string type, object body) => MessageCodec.Encode(type, 1, body);
        static AgentEventRecord Ev(string name, bool important) => new AgentEventRecord(name, null, important, AgentOutcome.Neutral, 0, 0);

        void Frame() => _core.OnFrameEnd(new ExecutorOutput[0], false);
        void ImportantFrame() => _core.OnFrameEnd(new ExecutorOutput[0], true);

        [Test]
        public void SendsHelloOnConnect()
        {
            _core.OnConnected();
            Assert.AreEqual("G", _host.OfType(MessageTypes.Hello).Single().As<HelloMessage>().Game);
        }

        [Test]
        public void SeqIncreasesAcrossMessages()
        {
            _core.OnConnected();
            _core.OnLine(Line(MessageTypes.GetState, null));
            Assert.Less(_host.Sent[0].Seq, _host.Sent[1].Seq);
        }

        [Test]
        public void TurnBasedPausesImmediatelyWithReasonMode()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            Assert.IsTrue(_host.Paused);
            Assert.AreEqual("mode", _host.OfType(MessageTypes.Paused).Single().As<PausedMessage>().Reason);
            Assert.AreEqual(Modes.TurnBased, _core.Mode);
        }

        [Test]
        public void ResumeRunsUntilBudgetThenPausesOnce()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _host.GameTime = 10;
            _core.OnLine(Line(MessageTypes.Resume, new ResumeMessage { MaxGameSeconds = 2 }));
            Assert.IsFalse(_host.Paused);

            _host.GameTime = 11.9; _core.Tick(true);
            Assert.IsFalse(_host.Paused);
            _host.GameTime = 12.0; _core.Tick(true);
            Assert.IsTrue(_host.Paused);
            _core.Tick(false);

            var paused = _host.OfType(MessageTypes.Paused);
            Assert.AreEqual(2, paused.Count);
            Assert.AreEqual("budget", paused[1].As<PausedMessage>().Reason);
        }

        [Test]
        public void ImportantEventPausesOnlyInTurnBasedAndAfterSendingTheEvent()
        {
            _core.OnEvent(Ev("e1", true));
            ImportantFrame();
            Assert.IsFalse(_host.Paused, "realtime never pauses");

            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _core.OnLine(Line(MessageTypes.Resume, new ResumeMessage { MaxGameSeconds = 0 }));
            _host.Sent.Clear();

            _core.OnEvent(Ev("minor", false));
            Frame();
            Assert.IsFalse(_host.Paused);
            _core.OnEvent(Ev("e2", true));
            ImportantFrame();
            Assert.IsTrue(_host.Paused);
            CollectionAssert.AreEqual(new[] { MessageTypes.Event, MessageTypes.Event, MessageTypes.Paused },
                _host.Sent.Select(e => e.Type).ToArray());
            Assert.AreEqual("event", _host.Sent[2].As<PausedMessage>().Reason);
        }
        [Test]
        public void SecondImportantEventWhileWaitingDoesNotSendAnotherPaused()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _core.OnEvent(Ev("a", true));
            ImportantFrame();
            _core.OnEvent(Ev("b", true));
            ImportantFrame();
            Assert.AreEqual(1, _host.OfType(MessageTypes.Paused).Count);
            Assert.IsTrue(_core.AwaitingResume);
        }
        [Test]
        public void ManualUnpauseClearsWaitingSoNextEventPausesAgain()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _host.Paused = false;          // user pressed the Editor pause button
            _core.Tick(true);
            Assert.IsFalse(_core.AwaitingResume);
            _core.OnEvent(Ev("a", true));
            ImportantFrame();
            Assert.IsTrue(_host.Paused);
            Assert.AreEqual(2, _host.OfType(MessageTypes.Paused).Count);
        }
        [Test]
        public void ReconnectWhileWaitingAnnouncesThePauseAgain()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _host.Sent.Clear();
            _core.OnConnected();   // a new runner attaches while the game waits for resume
            CollectionAssert.AreEqual(new[] { MessageTypes.Hello, MessageTypes.Paused }, _host.Sent.Select(e => e.Type).ToArray());
            Assert.AreEqual("mode", _host.Sent[1].As<PausedMessage>().Reason);
        }

        [Test]
        public void SetTurnBasedWhileWaitingAnnouncesThePauseAgain()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _core.OnEvent(Ev("a", true));
            ImportantFrame();
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            Assert.AreEqual(2, _host.OfType(MessageTypes.Paused).Count);
            Assert.IsTrue(_host.Paused);
        }
        [Test]
        public void ImportanceComesFromTheFrameTheExecutorEvaluated()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _core.OnLine(Line(MessageTypes.Resume, new ResumeMessage { MaxGameSeconds = 0 }));
            _core.OnEvent(Ev("emitted.during.tick", true));
            Frame();
            Assert.IsFalse(_host.Paused, "an event raised during the tick belongs to the next frame");
            ImportantFrame();
            Assert.IsTrue(_host.Paused);
        }

        [Test]
        public void RealtimeModeUnpauses()
        {
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.TurnBased }));
            _core.OnLine(Line(MessageTypes.SetMode, new SetModeMessage { Mode = Modes.Realtime }));
            Assert.IsFalse(_host.Paused);
            Assert.IsFalse(_core.AwaitingResume);
        }

        [Test]
        public void GetStateReturnsSnapshot()
        {
            _host.GameTime = 3; _host.Frame = 99;
            _core.OnLine(Line(MessageTypes.GetState, null));
            var s = _host.OfType(MessageTypes.State).Single().As<StateMessage>();
            Assert.AreEqual(3.0, s.T);
            Assert.AreEqual(99L, s.Frame);
            Assert.AreEqual(3.0, (double)s.State["x"]);
        }

        [TestCase("not json")]
        [TestCase("{\"type\":\"teleport\",\"seq\":1}")]
        [TestCase("{\"type\":\"set_mode\",\"seq\":1,\"mode\":\"fast\"}")]
        [TestCase("{\"type\":\"resume\",\"seq\":1,\"max_game_seconds\":\"abc\"}")]
        [TestCase("{\"type\":\"get_state\",\"seq\":99999999999999999999}")]
        public void BadMessagesGetAnErrorReplyAndNoException(string line)
        {
            Assert.DoesNotThrow(() => _core.OnLine(line));
            Assert.AreEqual(1, _host.OfType(MessageTypes.Error).Count);
            Assert.IsFalse(_host.Paused);
        }

        [Test]
        public void BlankLinesAreIgnored()
        {
            _core.OnLine("   ");
            Assert.AreEqual(0, _host.Sent.Count);
        }

        [Test]
        public void EventsAreTracedCountedAndKeptInRecentList()
        {
            for (int i = 0; i < SessionCore.RecentLimit + 3; i++) _core.OnEvent(Ev("e" + i, false));
            Assert.AreEqual(SessionCore.RecentLimit + 3, _core.EventCount);
            Assert.AreEqual(SessionCore.RecentLimit, _core.RecentEvents.Count);
            StringAssert.Contains("e" + (SessionCore.RecentLimit + 2), _core.RecentEvents[SessionCore.RecentLimit - 1]);
            StringAssert.Contains("\"type\":\"event\"", _traceText.ToString());
        }

        [Test]
        public void TickWritesSnapshotsOnIntervalOnlyWhileRunning()
        {
            _host.GameTime = 0; _core.Tick(true);
            _host.GameTime = 1; _core.Tick(true);
            _host.GameTime = 5; _core.Tick(false);
            _host.GameTime = 2; _core.Tick(true);
            int snapshots = _traceText.ToString().Split('\n').Count(l => l.Contains("\"type\":\"snapshot\""));
            Assert.AreEqual(2, snapshots);
        }
    }
}
