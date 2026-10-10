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
            var seq = o["seq"];
            if (seq == null || seq.Type != JTokenType.Integer)
                throw new ProtocolException("Missing integer field 'seq'");
            return new Envelope((string)type, (long)seq, o);
        }
    }
}
