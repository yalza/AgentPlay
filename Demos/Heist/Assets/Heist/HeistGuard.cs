using AgentPlay;
using UnityEngine;

namespace HeistDemo
{
    public sealed class HeistGuard : MonoBehaviour
    {
        public const float PatrolSpeed = 1.5f;
        public const float ChaseSpeed = 3.5f;
        public const float ViewDistance = 7f;
        public const float HalfAngle = 35f;
        public const float ChaseAfter = 1.5f;
        public const float GiveUpAfter = 3f;
        public const float CatchDistance = 1f;

        int _target = 1;
        float _seenFor;
        float _unseenFor;
        bool _sees;
        bool _chasing;
        Vector3 _forward = Vector3.back;

        HeistPlayer Player => HeistGame.Instance != null ? HeistGame.Instance.Player : null;

        [AgentObserve("distance", "Metres from this guard to the thief")]
        public float Distance => Player != null ? HeistLevel.FlatDistance(transform.position, Player.transform.position) : float.PositiveInfinity;

        [AgentObserve("sees_player")] public bool SeesPlayer => _sees;
        [AgentObserve("chasing")] public bool Chasing => _chasing;
        [AgentObserve("moving_north", "True while walking towards +z")] public bool MovingNorth => _forward.z > 0f;
        [AgentObserve("x")] public float X => transform.position.x;
        [AgentObserve("z")] public float Z => transform.position.z;
        [AgentObserve("zone")] public string Zone => HeistLevel.ZoneAt(transform.position);

        void Update()
        {
            var player = Player;
            if (HeistGame.Result != null || player == null) return;

            bool sees = CanSee(player);
            if (sees && !_sees) AgentEvents.Emit("guard.spotted", new { guard = name, distance = Distance }, important: true, outcome: AgentOutcome.Bad);
            if (!sees && _sees) AgentEvents.Emit("guard.lost_sight", new { guard = name });
            _sees = sees;
            _seenFor = sees ? _seenFor + Time.deltaTime : 0f;
            _unseenFor = sees ? 0f : _unseenFor + Time.deltaTime;
            if (!_chasing && _seenFor >= ChaseAfter) _chasing = true;
            if (_chasing && _unseenFor >= GiveUpAfter) _chasing = false;

            if (_chasing)
            {
                Step(player.transform.position, ChaseSpeed);
                if (Distance <= CatchDistance) HeistGame.Instance.Lose(name);
            }
            else if (Step(HeistLevel.GuardRoute[_target], PatrolSpeed))
            {
                _target = 1 - _target;
            }
        }

        bool Step(Vector3 target, float speed)
        {
            var p = transform.position;
            target.y = p.y;
            var next = Vector3.MoveTowards(p, target, speed * Time.deltaTime);
            var delta = next - p;
            if (delta.sqrMagnitude > 1e-8f) _forward = delta.normalized;
            transform.position = next;
            return (next - target).sqrMagnitude < 0.0025f;
        }

        bool CanSee(HeistPlayer player)
        {
            var eye = new Vector3(transform.position.x, 1.6f, transform.position.z);
            var to = player.EyeTarget - eye;
            var flat = new Vector3(to.x, 0f, to.z);
            if (flat.magnitude > ViewDistance) return false;
            if (Vector3.Angle(_forward, flat) > HalfAngle) return false;
            if (Physics.Raycast(eye, to.normalized, out var hit, to.magnitude) && hit.collider.name.StartsWith("Cover_")) return false;
            return true;
        }
    }
}
