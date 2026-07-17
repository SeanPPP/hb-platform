using System.Security.Claims;
using System.Reflection;
using System.Text.RegularExpressions;
using Hbpos.Api.Auth;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Hbpos.Contracts.Attendance;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Hbpos.Api.Tests;

public sealed class AttendanceSigningKeysControllerTests
{
    [Fact]
    public void Controller_RequiresOnlyDeviceAuthenticationScheme()
    {
        var authorize = Assert.Single(typeof(AttendanceSigningKeysController)
            .GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(DeviceAuthConstants.Scheme, authorize.AuthenticationSchemes);
        Assert.Null(authorize.Policy);
    }

    [Fact]
    public void ValidateRequest_AcceptsOnlyA256GcmThirtyTwoByteKey()
    {
        var keyMaterial = Base64UrlEncode(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        AttendanceSigningKeyRegistrationService.ValidateRequest(
            new("K1", "A256GCM", keyMaterial));

        Assert.Throws<AttendanceSigningKeyValidationException>(() =>
            AttendanceSigningKeyRegistrationService.ValidateRequest(
                new("K1", "ES256", keyMaterial)));
        Assert.Throws<AttendanceSigningKeyValidationException>(() =>
            AttendanceSigningKeyRegistrationService.ValidateRequest(
                new("K1", "A256GCM", Base64UrlEncode(new byte[31]))));
        Assert.Throws<AttendanceSigningKeyValidationException>(() =>
            AttendanceSigningKeyRegistrationService.ValidateRequest(
                new("bad+kid", "A256GCM", keyMaterial)));
    }

    [Fact]
    public void ValidateRequest_KeyMaterialLengthIsNotExactlyFortyThree_RejectsBeforeDecode()
    {
        Assert.Throws<AttendanceSigningKeyValidationException>(() =>
            AttendanceSigningKeyRegistrationService.ValidateRequest(
                new("K1", "A256GCM", new string('A', 44))));
    }

    [Theory]
    [InlineData(51000, true)]
    [InlineData(2601, true)]
    [InlineData(2627, true)]
    [InlineData(1205, true)]
    [InlineData(50000, false)]
    public void IsRegistrationConflictSqlErrorNumber_ClassifiesExpectedErrors(
        int number,
        bool expected)
    {
        Assert.Equal(
            expected,
            AttendanceSigningKeyRegistrationService.IsRegistrationConflictSqlErrorNumber(number));
    }

    [Fact]
    public async Task AttendanceKeyServices_ClearUnprotectedAesBuffers()
    {
        var root = FindRepoRoot();
        var registration = await File.ReadAllTextAsync(Path.Combine(
            root,
            "apps/pos-wpf/src/Hbpos.Api/Services/AttendanceSigningKeyRegistrationService.cs"));
        var attendance = await File.ReadAllTextAsync(Path.Combine(
            root,
            "services/backend/BlazorApp.Api/Services/React/AttendanceReactService.cs"));

        Assert.Contains("CryptographicOperations.ZeroMemory(key)", registration);
        Assert.Contains("CryptographicOperations.ZeroMemory(storedKey)", registration);
        Assert.Equal(2, Regex.Matches(attendance, "CryptographicOperations\\.ZeroMemory\\(qrKey\\)").Count);
    }

    [Theory]
    [InlineData(true, "ATTENDANCE_QR_KEY_INVALID")]
    [InlineData(false, "ATTENDANCE_QR_KEY_KID_CONFLICT")]
    public async Task Register_MapsExpectedFailuresWithoutLeakingInternalMessage(bool invalid, string expectedCode)
    {
        var controller = CreateController(new ThrowingService(invalid), includeHardwareClaim: true);
        var result = await controller.Register(new("K1", "A256GCM", "key"), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(invalid ? 400 : 409, objectResult.StatusCode);
        var body = Assert.IsType<ApiResult<AttendanceSigningKeyRegistrationResponse>>(objectResult.Value);
        Assert.Equal(expectedCode, body.ErrorCode);
        Assert.DoesNotContain("internal-secret", body.Message);
    }
    [Fact]
    public void RegistrationSql_PreservesKidIdentityAndRevokesDeviceOrHardwareHistory()
    {
        var sql = AttendanceSigningKeyRegistrationService.RegistrationSql;

        Assert.Contains("[StoreCode] <> @StoreCode", sql);
        Assert.Contains("[DeviceCode] <> @DeviceCode", sql);
        Assert.Contains("[HardwareId] <> @HardwareId", sql);
        Assert.Contains("[Algorithm] <> @Algorithm", sql);
        Assert.Contains("[Kid], [Algorithm], [ProtectedKey]", sql);
        Assert.Contains("[Status] <> N'Active'", sql);
        Assert.Contains("[DeviceCode] = @DeviceCode OR [HardwareId] = @HardwareId", sql);
        Assert.Contains("@ExistingRegisteredAtUtc", sql);
        Assert.Contains("SELECT [RegisteredAtUtc], [ProtectedKey]", sql);
    }

    [Fact]
    public async Task Register_UsesAuthenticatedDeviceClaimsOnly()
    {
        var service = new CapturingService();
        var controller = CreateController(service, includeHardwareClaim: true);
        var request = new AttendanceSigningKeyRegistrationRequest(
            "K1",
            "A256GCM",
            Base64UrlEncode(new byte[32]));

        var result = await controller.Register(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(ok.Value);
        Assert.Equal(new AttendanceSigningKeyDeviceIdentity("POS-001", "BRI", "HW-001"), service.Identity);
    }

    [Fact]
    public async Task Register_WhenDeviceClaimMissing_ReturnsUnauthorized()
    {
        var service = new CapturingService();
        var controller = CreateController(service, includeHardwareClaim: false);

        var result = await controller.Register(
            new AttendanceSigningKeyRegistrationRequest("K1", "A256GCM", "key"),
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Null(service.Identity);
    }

    private static AttendanceSigningKeysController CreateController(
        IAttendanceSigningKeyRegistrationService service,
        bool includeHardwareClaim)
    {
        var claims = new List<Claim>
        {
            new(DeviceAuthConstants.DeviceCodeClaim, "POS-001"),
            new(DeviceAuthConstants.StoreCodeClaim, "BRI"),
        };
        if (includeHardwareClaim)
        {
            claims.Add(new Claim(DeviceAuthConstants.HardwareIdClaim, "HW-001"));
        }

        return new AttendanceSigningKeysController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, DeviceAuthConstants.Scheme)),
                },
            },
        };
    }

    private sealed class CapturingService : IAttendanceSigningKeyRegistrationService
    {
        public AttendanceSigningKeyDeviceIdentity? Identity { get; private set; }

        public Task<AttendanceSigningKeyRegistrationResponse> RegisterAsync(
            AttendanceSigningKeyDeviceIdentity identity,
            AttendanceSigningKeyRegistrationRequest request,
            CancellationToken cancellationToken)
        {
            Identity = identity;
            var now = new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
            return Task.FromResult(new AttendanceSigningKeyRegistrationResponse(request.Kid, now, now));
        }
    }

    private sealed class ThrowingService(bool invalid) : IAttendanceSigningKeyRegistrationService
    {
        public Task<AttendanceSigningKeyRegistrationResponse> RegisterAsync(
            AttendanceSigningKeyDeviceIdentity identity,
            AttendanceSigningKeyRegistrationRequest request,
            CancellationToken cancellationToken) => throw (invalid
                ? new AttendanceSigningKeyValidationException("internal-secret")
                : new AttendanceSigningKeyConflictException("internal-secret"));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "apps"))
                && Directory.Exists(Path.Combine(directory.FullName, "services")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("找不到仓库根目录");
    }
}
