using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Contract
{
    public static class MessageCodec
    {
        /// <summary>Returns one line of JSON (no trailing newline). Does not modify <paramref name="body"/>.</summary>
        public static string Encode(string type, long seq, JObject body)
        {
            var o = body != null ? (JObject)body.DeepClone() : new JObject();
            o["type"] = type;
            o["seq"] = seq;
            return o.ToString(Formatting.None);
        }

        public static string Encode<T>(string type, long seq, T dto)
        {
            var body = dto == null ? null : JObject.FromObject(dto, ContractJson.Serializer);
            return Encode(type, seq, body);
        }

        public static Envelope Decode(string line)
        {
            JObject o;
            try
            {
                // DateParseHandling.None keeps ISO strings as strings instead of turning them into DateTime.
                using (var reader = new JsonTextReader(new StringReader(line)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
                {
                    o = JObject.Load(reader);
                    if (reader.Read()) throw new ProtocolException("Unexpected content after the JSON object");
                }
            }
            catch (JsonException e)
            {
                throw new ProtocolException("Invalid JSON: " + e.Message);
            }
            var type = o["type"];
            if (type == null || type.Type != JTokenType.String)
                throw new ProtocolException("Missing string field 'type'");
            // Integers beyond long parse as BigInteger; reject them instead of overflowing on the cast.
            if (!(o["seq"] is JValue seq) || !(seq.Value is long seqValue))
                throw new ProtocolException("Missing integer field 'seq' (64-bit)");
            return new Envelope((string)type, seqValue, o);
        }
    }
}
