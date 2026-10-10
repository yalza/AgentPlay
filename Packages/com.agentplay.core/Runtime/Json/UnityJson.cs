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
        /// <summary>Larger payloads are replaced by an error so one event cannot flood the bridge.</summary>
        public const int MaxPayloadChars = 65536;

        public static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Converters = new List<JsonConverter>
            {
                // Unity objects are written as references: reflecting over them has side effects
                // (Renderer.material instantiates a copy) and walks whole object graphs.
                new UnityObjectConverter(),
                new Vector3Converter(), new Vector2Converter(), new QuaternionConverter(), new ColorConverter(),
                new StringEnumConverter(),
            },
        });

        /// <summary>Turns any payload into a JSON object. Never throws: failures become {"error": ...}.</summary>
        public static JObject ToPayload(object payload)
        {
            if (payload == null) return null;
            JObject result;
            if (payload is JObject jo)
            {
                result = jo;
            }
            else
            {
                JToken token;
                try
                {
                    token = JToken.FromObject(payload, Serializer);
                }
                catch (Exception e)
                {
                    return new JObject { ["error"] = "Payload could not be serialized: " + e.Message };
                }
                result = token as JObject ?? new JObject { ["value"] = token };
            }
            int size = result.ToString(Formatting.None).Length;
            if (size > MaxPayloadChars)
                return new JObject { ["error"] = "Payload too large (" + size + " chars, limit " + MaxPayloadChars + ")" };
            return result;
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

        sealed class UnityObjectConverter : JsonConverter
        {
            public override bool CanRead => false;

            public override bool CanConvert(Type objectType) => typeof(UnityEngine.Object).IsAssignableFrom(objectType);

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var obj = (UnityEngine.Object)value;
                if (obj == null)   // destroyed
                {
                    writer.WriteNull();
                    return;
                }
                writer.WriteStartObject();
                writer.WritePropertyName("type");
                writer.WriteValue(obj.GetType().Name);
                writer.WritePropertyName("name");
                writer.WriteValue(obj.name);
                writer.WritePropertyName("id");
                writer.WriteValue(obj.GetInstanceID());
                writer.WriteEndObject();
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
                => throw new NotSupportedException();
        }

        sealed class QuaternionConverter : JsonConverter<Quaternion>
        {
            public override bool CanRead => false;

            public override void WriteJson(JsonWriter writer, Quaternion value, JsonSerializer serializer)
            {
                writer.WriteStartArray();
                WriteNumber(writer, value.x);
                WriteNumber(writer, value.y);
                WriteNumber(writer, value.z);
                WriteNumber(writer, value.w);
                writer.WriteEndArray();
            }

            public override Quaternion ReadJson(JsonReader reader, Type objectType, Quaternion existingValue, bool hasExistingValue, JsonSerializer serializer)
                => throw new NotSupportedException();
        }

        sealed class ColorConverter : JsonConverter<Color>
        {
            public override bool CanRead => false;

            public override void WriteJson(JsonWriter writer, Color value, JsonSerializer serializer)
            {
                writer.WriteStartArray();
                WriteNumber(writer, value.r);
                WriteNumber(writer, value.g);
                WriteNumber(writer, value.b);
                WriteNumber(writer, value.a);
                writer.WriteEndArray();
            }

            public override Color ReadJson(JsonReader reader, Type objectType, Color existingValue, bool hasExistingValue, JsonSerializer serializer)
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
