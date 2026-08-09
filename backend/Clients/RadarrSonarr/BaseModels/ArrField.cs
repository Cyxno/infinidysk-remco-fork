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

    public object? Value
    {
        get
        {
            if (ValueJson is not { } json) return null;
            return json.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => json.ToString(),
                JsonValueKind.Number => json.GetInt64(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => json.GetRawText(),
            };
        }
    }
}
