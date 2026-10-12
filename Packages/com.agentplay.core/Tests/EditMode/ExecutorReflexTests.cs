using System;
using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class ExecutorReflexTests
    {
        sealed class FakeRunner : IActionRunner
        {
            public readonly Dictionary<string, Func<AgentActionContext, AgentActionStatus>> Behaviour =
                new Dictionary<string, Func<AgentActionContext, AgentActionStatus>>();
            public readonly List<string> Calls = new List<string>();
            public readonly List<bool> FirstFrames = new List<bool>();

            public bool Has(string action) => Behaviour.ContainsKey(action);

            public AgentActionStatus Run(string action, JObject args, AgentActionContext context)
            {
                Calls.Add(action);
                FirstFrames.Add(context.IsFirstFrame);
                return Behaviour[action](context);
            }
        }

        FakeRunner _runner;
        ExecutorCore _core;
        JObject _state;
        HashSet<string> _events;

        [SetUp]
        public void SetUp()
        {
            _runner = new FakeRunner();
            _runner.Behaviour["safe"] = _ => AgentActionStatus.Done;
            _runner.Behaviour["walk"] = _ => AgentActionStatus.Running;
            _runner.Behaviour["hide"] = ctx => ctx.Elapsed >= 0.15 ? AgentActionStatus.Done : AgentActionStatus.Running;
            _core = new ExecutorCore(_runner, "safe");
            _state = new JObject { ["danger"] = false };
            _events = new HashSet<string>();
        }

        IReadOnlyList<ExecutorOutput> Tick(double dt = 0.1)
        {
            var o = _core.Tick(dt, () => _state, _events);
            _events.Clear();
            return o;
        }

        static string Kinds(IEnumerable<ExecutorOutput> o) => string.Join(",", o.Select(x => x.Kind));

        static Reflex R(string id, string when, string action, double cooldown = 0) =>
            new Reflex { Id = id, When = when, Do = action, Args = new JObject(), CooldownSeconds = cooldown, Status = ReflexStatuses.Active };

        static Plan WalkPlan(params PlanRule[] rules) =>
            new Plan { Id = "p", Steps = new List<PlanStep> { new PlanStep { Do = "walk" } }, Rules = rules.ToList(), TimeoutSeconds = 60 };

        [Test]
        public void ReflexInterruptsStepThenStepRestarts()
        {
            _core.SetReflexes(new[] { R("R1", "event:spotted", "hide") });
            _core.Load(WalkPlan());
            Tick();
            _events.Add("spotted");
            var fired = Tick();
            Assert.AreEqual("reflex_fired", Kinds(fired));
            Assert.AreEqual("R1", fired[0].ReflexId);
            Assert.IsTrue(_core.ReflexRunning);
            Tick();
            var ended = Tick();
            Assert.AreEqual("reflex_ended", Kinds(ended));
            Assert.AreEqual("done", ended[0].Reason);
            Tick();
            CollectionAssert.AreEqual(new[] { "walk", "hide", "hide", "hide", "walk" }, _runner.Calls);
            Assert.IsTrue(_runner.FirstFrames.Last(), "the interrupted step restarts from its first frame");
        }

        [Test]
        public void NoReflexBeforeControlIsTaken()
        {
            _core.SetReflexes(new[] { R("R1", "event:spotted", "hide") });
            _events.Add("spotted");
            Assert.AreEqual("", Kinds(Tick()));
            CollectionAssert.IsEmpty(_runner.Calls);
        }

        [Test]
        public void ReflexRunsBetweenPlansThenSafeResumes()
        {
            _runner.Behaviour["once"] = _ => AgentActionStatus.Done;
            _core.SetReflexes(new[] { R("R1", "danger == true", "hide") });
            _core.Load(new Plan { Id = "p", Steps = new List<PlanStep> { new PlanStep { Do = "once" } }, TimeoutSeconds = 60 });
            Tick();
            Assert.IsNull(_core.PlanId);
            _state["danger"] = true;
            Assert.AreEqual("reflex_fired", Kinds(Tick()));
            Tick();
            Assert.AreEqual("reflex_ended", Kinds(Tick()));
            Tick();
            Assert.AreEqual("safe", _runner.Calls.Last());
        }

        [Test]
        public void RulesWaitWhileReflexRuns()
        {
            _core.SetReflexes(new[] { R("R1", "event:spotted", "hide") });
            _core.Load(WalkPlan(new PlanRule { When = "danger == true", Do = "hide", Then = RuleThen.Replan }));
            Tick();
            _events.Add("spotted");
            _state["danger"] = true;
            Assert.AreEqual("reflex_fired", Kinds(Tick()), "the reflex outranks the rule in the same frame");
            Tick();
            Assert.AreEqual("reflex_ended", Kinds(Tick()));
            Assert.AreEqual("rule_fired", Kinds(Tick()), "the rule's rising edge is seen once the reflex ended");
        }

        [Test]
        public void CooldownBlocksRefiring()
        {
            _runner.Behaviour["hide"] = _ => AgentActionStatus.Done;
            _core.SetReflexes(new[] { R("R1", "event:spotted", "hide", cooldown: 1) });
            _core.Load(WalkPlan());
            _events.Add("spotted");
            Assert.AreEqual("reflex_fired,reflex_ended", Kinds(Tick()));
            Tick();
            _events.Add("spotted");
            Assert.AreEqual("", Kinds(Tick()), "a rising edge inside the cooldown is ignored");
            for (int i = 0; i < 10; i++) Tick();
            _events.Add("spotted");
            Assert.AreEqual("reflex_fired,reflex_ended", Kinds(Tick()));
        }

        [Test]
        public void EdgeWhileTheReflexRunsDoesNotSkipTheCooldown()
        {
            _core.SetReflexes(new[] { R("R1", "event:spotted", "hide", cooldown: 1) });
            _core.Load(WalkPlan());
            _events.Add("spotted");
            Assert.AreEqual("reflex_fired", Kinds(Tick()));
            Tick();
            _events.Add("spotted");                     // the guard spots the thief again while it is still hiding
            Assert.AreEqual("reflex_ended", Kinds(Tick()));
            Assert.AreEqual("", Kinds(Tick()), "the reflex must not fire again inside its cooldown");
        }

        [Test]
        public void FailingReflexDoesNotFailThePlan()
        {
            _runner.Behaviour["hide"] = _ => throw new InvalidOperationException("boom");
            _core.SetReflexes(new[] { R("R1", "event:spotted", "hide") });
            _core.Load(WalkPlan());
            _events.Add("spotted");
            var o = Tick();
            Assert.AreEqual("reflex_fired,reflex_ended", Kinds(o));
            StringAssert.StartsWith("failed:action_error:hide", o[1].Reason);
            Assert.AreEqual("p", _core.PlanId);
        }

        [Test]
        public void ReflexOverRunTimeFails()
        {
            _runner.Behaviour["hide"] = _ => AgentActionStatus.Running;
            _core.SetReflexes(new[] { R("R1", "event:spotted", "hide") });
            _core.Load(new Plan { Id = "p", Steps = new List<PlanStep> { new PlanStep { Do = "walk" } }, TimeoutSeconds = 600 });
            _events.Add("spotted");
            Tick(1);
            IReadOnlyList<ExecutorOutput> o = null;
            for (int i = 0; i < 20 && (o == null || Kinds(o) != "reflex_ended"); i++) o = Tick(1);
            Assert.AreEqual("reflex_ended", Kinds(o));
            Assert.AreEqual("failed:timeout", o[0].Reason);
        }

        [Test]
        public void RemovedReflexStopsFiring()
        {
            _core.SetReflexes(new[] { R("R1", "event:spotted", "hide") });
            _core.Load(WalkPlan());
            _core.RemoveReflex("R1");
            _events.Add("spotted");
            Assert.AreEqual("", Kinds(Tick()));
        }

        [Test]
        public void NoSnapshotWithoutPlanOrReflexes()
        {
            int snapshots = 0;
            _core.Tick(0.1, () => { snapshots++; return _state; }, _events);
            Assert.AreEqual(0, snapshots);
        }
    }
}
