using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;
using Microsoft.AspNetCore.DataProtection;

namespace BlazorApp.Api.Services.Attendance;

public sealed partial class FaceAttendanceService
{
    private sealed class Member
    {
        public string UserGuid { get; set; } = "";
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Relationship { get; set; } = "";
        public DateTime AssignedAt { get; set; }
    }
    private static Task<List<Member>> MembersAsync(ISqlSugarClient db, string storeCode) => db.Queryable<UserStore>()
        .InnerJoin<User>((us, u) => us.UserGUID == u.UserGUID).InnerJoin<Store>((us, u, s) => us.StoreGUID == s.StoreGUID)
        .Where((us, u, s) => !us.IsDeleted && !u.IsDeleted && u.IsActive && !s.IsDeleted && s.IsActive && s.StoreCode == storeCode)
        .Select((us, u, s) => new Member { UserGuid = u.UserGUID, Username = u.Username, FullName = u.FullName ?? "",
            Relationship = us.UserStoreGUID, AssignedAt = us.AssignedAt }).ToListAsync();
    private static SortedDictionary<string, string> Fingerprints(IEnumerable<Member> members)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in members.GroupBy(x => x.UserGuid))
        {
            var text = JsonSerializer.Serialize(group.OrderBy(x => x.Relationship).Select(x => new[] { x.UserGuid, x.Username, x.FullName, x.Relationship, IsoMillis(x.AssignedAt) }));
            result[group.Key] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }
        return result;
    }
    // 正式写入也在员工数据库锁内调用，防止 DNN 计算期间撤销档案后仍通过旧模板记卡。
    internal static async Task<bool> IsSubjectCurrentAsync(ISqlSugarClient db, FaceAttendanceEvent row)
    {
        var enrollment = await db.Queryable<FaceAttendanceEnrollment>().FirstAsync(x => x.UserGuid == row.UserGuid && x.StoreCode == row.StoreCode);
        if (enrollment?.Status != "active" || enrollment.Version != row.EnrollmentVersion) return false;
        var snapshot = await db.Queryable<FaceAttendanceRosterSnapshot>().FirstAsync(x => x.StoreCode == row.StoreCode && x.Version == row.RosterVersion);
        if (snapshot == null) return false;
        var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(snapshot.MemberFingerprintsJson);
        var current = Fingerprints(await MembersAsync(db, row.StoreCode));
        return saved != null && saved.TryGetValue(row.UserGuid, out var fingerprint) && current.TryGetValue(row.UserGuid, out var now) && fingerprint == now;
    }

    public async Task ProcessQueuedAsync(CancellationToken ct)
    {
        var now = UtcNow();
        var candidates = await _db.Queryable<FaceAttendanceEvent>()
            .Where(x => (x.Status == "queued" || x.Status == "verifying") && x.NextAttemptAtUtc <= now && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc < now))
            .OrderBy(x => x.OccurredAtUtc).OrderBy(x => x.LocalSequence).Take(20).ToListAsync();
        foreach (var row in candidates)
        {
            ct.ThrowIfCancellationRequested();
            // 同一员工已有更早待核验事件时先等待；跨设备只用时间线判断，不拿到达顺序切换上下班。
            var earlier = await _db.Queryable<FaceAttendanceEvent>().AnyAsync(x => x.UserGuid == row.UserGuid && x.EventGuid != row.EventGuid
                && (x.Status == "queued" || x.Status == "verifying") && (x.OccurredAtUtc < row.OccurredAtUtc
                    || (x.OccurredAtUtc == row.OccurredAtUtc && x.DeviceCode == row.DeviceCode && x.LocalSequence < row.LocalSequence)));
            if (earlier) continue;
            if (!await ClaimAsync(row, row.Status)) continue;
            try
            {
                // 崩溃可能发生在正式事务提交后、事件回执更新前；此时恢复已有唯一关联。
                var committed = await _db.Queryable<AttendancePunch>().FirstAsync(x => x.FaceEventGuid == row.EventGuid);
                if (committed != null) { row.PunchGuid = committed.PunchGuid; row.Status = "verified"; row.ReasonCode = null; }
                else if (!await IsSubjectCurrentAsync(_db, row)) { row.Status = "needsReview"; row.ReasonCode = "ROSTER_OR_ENROLLMENT_CHANGED"; }
                else
                {
                    var enrollment = await _db.Queryable<FaceAttendanceEnrollment>().FirstAsync(x => x.UserGuid == row.UserGuid && x.StoreCode == row.StoreCode);
                    await VerifyIdentityAsync(row, enrollment, ct);
                    if (!await RenewLeaseAsync(row)) continue;
                    row.PunchGuid = await Writer().CommitAsync(row, "FaceRecognitionWorker", ct);
                    row.Status = "verified"; row.ReasonCode = null;
                }
            }
            catch (FaceAttendanceException ex) when (ex.StatusCode is 409 or 410 or 422)
            { row.Status = ex.StatusCode == 422 ? "rejected" : "needsReview"; row.ReasonCode = ex.Code; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // 临时网络/模型/存储错误保留原始事件和照片，退避后用相同 GUID 继续。
                row.Status = "queued"; row.ReasonCode = "FACE_RECOGNITION_RETRY";
            }
            await SaveOutcomeAsync(row);
        }
        await CleanupExpiredPhotosAsync();
    }

    public async Task<FaceAttendanceEventDto> ReviewAsync(string eventGuid, FaceAttendanceReviewRequestDto request, FaceDeviceContext device, FaceManagementActor? actor, CancellationToken ct)
    {
        var row = await EventAsync(eventGuid); EnsureDeviceScope(row.StoreCode, device);
        await RequireManageAsync(actor, row.StoreCode, Permissions.Attendance.Face.ReviewManagedStore, ct);
        if (request.Decision is not ("approve" or "reject") || !ValidText(request.Reason, 500))
            throw new FaceAttendanceException(400, "REVIEW_DECISION_INVALID", "必须填写审核决定及原因");
        // 审核已提交但回执丢失时，相同操作重试只返回原审计结果，不再次核验或记卡。
        if (row.ReviewedBy == actor!.UserGuid && row.ReviewReason == request.Reason!.Trim()
            && ((request.Decision == "approve" && row.Status == "verified") || (request.Decision == "reject" && row.Status == "rejected"))) return ToDto(row);
        if (row.Status != "needsReview") throw new FaceAttendanceException(409, "EVENT_NOT_REVIEWABLE", "当前事件不需要人工审核");
        if (!await ClaimAsync(row, "needsReview")) throw new FaceAttendanceException(409, "EVENT_REVIEW_BUSY", "事件正在处理中");
        try
        {
            if (request.Decision == "approve")
            {
                if (Utc(row.OccurredAtUtc) > UtcNow().AddSeconds(30) || !await IsSubjectCurrentAsync(_db, row))
                    throw new FaceAttendanceException(409, "EXISTING_ADJUSTMENT_REQUIRED", "请通过现有补卡审批处理资料或时间变更");
                var enrollment = await _db.Queryable<FaceAttendanceEnrollment>().FirstAsync(x => x.UserGuid == row.UserGuid && x.StoreCode == row.StoreCode);
                // 时间需复核不代表身份已经通过；经理不能跳过实际 1:1 人脸核验。
                await VerifyIdentityAsync(row, enrollment, ct);
                await RequireManageAsync(actor, row.StoreCode, Permissions.Attendance.Face.ReviewManagedStore, ct);
                if (!await RenewLeaseAsync(row)) throw new FaceAttendanceException(409, "EVENT_REVIEW_BUSY", "事件处理租约已失效");
                row.PunchGuid = await Writer().CommitAsync(row, actor!.UserGuid, ct);
                row.Status = "verified"; row.ReasonCode = null;
            }
            else { await RequireManageAsync(actor, row.StoreCode, Permissions.Attendance.Face.ReviewManagedStore, ct); row.Status = "rejected"; row.ReasonCode = "MANAGER_REJECTED"; }
            row.ReviewedBy = actor!.UserGuid; row.ReviewedAtUtc = UtcNow(); row.ReviewReason = request.Reason!.Trim();
            await SaveOutcomeAsync(row); return ToDto(row);
        }
        catch
        {
            row.Status = "needsReview";
            await SaveOutcomeAsync(row);
            throw;
        }
    }

    private IFaceAttendancePunchWriter Writer() => _writer ?? throw new InvalidOperationException("Face attendance writer is not registered");
    private async Task<bool> ClaimAsync(FaceAttendanceEvent row, string expectedStatus)
    {
        var now = UtcNow(); var leaseId = Guid.NewGuid().ToString("N"); var until = now.AddMinutes(2);
        // 人工审核领取后仍保持 needsReview，崩溃恢复不能把未审批记录当成自动核验。
        var claimedStatus = expectedStatus == "needsReview" ? "needsReview" : "verifying";
        var claimed = await _db.Updateable<FaceAttendanceEvent>()
            .SetColumns(x => new FaceAttendanceEvent { LeaseId = leaseId, LeaseExpiresAtUtc = until, Status = claimedStatus, AttemptCount = x.AttemptCount + 1, UpdatedAtUtc = now })
            .Where(x => x.EventGuid == row.EventGuid && x.Status == expectedStatus && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc < now)).ExecuteCommandAsync();
        if (claimed != 1) return false;
        row.LeaseId = leaseId; row.LeaseExpiresAtUtc = until; row.AttemptCount++; return true;
    }
    private async Task<bool> RenewLeaseAsync(FaceAttendanceEvent row)
    {
        var now = UtcNow(); var until = now.AddMinutes(2);
        return await _db.Updateable<FaceAttendanceEvent>().SetColumns(x => new FaceAttendanceEvent { LeaseExpiresAtUtc = until })
            .Where(x => x.EventGuid == row.EventGuid && x.LeaseId == row.LeaseId && x.LeaseExpiresAtUtc > now).ExecuteCommandAsync() == 1;
    }
    private async Task SaveOutcomeAsync(FaceAttendanceEvent row)
    {
        row.UpdatedAtUtc = UtcNow(); row.NextAttemptAtUtc = row.UpdatedAtUtc.AddSeconds(Math.Min(900, 15 * Math.Pow(2, Math.Min(row.AttemptCount, 6))));
        // 只更新处理列，禁止把旧实例中的照片写回（保留期清理可能已删除照片）。
        await _db.Updateable<FaceAttendanceEvent>().SetColumns(x => new FaceAttendanceEvent {
            Status = row.Status, ReasonCode = row.ReasonCode, PunchGuid = row.PunchGuid, UpdatedAtUtc = row.UpdatedAtUtc, NextAttemptAtUtc = row.NextAttemptAtUtc,
            FaceVerifiedAtUtc = row.FaceVerifiedAtUtc, FaceSimilarity = row.FaceSimilarity,
            ReviewedBy = row.ReviewedBy, ReviewedAtUtc = row.ReviewedAtUtc, ReviewReason = row.ReviewReason,
            LeaseId = null, LeaseExpiresAtUtc = null
        }).Where(x => x.EventGuid == row.EventGuid && x.LeaseId == row.LeaseId).ExecuteCommandAsync();
    }
    public async Task CleanupExpiredPhotosAsync()
    {
        // 停用识别不等于停用保留期；兼容尚未执行扩展迁移的旧环境。
        if (!_db.DbMaintenance.IsAnyTable("FaceAttendanceEvent", false)) return;
        var now = UtcNow();
        var expired = await _db.Queryable<FaceAttendanceEvent>().Where(x => x.RetainUntilUtc != null && x.RetainUntilUtc <= now
            && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc < now)).Take(100).ToListAsync();
        foreach (var row in expired)
        {
            var status = row.Status is "queued" or "verifying" ? "needsReview" : row.Status;
            var reason = row.Status is "queued" or "verifying" ? "PHOTO_EXPIRED" : row.ReasonCode;
            await _db.Updateable<FaceAttendanceEvent>().SetColumns(x => new FaceAttendanceEvent { ProtectedPhoto = "", RetainUntilUtc = null, Status = status, ReasonCode = reason, UpdatedAtUtc = now })
                .Where(x => x.EventGuid == row.EventGuid && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc < now)).ExecuteCommandAsync();
        }
    }

    private async Task<string> CreateTemplatesAsync(IReadOnlyList<byte[]> photos, CancellationToken ct)
    {
        using var client = RecognitionClient();
        using var response = await client.PostAsJsonAsync("templates", new { imagesBase64 = photos.Select(Convert.ToBase64String).ToArray() }, ct);
        await CheckRecognitionResponseAsync(response, ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = body.RootElement;
        var templates = root.GetProperty("templates").EnumerateArray().Select(x => x.GetString()).ToArray();
        if (templates.Length != 3 || templates.Any(x => string.IsNullOrEmpty(x) || Convert.FromBase64String(x).Length != 512)
            || root.GetProperty("modelVersion").GetString() != ModelVersion)
            throw new FaceAttendanceException(503, "FACE_TEMPLATE_INVALID", "核验模型返回的数据无效");
        return JsonSerializer.Serialize(new { templates, modelVersion = ModelVersion });
    }
    private async Task VerifyIdentityAsync(FaceAttendanceEvent row, FaceAttendanceEnrollment enrollment, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(row.ProtectedPhoto) || row.RetainUntilUtc <= UtcNow()) throw new FaceAttendanceException(410, "PHOTO_EXPIRED", "照片已超过保留期限");
        using var client = RecognitionClient(); using var templates = JsonDocument.Parse(_protector.Unprotect(enrollment.ProtectedTemplatesJson));
        if (templates.RootElement.GetProperty("modelVersion").GetString() != ModelVersion) throw new FaceAttendanceException(409, "FACE_MODEL_CHANGED", "需要重新录入人脸档案");
        using var response = await client.PostAsJsonAsync("verify", new { imageBase64 = _protector.Unprotect(row.ProtectedPhoto),
            templates = templates.RootElement.GetProperty("templates").EnumerateArray().Select(x => x.GetString()).ToArray(), modelVersion = ModelVersion }, ct);
        await CheckRecognitionResponseAsync(response, ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var threshold = _configuration.GetValue("FaceRecognition:Threshold", 0.50);
        if (!double.IsFinite(threshold) || threshold <= 0 || threshold > 1 || !body.RootElement.TryGetProperty("score", out var score)
            || !score.TryGetDouble(out var value) || !double.IsFinite(value) || value < -1 || value > 1
            || body.RootElement.GetProperty("modelVersion").GetString() != ModelVersion)
            throw new FaceAttendanceException(503, "FACE_RECOGNITION_RESPONSE_INVALID", "核验服务响应无效");
        if (value < threshold) throw new FaceAttendanceException(422, "FACE_MATCH_BELOW_THRESHOLD", "人脸未匹配，请重新拍摄");
        row.FaceSimilarity = value; row.FaceVerifiedAtUtc = UtcNow();
    }
    private static async Task CheckRecognitionResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        if ((int)response.StatusCode == 422)
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var code = body.RootElement.TryGetProperty("code", out var item) ? item.GetString() : null;
            // 不把模型服务任意正文或照片内容写入错误原因/日志。
            var known = new[] { "invalid_photo", "invalid_photo_dimensions", "photo_too_large", "poor_quality", "no_face", "multiple_faces", "face_too_small", "photo_blurry", "photo_lighting", "invalid_image", "invalid_jpeg", "image_too_large", "image_dimensions_invalid", "enrollment_faces_inconsistent" };
            throw new FaceAttendanceException(422, known.Contains(code) ? "FACE_" + code!.ToUpperInvariant() : "FACE_PHOTO_INVALID", "照片不合格，请重新拍摄");
        }
        throw new FaceAttendanceException(503, "FACE_RECOGNITION_UNAVAILABLE", "人脸核验服务暂不可用");
    }
    private HttpClient RecognitionClient()
    {
        var baseUrl = _configuration["FaceRecognition:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new FaceAttendanceException(503, "FACE_RECOGNITION_UNAVAILABLE", "人脸核验服务尚未配置");
        var secret = _configuration["FaceRecognition:SharedSecret"] ?? Environment.GetEnvironmentVariable("ATTENDANCE_FACE_WORKER_TOKEN");
        if (string.IsNullOrEmpty(secret) || secret.Length < 32) throw new FaceAttendanceException(503, "FACE_RECOGNITION_UNAVAILABLE", "人脸核验服务凭据尚未配置");
        var client = _httpClientFactory.CreateClient("AttendanceFaceRecognition");
        client.BaseAddress = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/"); client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);
        return client;
    }
}
