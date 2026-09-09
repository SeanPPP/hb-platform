using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.DataProtection;
using SqlSugar;

namespace BlazorApp.Api.Services.Attendance;

public sealed partial class FaceAttendanceService
{
    public const int MaximumPhotoBytes = 2 * 1024 * 1024;
    public const string ModelVersion = "yunet-2023mar-sface-2021dec-v1";
    private readonly ISqlSugarClient _db;
    private readonly IAttendancePosDeviceStatusProvider _deviceStatus;
    private readonly IRoleService _roles;
    private readonly IDataProtector _protector;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _clock;
    private readonly IFaceAttendancePunchWriter? _writer;

    public FaceAttendanceService(SqlSugarContext context, IAttendancePosDeviceStatusProvider deviceStatus,
        IRoleService roles, IDataProtectionProvider protection, IConfiguration configuration, IHttpClientFactory httpClientFactory,
        TimeProvider? clock = null, IFaceAttendancePunchWriter? writer = null)
    {
        _db = context.Db; _deviceStatus = deviceStatus; _roles = roles;
        _protector = protection.CreateProtector("HB.FaceAttendance.v1.private-payload");
        _configuration = configuration; _httpClientFactory = httpClientFactory;
        _clock = clock ?? TimeProvider.System; _writer = writer;
    }

