using System;
using System.Collections.Generic;
using System.Linq;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class ExecutorCoreTests
    {
        sealed class FakeRunner : IActionRunner
        {
            public readonly Dictionary<string, Func<AgentActionContext, AgentActionStatus>> Behaviour =
                new Dictionary<string, Func<AgentActionContext, AgentActionStatus>>();
            public readonly List<string> Calls = new List<string>();
            public readonly List<bool> FirstFrames = new List<bool>();
            public readonly List<double> Elapsed = new List<double>();

            public bool Has(string action) => Behaviour.ContainsKey(action);

            public AgentActionStatus Run(string action, JObject args, AgentActionContext context)
            {
                Calls.Add(action);
                FirstFrames.Add(context.IsFirstFrame);
                Elapsed.Add(context.Elapsed);
                return Behaviour[action](context);
            }
        }

        FakeRunner _runner;
        ExecutorCore _core;
        JObject _state;
        HashSet<string> _events;
        int _snapshots;

        [SetUp]
        public void SetUp()
        {
            _runner = new FakeRunner();
            _runner.Behaviour["safe"] = _ => AgentActionStatus.Done;
            _core = new ExecutorCore(_runner, "safe");
            _state = new JObject { ["x"] = 0 };
            _events = new HashSet<string>();
            _snapshots = 0;
        }

        IReadOnlyList<ExecutorOutput> Tick(double dt = 0.1)
        {
            var outputs = _core.Tick(dt, () => { _snapshots++; return _state; }, _events);
            _events.Clear();
            return outputs;
        }

        static string Kinds(IEnumerable<ExecutorOutput> outputs) => string.Join(",", outputs.Select(o => o.Kind));

        static Plan Plan(params PlanStep[] steps) => new Plan { Id = "p", Steps = steps.ToList(), TimeoutSeconds = 60 };

        static PlanStep Step(string action, string until = null) => new PlanStep { Do = action, Until = until };

        static PlanRule Rule(string when, string action, string then) => new PlanRule { When = when, Do = action, Then = then };

        [Test]
        public void RunsStepsInOrderAndEndsWithDoneAndWaiting()
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Done;
            _runner.Behaviour["b"] = _ => AgentActionStatus.Done;
            _core.Load(Plan(Step("a"), Step("b")));

            Assert.AreEqual("step_done", Kinds(Tick()));
            var last = Tick();
            Assert.AreEqual("step_done,plan_done,waiting", Kinds(last));
            Assert.AreEqual(1, last[0].Step);
            Assert.AreEqual("p", last[1].PlanId);
            CollectionAssert.AreEqual(new[] { "a", "b" }, _runner.Calls);
            Assert.IsNull(_core.PlanId);
        }

        [Test]
        public void RunningActionGetsFirstFrameAndElapsed()
        {
            _runner.Behaviour["a"] = ctx => ctx.Elapsed >= 0.25 ? AgentActionStatus.Done : AgentActionStatus.Running;
            _core.Load(Plan(Step("a")));
            for (int i = 0; i < 3; i++) Assert.AreEqual("", Kinds(Tick()));
            Assert.AreEqual("step_done,plan_done,waiting", Kinds(Tick()));
            CollectionAssert.AreEqual(new[] { true, false, false, false }, _runner.FirstFrames);
            Assert.AreEqual(0.3, _runner.Elapsed[3], 1e-9);
        }

        [Test]
        public void UntilEndsAStepThatIsStillRunning()
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Running;
            _core.Load(Plan(Step("a", "x > 1")));
            Assert.AreEqual("", Kinds(Tick()));
            _state["x"] = 2;
            Assert.AreEqual("step_done,plan_done,waiting", Kinds(Tick()));
        }

        [Test]
        public void FailedActionFailsThePlan()
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Failed;
            _core.Load(Plan(Step("a")));
            var outputs = Tick();
            Assert.AreEqual("plan_failed,waiting", Kinds(outputs));
            Assert.AreEqual("action_failed:a", outputs[0].Reason);
        }

        [Test]
        public void ThrowingActionFailsThePlanWithItsMessage()
        {
            _runner.Behaviour["a"] = _ => throw new AgentActionException("boom");
            _core.Load(Plan(Step("a")));
            var outputs = Tick();
            Assert.AreEqual("plan_failed", outputs[0].Kind);
            Assert.AreEqual("action_error:a: boom", outputs[0].Reason);
        }

        [Test]
        public void TimeoutFailsThePlan()
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Running;
            var plan = Plan(Step("a"));
            plan.TimeoutSeconds = 0.3;
            _core.Load(plan);
            Assert.AreEqual("", Kinds(Tick(0.2)));
            var outputs = Tick(0.2);
            Assert.AreEqual("plan_failed,waiting", Kinds(outputs));
            Assert.AreEqual("timeout", outputs[0].Reason);
        }

        [Test]
        public void RuleFiresOnRisingEdgeAndResumeRestartsTheStep()
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Running;
            _runner.Behaviour["r"] = _ => AgentActionStatus.Done;
            var plan = Plan(Step("a"));
            plan.Rules.Add(Rule("x > 1", "r", "resume"));
            _core.Load(plan);

            Tick();                               // a (first frame)
            _state["x"] = 2;
            var fired = Tick();                   // rule fires, r runs and is done
            Assert.AreEqual("rule_fired", Kinds(fired));
            Assert.AreEqual(0, fired[0].Step);
            Assert.AreEqual("resume", fired[0].Reason);
            Tick();                               // still true: no new fire, a restarts
            _state["x"] = 0;
            Tick();
            _state["x"] = 2;
            Tick();                               // rising again
            CollectionAssert.AreEqual(new[] { "a", "r", "a", "a", "r" }, _runner.Calls);
            CollectionAssert.AreEqual(new[] { true, true, true, false, true }, _runner.FirstFrames);
        }

        [Test]
        public void RuleActionRunsAcrossFramesAndBlocksTheStep()
        {
            int ruleFrames = 0;
            _runner.Behaviour["a"] = _ => AgentActionStatus.Running;
            _runner.Behaviour["r"] = _ => ++ruleFrames < 3 ? AgentActionStatus.Running : AgentActionStatus.Done;
            var plan = Plan(Step("a"));
            plan.Rules.Add(Rule("x > 1", "r", "next"));
            _state["x"] = 2;
            _core.Load(plan);

            Assert.AreEqual("rule_fired", Kinds(Tick()));
            Assert.AreEqual("", Kinds(Tick()));
            Assert.AreEqual("step_done,plan_done,waiting", Kinds(Tick()));
            CollectionAssert.AreEqual(new[] { "r", "r", "r" }, _runner.Calls);
        }

        [TestCase("replan", "replan:0")]
        [TestCase("abort", "abort:0")]
        public void ReplanAndAbortEndThePlan(string then, string reason)
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Running;
            _runner.Behaviour["r"] = _ => AgentActionStatus.Done;
            var plan = Plan(Step("a"));
            plan.Rules.Add(Rule("event:hit", "r", then));
            _core.Load(plan);
            Tick();
            _events.Add("hit");
            var outputs = Tick();
            Assert.AreEqual("rule_fired,plan_failed,waiting", Kinds(outputs));
            Assert.AreEqual(reason, outputs[1].Reason);
        }

        [Test]
        public void FailingRuleActionFailsThePlan()
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Running;
            _runner.Behaviour["r"] = _ => AgentActionStatus.Failed;
            var plan = Plan(Step("a"));
            plan.Rules.Add(Rule("x == 0", "r", "resume"));
            _core.Load(plan);
            var outputs = Tick();
            Assert.AreEqual("rule_fired,plan_failed,waiting", Kinds(outputs));
            Assert.AreEqual("action_failed:r", outputs[1].Reason);
        }

        [Test]
        public void ConditionTrueAtPlanStartFiresImmediately()
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Running;
            _runner.Behaviour["r"] = _ => AgentActionStatus.Done;
            var plan = Plan(Step("a"));
            plan.Rules.Add(Rule("x == 0", "r", "resume"));
            _core.Load(plan);
            Assert.AreEqual("rule_fired", Kinds(Tick()));
        }

        [Test]
        public void SafeActionRunsOnlyAfterControlIsTaken()
        {
            Tick();
            CollectionAssert.IsEmpty(_runner.Calls);
            Assert.AreEqual(0, _snapshots);
            Assert.IsFalse(_core.ControlTaken);

            _runner.Behaviour["a"] = _ => AgentActionStatus.Done;
            _core.Load(Plan(Step("a")));
            Tick();
            Tick();
            CollectionAssert.AreEqual(new[] { "a", "safe" }, _runner.Calls);
            Assert.IsTrue(_core.ControlTaken);
        }

        [Test]
        public void StopSwitchesToTheSafeAction()
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Running;
            _core.Load(Plan(Step("a")));
            Tick();
            _core.Stop();
            Assert.AreEqual("", Kinds(Tick()));
            CollectionAssert.AreEqual(new[] { "a", "safe" }, _runner.Calls);
            Assert.IsNull(_core.PlanId);
        }

        [Test]
        public void LoadingANewPlanReplacesTheOldOne()
        {
            _runner.Behaviour["a"] = _ => AgentActionStatus.Running;
            _runner.Behaviour["b"] = _ => AgentActionStatus.Running;
            _core.Load(Plan(Step("a")));
            Tick();
            var second = Plan(Step("b"));
            second.Id = "q";
            _core.Load(second);
            Tick();
            CollectionAssert.AreEqual(new[] { "a", "b" }, _runner.Calls);
            Assert.AreEqual("q", _core.PlanId);
        }
    }
}
