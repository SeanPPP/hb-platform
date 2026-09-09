using System.Security.Claims;
using Hbpos.Api.Services;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = DeviceAuthConstants.Scheme)]
[Route("api/v1/attendance/face")]
[RequestSizeLimit(AttendanceFaceGateway.MaximumBodyBytes)]
public sealed class AttendanceFaceController(IAttendanceFaceGateway gateway, IAttendanceFaceActorResolver actors)
    : ControllerBase
{
    private const string EnrollPermission = "Attendance.Face.EnrollManagedStore";
    private const string PhotoPermission = "Attendance.Face.ViewPhotosManagedStore";
    private const string ReviewPermission = "Attendance.Face.ReviewManagedStore";

    [HttpGet("employees")]
    public Task<IActionResult> Employees(CancellationToken cancellationToken) =>
        ForwardAsync(HttpMethod.Get, "employees", false, null, true, cancellationToken);

    [HttpPost("device-session")]
    public Task<IActionResult> DeviceSession(CancellationToken cancellationToken) =>
        ForwardAsync(HttpMethod.Post, "device-session", true, null, false, cancellationToken);

    [HttpPost("enrollments")]
    public Task<IActionResult> Enroll(CancellationToken cancellationToken) =>
        ForwardAsync(HttpMethod.Post, "enrollments", true, EnrollPermission, false, cancellationToken);

    [HttpPost("enrollments/{userGuid:guid}/revoke")]
    public Task<IActionResult> Revoke(Guid userGuid, CancellationToken cancellationToken) =>
        ForwardAsync(HttpMethod.Post, $"enrollments/{userGuid:D}/revoke", true, EnrollPermission, false, cancellationToken);

    [HttpPost("events")]
    public Task<IActionResult> Upload(CancellationToken cancellationToken) =>
        ForwardAsync(HttpMethod.Post, "events", true, null, false, cancellationToken);

    [HttpGet("events/{eventGuid:guid}")]
    public Task<IActionResult> Event(Guid eventGuid, CancellationToken cancellationToken) =>
        ForwardAsync(HttpMethod.Get, $"events/{eventGuid:D}", false, null, true, cancellationToken);

    [HttpGet("events")]
    public Task<IActionResult> Events(CancellationToken cancellationToken) =>
        ForwardAsync(HttpMethod.Get, "events", false, ReviewPermission, false, cancellationToken);

    [HttpGet("events/{eventGuid:guid}/photo")]
    public Task<IActionResult> Photo(Guid eventGuid, CancellationToken cancellationToken) =>
        ForwardAsync(HttpMethod.Get, $"events/{eventGuid:D}/photo", false, PhotoPermission, false, cancellationToken);

    [HttpPost("events/{eventGuid:guid}/review")]
    public Task<IActionResult> Review(Guid eventGuid, CancellationToken cancellationToken) =>
        ForwardAsync(HttpMethod.Post, $"events/{eventGuid:D}/review", true, ReviewPermission, false, cancellationToken);

    private async Task<IActionResult> ForwardAsync(HttpMethod method, string path, bool hasBody,
        string? permission, bool includeManager, CancellationToken cancellationToken)
    {
        var store = User.FindFirstValue(DeviceAuthConstants.StoreCodeClaim);
        var device = User.FindFirstValue(DeviceAuthConstants.DeviceCodeClaim);
        var hardware = User.FindFirstValue(DeviceAuthConstants.HardwareIdClaim);
        if (User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(store)
            || string.IsNullOrWhiteSpace(device) || string.IsNullOrWhiteSpace(hardware))
            return Unauthorized(new { code = "DEVICE_AUTH_REQUIRED", errorCode = "DEVICE_AUTH_REQUIRED" });
        var scope = new AttendanceFaceDeviceScope(store, device, hardware);

        // 只接收过滤条件；门店、设备、人员管理身份均由服务端生成。
        var query = new Dictionary<string, string?> { ["storeCode"] = store };
        if (Request.Query.TryGetValue("storeCode", out var requestedStore)
            && !string.Equals(requestedStore.ToString(), store, StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { code = "FACE_STORE_FORBIDDEN", errorCode = "FACE_STORE_FORBIDDEN" });
        foreach (var key in new[] { "userGuid", "status", "fromUtc", "toUtc", "limit", "encoding" })
        {
            if (Request.Query.TryGetValue(key, out var value)) query[key] = value.ToString();
        }
        AttendanceFaceActor? actor = null;
        if (permission is not null) actor = await actors.ResolveAsync(HttpContext, scope, permission, cancellationToken);
        else if (includeManager)
        {
            // 名单返回独立管理能力；只有照片或审核权限的店长也能得到对应入口。
            var permissions = path.StartsWith("events/", StringComparison.Ordinal)
                ? new[] { ReviewPermission } : new[] { EnrollPermission, PhotoPermission, ReviewPermission };
            foreach (var candidate in permissions)
            {
                actor = await actors.ResolveAsync(HttpContext, scope, candidate, cancellationToken);
                if (actor is not null) break;
            }
        }
        if (permission is not null && actor is null)
            return StatusCode(403, new { code = "FACE_MANAGER_ONLINE_AUTH_REQUIRED", errorCode = "FACE_MANAGER_ONLINE_AUTH_REQUIRED" });

        byte[]? body = null;
        if (hasBody)
        {
            if (Request.ContentLength > AttendanceFaceGateway.MaximumBodyBytes)
                return StatusCode(413, new { code = "FACE_PAYLOAD_TOO_LARGE", errorCode = "FACE_PAYLOAD_TOO_LARGE" });
            body = await AttendanceFaceGateway.ReadBoundedAsync(Request.Body,
                AttendanceFaceGateway.MaximumBodyBytes, cancellationToken);
            if (body is null) return StatusCode(413, new { code = "FACE_PAYLOAD_TOO_LARGE", errorCode = "FACE_PAYLOAD_TOO_LARGE" });
        }
        var response = await gateway.SendAsync(method,
            Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(path, query), scope, actor,
            body, Request.ContentType, cancellationToken);
        Response.StatusCode = response.StatusCode;
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(response.Body, response.ContentType);
    }
}
