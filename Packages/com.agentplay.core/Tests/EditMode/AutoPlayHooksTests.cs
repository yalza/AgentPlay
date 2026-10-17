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

        public sealed class DefaultHook : IAgentAutoPlayHook
        {
            public void BeforePlay() { }
            public void AfterPlay() { }
        }

        abstract class AbstractHook : IAgentAutoPlayHook
        {
            public void BeforePlay() { }
            public void AfterPlay() { }
        }

        [Test]
        public void DiscoverKeepsOnlyConcreteHooksWithADefaultConstructor()
        {
            var found = AutoPlayHooks.Discover(new[] { typeof(Hook), typeof(DefaultHook), typeof(AbstractHook), typeof(IAgentAutoPlayHook) }, _ => false, out var skipped);
            CollectionAssert.AreEqual(new[] { typeof(DefaultHook) }, found);
            CollectionAssert.AreEqual(new[] { typeof(Hook) }, skipped);   // concrete but unusable: reported, not silently dropped
        }

        [Test]
        public void DiscoverSkipsTypesTheFilterRejects()
        {
            CollectionAssert.IsEmpty(AutoPlayHooks.Discover(new[] { typeof(DefaultHook) }, t => t == typeof(DefaultHook), out var skipped));
            CollectionAssert.AreEqual(new[] { typeof(DefaultHook) }, skipped);
        }

        [Test]
        public void OnlyAgentPlaysOwnTestAssemblyIsIgnored()
        {
            // A game's Assembly-CSharp-Editor may reference NUnit and still hold the game's save hook.
            Assert.IsTrue(AutoPlayHooks.IsAgentPlayTestAssembly(typeof(AutoPlayHooksTests).Assembly));
            Assert.IsFalse(AutoPlayHooks.IsAgentPlayTestAssembly(typeof(AutoPlayHooks).Assembly));
            Assert.IsFalse(AutoPlayHooks.IsAgentPlayTestAssembly(typeof(NUnit.Framework.Assert).Assembly));
        }

        [Test]
        public void RunsBeforeInOrderAndAfterInReverse()
        {
            var calls = new List<string>();
            var hooks = new List<IAgentAutoPlayHook> { new Hook("a", calls), new Hook("b", calls) };
            Assert.IsTrue(AutoPlayHooks.Before(hooks, _ => { }, out var started));
            Assert.IsTrue(AutoPlayHooks.After(started, _ => { }));
            CollectionAssert.AreEqual(new[] { "a.before", "b.before", "b.after", "a.after" }, calls);
        }

        [Test]
        public void FailingBeforeStopsAndTheFailedHookStillGetsAfter()
        {
            // A hook may have changed something before it threw (parked a save); its AfterPlay must get the chance to undo it.
            var calls = new List<string>();
            var errors = new List<string>();
            var hooks = new List<IAgentAutoPlayHook> { new Hook("a", calls), new Hook("b", calls) { FailBefore = true }, new Hook("c", calls) };
            Assert.IsFalse(AutoPlayHooks.Before(hooks, errors.Add, out var started));
            AutoPlayHooks.After(started, errors.Add);
            CollectionAssert.AreEqual(new[] { "a.before", "b.before", "b.after", "a.after" }, calls);
            Assert.AreEqual(1, errors.Count);
        }

        [Test]
        public void FailingAfterIsLoggedAndTheRestStillRun()
        {
            var calls = new List<string>();
            var errors = new List<string>();
            var started = new List<IAgentAutoPlayHook> { new Hook("a", calls), new Hook("b", calls) { FailAfter = true } };
            Assert.IsFalse(AutoPlayHooks.After(started, errors.Add));
            CollectionAssert.AreEqual(new[] { "b.after", "a.after" }, calls);
            Assert.AreEqual(1, errors.Count);
        }
    }
}
