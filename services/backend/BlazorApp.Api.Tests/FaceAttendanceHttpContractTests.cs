using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using BlazorApp.Api.Authentication;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Attendance;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>从网关转发的 multipart 请求必须穿过实际认证、授权、MVC 绑定与异常筛选器。</summary>
public sealed class FaceAttendanceHttpContractTests
{
    [Fact]
    public async Task 默认关闭时有效服务令牌仍得到503且不进入业务服务()
    {
        using var host = FaceHttpHost.Create(enabled: false, [ServiceApiScopes.AttendanceFaceGateway]);

        var response = await host.SendAsync(HttpMethod.Get, "api/internal/attendance/face/employees?storeCode=BRI");

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, (int)response.StatusCode);
        Assert.Equal("FACE_ATTENDANCE_DISABLED", await ErrorCodeAsync(response));
        Assert.Equal(0, await host.Db.Queryable<FaceAttendanceEvent>().CountAsync());
    }

    [Fact]
    public async Task 错误serviceScope在进入控制器前被拒绝()
    {
        using var host = FaceHttpHost.Create(enabled: true, [ServiceApiScopes.ReadAppUpdateDecisions]);

        var response = await host.SendAsync(HttpMethod.Get, "api/internal/attendance/face/employees?storeCode=BRI");

        Assert.Equal(StatusCodes.Status403Forbidden, (int)response.StatusCode);
        Assert.Equal(0, await host.Db.Queryable<FaceAttendanceEvent>().CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("OTHER")]
    public async Task 已启用但未列入灰度门店时不接收原始事件(string? configuredStoreCode)
    {
        using var host = FaceHttpHost.Create(enabled: true, [ServiceApiScopes.AttendanceFaceGateway], configuredStoreCode);

        var response = await host.PostEventAsync(host.NewCommand(Guid.NewGuid().ToString("D")));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, (int)response.StatusCode);
        Assert.Equal("FACE_ATTENDANCE_DISABLED", await ErrorCodeAsync(response));
        Assert.Equal(0, await host.Db.Queryable<FaceAttendanceEvent>().CountAsync());
    }

    [Fact]
    public async Task multipart元数据与JPEG通过真实HTTP绑定并保持eventGuid幂等和冲突语义()
    {
        using var host = FaceHttpHost.Create(enabled: true, [ServiceApiScopes.AttendanceFaceGateway]);
        var command = host.NewCommand(Guid.NewGuid().ToString("D"));

        var first = await host.PostEventAsync(command);
        Assert.Equal(StatusCodes.Status200OK, (int)first.StatusCode);
        var firstReceipt = await first.Content.ReadFromJsonAsync<FaceAttendanceEventDto>();
        Assert.NotNull(firstReceipt);
        Assert.Equal(command.EventGuid, firstReceipt.EventGuid);
        Assert.Equal(FaceAttendanceStatuses.EventQueued, firstReceipt.Status);
        var stored = await host.Db.Queryable<FaceAttendanceEvent>().FirstAsync(x => x.EventGuid == command.EventGuid);
        Assert.NotNull(stored);
        Assert.NotEqual(Convert.ToBase64String(host.Photo), stored.ProtectedPhoto);

        var duplicate = await host.PostEventAsync(command);
        Assert.Equal(StatusCodes.Status200OK, (int)duplicate.StatusCode);
        var duplicateReceipt = await duplicate.Content.ReadFromJsonAsync<FaceAttendanceEventDto>();
        Assert.NotNull(duplicateReceipt);
        Assert.Equal(firstReceipt.ReceivedAtUtc, duplicateReceipt.ReceivedAtUtc);
        Assert.Equal(1, await host.Db.Queryable<FaceAttendanceEvent>().CountAsync());

        command.LocalSequence++;
        host.Sign(command);
        var conflict = await host.PostEventAsync(command);
        Assert.Equal(StatusCodes.Status409Conflict, (int)conflict.StatusCode);
        Assert.Equal("EVENT_GUID_CONFLICT", await ErrorCodeAsync(conflict));
        Assert.Equal(1, await host.Db.Queryable<FaceAttendanceEvent>().CountAsync());
    }

    [Fact]
    public async Task 坏metadataJSON返回400且响应不回显照片或异常堆栈()
    {
        using var host = FaceHttpHost.Create(enabled: true, [ServiceApiScopes.AttendanceFaceGateway]);

        var response = await host.PostRawEventAsync("{not-json");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(StatusCodes.Status400BadRequest, (int)response.StatusCode);
        Assert.Equal("EVENT_METADATA_INVALID", JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(Convert.ToBase64String(host.Photo), body, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task attendanceFaceGatewayPurpose只签发最小AttendanceFaceGatewayScope()
    {
        var path = Path.Combine(Path.GetTempPath(), $"face-purpose-{Guid.NewGuid():N}.db");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = $"DataSource={path}" })
                .Build();
            var context = new SqlSugarContext(configuration, NullLogger<SqlSugarContext>.Instance, Mock.Of<ICurrentUserService>());
            context.Db.CodeFirst.InitTables(typeof(ServiceApiToken));
            var service = new ServiceApiTokenService(context, NullLogger<ServiceApiTokenService>.Instance);

            var created = await service.CreateAsync(new ServiceApiTokenCreateRequestDto
            {
                Name = "Attendance gateway",
                Purpose = ServiceApiTokenPurposes.AttendanceFaceGateway,
            }, "test");

            Assert.True(created.Success);
            Assert.Equal([ServiceApiScopes.AttendanceFaceGateway], created.Data!.Scopes);
            Assert.DoesNotContain(Permissions.System.ManageAppDownloads, created.Data.Scopes);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString();
    }

    private sealed class FaceHttpHost : IDisposable
    {
        private const string StoreCode = "BRI";
        private const string DeviceCode = "FACE-1";
        private const string HardwareId = "ipad-contract";
        private const string Token = "hbsvc_face-contract";
        private readonly string _path;
        private readonly SqliteConnection _connection;
        private readonly TestServer _server;
        private readonly HttpClient _client;
        private readonly byte[] _secret;
        private readonly DateTime _anchorTime;
        internal SqlSugarClient Db { get; }
        internal byte[] Photo { get; } = [0xff, 0xd8, 0xff, 0x00, 0x01];

        private FaceHttpHost(string path, SqliteConnection connection, TestServer server, SqlSugarClient db, byte[] secret, DateTime anchorTime)
        {
            _path = path; _connection = connection; _server = server; _client = server.CreateClient(); Db = db; _secret = secret; _anchorTime = anchorTime;
        }

        internal static FaceHttpHost Create(bool enabled, IReadOnlyList<string> scopes, string? configuredStoreCode = StoreCode)
        {
            var path = Path.Combine(Path.GetTempPath(), $"face-http-{Guid.NewGuid():N}.db");
            var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();
            var db = new SqlSugarClient(new ConnectionConfig { ConnectionString = connection.ConnectionString, DbType = DbType.Sqlite, IsAutoCloseConnection = false, InitKeyType = InitKeyType.Attribute });
            db.CodeFirst.InitTables(typeof(FaceAttendanceEvent), typeof(FaceAttendanceDeviceKey), typeof(FaceAttendanceTimeAnchor));
            var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
            typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
            var dataProtection = new EphemeralDataProtectionProvider();
            var secret = RandomNumberGenerator.GetBytes(32);
            var anchorTime = DateTime.UtcNow.AddSeconds(-1);
            db.Insertable(new FaceAttendanceDeviceKey
            {
                KeyId = "key-1", StoreCode = StoreCode, DeviceCode = DeviceCode, HardwareId = HardwareId,
                ProtectedSecret = dataProtection.CreateProtector("HB.FaceAttendance.v1.private-payload").Protect(Convert.ToBase64String(secret)),
                Status = "active", CreatedAtUtc = anchorTime,
            }).ExecuteCommand();
            db.Insertable(new FaceAttendanceTimeAnchor
            {
                TimeAnchorId = "anchor-1", KeyId = "key-1", ServerObservedAtUtc = anchorTime,
                DeviceObservedAtUtc = anchorTime, ExpiresAtUtc = anchorTime.AddHours(1),
            }).ExecuteCommand();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["FaceAttendance:Enabled"] = enabled ? "true" : "false", ["FaceAttendance:StoreCodes:0"] = configuredStoreCode })
                .Build();
            var tokenService = new TestTokenService(scopes);
            var server = new TestServer(new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IConfiguration>(configuration);
                    services.AddSingleton(context);
                    services.AddSingleton<IDataProtectionProvider>(dataProtection);
                    services.AddSingleton<IAttendancePosDeviceStatusProvider>(Mock.Of<IAttendancePosDeviceStatusProvider>());
                    services.AddSingleton<IRoleService>(Mock.Of<IRoleService>());
                    services.AddSingleton<IHttpClientFactory>(Mock.Of<IHttpClientFactory>());
                    services.AddSingleton<IServiceApiTokenService>(tokenService);
                    services.AddSingleton<IClientIpResolver>(new FixedClientIpResolver());
                    services.AddSingleton<FaceAttendanceService>();
                    services.AddLogging();
                    services.AddAuthentication(ServiceApiTokenAuthenticationDefaults.AuthenticationScheme)
                        .AddScheme<AuthenticationSchemeOptions, ServiceApiTokenAuthenticationHandler>(
                            ServiceApiTokenAuthenticationDefaults.AuthenticationScheme, _ => { });
                    services.AddAuthorization(options => options.AddPolicy(ServiceApiScopes.AttendanceFaceGateway, policy =>
                    {
                        policy.AuthenticationSchemes.Add(ServiceApiTokenAuthenticationDefaults.AuthenticationScheme);
                        policy.RequireAuthenticatedUser();
                        policy.RequireClaim(ServiceApiTokenAuthenticationDefaults.ScopeClaim, ServiceApiScopes.AttendanceFaceGateway);
                    }));
                    services.AddControllers().AddApplicationPart(typeof(FaceAttendanceController).Assembly);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }));
            return new FaceHttpHost(path, connection, server, db, secret, anchorTime);
        }

        internal FaceAttendanceEventCommandDto NewCommand(string eventGuid)
        {
            var command = new FaceAttendanceEventCommandDto
            {
                EventGuid = eventGuid, UserGuid = "employee-1", StoreCode = StoreCode, DeviceCode = DeviceCode, HardwareId = HardwareId,
                PunchType = FaceAttendanceStatuses.ClockIn, OccurredAtUtc = _anchorTime.AddSeconds(1), DeviceObservedAtUtc = _anchorTime.AddSeconds(1),
                LocalSequence = 1, RosterVersion = 0, EnrollmentVersion = 0, TimeAnchorId = "anchor-1", TimeTrusted = true,
                PhotoSha256 = Convert.ToHexString(SHA256.HashData(Photo)).ToLowerInvariant(), KeyId = "key-1",
            };
            Sign(command);
            return command;
        }

        internal void Sign(FaceAttendanceEventCommandDto command) =>
            command.Signature = Convert.ToBase64String(HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(FaceAttendanceService.EventCanonical(command))));

        internal Task<HttpResponseMessage> PostEventAsync(FaceAttendanceEventCommandDto command) =>
            PostRawEventAsync(JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        internal Task<HttpResponseMessage> PostRawEventAsync(string metadata)
        {
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(metadata, Encoding.UTF8, "application/json"), "metadata");
            var image = new ByteArrayContent(Photo);
            image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            form.Add(image, "photo", "capture.jpg");
            return SendAsync(HttpMethod.Post, "api/internal/attendance/face/events", form);
        }

        internal Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativeUrl, HttpContent? content = null)
        {
            var request = new HttpRequestMessage(method, relativeUrl) { Content = content };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
            request.Headers.Add("X-HB-Face-Store", StoreCode);
            request.Headers.Add("X-HB-Face-Device", DeviceCode);
            request.Headers.Add("X-HB-Face-Hardware", HardwareId);
            return _client.SendAsync(request);
        }

        public void Dispose()
        {
            _client.Dispose(); _server.Dispose(); _connection.Dispose();
            if (File.Exists(_path)) File.Delete(_path);
        }
    }

    private sealed class FixedClientIpResolver : IClientIpResolver
    {
        public string Resolve(HttpContext context) => "198.51.100.10";
    }

    private sealed class TestTokenService(IReadOnlyList<string> scopes) : IServiceApiTokenService
    {
        public Task<ServiceApiTokenValidationResult?> ValidateAsync(string token, string? lastUsedIp) =>
            Task.FromResult<ServiceApiTokenValidationResult?>(token == "hbsvc_face-contract"
                ? new ServiceApiTokenValidationResult { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "face test", TokenPrefix = token, Scopes = scopes.ToList() }
                : null);
        public Task<ApiResponse<List<ServiceApiTokenDto>>> ListAsync() => throw new NotSupportedException();
        public Task<ApiResponse<ServiceApiTokenCreateResponseDto>> CreateAsync(ServiceApiTokenCreateRequestDto request, string createdBy) => throw new NotSupportedException();
        public Task<ApiResponse<ServiceApiTokenDto>> RevokeAsync(Guid id, string revokedBy) => throw new NotSupportedException();
    }
}
