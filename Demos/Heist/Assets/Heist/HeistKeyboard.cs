using UnityEngine;

namespace HeistDemo
{
    /// <summary>
    /// Lets a person play the demo: WASD or arrows to walk, C to crouch or stand, E to pick up the nearest item.
    /// The demo project uses the old Input Manager, so this reads UnityEngine.Input.
    /// </summary>
    public sealed class HeistKeyboard : MonoBehaviour
    {
        HeistPlayer _player;

        void Start() => _player = GetComponent<HeistPlayer>();

        void Update()
        {
            if (_player == null || HeistGame.Result != null) return;
            float x = Axis(KeyCode.D, KeyCode.RightArrow) - Axis(KeyCode.A, KeyCode.LeftArrow);
            float z = Axis(KeyCode.W, KeyCode.UpArrow) - Axis(KeyCode.S, KeyCode.DownArrow);
            _player.ManualMove(new Vector3(x, 0f, z));
            if (Input.GetKeyDown(KeyCode.C)) _player.SetCrouched(!_player.Crouched);
            if (Input.GetKeyDown(KeyCode.E)) _player.PickNearest();
        }

        static float Axis(KeyCode a, KeyCode b) => Input.GetKey(a) || Input.GetKey(b) ? 1f : 0f;
    }
}
