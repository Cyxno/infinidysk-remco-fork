using System.Text.Json;
using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.RadarrSonarr.BaseModels;

public class ArrField
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("label")]
    public string Label { get; set; } = null!;

    [JsonPropertyName("value")]
    public JsonElement? ValueJson { get; set; }

    public object? Value => ValueJson?.ValueKind switch
    {
        null => null,
        JsonValueKind.Null => null,
        JsonValueKind.String => ValueJson?.ToString(),
        JsonValueKind.Number => ValueJson?.GetInt64(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => ValueJson?.GetRawText(),
    };
}
