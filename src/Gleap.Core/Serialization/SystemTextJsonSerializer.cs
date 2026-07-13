using System.Text.Json;
using System.Text.Json.Serialization;

namespace GleapSDK.Serialization;

public sealed class SystemTextJsonSerializer : IJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Serialize(object value) => JsonSerializer.Serialize(value, Options);

    public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
}
