using Newtonsoft.Json;

namespace AgentPlay.Contract
{
    public static class ContractJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Include,
            FloatParseHandling = FloatParseHandling.Double,
        };

        public static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);
    }
}
