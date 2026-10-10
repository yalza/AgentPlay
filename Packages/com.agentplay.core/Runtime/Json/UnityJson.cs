using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AgentPlay
{
    public static class UnityJson
    {
        public static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Converters = new List<JsonConverter> { new Vector3Converter(), new Vector2Converter(), new StringEnumConverter() },
        });

        /// <summary>Turns any payload into a JSON object. Never throws: failures become {"error": ...}.</summary>
        public static JObject ToPayload(object payload)
        {
            if (payload == null) return null;
            if (payload is JObject jo) return jo;
            JToken token;
            try
            {
                token = JToken.FromObject(payload, Serializer);
            }
            catch (Exception e)
            {
                return new JObject { ["error"] = "Payload could not be serialized: " + e.Message };
            }
            return token as JObject ?? new JObject { ["value"] = token };
        }

        /// <summary>Rounds to 3 decimals; NaN and infinity become null so the JSON stays valid.</summary>
        public static double? Clean(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return null;
            return Math.Round(d, 3);
        }

        internal static void WriteNumber(JsonWriter writer, float f)
        {
            var c = Clean(f);
            if (c.HasValue) writer.WriteValue(c.Value);
            else writer.WriteNull();
        }

        sealed class Vector3Converter : JsonConverter<Vector3>
        {
            public override bool CanRead => false;

            public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
            {
                writer.WriteStartArray();
                WriteNumber(writer, value.x);
                WriteNumber(writer, value.y);
                WriteNumber(writer, value.z);
                writer.WriteEndArray();
            }

            public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue, bool hasExistingValue, JsonSerializer serializer)
                => throw new NotSupportedException();
        }

        sealed class Vector2Converter : JsonConverter<Vector2>
        {
            public override bool CanRead => false;

            public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer)
            {
                writer.WriteStartArray();
                WriteNumber(writer, value.x);
                WriteNumber(writer, value.y);
                writer.WriteEndArray();
            }

            public override Vector2 ReadJson(JsonReader reader, Type objectType, Vector2 existingValue, bool hasExistingValue, JsonSerializer serializer)
                => throw new NotSupportedException();
        }
    }
}
