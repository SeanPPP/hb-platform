using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services;

public sealed class EmployeeProfileSensitiveChangeService
{
    public const string VersionConflictCode = "EMPLOYEE_PROFILE_SENSITIVE_VERSION_CONFLICT";

    private readonly SqlSugarContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<EmployeeProfileSensitiveChangeService> _logger;
    private readonly TencentCloudUploadService? _storage;

    public EmployeeProfileSensitiveChangeService(
        SqlSugarContext context,
        ICurrentUserService currentUser,
        ILogger<EmployeeProfileSensitiveChangeService> logger,
        TencentCloudUploadService? storage = null
    )
    {
        _context = context;
        _currentUser = currentUser;
        _logger = logger;
        _storage = storage;
    }

    public async Task<ApiResponse<EmployeeProfileSensitiveChangeDetailDto?>> GetSelfAsync()
    {
        var userGuid = _currentUser.GetCurrentUserGuid();
        if (string.IsNullOrWhiteSpace(userGuid))
        {
            return ApiResponse<EmployeeProfileSensitiveChangeDetailDto?>.Error(
                "未找到当前用户",
                "CURRENT_USER_NOT_FOUND"
            );
        }
        var request = await _context.Db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .Where(item => item.UserGUID == userGuid)
            .OrderBy(item => item.SubmittedAt, SqlSugar.OrderByType.Desc)
            .FirstAsync();
        return ApiResponse<EmployeeProfileSensitiveChangeDetailDto?>.OK(
            request is null ? null : MapDetail(request)
        );
    }

    public async Task<ApiResponse<EmployeeProfileSensitiveChangeDetailDto>> UpsertSelfAsync(
        EmployeeProfileSensitiveChangeUpsertDto dto
    )
    {
        var userGuid = _currentUser.GetCurrentUserGuid();
        if (string.IsNullOrWhiteSpace(userGuid))
        {
            return ApiResponse<EmployeeProfileSensitiveChangeDetailDto>.Error(
                "未找到当前用户",
                "CURRENT_USER_NOT_FOUND"
            );
        }
        return await ReplacePendingAsync(userGuid, dto, identityPhotoObjectKey: null, preservePendingPhoto: true);
    }

    /// <summary>证件图 complete/delete 只更新待审快照，正式资料保持不变。</summary>
    public async Task<ApiResponse<EmployeeProfileSensitiveChangeDetailDto>> ReplacePendingIdentityPhotoAsync(
        string identityPhotoObjectKey
    )
    {
        var userGuid = _currentUser.GetCurrentUserGuid();
        if (string.IsNullOrWhiteSpace(userGuid))
        {
            return ApiResponse<EmployeeProfileSensitiveChangeDetailDto>.Error(
                "未找到当前用户",
                "CURRENT_USER_NOT_FOUND"
            );
        }
        var snapshot = await BuildCurrentSnapshotAsync(userGuid);
        return await ReplacePendingAsync(
            userGuid,
            snapshot,
            Normalize(identityPhotoObjectKey),
            preservePendingPhoto: false
        );
    }

    public async Task<ApiResponse<EmployeeProfileSensitiveChangeDetailDto>> DeletePendingIdentityPhotoAsync()
    {
        var userGuid = _currentUser.GetCurrentUserGuid();
        if (string.IsNullOrWhiteSpace(userGuid))
        {
            return ApiResponse<EmployeeProfileSensitiveChangeDetailDto>.Error(
                "未找到当前用户",
                "CURRENT_USER_NOT_FOUND"
            );
        }
        var snapshot = await BuildCurrentSnapshotAsync(userGuid);
        return await ReplacePendingAsync(userGuid, snapshot, identityPhotoObjectKey: null, preservePendingPhoto: false);
    }

