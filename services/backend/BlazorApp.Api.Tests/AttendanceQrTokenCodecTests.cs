using System.Security.Cryptography;
using BlazorApp.Shared.Security;
using BlazorApp.Api.Security;
using AttendanceQrKeyDataProtection = BlazorApp.Api.Security.AttendanceQrKeyDataProtection;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class AttendanceQrTokenCodecTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private static readonly DateTime IssuedAt = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EncryptAndDecrypt_ValidA256GcmToken_ReturnsCompactPayload()
    {
        var expected = CreatePayload();

        var token = AttendanceQrTokenCodec.Encrypt(expected, "K20260716", Key);
        var verified = AttendanceQrTokenCodec.TryDecrypt(
            token,
            new Dictionary<string, byte[]> { ["K20260716"] = Key },
            IssuedAt.AddSeconds(14),
            out var actual,
            out var kid,
            out var errorCode);

        Assert.True(verified);
        Assert.StartsWith("HBATE1.K20260716.", token, StringComparison.Ordinal);
        Assert.True(token.Length < 200);
        Assert.Equal(string.Empty, errorCode);
        Assert.Equal("K20260716", kid);
        Assert.Equal(expected.TokenId, actual!.TokenId);
        Assert.Equal("BRI", actual.StoreCode);
        Assert.Equal("POS-001", actual.DeviceCode);
        Assert.Equal(IssuedAt, actual.IssuedAtUtc);
        Assert.Equal(IssuedAt.AddSeconds(15), actual.ExpiresAtUtc);
    }

    [Theory]
    [InlineData(-1, "ATTENDANCE_QR_NOT_ACTIVE")]
    [InlineData(15, "ATTENDANCE_QR_EXPIRED")]
    public void TryDecrypt_OutsideStrictFifteenSecondWindow_Rejects(int seconds, string expectedError)
    {
        var token = AttendanceQrTokenCodec.Encrypt(CreatePayload(), "K1", Key);

        var verified = AttendanceQrTokenCodec.TryDecrypt(
            token,
            new Dictionary<string, byte[]> { ["K1"] = Key },
            IssuedAt.AddSeconds(seconds),
            out _, out _, out var errorCode);

        Assert.False(verified);
        Assert.Equal(expectedError, errorCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void TryDecryptIdentity_TamperedAuthenticatedSegment_Rejects(int segment)
    {
        var token = AttendanceQrTokenCodec.Encrypt(CreatePayload(), "K1", Key);
        var parts = token.Split('.');
        parts[segment] = Mutate(parts[segment]);

        var verified = AttendanceQrTokenCodec.TryDecryptIdentity(
            string.Join('.', parts),
            Key,
            out _, out _, out var errorCode);

        Assert.False(verified);
        Assert.Equal("ATTENDANCE_QR_AUTH_INVALID", errorCode);
    }

    [Theory]
    [InlineData("bad-token")]
    [InlineData("HBATE1.K1.bad.bad.bad.extra")]
    [InlineData("HBATE1.K1.AA.AA.AA")]
    [InlineData("HBATE1.K1.AAAAAAAAAAAAAAAA.A=.AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("HBATE1.bad+kid.AAAAAAAAAAAAAAAA.AA.AAAAAAAAAAAAAAAAAAAAAA")]
    public void TryGetKeyId_MalformedToken_RejectsFormat(string token)
    {
        Assert.False(AttendanceQrTokenCodec.TryGetKeyId(token, out _, out var errorCode));
        Assert.Equal("ATTENDANCE_QR_FORMAT_INVALID", errorCode);
    }

    [Fact]
    public void TryGetKeyId_OversizedToken_RejectsFormat()
    {
        var token = new string('A', AttendanceQrTokenCodec.MaxTokenLength + 1);

        Assert.False(AttendanceQrTokenCodec.TryGetKeyId(token, out _, out var errorCode));
        Assert.Equal("ATTENDANCE_QR_FORMAT_INVALID", errorCode);
    }

    [Fact]
    public void Encrypt_KeyIsNotExactlyThirtyTwoBytes_Rejects()
    {
        Assert.Throws<ArgumentException>(() =>
            AttendanceQrTokenCodec.Encrypt(CreatePayload(), "K1", new byte[31]));
    }

    [Fact]
    public void AttendanceProtector_SharedKeyRingDecryptsAcrossProvidersAndRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"attendance-dp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            var first = AttendanceQrKeyDataProtection.CreateProtector(
                AttendanceQrKeyDataProtection.CreateProvider(path));
            var protectedKey = first.Protect(Key);

            var second = AttendanceQrKeyDataProtection.CreateProtector(
                AttendanceQrKeyDataProtection.CreateProvider(path));
            var unprotected = second.Unprotect(protectedKey);

            Assert.NotEqual(Convert.ToBase64String(Key), protectedKey);
            Assert.Equal(Key, unprotected);
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void AttendanceProtector_UsesSharedApplicationNameAndPurposeConstants()
    {
        Assert.Equal(
            BlazorApp.Shared.Security.AttendanceQrKeyDataProtection.ApplicationName,
            BlazorApp.Api.Security.AttendanceQrKeyDataProtection.ApplicationName);
        Assert.Equal(
            BlazorApp.Shared.Security.AttendanceQrKeyDataProtection.Purpose,
            BlazorApp.Api.Security.AttendanceQrKeyDataProtection.Purpose);
    }

    private static AttendanceQrTokenPayload CreatePayload() => new()
    {
        TokenId = Guid.Parse("633344a3-9d80-4d5f-8e19-c8d317d025e2"),
        StoreCode = "BRI",
        DeviceCode = "POS-001",
        IssuedAtUtc = IssuedAt,
    };

    private static string Mutate(string value)
    {
        var index = value.Length / 2;
        return value[..index] + (value[index] == 'A' ? 'B' : 'A') + value[(index + 1)..];
    }
}
