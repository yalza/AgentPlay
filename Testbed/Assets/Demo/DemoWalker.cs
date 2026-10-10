using AgentPlay;
using UnityEngine;

/// <summary>A cube walking back and forth. Every turn emits an event; every second turn is important.</summary>
[AgentGame("AgentPlay Testbed", Version = "m1")]
public class DemoWalker : MonoBehaviour
{
    public float speed = 2f;
    public float halfWidth = 5f;
    int _direction = 1;

    [AgentObserve("walker.x", "Walker x position")] public float X => transform.position.x;
    [AgentObserve("walker.laps", "Number of turns so far")] public int Laps { get; private set; }
    [AgentObserve("walker.pos")] public Vector3 Pos => transform.position;

    [AgentAction("wait", "Do nothing", Safe = true)]
    public AgentActionStatus Wait() => AgentActionStatus.Done;

    void Update()
    {
        var p = transform.position;
        p.x += _direction * speed * Time.deltaTime;
        if (Mathf.Abs(p.x) >= halfWidth)
        {
            p.x = Mathf.Clamp(p.x, -halfWidth, halfWidth);
            _direction = -_direction;
            Laps++;
            AgentEvents.Emit("walker.turned", new { laps = Laps, at = p }, important: Laps % 2 == 0);
        }
        transform.position = p;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        if (FindFirstObjectByType<DemoWalker>() != null) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "DemoWalker";
        go.AddComponent<DemoWalker>();
    }
}
