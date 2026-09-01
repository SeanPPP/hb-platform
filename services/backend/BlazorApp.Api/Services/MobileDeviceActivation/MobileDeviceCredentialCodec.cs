using System.Security.Cryptography;
using System.Text;

namespace BlazorApp.Api.Services.MobileDeviceActivation;

public static class MobileDeviceCredentialCodec
{
    public const int MinimumCredentialLength = 16;
    public const int MaximumCredentialLength = 256;

    public static bool IsCredentialShapeValid(string? credential) =>
        !string.IsNullOrWhiteSpace(credential)
        && credential.Length is >= MinimumCredentialLength and <= MaximumCredentialLength;

    public static string HashCredentialToHex(string credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);
        var credentialBytes = Encoding.UTF8.GetBytes(credential);
        byte[]? hash = null;
        try
        {
            hash = SHA256.HashData(credentialBytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credentialBytes);
            if (hash != null)
            {
                CryptographicOperations.ZeroMemory(hash);
            }
        }
    }

    public static bool TryParseVerifier(string? verifier, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (verifier is not { Length: 64 }
            || verifier.Any(character => character is not (>= '0' and <= '9')
                and not (>= 'a' and <= 'f')))
        {
            return false;
        }

        bytes = Convert.FromHexString(verifier);
        return bytes.Length == SHA256.HashSizeInBytes;
    }

    public static bool MatchesCredential(ReadOnlySpan<byte> expectedVerifier, string? credential)
    {
        if (expectedVerifier.Length != SHA256.HashSizeInBytes
            || !IsCredentialShapeValid(credential))
        {
            return false;
        }

        var credentialBytes = Encoding.UTF8.GetBytes(credential!);
        Span<byte> actual = stackalloc byte[SHA256.HashSizeInBytes];
        try
        {
            SHA256.HashData(credentialBytes, actual);
            return CryptographicOperations.FixedTimeEquals(expectedVerifier, actual);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credentialBytes);
            CryptographicOperations.ZeroMemory(actual);
        }
    }

    public static bool MatchesVerifier(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) =>
        left.Length == SHA256.HashSizeInBytes
        && right.Length == SHA256.HashSizeInBytes
        && CryptographicOperations.FixedTimeEquals(left, right);
}