    public async Task<ApiResponse<PagedResult<EmployeeProfileSensitiveChangeSummaryDto>>> GetAdminListAsync(
        EmployeeProfileSensitiveChangeQueryDto query
    )
    {
        var db = _context.Db;
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var status = ParseStatus(query.Status);
        var source = db.Queryable<EmployeeProfileSensitiveChangeRequest, User, EmployeeProfile>(
            (request, user, profile) => new object[]
            {
                SqlSugar.JoinType.Left,
                request.UserGUID == user.UserGUID,
                SqlSugar.JoinType.Left,
                request.UserGUID == profile.UserGUID && !profile.IsDeleted,
            }
        );
        if (status.HasValue)
        {
            source = source.Where((request, user, profile) => request.Status == status.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where((request, user, profile) =>
                request.UserGUID.Contains(search)
                || user.Username.Contains(search)
                || (user.FullName != null && user.FullName.Contains(search))
            );
        }
        var total = await source.CountAsync();
        var rows = await source
            .OrderBy((request, user, profile) => request.SubmittedAt, SqlSugar.OrderByType.Desc)
            .Select((request, user, profile) => new { Request = request, user.Username, Profile = profile })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return ApiResponse<PagedResult<EmployeeProfileSensitiveChangeSummaryDto>>.OK(new()
        {
            Items = rows.Select(row => MapSummary(row.Request, row.Username, row.Profile)).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    public async Task<ApiResponse<EmployeeProfileSensitiveChangeDetailDto>> GetAdminDetailAsync(int requestId)
    {
        var request = await _context.Db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.RequestId == requestId);
        return request is null
            ? ApiResponse<EmployeeProfileSensitiveChangeDetailDto>.Error("申请不存在", "REQUEST_NOT_FOUND")
            : ApiResponse<EmployeeProfileSensitiveChangeDetailDto>.OK(MapDetail(request));
    }

    public async Task<ApiResponse<EmployeeProfileSensitiveChangeDetailDto>> ApproveAsync(
        int requestId,
        EmployeeProfileSensitiveReviewDto dto
    )
    {
        var db = _context.Db;
        var request = await db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.RequestId == requestId);
        if (request is null)
        {
            return ApiResponse<EmployeeProfileSensitiveChangeDetailDto>.Error("申请不存在", "REQUEST_NOT_FOUND");
        }
        if (request.Status != EmployeeProfileSensitiveChangeStatus.Pending)
        {
            return ApiResponse<EmployeeProfileSensitiveChangeDetailDto>.Error("申请已处理", "REQUEST_NOT_PENDING");
        }
        var profile = await db.Queryable<EmployeeProfile>()
            .FirstAsync(item => item.UserGUID == request.UserGUID && !item.IsDeleted);
        if (profile is null || profile.SensitiveRevision != request.BaseSensitiveRevision)
        {
            return VersionConflict();
        }

        var actor = _currentUser.GetCurrentUsername();
        var now = DateTime.UtcNow;
        var oldPhoto = profile.IdentityPhotoObjectKey;
        var photoChanged = !NormalizedEquals(
            profile.IdentityPhotoObjectKey,
            request.IdentityPhotoObjectKey
        );
        var sensitiveValuesChanged = HasSensitiveValueChanges(profile, request);
        await db.Ado.BeginTranAsync();
        try
        {
            // 关键逻辑：revision 条件更新是防止管理员并发修改被审批静默覆盖的最终防线。
            int updated;
            if (sensitiveValuesChanged)
            {
                var profileUpdate = db.Updateable<EmployeeProfile>()
                    .SetColumns(item => item.BankBSB == request.BankBsb)
                    .SetColumns(item => item.BankACC == request.BankAccountNumber)
                    .SetColumns(item => item.SuperannuationCompanyName == request.SuperannuationCompanyName)
                    .SetColumns(item => item.SuperannuationCompanyCode == request.SuperannuationCompanyCode)
                    .SetColumns(item => item.SuperannuationAccount == request.SuperannuationAccountNumber)
                    .SetColumns(item => item.IdentityType == request.IdentityType)
                    .SetColumns(item => item.IdentityId == request.IdentityId)
                    .SetColumns(item => item.SensitiveRevision == request.BaseSensitiveRevision + 1)
                    .SetColumns(item => item.UpdatedAt == now)
                    .SetColumns(item => item.UpdatedBy == actor);
                if (photoChanged)
                {
                    // 仅证件照对象实际变化时移除旧式 URL，避免字段审批误清正式证件照。
                    profileUpdate = profileUpdate
                        .SetColumns(item => item.IdentityPhotoObjectKey == request.IdentityPhotoObjectKey)
                        .SetColumns(item => item.IdentityPhotoUrl == null);
                }
                updated = await profileUpdate
                    .Where(item => item.EmployeeInfoId == profile.EmployeeInfoId
                        && item.SensitiveRevision == request.BaseSensitiveRevision)
                    .ExecuteCommandAsync();
            }
            else
            {
                // 等值申请仍执行 revision CAS，但不制造虚假的正式资料版本。
                updated = await db.Updateable<EmployeeProfile>()
                    .SetColumns(item => item.SensitiveRevision == request.BaseSensitiveRevision)
                    .Where(item => item.EmployeeInfoId == profile.EmployeeInfoId
                        && item.SensitiveRevision == request.BaseSensitiveRevision)
                    .ExecuteCommandAsync();
            }
            if (updated != 1)
            {
                throw new SensitiveRevisionConflictException();
            }
            var reviewed = await db.Updateable<EmployeeProfileSensitiveChangeRequest>()
                .SetColumns(item => new EmployeeProfileSensitiveChangeRequest
                {
                    Status = EmployeeProfileSensitiveChangeStatus.Approved,
                    ReviewedAt = now,
                    ReviewedBy = actor,
                    ReviewReason = Normalize(dto.Reason),
                })
                .Where(item => item.RequestId == requestId
                    && item.Status == EmployeeProfileSensitiveChangeStatus.Pending)
                .ExecuteCommandAsync();
            if (reviewed != 1)
            {
                throw new InvalidOperationException("申请状态已改变");
            }
            if (!string.IsNullOrWhiteSpace(oldPhoto)
                && !string.Equals(oldPhoto, request.IdentityPhotoObjectKey, StringComparison.Ordinal))
            {
                await ScheduleTicketCleanupAsync(requestId, oldPhoto, request.UserGUID);
            }
            await db.Ado.CommitTranAsync();
        }
        catch (SensitiveRevisionConflictException)
        {
            await db.Ado.RollbackTranAsync();
            return VersionConflict();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
        request.Status = EmployeeProfileSensitiveChangeStatus.Approved;
        request.ReviewedAt = now;
        request.ReviewedBy = actor;
        request.ReviewReason = Normalize(dto.Reason);
        await CleanupObjectAsync(oldPhoto, request.IdentityPhotoObjectKey, request.UserGUID, "批准后旧正式证件照", requestId);
        return ApiResponse<EmployeeProfileSensitiveChangeDetailDto>.OK(MapDetail(request), "申请已批准");
    }

    public async Task<ApiResponse<EmployeeProfileSensitiveChangeDetailDto>> RejectAsync(
        int requestId,
        EmployeeProfileSensitiveRejectDto dto
    )
    {
        if (string.IsNullOrWhiteSpace(dto.Reason))
        {
            return ApiResponse<EmployeeProfileSensitiveChangeDetailDto>.Error("拒绝原因必填", "VALIDATION_ERROR");
        }
        var request = await _context.Db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.RequestId == requestId);
        if (request is null)
        {
            return ApiResponse<EmployeeProfileSensitiveChangeDetailDto>.Error("申请不存在", "REQUEST_NOT_FOUND");
        }
        if (request.Status != EmployeeProfileSensitiveChangeStatus.Pending)
        {
            return ApiResponse<EmployeeProfileSensitiveChangeDetailDto>.Error("申请已处理", "REQUEST_NOT_PENDING");
        }
        var actor = _currentUser.GetCurrentUsername();
        var now = DateTime.UtcNow;
        await _context.Db.Ado.BeginTranAsync();
        try
        {
            var changed = await _context.Db.Updateable<EmployeeProfileSensitiveChangeRequest>()
                .SetColumns(item => new EmployeeProfileSensitiveChangeRequest
                {
                    Status = EmployeeProfileSensitiveChangeStatus.Rejected,
                    ReviewedAt = now,
                    ReviewedBy = actor,
                    ReviewReason = dto.Reason.Trim(),
                })
                .Where(item => item.RequestId == requestId
                    && item.Status == EmployeeProfileSensitiveChangeStatus.Pending)
                .ExecuteCommandAsync();
            if (changed != 1)
            {
                await _context.Db.Ado.RollbackTranAsync();
                return ApiResponse<EmployeeProfileSensitiveChangeDetailDto>.Error("申请已处理", "REQUEST_NOT_PENDING");
            }
            await ScheduleTicketCleanupAsync(
                requestId,
                request.IdentityPhotoObjectKey,
                request.UserGUID
            );
            await _context.Db.Ado.CommitTranAsync();
        }
        catch
        {
            await _context.Db.Ado.RollbackTranAsync();
            throw;
        }
        request.Status = EmployeeProfileSensitiveChangeStatus.Rejected;
        request.ReviewedAt = now;
        request.ReviewedBy = actor;
        request.ReviewReason = dto.Reason.Trim();
        await CleanupObjectAsync(request.IdentityPhotoObjectKey, null, request.UserGUID, "驳回待审证件照", requestId);
        return ApiResponse<EmployeeProfileSensitiveChangeDetailDto>.OK(MapDetail(request), "申请已拒绝");
    }

    /// <summary>管理员直接改正式资料后在同一数据库事务内调用，终结所有待审申请。</summary>
    public async Task<List<string>> SupersedePendingWithinTransactionAsync(string userGuid, string actor)
    {
        var pending = await _context.Db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .Where(item => item.UserGUID == userGuid
                && item.Status == EmployeeProfileSensitiveChangeStatus.Pending)
            .ToListAsync();
        if (pending.Count == 0)
        {
            return new();
        }
        var now = DateTime.UtcNow;
        await _context.Db.Updateable<EmployeeProfileSensitiveChangeRequest>()
            .SetColumns(item => new EmployeeProfileSensitiveChangeRequest
            {
                Status = EmployeeProfileSensitiveChangeStatus.Superseded,
                SupersededAt = now,
                SupersededBy = actor,
            })
            .Where(item => item.UserGUID == userGuid
                && item.Status == EmployeeProfileSensitiveChangeStatus.Pending)
            .ExecuteCommandAsync();
        foreach (var item in pending)
        {
            await ScheduleTicketCleanupAsync(
                item.RequestId,
                item.IdentityPhotoObjectKey,
                item.UserGUID
            );
        }
        return pending.Select(item => item.IdentityPhotoObjectKey)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public async Task CleanupSupersededObjectsAsync(string userGuid, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            var request = await _context.Db.Queryable<EmployeeProfileSensitiveChangeRequest>()
                .Where(item => item.UserGUID == userGuid
                    && item.Status == EmployeeProfileSensitiveChangeStatus.Superseded
                    && item.IdentityPhotoObjectKey == key)
                .OrderBy(item => item.SupersededAt, SqlSugar.OrderByType.Desc)
                .FirstAsync();
            await CleanupObjectAsync(key, null, userGuid, "管理员直改后待审证件照", request?.RequestId);
        }
    }

    private async Task<ApiResponse<EmployeeProfileSensitiveChangeDetailDto>> ReplacePendingAsync(
        string userGuid,
        EmployeeProfileSensitiveChangeUpsertDto dto,
        string? identityPhotoObjectKey,
        bool preservePendingPhoto
    )
    {
        await using var sensitiveLock = await EmployeeProfileMediaLock.AcquireAsync(
            _context.Db,
            userGuid,
            "sensitive-change",
            _logger
        );
        return await ReplacePendingLockedAsync(
            userGuid,
            dto,
            identityPhotoObjectKey,
            preservePendingPhoto
        );
    }

    private async Task<ApiResponse<EmployeeProfileSensitiveChangeDetailDto>> ReplacePendingLockedAsync(
        string userGuid,
        EmployeeProfileSensitiveChangeUpsertDto dto,
        string? identityPhotoObjectKey,
        bool preservePendingPhoto
    )
    {
        var db = _context.Db;
        var actor = _currentUser.GetCurrentUsername();
        var now = DateTime.UtcNow;
        var profile = await EnsureProfileAsync(userGuid, actor, now);
        var old = await db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.UserGUID == userGuid
                && item.Status == EmployeeProfileSensitiveChangeStatus.Pending);
        var retainedPhoto = preservePendingPhoto
            ? old?.IdentityPhotoObjectKey ?? profile.IdentityPhotoObjectKey
            : identityPhotoObjectKey;
        var request = new EmployeeProfileSensitiveChangeRequest
        {
            UserGUID = userGuid,
            BankBsb = Normalize(dto.BankBsb),
            BankAccountNumber = Normalize(dto.BankAccountNumber),
            SuperannuationCompanyName = Normalize(dto.SuperannuationCompanyName),
            SuperannuationCompanyCode = Normalize(dto.SuperannuationCompanyCode),
            SuperannuationAccountNumber = Normalize(dto.SuperannuationAccountNumber),
            IdentityType = Normalize(dto.IdentityType),
            IdentityId = Normalize(dto.IdentityId),
            IdentityPhotoObjectKey = retainedPhoto,
            Status = EmployeeProfileSensitiveChangeStatus.Pending,
            BaseSensitiveRevision = profile.SensitiveRevision,
            SubmittedAt = now,
            SubmittedBy = actor,
        };
        await db.Ado.BeginTranAsync();
        try
        {
            if (old is not null)
            {
                await db.Updateable<EmployeeProfileSensitiveChangeRequest>()
                    .SetColumns(item => new EmployeeProfileSensitiveChangeRequest
                    {
                        Status = EmployeeProfileSensitiveChangeStatus.Superseded,
                        SupersededAt = now,
                        SupersededBy = actor,
                    })
                    .Where(item => item.RequestId == old.RequestId
                        && item.Status == EmployeeProfileSensitiveChangeStatus.Pending)
                    .ExecuteCommandAsync();
                if (!string.IsNullOrWhiteSpace(old.IdentityPhotoObjectKey)
                    && !string.Equals(old.IdentityPhotoObjectKey, retainedPhoto, StringComparison.Ordinal))
                {
                    await ScheduleTicketCleanupAsync(
                        old.RequestId,
                        old.IdentityPhotoObjectKey,
                        old.UserGUID
                    );
                }
            }
            request.RequestId = await db.Insertable(request).ExecuteReturnIdentityAsync();
            if (old is not null && !string.IsNullOrWhiteSpace(retainedPhoto))
            {
                // 覆盖申请但复用同一待审证件照时，恢复清理责任必须随申请迁移。
                await db.Updateable<EmployeeImageUploadTicket>()
                    .SetColumns(item => item.SensitiveChangeRequestId == request.RequestId)
                    .Where(item => item.SensitiveChangeRequestId == old.RequestId
                        && item.UserGUID == userGuid
                        && item.Kind == "identity"
                        && item.Status == EmployeeImageUploadStatus.Completed
                        && item.FinalObjectKey == retainedPhoto)
                    .ExecuteCommandAsync();
            }
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
        if (old is not null
            && !string.IsNullOrWhiteSpace(old.IdentityPhotoObjectKey)
            && !string.Equals(old.IdentityPhotoObjectKey, retainedPhoto, StringComparison.Ordinal))
        {
            await CleanupObjectAsync(old.IdentityPhotoObjectKey, retainedPhoto, userGuid, "覆盖待审证件照", old.RequestId);
        }
        return ApiResponse<EmployeeProfileSensitiveChangeDetailDto>.OK(MapDetail(request), "敏感资料变更已提交审批");
    }

    private async Task<EmployeeProfileSensitiveChangeUpsertDto> BuildCurrentSnapshotAsync(string userGuid)
    {
        var pending = await _context.Db.Queryable<EmployeeProfileSensitiveChangeRequest>()
            .FirstAsync(item => item.UserGUID == userGuid
                && item.Status == EmployeeProfileSensitiveChangeStatus.Pending);
        if (pending is not null)
        {
            return new()
            {
                BankBsb = pending.BankBsb,
                BankAccountNumber = pending.BankAccountNumber,
                SuperannuationCompanyName = pending.SuperannuationCompanyName,
                SuperannuationCompanyCode = pending.SuperannuationCompanyCode,
                SuperannuationAccountNumber = pending.SuperannuationAccountNumber,
                IdentityType = pending.IdentityType,
                IdentityId = pending.IdentityId,
            };
        }
        var profile = await _context.Db.Queryable<EmployeeProfile>()
            .FirstAsync(item => item.UserGUID == userGuid && !item.IsDeleted);
        return new()
        {
            BankBsb = profile?.BankBSB,
            BankAccountNumber = profile?.BankACC,
            SuperannuationCompanyName = profile?.SuperannuationCompanyName,
            SuperannuationCompanyCode = profile?.SuperannuationCompanyCode,
            SuperannuationAccountNumber = profile?.SuperannuationAccount,
            IdentityType = profile?.IdentityType,
            IdentityId = profile?.IdentityId,
        };
    }

    private async Task<EmployeeProfile> EnsureProfileAsync(string userGuid, string actor, DateTime now)
    {
        var profile = await _context.Db.Queryable<EmployeeProfile>()
            .FirstAsync(item => item.UserGUID == userGuid && !item.IsDeleted);
        if (profile is not null)
        {
            return profile;
        }
        profile = new EmployeeProfile
        {
            UserGUID = userGuid,
            CreatedAt = now,
            CreatedBy = actor,
            UpdatedAt = now,
            UpdatedBy = actor,
        };
        profile.EmployeeInfoId = await _context.Db.Insertable(profile).ExecuteReturnIdentityAsync();
        return profile;
    }

    private async Task CleanupObjectAsync(
        string? key,
        string? retainedKey,
        string userGuid,
        string context,
        int? requestId = null
    )
    {
        if (_storage is null
            || string.IsNullOrWhiteSpace(key)
            || string.Equals(key, retainedKey, StringComparison.Ordinal)
            || !EmployeeProfileImageRules.OwnsObjectKey(key, userGuid, "identity"))
        {
            return;
        }
        var profile = await _context.Db.Queryable<EmployeeProfile>()
            .FirstAsync(item => item.UserGUID == userGuid && !item.IsDeleted);
        if (string.Equals(profile?.IdentityPhotoObjectKey, key, StringComparison.Ordinal))
        {
            // 待审快照可能复用当前正式证件照；终结申请绝不能删除仍被正式资料引用的对象。
            if (requestId.HasValue)
            {
                await MarkTicketCleanupCompletedAsync(requestId.Value, key);
            }
            return;
        }
        var result = await _storage.DeleteObjectAsync(key, CancellationToken.None);
        if (!result.Success)
        {
            _logger.LogWarning("{Context}清理失败。UserGUID: {UserGUID}, ObjectKey: {ObjectKey}", context, userGuid, key);
        }
        else if (requestId.HasValue)
        {
            await MarkTicketCleanupCompletedAsync(requestId.Value, key);
        }
    }

    private Task<int> MarkTicketCleanupCompletedAsync(int requestId, string objectKey) =>
        _context.Db.Updateable<EmployeeImageUploadTicket>()
            .SetColumns(item => new EmployeeImageUploadTicket
            {
                PreviousObjectCleanupStatus = EmployeeImageObjectCleanupStatus.Completed,
                PreviousObjectCleanupStartedAt = null,
            })
            .Where(item => item.SensitiveChangeRequestId == requestId
                && item.PreviousObjectKey == objectKey)
            .ExecuteCommandAsync();

    private async Task<int> ScheduleTicketCleanupAsync(
        int requestId,
        string? objectKey,
        string userGuid
    )
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return 0;
        }
        var scheduled = await _context.Db.Updateable<EmployeeImageUploadTicket>()
            .SetColumns(item => new EmployeeImageUploadTicket
            {
                PreviousObjectKey = objectKey,
                PreviousObjectCleanupStatus = EmployeeImageObjectCleanupStatus.Pending,
                PreviousObjectCleanupStartedAt = null,
            })
            .Where(item => item.SensitiveChangeRequestId == requestId
                && item.Status == EmployeeImageUploadStatus.Completed)
            .ExecuteCommandAsync();
        if (scheduled > 0)
        {
            return scheduled;
        }

        // 删除正式证件照可能没有对应上传票据；补建恢复票据，确保 COS 短暂失败后可重试。
        var now = DateTime.UtcNow;
        var recoveryTicket = new EmployeeImageUploadTicket
        {
            PendingObjectKey = $"cleanup/employee-profiles/{userGuid}/identity/{Guid.NewGuid():N}",
            UserGUID = userGuid,
            Kind = "identity",
            ContentType = "application/octet-stream",
            FileSize = 0,
            CreatedAt = now,
            ExpiresAt = now,
            Status = EmployeeImageUploadStatus.Completed,
            CompletedAt = now,
            StageChangedAt = now,
            PreviousObjectKey = objectKey,
            PreviousObjectCleanupStatus = EmployeeImageObjectCleanupStatus.Pending,
            SensitiveChangeRequestId = requestId,
        };
        return await _context.Db.Insertable(recoveryTicket).ExecuteCommandAsync();
    }

