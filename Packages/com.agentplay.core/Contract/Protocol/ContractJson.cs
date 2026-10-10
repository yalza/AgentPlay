using Newtonsoft.Json;

namespace AgentPlay.Contract
{
    public static class ContractJson
    {
        public static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Include,
            FloatParseHandling = FloatParseHandling.Double,
        });
    }
}
