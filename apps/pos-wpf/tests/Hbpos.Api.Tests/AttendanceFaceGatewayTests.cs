using System.Net;
using System.Security.Claims;
using System.Text;
using Hbpos.Api.Auth;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Hbpos.Contracts.Cashiers;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Tests;

public sealed class AttendanceFaceGatewayTests
{
    private static readonly AttendanceFaceDeviceScope Device = new("STORE", "IPAD", "HARDWARE");

    [Fact]
    public async Task 网关保留幂等冲突并只发送服务端身份()
    {
        var handler = new Handler(HttpStatusCode.Conflict, "{\"code\":\"EVENT_GUID_CONFLICT\"}");
        var gateway = CreateGateway(handler);
        var result = await gateway.SendAsync(HttpMethod.Post, "events?storeCode=STORE", Device,
            new("employee-guid", DateTimeOffset.Parse("2026-09-09T09:00:00.123Z")),
            Encoding.UTF8.GetBytes("immutable-json"), "application/json", CancellationToken.None);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("EVENT_GUID_CONFLICT", Encoding.UTF8.GetString(result.Body));
        Assert.Equal("https://center.test/api/internal/attendance/face/events?storeCode=STORE", handler.Uri);
        Assert.Equal("STORE", handler.Headers["X-HB-Face-Store"]);
        Assert.Equal("IPAD", handler.Headers["X-HB-Face-Device"]);
        Assert.Equal("employee-guid", handler.Headers["X-HB-Face-Actor-User"]);
        Assert.Equal("2026-09-09T09:00:00.123Z", handler.Headers["X-HB-Face-Actor-Authenticated-At"]);
        Assert.Equal("immutable-json", handler.Body);
    }

    [Theory]
    [InlineData("http://center.test")]
    [InlineData("https://user:password@center.test")]
    [InlineData("https://center.test?url=evil")]
    public async Task 不向不安全地址发送服务令牌(string url)
    {
        var handler = new Handler(HttpStatusCode.OK, "{}");
        var response = await CreateGateway(handler, url).SendAsync(HttpMethod.Get, "employees", Device, null, null, null, CancellationToken.None);
        Assert.Equal(503, response.StatusCode);
        Assert.Null(handler.Uri);
    }

    [Fact]
    public async Task 不跟随中央重定向且限制响应大小()
    {
        var redirected = await CreateGateway(new Handler(HttpStatusCode.Found, "")).SendAsync(HttpMethod.Get, "employees", Device, null, null, null, CancellationToken.None);
        Assert.Equal(502, redirected.StatusCode);
        using var oversized = new MemoryStream(new byte[9]);
        Assert.Null(await AttendanceFaceGateway.ReadBoundedAsync(oversized, 8, CancellationToken.None));
    }

    [Fact]
    public async Task 无可信设备拒绝请求且不转发伪造管理头()
    {
        var gateway = new RecordingGateway();
        var controller = new AttendanceFaceController(gateway, new NoActor()) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<UnauthorizedObjectResult>(await controller.Employees(CancellationToken.None));
        Assert.Equal(0, gateway.Calls);
        controller.HttpContext.User = DeviceUser();
        controller.HttpContext.Request.Headers["X-HB-Face-Actor-User"] = "forged-manager";
        await controller.Employees(CancellationToken.None);
        Assert.Null(gateway.Actor);
        Assert.Equal(Device, gateway.Device);
    }

    [Fact]
    public async Task 不同分店查询和无管理票据的录入均拒绝()
    {
        var gateway = new RecordingGateway();
        var context = new DefaultHttpContext { User = DeviceUser() };
        var controller = new AttendanceFaceController(gateway, new NoActor()) { ControllerContext = new() { HttpContext = context } };
        context.Request.QueryString = new("?storeCode=OTHER");
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Employees(CancellationToken.None)).StatusCode);
        context.Request.QueryString = QueryString.Empty;
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Enroll(CancellationToken.None)).StatusCode);
        Assert.Equal(0, gateway.Calls);
    }

    [Theory]
    [InlineData(-3, "HARDWARE", true, false)]
    [InlineData(2, "HARDWARE", true, false)]
    [InlineData(0, "WRONG", true, false)]
    [InlineData(0, "HARDWARE", false, false)]
    [InlineData(0, "HARDWARE", true, true)]
    public async Task 管理必须新鲜在线硬件票据及实时权限(int ageMinutes, string hardware, bool permission, bool expected)
    {
        var now = DateTimeOffset.Parse("2026-09-09T00:00:00Z");
        var ticket = new CashierAuthorizationTicket("cashier", "user", "STORE", "IPAD", now.AddDays(1), hardware) { BarcodeAuthenticatedAtUtc = now.AddMinutes(ageMinutes) };
        var resolver = new AttendanceFaceActorResolver(new TicketStub(ticket), new CashierStub(permission), new FixedClock(now));
        var actor = await resolver.ResolveAsync(new DefaultHttpContext(), Device, "Attendance.Face.EnrollManagedStore", CancellationToken.None);
        Assert.Equal(expected, actor is not null);
    }

    private static ClaimsPrincipal DeviceUser() => new(new ClaimsIdentity(new[] {
        new Claim(DeviceAuthConstants.StoreCodeClaim, "STORE"), new Claim(DeviceAuthConstants.DeviceCodeClaim, "IPAD"), new Claim(DeviceAuthConstants.HardwareIdClaim, "HARDWARE") }, DeviceAuthConstants.Scheme));
    private static AttendanceFaceGateway CreateGateway(Handler handler, string url = "https://center.test") => new(new HttpClient(handler), Options.Create(new AttendanceFaceGatewayOptions { Enabled = true, CenterBaseUrl = url, ServiceToken = "unit-test-service-token" }));
    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Uri; public string? Body; public Dictionary<string, string> Headers = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uri = request.RequestUri!.AbsoluteUri;
            Headers = request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value));
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
    private sealed class NoActor : IAttendanceFaceActorResolver
    { public Task<AttendanceFaceActor?> ResolveAsync(HttpContext context, AttendanceFaceDeviceScope device, string permission, CancellationToken ct) => Task.FromResult<AttendanceFaceActor?>(null); }
    private sealed class RecordingGateway : IAttendanceFaceGateway
    {
        public int Calls; public AttendanceFaceActor? Actor; public AttendanceFaceDeviceScope? Device;
        public Task<AttendanceFaceGatewayResponse> SendAsync(HttpMethod method, string path, AttendanceFaceDeviceScope device, AttendanceFaceActor? actor, byte[]? body, string? type, CancellationToken ct)
        { Calls++; Actor = actor; Device = device; return Task.FromResult(new AttendanceFaceGatewayResponse(200, "application/json", "{}"u8.ToArray())); }
    }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class TicketStub(CashierAuthorizationTicket ticket) : ICashierAuthorizationTicketService
    {
        public CashierAuthorizationTicket? Validate(string? token) => ticket;
        public (string Token, DateTimeOffset ExpiresAtUtc) Issue(string a, string b, string c, string d) => throw new NotSupportedException();
    }
    private sealed class CashierStub(bool allowed) : ICashierService
    {
        public Task<bool> HasAnyPermissionAsync(string userGuid, string storeCode, IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken) => Task.FromResult(allowed);
        public Task<CashierSessionDto?> BarcodeLoginAsync(CashierBarcodeLoginRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CashierSessionDto?> RefreshSessionAsync(CashierAuthorizationTicket ticket, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
