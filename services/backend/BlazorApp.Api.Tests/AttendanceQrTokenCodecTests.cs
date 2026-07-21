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

    [Fact]
    public void Backend_UsesDedicatedAttendanceKeyRingWithoutChangingGlobalKeyRing()
    {
        var repoRoot = FindRepoRoot();
        var program = File.ReadAllText(
            Path.Combine(repoRoot, "services", "backend", "BlazorApp.Api", "Program.cs"));
        var compose = File.ReadAllText(
            Path.Combine(repoRoot, "services", "backend", "docker-compose.yml"));
        var posCompose = File.ReadAllText(
            Path.Combine(repoRoot, "apps", "pos-wpf", "docker-compose.hotbargain.yml"));
        var backendGitIgnore = File.ReadAllText(
            Path.Combine(repoRoot, "services", "backend", ".gitignore"));
        const string requiredAttendanceMount =
            "${ATTENDANCE_QR_DATA_PROTECTION_KEYS_HOST_PATH:?required}:/app/App_Data/AttendanceQrDataProtectionKeys";

        // 关键契约：全局 ring 继续服务现有密文，考勤 ring 必须由独立配置和挂载提供。
        Assert.Contains("GetValue<string>(\"DataProtection:KeysPath\")", program, StringComparison.Ordinal);
        Assert.Contains(
            "GetValue<string>(\n    \"AttendanceQrDataProtection:KeysPath\")",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            "Path.Combine(\"App_Data\", \"AttendanceQrDataProtectionKeys\")",
            program,
            StringComparison.Ordinal);
        Assert.Contains("builder.Environment.IsProduction()", program, StringComparison.Ordinal);
        Assert.Contains(
            "生产环境必须配置 AttendanceQrDataProtection:KeysPath。",
            program,
            StringComparison.Ordinal);
        Assert.True(
            program.IndexOf("builder.Environment.IsProduction()", StringComparison.Ordinal)
            < program.IndexOf(
                "attendanceQrDataProtectionKeysPath = Path.Combine(\"App_Data\", \"AttendanceQrDataProtectionKeys\")",
                StringComparison.Ordinal),
            "生产环境的 fail-closed 检查必须发生在开发默认路径回退之前。");
        Assert.Contains(
            "AttendanceQrKeyDataProtection.CreateProvider(\n            attendanceQrDataProtectionKeysPath)",
            program,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AttendanceQrKeyDataProtection.CreateProvider(dataProtectionKeysPath)",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            "AttendanceQrDataProtection__KeysPath=/app/App_Data/AttendanceQrDataProtectionKeys",
            compose,
            StringComparison.Ordinal);
        Assert.Contains(requiredAttendanceMount, compose, StringComparison.Ordinal);
        Assert.Contains(requiredAttendanceMount, posCompose, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "./attendance-qr-data-protection-keys:/app/App_Data/AttendanceQrDataProtectionKeys",
            compose,
            StringComparison.Ordinal);
        Assert.Contains(
            "./data-protection-keys:/app/App_Data/DataProtectionKeys",
            compose,
            StringComparison.Ordinal);
        Assert.Contains(
            "**/App_Data/AttendanceQrDataProtectionKeys/",
            backendGitIgnore,
            StringComparison.Ordinal);
        Assert.Contains(
            "attendance-qr-data-protection-keys/",
            backendGitIgnore,
            StringComparison.Ordinal);
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

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "services", "backend", "docker-compose.yml")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位仓库根目录。");
    }
}
