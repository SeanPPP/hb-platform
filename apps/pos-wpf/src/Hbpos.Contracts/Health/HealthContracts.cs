namespace Hbpos.Contracts.Health;

public sealed record HealthCheckResponse(
    bool IsOnline,
    DateTimeOffset ServerTime,
    string Status);
