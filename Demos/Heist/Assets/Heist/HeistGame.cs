using System.Collections.Generic;
using System.Linq;
using AgentPlay;
using UnityEngine;

namespace HeistDemo
{
    [AgentGame("AgentPlay Heist", Version = "m2")]
    [AgentEventInfo("item.picked", "The thief picked up an item")]
    [AgentEventInfo("guard.spotted", "A guard started seeing the thief", Important = true, Outcome = AgentOutcome.Bad)]
    [AgentEventInfo("guard.lost_sight", "A guard stopped seeing the thief")]
    [AgentEventInfo("game.won", "The thief left through the exit with all three items", Important = true, Outcome = AgentOutcome.Good)]
    [AgentEventInfo("game.lost", "A guard caught the thief", Important = true, Outcome = AgentOutcome.Bad)]
    public sealed class HeistGame : MonoBehaviour
    {
        public static HeistGame Instance { get; private set; }
        /// <summary>"won" or "lost" once the round is over; null while playing.</summary>
        public static string Result { get; private set; }

        public HeistPlayer Player { get; private set; }
        public readonly List<HeistGuard> GuardList = new List<HeistGuard>();
        public readonly Dictionary<string, GameObject> Items = new Dictionary<string, GameObject>();

        [AgentObserve("items.left", "Items still lying in the room")]
        public int ItemsLeft => Items.Values.Count(item => item.activeSelf);

        [AgentObserveList("guards", "Guards in the room")]
        public IEnumerable<HeistGuard> Guards => GuardList;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Build()
        {
            if (Instance != null) return;
            Result = null;
            Instance = new GameObject("HeistGame").AddComponent<HeistGame>();
            Instance.BuildLevel();
        }

        void BuildLevel()
        {
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(0f, 20f, 0f), Quaternion.Euler(90f, 0f, 0f));
            camera.orthographic = true;
            camera.orthographicSize = 7f;
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(2f, 1f, 1.2f);

            foreach (var box in HeistLevel.CoverBoxes)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Cover_" + box.Key;
                go.transform.position = box.Value;
                go.transform.localScale = HeistLevel.CoverBoxSize;
            }

            foreach (var name in HeistLevel.ItemNames)
            {
                var item = Visual(PrimitiveType.Cube, "Item_" + name, HeistLevel.Zones[name] + Vector3.up * 0.2f, Color.yellow);
                item.transform.localScale = Vector3.one * 0.4f;
                Items[name] = item;
            }
            Visual(PrimitiveType.Cylinder, "Exit", HeistLevel.Zones["exit"], Color.green).transform.localScale = new Vector3(1.6f, 0.02f, 1.6f);

            Player = Visual(PrimitiveType.Capsule, "Thief", HeistLevel.Zones["start"] + Vector3.up, Color.blue).AddComponent<HeistPlayer>();
            GuardList.Add(Visual(PrimitiveType.Capsule, "Guard", HeistLevel.GuardRoute[0], Color.red).AddComponent<HeistGuard>());
        }

        /// <summary>A primitive without a collider, so guard sight rays only ever hit cover boxes.</summary>
        static GameObject Visual(PrimitiveType type, string name, Vector3 position, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.position = position;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().material.color = color;
            return go;
        }

        public void CheckWin()
        {
            if (Result != null || Player.Carried.Count < HeistLevel.ItemNames.Length || Player.Zone != "exit") return;
            Result = "won";
            Debug.Log("[Heist] result=won");
            AgentEvents.Emit("game.won", new { items = Player.Carried.Count }, important: true, outcome: AgentOutcome.Good);
        }

        public void Lose(string guard)
        {
            if (Result != null) return;
            Result = "lost";
            Debug.Log("[Heist] result=lost");
            AgentEvents.Emit("game.lost", new { guard }, important: true, outcome: AgentOutcome.Bad);
        }
    }
}