    public async Task<FaceAttendanceRosterDto> GetRosterAsync(string storeCode, FaceDeviceContext device, FaceManagementActor? actor, CancellationToken ct)
    {
        EnsureDeviceScope(storeCode, device);
        var store = await ActiveStoreAsync(storeCode);
        var members = await MembersAsync(_db, storeCode);
        var fingerprints = Fingerprints(members);
        var snapshotJson = JsonSerializer.Serialize(fingerprints);
        // 48 位内容摘要可精确放入 JS number；排班或其他员工的改动不代表本人的资料失效。
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson));
        long version = 0;
        for (var i = 0; i < 6; i++) version = (version << 8) | digest[i];
        var snapshot = await _db.Queryable<FaceAttendanceRosterSnapshot>().FirstAsync(x => x.StoreCode == storeCode && x.Version == version);
        if (snapshot == null)
        {
            try { await _db.Insertable(new FaceAttendanceRosterSnapshot { StoreCode = storeCode, Version = version, MemberFingerprintsJson = snapshotJson, CreatedAtUtc = UtcNow() }).ExecuteCommandAsync(); }
            catch { if (!await _db.Queryable<FaceAttendanceRosterSnapshot>().AnyAsync(x => x.StoreCode == storeCode && x.Version == version)) throw; }
        }
        else if (snapshot.MemberFingerprintsJson != snapshotJson)
            throw new FaceAttendanceException(503, "ROSTER_VERSION_CONFLICT", "员工资料版本冲突，请联系管理员");
        var enrollments = await _db.Queryable<FaceAttendanceEnrollment>().Where(x => x.StoreCode == storeCode).ToListAsync();
        var employees = new List<FaceAttendanceEmployeeDto>();
        foreach (var member in members.GroupBy(x => x.UserGuid).Select(x => x.First()))
        {
            var enrollment = enrollments.FirstOrDefault(x => x.UserGuid == member.UserGuid);
            // 每人只读取最后一条，避免设备首次同步载入全店历史考勤及照片。
            var last = await _db.Queryable<AttendancePunch>().Where(x => !x.IsDeleted && x.UserGuid == member.UserGuid && x.StoreCode == storeCode)
                .OrderByDescending(x => x.PunchTimeUtc).FirstAsync();
            employees.Add(new FaceAttendanceEmployeeDto {
                UserGuid = member.UserGuid, EmployeeCode = member.Username, DisplayName = string.IsNullOrWhiteSpace(member.FullName) ? member.Username : member.FullName,
                StoreCode = storeCode, EnrollmentVersion = enrollment?.Version ?? 0,
                EnrollmentStatus = enrollment?.Status ?? "none",
                LastPunchType = last?.PunchType == "ClockIn" ? FaceAttendanceStatuses.ClockIn : last?.PunchType == "ClockOut" ? FaceAttendanceStatuses.ClockOut : null,
                LastPunchTimeUtc = last == null ? null : Utc(last.PunchTimeUtc)
            });
        }
        return new FaceAttendanceRosterDto { StoreCode = storeCode, StoreTimeZone = store.TimeZoneId ?? "Australia/Sydney",
            ServerTimeUtc = UtcNow(), RosterVersion = version, CanManage = await CanManageStoreAsync(actor, storeCode, Permissions.Attendance.Face.EnrollManagedStore, ct),
            CanViewPhotos = await CanManageStoreAsync(actor, storeCode, Permissions.Attendance.Face.ViewPhotosManagedStore, ct),
            CanReview = await CanManageStoreAsync(actor, storeCode, Permissions.Attendance.Face.ReviewManagedStore, ct),
            Employees = employees.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.EmployeeCode).ToList() };
    }

    public async Task<FaceDeviceSessionDto> CreateSessionAsync(FaceDeviceSessionRequestDto request, FaceDeviceContext device, CancellationToken ct)
    {
        EnsureDeviceScope(request.StoreCode, request.DeviceCode, request.HardwareId, device);
        if (!await _deviceStatus.IsActiveAsync(device.DeviceCode, device.StoreCode, device.HardwareId, ct))
            throw new FaceAttendanceException(403, "FACE_DEVICE_DISABLED", "设备未开通人脸考勤");
        if (!ValidText(request.Nonce, 120) || request.DeviceObservedAtUtc == default)
            throw new FaceAttendanceException(400, "DEVICE_SESSION_NONCE_INVALID", "设备会话字段无效");
        var now = UtcNow();
        var key = await ActiveDeviceKeyAsync(device);
        if (key == null)
        {
            if (!string.IsNullOrEmpty(request.KeyId)) throw new FaceAttendanceException(401, "DEVICE_SESSION_KEY_REVOKED", "设备签名密钥已失效");
            var candidate = new FaceAttendanceDeviceKey { KeyId = Guid.NewGuid().ToString("N"), StoreCode = device.StoreCode, DeviceCode = device.DeviceCode,
                HardwareId = device.HardwareId, ProtectedSecret = _protector.Protect(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), CreatedAtUtc = now };
            try { await _db.Insertable(candidate).ExecuteCommandAsync(); key = candidate; }
            catch { key = await ActiveDeviceKeyAsync(device); if (key == null) throw; }
        }
        var secret = _protector.Unprotect(key.ProtectedSecret);
        if (!string.IsNullOrEmpty(request.KeyId) && (request.KeyId != key.KeyId || !VerifyHmac(secret, DeviceSessionCanonical(request), request.Signature)))
            throw new FaceAttendanceException(401, "DEVICE_SESSION_SIGNATURE_INVALID", "设备会话签名无效");
        // 服务端保留锚点，上传时独立核验；不能把客户端的 TimeTrusted 当作服务端证明。
        var anchor = new FaceAttendanceTimeAnchor { TimeAnchorId = Guid.NewGuid().ToString("N"), KeyId = key.KeyId,
            ServerObservedAtUtc = now, DeviceObservedAtUtc = Utc(request.DeviceObservedAtUtc), ExpiresAtUtc = now.AddHours(24) };
        await _db.Insertable(anchor).ExecuteCommandAsync();
        return new FaceDeviceSessionDto { SessionId = anchor.TimeAnchorId, TimeAnchorId = anchor.TimeAnchorId,
            ServerObservedAtUtc = now, ExpiresAtUtc = anchor.ExpiresAtUtc, KeyId = key.KeyId,
            DeviceKeySecret = string.IsNullOrEmpty(request.KeyId) ? secret : null };
    }

    public async Task<FaceEnrollmentDto> EnrollAsync(string userGuid, string storeCode, IReadOnlyList<byte[]> photos, FaceDeviceContext device, FaceManagementActor? actor, CancellationToken ct)
    {
        EnsureDeviceScope(storeCode, device);
        await RequireManageAsync(actor, storeCode, Permissions.Attendance.Face.EnrollManagedStore, ct);
        if (photos.Count != 3 || photos.Any(x => !ValidPhoto(x))) throw new FaceAttendanceException(400, "ENROLLMENT_PHOTOS_INVALID", "需要三张不超过2MiB的JPEG照片");
        if (!(await MembersAsync(_db, storeCode)).Any(x => x.UserGuid == userGuid)) throw new FaceAttendanceException(404, "EMPLOYEE_NOT_IN_STORE", "员工不在本店有效名单中");
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(":", photos.Select(x => Convert.ToHexString(SHA256.HashData(x)))))));
        var existing = await _db.Queryable<FaceAttendanceEnrollment>().FirstAsync(x => x.UserGuid == userGuid && x.StoreCode == storeCode);
        if (existing?.Status == "active" && existing.LastRequestHash == requestHash) return EnrollmentDto(existing);
        var templates = _protector.Protect(await CreateTemplatesAsync(photos, ct));
        var resource = AttendanceDailyMutationLock.BuildResource(userGuid, storeCode, UtcNow().Date);
        await using var employeeLock = await AttendanceDailyMutationLock.AcquireProcessAsync(resource);
        await _db.Ado.BeginTranAsync();
        try
        {
            await AttendanceDailyMutationLock.AcquireDatabaseAsync(_db, resource);
            await RequireManageAsync(actor, storeCode, Permissions.Attendance.Face.EnrollManagedStore, ct);
            if (!(await MembersAsync(_db, storeCode)).Any(x => x.UserGuid == userGuid)) throw new FaceAttendanceException(409, "EMPLOYEE_NOT_IN_STORE", "员工资料已变更");
            existing = await _db.Queryable<FaceAttendanceEnrollment>().FirstAsync(x => x.UserGuid == userGuid && x.StoreCode == storeCode);
            if (existing?.Status == "active" && existing.LastRequestHash == requestHash) { await _db.Ado.CommitTranAsync(); return EnrollmentDto(existing); }
            var row = existing ?? new FaceAttendanceEnrollment { UserGuid = userGuid, StoreCode = storeCode, CreatedAtUtc = UtcNow() };
            row.Version++; row.Status = "active"; row.ProtectedTemplatesJson = templates; row.LastRequestHash = requestHash;
            row.UpdatedAtUtc = UtcNow(); row.UpdatedBy = actor!.UserGuid;
            if (existing == null) await _db.Insertable(row).ExecuteCommandAsync(); else await _db.Updateable(row).ExecuteCommandAsync();
            await _db.Ado.CommitTranAsync(); return EnrollmentDto(row);
        }
        catch { await _db.Ado.RollbackTranAsync(); throw; }
    }

    public async Task<FaceEnrollmentDto> RevokeAsync(string userGuid, FaceEnrollmentRevokeRequestDto request, FaceDeviceContext device, FaceManagementActor? actor, CancellationToken ct)
    {
        EnsureDeviceScope(request.StoreCode, device);
        await RequireManageAsync(actor, request.StoreCode, Permissions.Attendance.Face.EnrollManagedStore, ct);
        var resource = AttendanceDailyMutationLock.BuildResource(userGuid, request.StoreCode, UtcNow().Date);
        await using var employeeLock = await AttendanceDailyMutationLock.AcquireProcessAsync(resource);
        await _db.Ado.BeginTranAsync();
        try
        {
            await AttendanceDailyMutationLock.AcquireDatabaseAsync(_db, resource);
            await RequireManageAsync(actor, request.StoreCode, Permissions.Attendance.Face.EnrollManagedStore, ct);
            var row = await _db.Queryable<FaceAttendanceEnrollment>().FirstAsync(x => x.UserGuid == userGuid && x.StoreCode == request.StoreCode);
            if (row == null) throw new FaceAttendanceException(404, "ENROLLMENT_NOT_FOUND", "人脸档案不存在");
            if (row.Status != "revoked")
            {
                row.Version++; row.Status = "revoked"; row.ProtectedTemplatesJson = _protector.Protect("[]"); row.LastRequestHash = null;
                row.UpdatedAtUtc = UtcNow(); row.UpdatedBy = actor!.UserGuid;
                await _db.Updateable(row).ExecuteCommandAsync();
            }
            await _db.Ado.CommitTranAsync(); return EnrollmentDto(row);
        }
        catch { await _db.Ado.RollbackTranAsync(); throw; }
    }

    public async Task<FaceAttendanceEventDto> SubmitAsync(FaceAttendanceEventCommandDto command, byte[] photo, FaceDeviceContext device, CancellationToken ct)
    {
        EnsureDeviceScope(command.StoreCode, command.DeviceCode, command.HardwareId, device);
        ValidateEvent(command, photo);
        var hash = HashImmutable(command);
        var existing = await _db.Queryable<FaceAttendanceEvent>().FirstAsync(x => x.EventGuid == command.EventGuid);
        if (existing != null) return SameEvent(existing, hash, device);
        var key = await ActiveDeviceKeyAsync(device);
        if (key?.KeyId != command.KeyId || !VerifyHmac(_protector.Unprotect(key.ProtectedSecret), EventCanonical(command), command.Signature))
            throw new FaceAttendanceException(401, "EVENT_SIGNATURE_INVALID", "考勤事件签名无效");
        var now = UtcNow();
        var reason = await TimeProblemAsync(command, now);
        var row = new FaceAttendanceEvent { EventGuid = command.EventGuid, ImmutablePayloadHash = hash, UserGuid = command.UserGuid, StoreCode = command.StoreCode,
            DeviceCode = command.DeviceCode, HardwareId = command.HardwareId, PunchType = command.PunchType, OccurredAtUtc = Utc(command.OccurredAtUtc), DeviceObservedAtUtc = Utc(command.DeviceObservedAtUtc),
            LocalSequence = command.LocalSequence, RosterVersion = command.RosterVersion, EnrollmentVersion = command.EnrollmentVersion,
            TimeAnchorId = command.TimeAnchorId, TimeTrusted = command.TimeTrusted, PhotoSha256 = command.PhotoSha256.ToLowerInvariant(), KeyId = command.KeyId, Signature = command.Signature,
            ProtectedPhoto = _protector.Protect(Convert.ToBase64String(photo)), Status = reason == null ? "queued" : "needsReview", ReasonCode = reason,
            NextAttemptAtUtc = now, ReceivedAtUtc = now, UpdatedAtUtc = now, RetainUntilUtc = now.AddDays(30) };
        // 一行原子持久化事件与加密照片，只有成功插入（或读回同一事件）才返回完整接收回执。
        try { await _db.Insertable(row).ExecuteCommandAsync(); }
        catch
        {
            existing = await _db.Queryable<FaceAttendanceEvent>().FirstAsync(x => x.EventGuid == command.EventGuid);
            if (existing != null) return SameEvent(existing, hash, device);
            throw;
        }
        return ToDto(row);
    }

    public async Task<FaceAttendanceEventDto> GetAsync(string eventGuid, FaceDeviceContext device, FaceManagementActor? actor, CancellationToken ct)
    {
        var row = await EventAsync(eventGuid); EnsureDeviceScope(row.StoreCode, device);
        // 普通设备只能轮询自己的回执；跨设备查询和列表需要在线管理权限。
        if (row.DeviceCode != device.DeviceCode || row.HardwareId != device.HardwareId)
            await RequireManageAsync(actor, row.StoreCode, Permissions.Attendance.Face.ReviewManagedStore, ct);
        return ToDto(row);
    }
    public async Task<FaceAttendanceEventPageDto> ListAsync(string storeCode, string? userGuid, string? status, FaceDeviceContext device, FaceManagementActor? actor, CancellationToken ct)
    {
        EnsureDeviceScope(storeCode, device); await RequireManageAsync(actor, storeCode, Permissions.Attendance.Face.ReviewManagedStore, ct);
        var rows = await _db.Queryable<FaceAttendanceEvent>().Where(x => x.StoreCode == storeCode)
            .WhereIF(!string.IsNullOrWhiteSpace(userGuid), x => x.UserGuid == userGuid)
            .WhereIF(!string.IsNullOrWhiteSpace(status), x => x.Status == status)
            .OrderByDescending(x => x.ReceivedAtUtc).Take(200).ToListAsync();
        return new FaceAttendanceEventPageDto { Items = rows.Select(ToDto).ToList() };
    }
    public async Task<byte[]> GetPhotoAsync(string eventGuid, FaceDeviceContext device, FaceManagementActor? actor, CancellationToken ct)
    {
        var row = await EventAsync(eventGuid); EnsureDeviceScope(row.StoreCode, device);
        await RequireManageAsync(actor, row.StoreCode, Permissions.Attendance.Face.ViewPhotosManagedStore, ct);
        if (string.IsNullOrEmpty(row.ProtectedPhoto) || row.RetainUntilUtc == null || row.RetainUntilUtc <= UtcNow())
            throw new FaceAttendanceException(410, "PHOTO_EXPIRED", "照片已超过保留期限");
        return Convert.FromBase64String(_protector.Unprotect(row.ProtectedPhoto));
    }

    private Task<FaceAttendanceDeviceKey> ActiveDeviceKeyAsync(FaceDeviceContext d) => _db.Queryable<FaceAttendanceDeviceKey>()
        .FirstAsync(x => x.StoreCode == d.StoreCode && x.DeviceCode == d.DeviceCode && x.HardwareId == d.HardwareId && x.Status == "active");
    private async Task<FaceAttendanceEvent> EventAsync(string id) => await _db.Queryable<FaceAttendanceEvent>().FirstAsync(x => x.EventGuid == id)
        ?? throw new FaceAttendanceException(404, "EVENT_NOT_FOUND", "考勤事件不存在");
    private async Task<Store> ActiveStoreAsync(string code) => await _db.Queryable<Store>().FirstAsync(x => x.StoreCode == code && x.IsActive && !x.IsDeleted)
        ?? throw new FaceAttendanceException(404, "STORE_NOT_FOUND", "分店不存在或已停用");
    private static FaceEnrollmentDto EnrollmentDto(FaceAttendanceEnrollment x) => new() { UserGuid = x.UserGuid, StoreCode = x.StoreCode, EnrollmentVersion = x.Version, Status = x.Status, UpdatedAtUtc = Utc(x.UpdatedAtUtc) };
    private static FaceAttendanceEventDto ToDto(FaceAttendanceEvent x) => new() { EventGuid = x.EventGuid, UserGuid = x.UserGuid, StoreCode = x.StoreCode, DeviceCode = x.DeviceCode, HardwareId = x.HardwareId, PunchType = x.PunchType, OccurredAtUtc = Utc(x.OccurredAtUtc), Status = x.Status, ReasonCode = x.ReasonCode, PunchGuid = x.PunchGuid, ReceivedAtUtc = Utc(x.ReceivedAtUtc), UpdatedAtUtc = Utc(x.UpdatedAtUtc) };
    private static FaceAttendanceEventDto SameEvent(FaceAttendanceEvent existing, string hash, FaceDeviceContext device)
    {
        EnsureDeviceScope(existing.StoreCode, existing.DeviceCode, existing.HardwareId, device);
        if (existing.ImmutablePayloadHash != hash) throw new FaceAttendanceException(409, "EVENT_GUID_CONFLICT", "同一eventGuid的内容不能变更");
        return ToDto(existing);
    }
    private async Task<string?> TimeProblemAsync(FaceAttendanceEventCommandDto c, DateTime received)
    {
        if (!c.TimeTrusted) return "DEVICE_TIME_UNTRUSTED";
        var a = await _db.Queryable<FaceAttendanceTimeAnchor>().FirstAsync(x => x.TimeAnchorId == c.TimeAnchorId && x.KeyId == c.KeyId);
        if (a == null) return "TIME_ANCHOR_INVALID";
        var occurred = Utc(c.OccurredAtUtc);
        if (occurred < Utc(a.ServerObservedAtUtc).AddSeconds(-5) || occurred > Utc(a.ExpiresAtUtc) || occurred > received.AddSeconds(30)) return "TIME_ANCHOR_EXPIRED_OR_FUTURE";
        var serverElapsed = occurred - Utc(a.ServerObservedAtUtc);
        var deviceElapsed = Utc(c.DeviceObservedAtUtc) - Utc(a.DeviceObservedAtUtc);
        if (deviceElapsed < TimeSpan.FromSeconds(-5) || Math.Abs((deviceElapsed - serverElapsed).TotalSeconds) > 30) return "DEVICE_TIME_DISCONTINUITY";
        return null;
    }
    private async Task RequireManageAsync(FaceManagementActor? actor, string storeCode, string permission, CancellationToken ct)
    {
        if (!await CanManageStoreAsync(actor, storeCode, permission, ct)) throw new FaceAttendanceException(403, "FACE_MANAGEMENT_FORBIDDEN", "需要有效的在线分店考勤管理权限");
    }
    private async Task<bool> CanManageStoreAsync(FaceManagementActor? actor, string storeCode, string permission, CancellationToken ct)
    {
        if (actor == null || Utc(actor.AuthenticatedAtUtc) > UtcNow().AddSeconds(30) || UtcNow() - Utc(actor.AuthenticatedAtUtc) > TimeSpan.FromMinutes(2)) return false;
        if (!(await _roles.UserHasPermissionAsync(actor.UserGuid, permission)).Data) return false;
        return (await MembersAsync(_db, storeCode)).Any(x => x.UserGuid == actor.UserGuid);
    }
    private static void EnsureDeviceScope(string storeCode, FaceDeviceContext device)
    {
        if (!ValidText(storeCode, 50) || !storeCode.Equals(device.StoreCode, StringComparison.OrdinalIgnoreCase)) throw new FaceAttendanceException(403, "FACE_DEVICE_SCOPE_MISMATCH", "设备与请求分店不一致");
    }
    private static void EnsureDeviceScope(string store, string code, string hardware, FaceDeviceContext device)
    {
        EnsureDeviceScope(store, device);
        if (!ValidText(code, 50) || !ValidText(hardware, 100) || !code.Equals(device.DeviceCode, StringComparison.OrdinalIgnoreCase) || hardware != device.HardwareId)
            throw new FaceAttendanceException(403, "FACE_DEVICE_SCOPE_MISMATCH", "设备上下文与请求设备不一致");
    }
    private static void ValidateEvent(FaceAttendanceEventCommandDto c, byte[] photo)
    {
        if (!Guid.TryParseExact(c.EventGuid, "D", out _) || !ValidText(c.UserGuid, 50) || !ValidText(c.KeyId, 64)
            || c.TimeAnchorId?.Length > 64 || c.Signature?.Length > 128 || c.OccurredAtUtc == default || c.DeviceObservedAtUtc == default
            || c.LocalSequence < 1 || c.LocalSequence > 9007199254740991L || c.RosterVersion < 0 || c.EnrollmentVersion < 0
            || (c.PunchType != "clockIn" && c.PunchType != "clockOut") || !ValidPhoto(photo) || !IsSha256(c.PhotoSha256))
            throw new FaceAttendanceException(400, "FACE_EVENT_INVALID", "人脸考勤事件字段无效");
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(c.PhotoSha256), SHA256.HashData(photo)))
            throw new FaceAttendanceException(400, "PHOTO_HASH_MISMATCH", "照片摘要不一致");
    }
    private static bool ValidText(string? v, int max) => !string.IsNullOrWhiteSpace(v) && v.Length <= max;
    private static bool ValidPhoto(byte[] p) => p.Length is > 3 and <= MaximumPhotoBytes && p[0] == 0xff && p[1] == 0xd8 && p[2] == 0xff;
    private static bool IsSha256(string? v) => v?.Length == 64 && v.All(Uri.IsHexDigit);
    private DateTime UtcNow() => _clock.GetUtcNow().UtcDateTime;
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    public static string IsoMillis(DateTime value) => Utc(value).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    public static string EventCanonical(FaceAttendanceEventCommandDto c) => JsonSerializer.Serialize(new[] { "v1", c.EventGuid, c.UserGuid, c.StoreCode, c.DeviceCode, c.HardwareId, c.PunchType, IsoMillis(c.OccurredAtUtc), IsoMillis(c.DeviceObservedAtUtc), c.LocalSequence.ToString(CultureInfo.InvariantCulture), c.RosterVersion.ToString(CultureInfo.InvariantCulture), c.EnrollmentVersion.ToString(CultureInfo.InvariantCulture), c.TimeAnchorId, c.TimeTrusted ? "true" : "false", c.PhotoSha256.ToLowerInvariant(), c.KeyId }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    public static string DeviceSessionCanonical(FaceDeviceSessionRequestDto r) => JsonSerializer.Serialize(new[] { "v1", "device-session", r.StoreCode, r.DeviceCode, r.HardwareId, IsoMillis(r.DeviceObservedAtUtc), r.Nonce, r.KeyId ?? string.Empty }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    private static string HashImmutable(FaceAttendanceEventCommandDto c) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(EventCanonical(c))));
    private static bool VerifyHmac(string secret, string canonical, string? signature)
    {
        try { return CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(Convert.FromBase64String(secret), Encoding.UTF8.GetBytes(canonical)), Convert.FromBase64String(signature ?? "")); }
        catch (FormatException) { return false; }
    }
}
public sealed record FaceDeviceContext(string StoreCode, string DeviceCode, string HardwareId);
public sealed record FaceManagementActor(string UserGuid, DateTime AuthenticatedAtUtc);
public sealed class FaceAttendanceException(int statusCode, string code, string message) : Exception(message) { public int StatusCode { get; } = statusCode; public string Code { get; } = code; }
