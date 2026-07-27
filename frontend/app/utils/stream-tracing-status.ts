export type StreamTracingStatus = {
    enabled: boolean;
    source: string;
    expiresAtUnixMs: number;
    capacity: number;
    eventCount: number;
    sessionCount: number;
    retained: boolean;
    retainedUntilUnixMs: number;
};

export function toStreamTracingStatus(data: Record<string, unknown>): StreamTracingStatus {
    return {
        enabled: Boolean(data.enabled),
        source: typeof data.source === "string" ? data.source : "env",
        expiresAtUnixMs: Number(data.expiresAtUnixMs ?? 0),
        capacity: Number(data.capacity ?? 0),
        eventCount: Number(data.eventCount ?? 0),
        sessionCount: Number(data.sessionCount ?? 0),
        retained: Boolean(data.retained),
        retainedUntilUnixMs: Number(data.retainedUntilUnixMs ?? 0),
    };
}
