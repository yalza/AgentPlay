using System;
using System.Collections.Generic;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;

namespace AgentPlay
{
    public sealed class ExecutorOutput
    {
        /// <summary>One of PlanStatuses.* or ExecutorOutputKinds.Waiting.</summary>
        public string Kind;
        public string PlanId;
        /// <summary>Step index, or rule index for rule_fired.</summary>
        public int Step;
        public string Reason;
    }

    public static class ExecutorOutputKinds
    {
        public const string Waiting = "waiting";
    }

    /// <summary>Runs one plan frame by frame (spec M2 section 5). Pure: no Unity calls, main thread only.</summary>
    public sealed class ExecutorCore
    {
        static readonly IReadOnlyList<ExecutorOutput> None = new ExecutorOutput[0];

        readonly IActionRunner _actions;
        readonly string _safeAction;
        readonly AgentActionContext _stepContext = new AgentActionContext();
        readonly AgentActionContext _ruleContext = new AgentActionContext();
        readonly AgentActionContext _safeContext = new AgentActionContext();

        Plan _plan;
        CondNode[] _until;
        CondNode[] _when;
        bool[] _ruleWasTrue;
        bool[] _rulePending;
        int _step;
        int _activeRule = -1;
        double _planTime;

        public ExecutorCore(IActionRunner actions, string safeAction)
        {
            _actions = actions;
            _safeAction = safeAction;
        }

        public string PlanId => _plan?.Id;

        /// <summary>True once any plan has been loaded; before that the safe action does not run.</summary>
        public bool ControlTaken { get; private set; }

        /// <summary>Loads a validated plan, replacing the current one.</summary>
        public void Load(Plan plan)
        {
            var rules = plan.Rules ?? new List<PlanRule>();
            _until = new CondNode[plan.Steps.Count];
            for (int i = 0; i < plan.Steps.Count; i++)
                _until[i] = string.IsNullOrWhiteSpace(plan.Steps[i].Until) ? null : ConditionParser.Parse(plan.Steps[i].Until);
            _when = new CondNode[rules.Count];
            for (int i = 0; i < rules.Count; i++) _when[i] = ConditionParser.Parse(rules[i].When);

            _plan = plan;
            _plan.Rules = rules;
            _ruleWasTrue = new bool[_when.Length];
            _rulePending = new bool[_when.Length];
            _step = 0;
            _activeRule = -1;
            _planTime = 0;
            Reset(_stepContext);
            ControlTaken = true;
        }

        public void Stop()
        {
            _plan = null;
            _activeRule = -1;
            Reset(_safeContext);
        }

        public IReadOnlyList<ExecutorOutput> Tick(double deltaTime, Func<JObject> snapshot, ICollection<string> events)
        {
            if (_plan == null)
            {
                RunSafe(deltaTime);
                return None;
            }

            var outputs = new List<ExecutorOutput>();
            _planTime += deltaTime;
            if (_planTime > _plan.TimeoutSeconds)
            {
                End(outputs, PlanStatuses.PlanFailed, "timeout");
                return outputs;
            }

            var context = new SnapshotContext(snapshot(), events);
            // A rule whose condition rose stays pending until it gets to fire, so one outranked in the
            // same frame (or rising while it already runs) is not lost.
            int fire = -1;
            for (int i = 0; i < _when.Length; i++)
            {
                bool now = ConditionEvaluator.Evaluate(_when[i], context);
                if (now && !_ruleWasTrue[i]) _rulePending[i] = true;
                _ruleWasTrue[i] = now;
                if (fire < 0 && _rulePending[i] && i != _activeRule) fire = i;
            }
            if (fire >= 0)
            {
                _rulePending[fire] = false;
                _activeRule = fire;
                Reset(_ruleContext);
                outputs.Add(new ExecutorOutput { Kind = PlanStatuses.RuleFired, PlanId = _plan.Id, Step = fire, Reason = _plan.Rules[fire].Then });
            }

            if (_activeRule >= 0) RunRule(deltaTime, outputs);
            else RunStep(deltaTime, context, outputs);
            return outputs;
        }

        void RunRule(double deltaTime, List<ExecutorOutput> outputs)
        {
            var rule = _plan.Rules[_activeRule];
            if (!TryRun(rule.Do, rule.Args, _ruleContext, deltaTime, out var status, out var error))
            {
                End(outputs, PlanStatuses.PlanFailed, error);
                return;
            }
            if (status == AgentActionStatus.Running) return;
            if (status == AgentActionStatus.Failed)
            {
                End(outputs, PlanStatuses.PlanFailed, "action_failed:" + rule.Do);
                return;
            }

            int index = _activeRule;
            _activeRule = -1;
            switch (rule.Then)
            {
                case RuleThen.Resume:
                    Reset(_stepContext);
                    return;
                case RuleThen.Next:
                    Advance(outputs);
                    return;
                case RuleThen.Replan:
                    End(outputs, PlanStatuses.PlanFailed, "replan:" + index);
                    return;
                default:
                    End(outputs, PlanStatuses.PlanFailed, "abort:" + index);
                    return;
            }
        }

        void RunStep(double deltaTime, SnapshotContext context, List<ExecutorOutput> outputs)
        {
            var step = _plan.Steps[_step];
            if (!TryRun(step.Do, step.Args, _stepContext, deltaTime, out var status, out var error))
            {
                End(outputs, PlanStatuses.PlanFailed, error);
                return;
            }
            if (status == AgentActionStatus.Failed)
            {
                End(outputs, PlanStatuses.PlanFailed, "action_failed:" + step.Do);
                return;
            }
            if (status == AgentActionStatus.Done || (_until[_step] != null && ConditionEvaluator.Evaluate(_until[_step], context)))
                Advance(outputs);
        }

        void Advance(List<ExecutorOutput> outputs)
        {
            outputs.Add(new ExecutorOutput { Kind = PlanStatuses.StepDone, PlanId = _plan.Id, Step = _step });
            _step++;
            Reset(_stepContext);
            if (_step >= _plan.Steps.Count) End(outputs, PlanStatuses.PlanDone, null);
        }

        void End(List<ExecutorOutput> outputs, string kind, string reason)
        {
            outputs.Add(new ExecutorOutput { Kind = kind, PlanId = _plan.Id, Step = _step, Reason = reason });
            outputs.Add(new ExecutorOutput { Kind = ExecutorOutputKinds.Waiting, PlanId = _plan.Id });
            Stop();
        }

        void RunSafe(double deltaTime)
        {
            if (!ControlTaken || _safeAction == null) return;
            if (TryRun(_safeAction, null, _safeContext, deltaTime, out var status, out _) && status != AgentActionStatus.Running)
                Reset(_safeContext);
        }

        bool TryRun(string action, JObject args, AgentActionContext context, double deltaTime, out AgentActionStatus status, out string error)
        {
            if (!context.IsFirstFrame) context.Elapsed += deltaTime;
            try
            {
                status = _actions.Run(action, args, context);
                error = null;
                return true;
            }
            catch (Exception e)
            {
                status = AgentActionStatus.Failed;
                error = "action_error:" + action + ": " + e.Message;
                return false;
            }
            finally
            {
                context.IsFirstFrame = false;
            }
        }

        static void Reset(AgentActionContext context)
        {
            context.IsFirstFrame = true;
            context.Elapsed = 0;
        }
    }
}
