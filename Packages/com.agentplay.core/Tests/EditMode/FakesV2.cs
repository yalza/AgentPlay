using System.Collections.Generic;
using System.Linq;

namespace AgentPlay.Tests
{
    public class FakeGuardView
    {
        [AgentObserve("distance", "Metres to the player")] public float Distance;
        [AgentObserve("sees_player")] public bool SeesPlayer;
        [AgentObserve("ignored.static")] public static int Static = 1;
    }

    [AgentEventInfo("guard.spotted", "Guard sees the player", Important = true, Outcome = AgentOutcome.Bad)]
    [AgentEventInfo("game.won", Outcome = AgentOutcome.Good)]
    public class FakeHeist
    {
        public List<FakeGuardView> GuardList = new List<FakeGuardView>
        {
            new FakeGuardView { Distance = 4.25f },
            new FakeGuardView { Distance = 9f, SeesPlayer = true },
        };

        [AgentObserveList("guards", "Guards in the level")] public IEnumerable<FakeGuardView> Guards => GuardList;

        [AgentAction("goto")] public AgentActionStatus GoTo(AgentActionContext ctx, string zone) => AgentActionStatus.Done;
        [AgentAction("stop", Safe = true)] public AgentActionStatus Stop() => AgentActionStatus.Done;
    }

    public class FakeBadV2
    {
        [AgentObserveList("nothing")] public IEnumerable<int> Ints => null;
        [AgentObserveList("notlist")] public int NotList;
        [AgentAction("ctx.late")] public AgentActionStatus Late(string a, AgentActionContext ctx) => AgentActionStatus.Done;
        [AgentAction("safe.args", Safe = true)] public AgentActionStatus SafeArgs(int x) => AgentActionStatus.Done;
    }

    [AgentEventInfo("dup")]
    [AgentEventInfo("dup")]
    public class FakeDupEvents { }

    public class FakeManyGuards
    {
        [AgentObserveList("crowd")]
        public IEnumerable<FakeGuardView> Crowd => Enumerable.Range(0, 20).Select(i => new FakeGuardView { Distance = i });
    }
}
