using UnityEngine;

namespace AgentPlay.Tests
{
    public class FakeObservedBehaviour : MonoBehaviour
    {
        [AgentObserve("fake.hp")] public int Hp = 10;
    }
}
