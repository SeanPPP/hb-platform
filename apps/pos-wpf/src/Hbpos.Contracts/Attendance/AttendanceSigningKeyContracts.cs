namespace Hbpos.Contracts.Attendance;

public sealed record AttendanceSigningKeyRegistrationRequest(
    string Kid,
    string Algorithm,
    string KeyMaterial);

public sealed record AttendanceSigningKeyRegistrationResponse(
    string Kid,
    DateTime RegisteredAtUtc,
    DateTime ServerTimeUtc);
