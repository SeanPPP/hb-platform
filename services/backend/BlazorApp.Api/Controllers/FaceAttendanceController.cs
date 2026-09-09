using BlazorApp.Api.Authentication;
using BlazorApp.Api.Services.Attendance;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers;

[ApiController]
[TypeFilter(typeof(FaceAttendanceFeatureFilter))]
[TypeFilter(typeof(FaceAttendanceExceptionFilter), Order = int.MaxValue)]
[RequestSizeLimit(8 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 8 * 1024 * 1024)]
[Route("api/internal/attendance/face")]
[Authorize(AuthenticationSchemes = ServiceApiTokenAuthenticationDefaults.AuthenticationScheme, Policy = "Attendance.FaceGateway")]
public sealed class FaceAttendanceController(FaceAttendanceService service) : ControllerBase
{
    [HttpGet("employees")]
    public async Task<IActionResult> Employees([FromQuery] string storeCode, CancellationToken ct) => Ok(await service.GetRosterAsync(storeCode, Device(), Actor(), ct));

    [HttpPost("device-session")]
    public async Task<IActionResult> DeviceSession([FromBody] FaceDeviceSessionRequestDto request, CancellationToken ct) => Ok(await service.CreateSessionAsync(request, Device(), ct));

    [HttpPost("enrollments")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Enroll([FromForm] string userGuid, [FromForm] string storeCode, [FromForm] string deviceCode, [FromForm] string hardwareId, [FromForm] List<IFormFile> photos, CancellationToken ct)
    {
        var device = Device(); AssertFormDevice(storeCode, deviceCode, hardwareId, device);
        return Ok(await service.EnrollAsync(userGuid, storeCode, await ReadPhotosAsync(photos, ct), device, Actor(), ct));
    }

    [HttpPost("enrollments/{userGuid}/revoke")]
    public async Task<IActionResult> Revoke(string userGuid, [FromBody] FaceEnrollmentRevokeRequestDto request, CancellationToken ct)
        => Ok(await service.RevokeAsync(userGuid, request, Device(), Actor(), ct));

    [HttpPost("events")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Submit([FromForm] string metadata, [FromForm] IFormFile photo, CancellationToken ct)
    {
        if (metadata.Length > 16 * 1024) throw new FaceAttendanceException(400, "EVENT_METADATA_INVALID", "metadata 过大");
        var command = System.Text.Json.JsonSerializer.Deserialize<FaceAttendanceEventCommandDto>(metadata, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
            ?? throw new FaceAttendanceException(400, "EVENT_METADATA_INVALID", "metadata JSON 无效");
        var bytes = await ReadPhotoAsync(photo, ct);
        return Ok(await service.SubmitAsync(command, bytes, Device(), ct));
    }

    [HttpGet("events/{eventGuid}")]
    public async Task<IActionResult> Get(string eventGuid, CancellationToken ct) => Ok(await service.GetAsync(eventGuid, Device(), Actor(), ct));

    [HttpGet("events")]
    public async Task<IActionResult> List([FromQuery] string storeCode, [FromQuery] string? userGuid, [FromQuery] string? status, CancellationToken ct)
        => Ok(await service.ListAsync(storeCode, userGuid, status, Device(), Actor(), ct));

    [HttpGet("events/{eventGuid}/photo")]
    public async Task<IActionResult> Photo(string eventGuid, [FromQuery] string? encoding, CancellationToken ct)
    {
        var photo = await service.GetPhotoAsync(eventGuid, Device(), Actor(), ct);
        return encoding == "base64" ? Ok(new { imageBase64 = Convert.ToBase64String(photo) }) : File(photo, "image/jpeg");
    }

    [HttpPost("events/{eventGuid}/review")]
    public async Task<IActionResult> Review(string eventGuid, [FromBody] FaceAttendanceReviewRequestDto request, CancellationToken ct)
        => Ok(await service.ReviewAsync(eventGuid, request, Device(), Actor(), ct));

    private FaceDeviceContext Device()
    {
        var store = Header("X-HB-Face-Store"); var device = Header("X-HB-Face-Device"); var hardware = Header("X-HB-Face-Hardware");
        if (string.IsNullOrWhiteSpace(store) || string.IsNullOrWhiteSpace(device) || string.IsNullOrWhiteSpace(hardware))
            throw new FaceAttendanceException(403, "FACE_DEVICE_CONTEXT_MISSING", "缺少经网关验证的设备上下文");
        return new FaceDeviceContext(store, device, hardware);
    }
    private static void AssertFormDevice(string storeCode, string deviceCode, string hardwareId, FaceDeviceContext device)
    {
        if (!storeCode.Equals(device.StoreCode, StringComparison.OrdinalIgnoreCase) || !deviceCode.Equals(device.DeviceCode, StringComparison.OrdinalIgnoreCase) || !hardwareId.Equals(device.HardwareId, StringComparison.Ordinal))
            throw new FaceAttendanceException(403, "FACE_DEVICE_SCOPE_MISMATCH", "设备上下文与表单不一致");
    }
    private FaceManagementActor? Actor()
    {
        var userGuid = Header("X-HB-Face-Actor-User");
        var authenticatedAt = Header("X-HB-Face-Actor-Authenticated-At");
        return !string.IsNullOrWhiteSpace(userGuid)
            && DateTime.TryParse(authenticatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? new FaceManagementActor(userGuid, DateTime.SpecifyKind(parsed.ToUniversalTime(), DateTimeKind.Utc))
            : null;
    }
    private string? Header(string name) => Request.Headers.TryGetValue(name, out var value) ? value.ToString() : null;
    private static async Task<List<byte[]>> ReadPhotosAsync(List<IFormFile> photos, CancellationToken ct)
    {
        if (photos.Count != 3) throw new FaceAttendanceException(400, "ENROLLMENT_PHOTOS_INVALID", "需要三张照片");
        var result = new List<byte[]>();
        foreach (var photo in photos) result.Add(await ReadPhotoAsync(photo, ct));
        return result;
    }
    private static async Task<byte[]> ReadPhotoAsync(IFormFile? photo, CancellationToken ct)
    {
        if (photo == null || photo.Length == 0 || photo.Length > FaceAttendanceService.MaximumPhotoBytes
            || !string.Equals(photo.ContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
            throw new FaceAttendanceException(400, "PHOTO_INVALID", "照片必须是不超过2MiB的JPEG");
        await using var stream = photo.OpenReadStream();
        using var output = new MemoryStream();
        await stream.CopyToAsync(output, ct);
        return output.ToArray();
    }
}
