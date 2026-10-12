using System.Collections.Generic;
using AgentPlay;
using UnityEngine;

namespace HeistDemo
{
    public sealed class HeistPlayer : MonoBehaviour
    {
        public const float Speed = 3f;
        public const float PickRange = 1.5f;

        public readonly List<string> Carried = new List<string>();
        public bool Crouched { get; private set; }

        /// <summary>Point guards look at: head height standing, low when crouched.</summary>
        public Vector3 EyeTarget => new Vector3(transform.position.x, Crouched ? 0.5f : 1.6f, transform.position.z);

        [AgentObserve("player.pos")] public Vector3 Pos => transform.position;

        [AgentObserve("player.zone", "Named zone the thief stands in, or empty")]
        public string Zone => HeistLevel.ZoneAt(transform.position);

        [AgentObserve("player.carrying", "Number of items carried")] public int Carrying => Carried.Count;

        [AgentObserve("player.crouched")] public bool IsCrouched => Crouched;

        [AgentObserve("player.hidden", "Crouched at a cover spot; guards on the far side of its box cannot see the thief")]
        public bool Hidden => Crouched && Zone.StartsWith("cover_");

        [AgentAction("goto", "Walk straight to a zone: start, exit, item_a, item_b, item_c, cover_1, cover_2")]
        public AgentActionStatus GoTo(AgentActionContext ctx, string zone)
        {
            if (HeistGame.Result != null) return AgentActionStatus.Done;   // round over: nothing left to do
            if (!HeistLevel.Zones.TryGetValue(zone, out var target)) return AgentActionStatus.Failed;
            if (ctx.IsFirstFrame) Crouched = false;
            if (!MoveTowards(target)) return AgentActionStatus.Running;
            HeistGame.Instance.CheckWin();
            return AgentActionStatus.Done;
        }

        [AgentAction("pick", "Pick up an item within 1.5 m: item_a, item_b or item_c")]
        public AgentActionStatus Pick(string item)
        {
            if (HeistGame.Result != null) return AgentActionStatus.Done;
            if (!HeistGame.Instance.Items.TryGetValue(item, out var go) || !go.activeSelf) return AgentActionStatus.Failed;
            if (HeistLevel.FlatDistance(transform.position, go.transform.position) > PickRange) return AgentActionStatus.Failed;
            go.SetActive(false);
            Carried.Add(item);
            AgentEvents.Emit("item.picked", new { item, carrying = Carried.Count });
            return AgentActionStatus.Done;
        }

        [AgentAction("hide", "Walk to a cover spot (cover_1 or cover_2) and crouch behind its box")]
        public AgentActionStatus Hide(AgentActionContext ctx, string zone)
        {
            if (HeistGame.Result != null) return AgentActionStatus.Done;
            if (!zone.StartsWith("cover_") || !HeistLevel.Zones.TryGetValue(zone, out var target))
                return AgentActionStatus.Failed;
            if (ctx.IsFirstFrame) Crouched = false;
            if (!MoveTowards(target)) return AgentActionStatus.Running;
            Crouched = true;
            return AgentActionStatus.Done;
        }

        [AgentAction("wait", "Stay still for the given number of seconds")]
        public AgentActionStatus Wait(AgentActionContext ctx, float seconds) =>
            ctx.Elapsed >= seconds ? AgentActionStatus.Done : AgentActionStatus.Running;

        [AgentAction("stop", "Stay still (used whenever there is no plan)", Safe = true)]
        public AgentActionStatus Stop() => AgentActionStatus.Done;

        bool MoveTowards(Vector3 target)
        {
            var p = transform.position;
            target.y = p.y;
            transform.position = Vector3.MoveTowards(p, target, Speed * Time.deltaTime);
            return HeistLevel.FlatDistance(transform.position, target) < 0.05f;
        }

        void Update()
        {
            var p = transform.position;
            transform.position = new Vector3(p.x, Crouched ? 0.5f : 1f, p.z);
            transform.localScale = new Vector3(1f, Crouched ? 0.5f : 1f, 1f);
            if (HeistGame.Instance != null) HeistGame.Instance.CheckWin();
        }
    }
}
