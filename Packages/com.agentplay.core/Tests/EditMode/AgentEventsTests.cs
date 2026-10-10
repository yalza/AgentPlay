using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AgentPlay.Tests
{
    public class AgentEventsTests
    {
        readonly List<AgentEventRecord> _got = new List<AgentEventRecord>();
        Func<double> _oldTime;
        Func<long> _oldFrame;

        void Collect(AgentEventRecord r) => _got.Add(r);

        [SetUp]
        public void SetUp()
        {
            _got.Clear();
            _oldTime = AgentEvents.TimeSource;
            _oldFrame = AgentEvents.FrameSource;
            AgentEvents.TimeSource = () => 12.5;
            AgentEvents.FrameSource = () => 300;
        }

        [TearDown]
        public void TearDown()
        {
            AgentEvents.Emitted -= Collect;
            AgentEvents.TimeSource = _oldTime;
            AgentEvents.FrameSource = _oldFrame;
        }

        [Test]
        public void DeliversRecordWithAllFields()
        {
            AgentEvents.Emitted += Collect;
            AgentEvents.Emit("guard.spotted", new { at = new Vector3(1, 0, 2) }, important: true, outcome: AgentOutcome.Bad);

            Assert.AreEqual(1, _got.Count);
            var r = _got[0];
            Assert.AreEqual("guard.spotted", r.Name);
            Assert.IsTrue(r.Important);
            Assert.AreEqual(AgentOutcome.Bad, r.Outcome);
            Assert.AreEqual(12.5, r.Time);
            Assert.AreEqual(300L, r.Frame);
            Assert.AreEqual(3, ((Newtonsoft.Json.Linq.JArray)r.Payload["at"]).Count);
        }

        [Test]
        public void DoesNotTouchPayloadWhenNobodyListens()
        {
            var probe = new CountingPayload();
            AgentEvents.Emit("quiet", probe);
            Assert.AreEqual(0, probe.Reads);
        }

        [Test]
        public void NonObjectPayloadIsWrapped()
        {
            AgentEvents.Emitted += Collect;
            AgentEvents.Emit("count", 3);
            Assert.AreEqual(3, (int)_got[0].Payload["value"]);
        }

        [Test]
        public void EmptyNameIsIgnoredWithWarning()
        {
            AgentEvents.Emitted += Collect;
            LogAssert.Expect(LogType.Warning, new Regex("empty event name"));
            AgentEvents.Emit("");
            Assert.AreEqual(0, _got.Count);
        }

        [Test]
        public void ThrowingSubscriberDoesNotStopOthersOrTheGame()
        {
            Action<AgentEventRecord> boom = _ => throw new InvalidOperationException("boom");
            AgentEvents.Emitted += boom;
            AgentEvents.Emitted += Collect;
            try
            {
                LogAssert.Expect(LogType.Exception, new Regex("boom"));
                Assert.DoesNotThrow(() => AgentEvents.Emit("x"));
                Assert.AreEqual(1, _got.Count);
            }
            finally
            {
                AgentEvents.Emitted -= boom;
            }
        }

        class CountingPayload
        {
            public int Reads;
            public int Value { get { Reads++; return 1; } }
        }
    }
}
