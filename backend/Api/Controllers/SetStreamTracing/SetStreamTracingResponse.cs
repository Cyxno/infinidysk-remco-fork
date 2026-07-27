using System.Text.Json.Serialization;
using NzbWebDAV.Services.StreamTrace;

namespace NzbWebDAV.Api.Controllers.SetStreamTracing;

public sealed class SetStreamTracingResponse : BaseApiResponse
{
    [JsonPropertyName("enabled")] public required bool Enabled { get; init; }
    [JsonPropertyName("retained")] public required bool Retained { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("expiresAtUnixMs")] public required long ExpiresAtUnixMs { get; init; }
    [JsonPropertyName("retainedUntilUnixMs")] public required long RetainedUntilUnixMs { get; init; }
    [JsonPropertyName("capacity")] public required int Capacity { get; init; }
    [JsonPropertyName("eventCount")] public required long EventCount { get; init; }
    [JsonPropertyName("sessionCount")] public required int SessionCount { get; init; }

    public static SetStreamTracingResponse From(StreamTraceStatus status) => new()
    {
        Status = true,
        Enabled = status.Enabled,
        Retained = status.Retained,
        Source = status.Source,
        ExpiresAtUnixMs = status.ExpiresAtUnixMs,
        RetainedUntilUnixMs = status.RetainedUntilUnixMs,
        Capacity = status.Capacity,
        EventCount = status.EventCount,
        SessionCount = status.SessionCount,
    };
}
