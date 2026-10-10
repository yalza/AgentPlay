using System;
using UnityEngine;

namespace AgentPlay.Tests
{
    public enum FakeLevel { Calm, Alert }

    [AgentGame("FakeGame", Version = "7")]
    public class FakeGame
    {
        [AgentObserve("noise.value", "Noise 0-100")] public float Noise = 12.3456f;
        [AgentObserve("sack.tiles")] public int Tiles { get; set; } = 4;
        [AgentObserve("sack.full")] public bool Full => false;
        [AgentObserve("player.name")] public string Name = "thief";
        [AgentObserve("noise.level")] public FakeLevel Level = FakeLevel.Alert;
        [AgentObserve("player.pos")] public Vector3 Pos = new Vector3(1f, 2f, 3.00049f);
        [AgentObserve("game.round")] public static int Round = 2;

        [AgentAction("goto", "Walk to a zone")] public AgentActionStatus GoTo(string zone, float speed) => AgentActionStatus.Done;
        [AgentAction("hide", Safe = true)] public AgentActionStatus Hide() => AgentActionStatus.Done;
    }

    public class FakeBroken
    {
        [AgentObserve("noise.value")] public float Duplicate;                 // duplicate path
        [AgentObserve("")] public float Empty;                                 // empty path
        [AgentObserve("bad.type")] public object Unsupported;                  // unsupported type
        [AgentObserve("write.only")] public int WriteOnly { set { } }          // no getter
        [AgentAction("wrong.return")] public void WrongReturn() { }             // must return AgentActionStatus
        [AgentAction("bad.param")] public AgentActionStatus BadParam(Vector3 v) => AgentActionStatus.Done;
        [AgentAction("goto")] public AgentActionStatus DuplicateAction() => AgentActionStatus.Done;
        [AgentAction("rest", Safe = true)] public AgentActionStatus SecondSafe() => AgentActionStatus.Done;
    }

    [AgentGame("Dynamic", VersionMember = nameof(ComputeVersion))]
    public class FakeDynamicVersion
    {
        public static string ComputeVersion() => "hash-abc";
    }

    [AgentGame("Throwing", VersionMember = nameof(Boom))]
    public class FakeThrowingVersion
    {
        public static string Boom => throw new InvalidOperationException("no config");
    }

    [AgentGame("Second")]
    public class FakeSecondGame { }

    public class FakeThrowingGetter
    {
        [AgentObserve("explodes")] public float Explodes => throw new InvalidOperationException("kaboom");
        [AgentObserve("not.a.number")] public float NaN = float.NaN;
    }
}