    private EmployeeProfileSensitiveChangeDetailDto MapDetail(
        EmployeeProfileSensitiveChangeRequest request
    )
    {
        var dto = new EmployeeProfileSensitiveChangeDetailDto
        {
            RequestId = request.RequestId,
            UserGuid = request.UserGUID,
            Status = FormatStatus(request.Status),
            BankBsb = request.BankBsb,
            BankAccountSummary = Mask(request.BankAccountNumber),
            BankAccountNumber = request.BankAccountNumber,
            SuperannuationCompanyName = request.SuperannuationCompanyName,
            SuperannuationCompanyCode = request.SuperannuationCompanyCode,
            SuperannuationAccountSummary = Mask(request.SuperannuationAccountNumber),
            SuperannuationAccountNumber = request.SuperannuationAccountNumber,
            IdentityType = request.IdentityType,
            IdentityIdSummary = Mask(request.IdentityId),
            IdentityId = request.IdentityId,
            HasIdentityPhoto = !string.IsNullOrWhiteSpace(request.IdentityPhotoObjectKey),
            BaseSensitiveRevision = request.BaseSensitiveRevision,
            SubmittedAt = request.SubmittedAt,
            SubmittedBy = request.SubmittedBy,
            ReviewedAt = request.ReviewedAt,
            ReviewedBy = request.ReviewedBy,
            ReviewReason = request.ReviewReason,
        };
        if (_storage is not null && !string.IsNullOrWhiteSpace(request.IdentityPhotoObjectKey))
        {
            var signed = _storage.GetSignedDownload(request.IdentityPhotoObjectKey, 300);
            dto.IdentityPhotoUrl = signed.Url;
            dto.IdentityPhotoUrlExpiresAt = signed.ExpiresAtUtc;
        }
        return dto;
    }

