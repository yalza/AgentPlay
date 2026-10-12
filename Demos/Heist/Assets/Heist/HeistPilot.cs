using UnityEngine;

namespace HeistDemo
{
    /// <summary>
    /// A scripted stand-in for a human tester (E2E only, AGENTPLAY_HEIST_PILOT=1). It uses the human controls only,
    /// never agent actions, and plays recklessly so the session has something to learn from: it goes for item_b just as
    /// the guard walks towards it and, when the guard spots it, runs to the nearest cover and waits until the guard walks away.
    /// </summary>
    public sealed class HeistPilot : MonoBehaviour
    {
        static readonly string[] Route = { "item_a", "item_b", "item_c", "exit" };

        int _goal;
        bool _hiding;
        string _cover;
        float _calmFor;

        void Update()
        {
            var game = HeistGame.Instance;
            if (game == null || HeistGame.Result != null || _goal >= Route.Length || game.Player == null) return;
            var player = game.Player;
            var guard = game.GuardList[0];

            if (!_hiding && guard.SeesPlayer)
            {
                _hiding = true;
                _calmFor = 0f;
                _cover = HeistLevel.NearestCover(player.transform.position);
            }
            if (_hiding)
            {
                var spot = HeistLevel.Zones[_cover];
                if (HeistLevel.FlatDistance(player.transform.position, spot) > 0.1f)
                {
                    player.ManualMove(spot - player.transform.position);
                    return;
                }
                player.SetCrouched(true);
                float pz = player.transform.position.z;
                bool walkingAway = guard.MovingNorth ? guard.Z > pz : guard.Z < pz;
                _calmFor = !guard.SeesPlayer && !guard.Chasing && walkingAway && guard.Distance > 5f ? _calmFor + Time.deltaTime : 0f;
                if (_calmFor < 1f) return;
                _hiding = false;
                player.SetCrouched(false);
            }

            string goal = Route[_goal];
            // Careless timing: leave for item_b only once the guard is coming north towards it.
            if (goal == "item_b" && !(guard.MovingNorth && guard.Z > -4.5f && guard.Z < -2f)) return;
            var target = HeistLevel.Zones[goal];
            if (HeistLevel.FlatDistance(player.transform.position, target) > (goal == "exit" ? 0.3f : 1.0f))
            {
                player.ManualMove(target - player.transform.position);
                return;
            }
            if (goal == "exit" || player.PickNearest()) _goal++;
        }
    }
}
