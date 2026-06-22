namespace Hbpos.Contracts.Square;

public sealed record SquareTokenResponse(
    string Environment,
    string AccessToken,
    DateTimeOffset UpdatedAt);

public sealed record SquareTokenStatusResponse(
    string Environment,
    bool Configured,
    bool Enabled,
    DateTimeOffset UpdatedAt);
