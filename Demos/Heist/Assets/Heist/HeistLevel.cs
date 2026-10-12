using System.Collections.Generic;
using UnityEngine;

namespace HeistDemo
{
    /// <summary>Fixed layout of the heist room (x -10..10, z -6..6).</summary>
    public static class HeistLevel
    {
        public const float ZoneRadius = 1f;

        public static readonly Dictionary<string, Vector3> Zones = new Dictionary<string, Vector3>
        {
            { "start", new Vector3(-8f, 0f, 0f) },
            { "exit", new Vector3(-9f, 0f, -5f) },
            { "item_a", new Vector3(-3f, 0f, 4f) },
            { "item_b", new Vector3(4f, 0f, 4f) },
            { "item_c", new Vector3(4f, 0f, -4f) },
            { "cover_1", new Vector3(2f, 0f, 0f) },
            { "cover_2", new Vector3(-5f, 0f, -2f) },
        };

        /// <summary>Box that hides a thief crouching at the cover spot of the same name.</summary>
        public static readonly Dictionary<string, Vector3> CoverBoxes = new Dictionary<string, Vector3>
        {
            { "cover_1", new Vector3(3f, 0.6f, 0f) },
            { "cover_2", new Vector3(-4f, 0.6f, -2f) },
        };

        public static readonly Vector3 CoverBoxSize = new Vector3(0.6f, 1.2f, 3f);

        public static readonly Vector3[] GuardRoute = { new Vector3(6f, 1f, 5f), new Vector3(6f, 1f, -5f) };

        public static readonly string[] ItemNames = { "item_a", "item_b", "item_c" };

        /// <summary>Name of the zone within ZoneRadius of <paramref name="position"/> on the floor plane, or "".</summary>
        public static string ZoneAt(Vector3 position)
        {
            string best = "";
            float bestDistance = ZoneRadius;
            foreach (var zone in Zones)
            {
                float d = FlatDistance(position, zone.Value);
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = zone.Key;
                }
            }
            return best;
        }

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