    private static EmployeeProfileSensitiveChangeSummaryDto MapSummary(
        EmployeeProfileSensitiveChangeRequest request,
        string? username,
        EmployeeProfile? profile
    ) => new()
    {
        RequestId = request.RequestId,
        UserGuid = request.UserGUID,
        Username = username,
        Status = FormatStatus(request.Status),
        BankBsb = request.BankBsb,
        BankAccountSummary = Mask(request.BankAccountNumber),
        SuperannuationCompanyName = request.SuperannuationCompanyName,
        SuperannuationCompanyCode = request.SuperannuationCompanyCode,
        SuperannuationAccountSummary = Mask(request.SuperannuationAccountNumber),
        IdentityType = request.IdentityType,
        IdentityIdSummary = Mask(request.IdentityId),
        HasIdentityPhoto = !string.IsNullOrWhiteSpace(request.IdentityPhotoObjectKey),
        BaseSensitiveRevision = request.BaseSensitiveRevision,
        SubmittedAt = request.SubmittedAt,
        ReviewedAt = request.ReviewedAt,
        ReviewReason = request.ReviewReason,
        // 列表只返回字段标识；完整正式值和申请值只在服务端内存中参与比较。
        ChangedFields = GetChangedFields(profile, request),
    };

    private static List<string> GetChangedFields(
        EmployeeProfile? profile,
        EmployeeProfileSensitiveChangeRequest request
    )
    {
        var fields = new List<string>();
        AddChanged(fields, "bankBsb", profile?.BankBSB, request.BankBsb);
        AddChanged(fields, "bankAccountNumber", profile?.BankACC, request.BankAccountNumber);
        AddChanged(fields, "superannuationCompanyName", profile?.SuperannuationCompanyName, request.SuperannuationCompanyName);
        AddChanged(fields, "superannuationCompanyCode", profile?.SuperannuationCompanyCode, request.SuperannuationCompanyCode);
        AddChanged(fields, "superannuationAccountNumber", profile?.SuperannuationAccount, request.SuperannuationAccountNumber);
        AddChanged(fields, "identityType", profile?.IdentityType, request.IdentityType);
        AddChanged(fields, "identityId", profile?.IdentityId, request.IdentityId);
        if (!NormalizedEquals(profile?.IdentityPhotoObjectKey, request.IdentityPhotoObjectKey))
        {
            fields.Add("identityPhotoUrl");
        }
        return fields;
    }

