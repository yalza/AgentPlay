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
        string _hideTarget;
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

        [AgentAction("hide_nearest", "Walk to the nearest cover spot and crouch behind its box")]
        public AgentActionStatus HideNearest(AgentActionContext ctx)
        {
            if (HeistGame.Result != null) return AgentActionStatus.Done;
            if (ctx.IsFirstFrame)
            {
                Crouched = false;
                _hideTarget = HeistLevel.NearestCover(transform.position);
            }
            if (!MoveTowards(HeistLevel.Zones[_hideTarget])) return AgentActionStatus.Running;
            Crouched = true;
            return AgentActionStatus.Done;
        }

        /// <summary>Human control (keyboard or the E2E pilot): one frame of walking in a direction on the floor.</summary>
        public void ManualMove(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) return;
            Crouched = false;
            var p = transform.position + direction.normalized * Speed * Time.deltaTime;
            transform.position = new Vector3(Mathf.Clamp(p.x, -9.5f, 9.5f), p.y, Mathf.Clamp(p.z, -5.5f, 5.5f));
        }

        public void SetCrouched(bool crouched) => Crouched = crouched;

        /// <summary>Human control: picks up the first item lying within reach.</summary>
        public bool PickNearest()
        {
            if (HeistGame.Instance == null) return false;
            foreach (var item in HeistGame.Instance.Items)
                if (item.Value.activeSelf && HeistLevel.FlatDistance(transform.position, item.Value.transform.position) <= PickRange)
                    return Pick(item.Key) == AgentActionStatus.Done;
            return false;
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
