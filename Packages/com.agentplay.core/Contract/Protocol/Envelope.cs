using Newtonsoft.Json.Linq;

namespace AgentPlay.Contract
{
    /// <summary>A decoded message. Body is the whole JSON object, including "type" and "seq".</summary>
    public sealed class Envelope
    {
        public string Type { get; }
        public long Seq { get; }
        public JObject Body { get; }

        public Envelope(string type, long seq, JObject body)
        {
            Type = type;
            Seq = seq;
            Body = body;
        }

        public T As<T>() => Body.ToObject<T>(ContractJson.Serializer);
    }
}
