using System;
using System.Collections.Generic;
using AgentPlay.Editor;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class AutoPlayHooksTests
    {
        sealed class Hook : IAgentAutoPlayHook
        {
            readonly string _name;
            readonly List<string> _calls;
            public bool FailBefore, FailAfter;
            public Hook(string name, List<string> calls) { _name = name; _calls = calls; }
            public void BeforePlay() { _calls.Add(_name + ".before"); if (FailBefore) throw new InvalidOperationException("x"); }
            public void AfterPlay() { _calls.Add(_name + ".after"); if (FailAfter) throw new InvalidOperationException("y"); }
        }

        [Test]
        public void RunsBeforeInOrderAndAfterInReverse()
        {
            var calls = new List<string>();
            var hooks = new List<IAgentAutoPlayHook> { new Hook("a", calls), new Hook("b", calls) };
            Assert.IsTrue(AutoPlayHooks.Before(hooks, _ => { }, out var started));
            AutoPlayHooks.After(started, _ => { });
            CollectionAssert.AreEqual(new[] { "a.before", "b.before", "b.after", "a.after" }, calls);
        }

        [Test]
        public void FailingBeforeStopsAndOnlyStartedHooksGetAfter()
        {
            var calls = new List<string>();
            var errors = new List<string>();
            var hooks = new List<IAgentAutoPlayHook> { new Hook("a", calls), new Hook("b", calls) { FailBefore = true }, new Hook("c", calls) };
            Assert.IsFalse(AutoPlayHooks.Before(hooks, errors.Add, out var started));
            AutoPlayHooks.After(started, errors.Add);
            CollectionAssert.AreEqual(new[] { "a.before", "b.before", "a.after" }, calls);
            Assert.AreEqual(1, errors.Count);
        }

        [Test]
        public void FailingAfterIsLoggedAndTheRestStillRun()
        {
            var calls = new List<string>();
            var errors = new List<string>();
            var started = new List<IAgentAutoPlayHook> { new Hook("a", calls), new Hook("b", calls) { FailAfter = true } };
            AutoPlayHooks.After(started, errors.Add);
            CollectionAssert.AreEqual(new[] { "b.after", "a.after" }, calls);
            Assert.AreEqual(1, errors.Count);
        }
    }
}
