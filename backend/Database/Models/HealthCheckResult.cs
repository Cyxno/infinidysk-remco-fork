namespace NzbWebDAV.Database.Models;

public class HealthCheckResult
{
    public Guid Id { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid DavItemId { get; init; }
    public string Path { get; init; } = null!;
    public string? NzbFileName { get; init; }
    public string? JobName { get; init; }
    public HealthResult Result { get; init; }
    public RepairAction RepairStatus { get; set; }
    public string? Message { get; set; }

    public enum HealthResult
    {
        Healthy = 0,
        Unhealthy = 1,

        /// <summary>
        /// Confirmed segment holes within the degraded-damage tolerance caps in a
        /// resync-tolerant container. Playback zero-fills the gaps; repair is skipped.
        /// </summary>
        Degraded = 2,
    }

    public enum RepairAction
    {
        None = 0,
        Repaired = 1,
        Deleted = 2,
        ActionNeeded = 3,
        RepairedViaPar2 = 4,
    }
}
