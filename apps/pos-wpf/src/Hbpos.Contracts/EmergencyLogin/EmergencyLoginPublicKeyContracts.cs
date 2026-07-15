using System.Text.Json.Serialization;

namespace Hbpos.Contracts.EmergencyLogin;

public sealed record EmergencyLoginPublicKey(
    string Kid,
    string Algorithm,
    [property: JsonPropertyName("pem")]
    string PublicKeyPem,
    string Fingerprint);

public sealed record EmergencyLoginPublicKeyPackage(
    long Version,
    string? ActiveKeyId,
    DateTime GeneratedAtUtc,
    IReadOnlyList<EmergencyLoginPublicKey> Keys);

public sealed record EmergencyLoginPublicKeyAckRequest(long Version);

public sealed record EmergencyLoginPublicKeyAckResponse(long Version);