    private static void AddChanged(List<string> fields, string field, string? current, string? proposed)
    {
        if (!NormalizedEquals(current, proposed))
        {
            fields.Add(field);
        }
    }

    private static ApiResponse<EmployeeProfileSensitiveChangeDetailDto> VersionConflict() =>
        ApiResponse<EmployeeProfileSensitiveChangeDetailDto>.Error(
            "正式敏感资料已被管理员更新，请重新提交申请",
            VersionConflictCode
        );

    private static string? Mask(string? value)
    {
        var normalized = Normalize(value);
        return normalized is null ? null : $"****{normalized[^Math.Min(4, normalized.Length)..]}";
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool NormalizedEquals(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    private static bool HasSensitiveValueChanges(
        EmployeeProfile profile,
        EmployeeProfileSensitiveChangeRequest request
    ) =>
        !NormalizedEquals(profile.BankBSB, request.BankBsb)
        || !NormalizedEquals(profile.BankACC, request.BankAccountNumber)
        || !NormalizedEquals(profile.SuperannuationCompanyName, request.SuperannuationCompanyName)
        || !NormalizedEquals(profile.SuperannuationCompanyCode, request.SuperannuationCompanyCode)
        || !NormalizedEquals(profile.SuperannuationAccount, request.SuperannuationAccountNumber)
        || !NormalizedEquals(profile.IdentityType, request.IdentityType)
        || !NormalizedEquals(profile.IdentityId, request.IdentityId)
        || !NormalizedEquals(profile.IdentityPhotoObjectKey, request.IdentityPhotoObjectKey);

    private static string FormatStatus(EmployeeProfileSensitiveChangeStatus status) =>
        status.ToString();

    private static EmployeeProfileSensitiveChangeStatus? ParseStatus(string? status) =>
        Enum.TryParse<EmployeeProfileSensitiveChangeStatus>(status, true, out var parsed)
            ? parsed
            : null;

    private sealed class SensitiveRevisionConflictException : Exception
    {
    }
}
