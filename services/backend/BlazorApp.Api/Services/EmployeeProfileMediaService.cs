using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services
{
    public sealed class EmployeeProfileMediaService
    {
        private readonly SqlSugarContext _context;
        private readonly ICurrentUserService _currentUser;
        private readonly TencentCloudUploadService _uploadService;
        private readonly ILogger<EmployeeProfileMediaService> _logger;
        private readonly EmployeeProfileSensitiveChangeService? _sensitiveChangeService;

        public EmployeeProfileMediaService(
            SqlSugarContext context,
            ICurrentUserService currentUser,
            TencentCloudUploadService uploadService,
            ILogger<EmployeeProfileMediaService> logger,
            EmployeeProfileSensitiveChangeService? sensitiveChangeService = null
        )
        {
            _context = context;
            _currentUser = currentUser;
            _uploadService = uploadService;
            _logger = logger;
            _sensitiveChangeService = sensitiveChangeService;
        }

        public async Task<ApiResponse<DirectUploadSignature>> CreateUploadSignatureAsync(
            EmployeeImageUploadSignatureRequest request
        )
        {
            var userGuid = _currentUser.GetCurrentUserGuid();
            if (string.IsNullOrWhiteSpace(userGuid))
            {
                return ApiResponse<DirectUploadSignature>.Error("未找到当前用户", "CURRENT_USER_NOT_FOUND");
            }
            var kind = request.Kind?.Trim().ToLowerInvariant() ?? string.Empty;
            var contentType = request.ContentType?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!EmployeeProfileImageRules.IsValid(kind, contentType, request.FileSize))
            {
                return ApiResponse<DirectUploadSignature>.Error(
                    "仅支持不超过 5 MiB 的 JPEG、PNG 或 WebP 图片",
                    "INVALID_EMPLOYEE_IMAGE"
                );
            }

            await using (await EmployeeProfileMediaLock.AcquireAsync(
                _context.Db,
                userGuid,
                "upload-signature",
                _logger
            ))
            {
                var now = DateTime.UtcNow;
                var pendingCount = await _context.Db.Queryable<EmployeeImageUploadTicket>()
                    .CountAsync(item => item.UserGUID == userGuid
                        && item.Status != EmployeeImageUploadStatus.Completed
                        && item.Status != EmployeeImageUploadStatus.Failed
                        && item.ExpiresAt > now);
                if (pendingCount >= 3)
                {
                    return ApiResponse<DirectUploadSignature>.Error(
                        "待完成图片上传过多，请稍后重试",
                        "TOO_MANY_PENDING_UPLOADS"
                    );
                }
                var objectKey = EmployeeProfileImageRules.BuildPendingObjectKey(
                    userGuid,
                    kind,
                    contentType
                );
                var finalObjectKey = objectKey["pending/".Length..];
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["x-cos-meta-owner"] = userGuid,
                    ["x-cos-meta-kind"] = kind,
                    ["x-cos-meta-content-type"] = contentType,
                    ["x-cos-meta-file-size"] = request.FileSize.ToString(),
                    // 所有未完成上传统一私有，头像只有 complete 成功后才转为公开正式对象。
                    ["x-cos-acl"] = "private",
                };
                await _context.Db.Insertable(new EmployeeImageUploadTicket
                {
                    PendingObjectKey = objectKey,
                    UserGUID = userGuid,
                    Kind = kind,
                    ContentType = contentType,
                    FileSize = request.FileSize,
                    CreatedAt = now,
                    ExpiresAt = now.AddMinutes(15),
                    Status = EmployeeImageUploadStatus.Pending,
                    FinalObjectKey = finalObjectKey,
                    StageChangedAt = now,
                }).ExecuteCommandAsync();
                return ApiResponse<DirectUploadSignature>.OK(
                    _uploadService.GetDirectUploadSignature(objectKey, contentType, 900, headers)
                );
            }
        }

        public async Task<ApiResponse<bool>> CompleteAsync(
            EmployeeImageCompleteRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var userGuid = _currentUser.GetCurrentUserGuid();
            var kind = request.Kind?.Trim().ToLowerInvariant() ?? string.Empty;
            if (
                string.IsNullOrWhiteSpace(userGuid)
                || !EmployeeProfileImageRules.OwnsPendingObjectKey(request.ObjectKey, userGuid, kind)
            )
            {
                return ApiResponse<bool>.Error("图片对象不属于当前用户", "IMAGE_OBJECT_NOT_OWNED");
            }
            await using (await EmployeeProfileMediaLock.AcquireAsync(
                _context.Db,
                userGuid,
                kind,
                _logger,
                cancellationToken
            ))
            {
                return await CompleteLockedAsync(request.ObjectKey, userGuid, kind, cancellationToken);
            }
        }

        private async Task<ApiResponse<bool>> CompleteLockedAsync(
            string pendingObjectKey,
            string userGuid,
            string kind,
            CancellationToken cancellationToken
        )
        {
            var db = _context.Db;
            var ticket = await db.Queryable<EmployeeImageUploadTicket>()
                .FirstAsync(item => item.PendingObjectKey == pendingObjectKey
                    && item.UserGUID == userGuid && item.Kind == kind);
            if (ticket is null)
            {
                return ApiResponse<bool>.Error("图片上传票据不存在", "IMAGE_UPLOAD_TICKET_EXPIRED");
            }
            if (ticket.Status == EmployeeImageUploadStatus.Completed)
            {
                return ApiResponse<bool>.OK(true, "图片已保存");
            }
            if (ticket.Status == EmployeeImageUploadStatus.Failed)
            {
                return ApiResponse<bool>.Error("图片上传票据已失效", "IMAGE_UPLOAD_TICKET_FAILED");
            }
            if (ticket.Status == EmployeeImageUploadStatus.Cleaning)
            {
                return ApiResponse<bool>.Error("图片上传票据正在清理", "IMAGE_UPLOAD_TICKET_CLEANING");
            }
            if (ticket.ExpiresAt <= DateTime.UtcNow && ticket.Status == EmployeeImageUploadStatus.Pending)
            {
                await MarkFailedAndCleanupAsync(ticket, deleteFinal: false);
                return ApiResponse<bool>.Error("图片上传票据已过期", "IMAGE_UPLOAD_TICKET_EXPIRED");
            }

            var finalObjectKey = ticket.FinalObjectKey;
            if (string.IsNullOrWhiteSpace(finalObjectKey))
            {
                finalObjectKey = pendingObjectKey["pending/".Length..];
            }
            if (ticket.Status == EmployeeImageUploadStatus.Pending)
            {
                var claimedAt = DateTime.UtcNow;
                // 关键逻辑：CAS 抢占票据，并在 COS promote 前持久化可恢复的正式对象键。
                var claimed = await db.Updateable<EmployeeImageUploadTicket>()
                    .SetColumns(item => new EmployeeImageUploadTicket
                    {
                        Status = EmployeeImageUploadStatus.Processing,
                        FinalObjectKey = finalObjectKey,
                        ProcessingStartedAt = claimedAt,
                        StageChangedAt = claimedAt,
                    })
                    .Where(item => item.PendingObjectKey == pendingObjectKey
                        && item.Status == EmployeeImageUploadStatus.Pending)
                    .ExecuteCommandAsync(cancellationToken);
                if (claimed != 1)
                {
                    return ApiResponse<bool>.Error("图片正在处理", "IMAGE_UPLOAD_IN_PROGRESS");
                }
                ticket.Status = EmployeeImageUploadStatus.Processing;
                ticket.FinalObjectKey = finalObjectKey;

                var validationError = await ValidatePendingObjectAsync(ticket, cancellationToken);
                if (validationError is not null)
                {
                    await MarkFailedAndCleanupAsync(ticket, deleteFinal: false);
                    return validationError;
                }
                var promoteResult = await _uploadService.PromoteObjectAsync(
                    pendingObjectKey,
                    finalObjectKey,
                    ticket.ContentType,
                    isPublic: kind == "avatar",
                    cancellationToken
                );
                if (!promoteResult.Success)
                {
                    await MarkFailedAndCleanupAsync(ticket, deleteFinal: true);
                    return ApiResponse<bool>.Error("图片转正失败", "IMAGE_PROMOTE_FAILED");
                }
                var promotedAt = DateTime.UtcNow;
                var promoted = await db.Updateable<EmployeeImageUploadTicket>()
                    .SetColumns(item => new EmployeeImageUploadTicket
                    {
                        Status = EmployeeImageUploadStatus.Promoted,
                        PromotedAt = promotedAt,
                        StageChangedAt = promotedAt,
                    })
                    .Where(item => item.PendingObjectKey == pendingObjectKey
                        && item.Status == EmployeeImageUploadStatus.Processing)
                    .ExecuteCommandAsync(cancellationToken);
                if (promoted != 1)
                {
                    return await ResolveTransitionFailureAsync(pendingObjectKey);
                }
                ticket.Status = EmployeeImageUploadStatus.Promoted;
            }
            else if (ticket.Status == EmployeeImageUploadStatus.Processing)
            {
                return ApiResponse<bool>.Error("图片正在处理，请稍后重试", "IMAGE_UPLOAD_IN_PROGRESS");
            }

            if (kind == "identity" && _sensitiveChangeService is not null)
            {
                // 关键逻辑：证件照只关联待审快照；审批前绝不触碰正式 EmployeeProfile。
                var pending = await _sensitiveChangeService.ReplacePendingIdentityPhotoAsync(finalObjectKey);
                if (!pending.Success)
                {
                    await MarkFailedAndCleanupAsync(ticket, deleteFinal: true);
                    return ApiResponse<bool>.Error(pending.Message, pending.ErrorCode);
                }
                var pendingCompletedAt = DateTime.UtcNow;
                var completedPending = await db.Updateable<EmployeeImageUploadTicket>()
                    .SetColumns(item => new EmployeeImageUploadTicket
                    {
                        Status = EmployeeImageUploadStatus.Completed,
                        CompletedAt = pendingCompletedAt,
                        StageChangedAt = pendingCompletedAt,
                        SensitiveChangeRequestId = pending.Data!.RequestId,
                    })
                    .Where(item => item.PendingObjectKey == pendingObjectKey
                        && item.Status == EmployeeImageUploadStatus.Promoted)
                    .ExecuteCommandAsync(cancellationToken);
                if (completedPending != 1)
                {
                    return await ResolveTransitionFailureAsync(pendingObjectKey);
                }
                _ = await _uploadService.DeleteObjectAsync(pendingObjectKey, CancellationToken.None);
                return ApiResponse<bool>.OK(true, "证件照已提交审批");
            }

            var profile = await db.Queryable<EmployeeProfile>()
                .FirstAsync(item => item.UserGUID == userGuid && !item.IsDeleted);
            var oldObjectKey = GetManagedObjectKey(profile, userGuid, kind);
            var now = DateTime.UtcNow;
            var previousCleanupStatus = string.IsNullOrWhiteSpace(oldObjectKey)
                || string.Equals(oldObjectKey, finalObjectKey, StringComparison.Ordinal)
                    ? EmployeeImageObjectCleanupStatus.Completed
                    : EmployeeImageObjectCleanupStatus.Pending;
            // 关键逻辑：先持久化旧对象键，再更新 profile，崩溃后后台仍能恢复清理。
            var previousRecorded = await db.Updateable<EmployeeImageUploadTicket>()
                .SetColumns(item => new EmployeeImageUploadTicket
                {
                    PreviousObjectKey = oldObjectKey,
                    PreviousObjectCleanupStatus = previousCleanupStatus,
                })
                .Where(item => item.PendingObjectKey == pendingObjectKey
                    && item.Status == EmployeeImageUploadStatus.Promoted)
                .ExecuteCommandAsync(cancellationToken);
            if (previousRecorded != 1)
            {
                return await ResolveTransitionFailureAsync(pendingObjectKey);
            }
            profile ??= new EmployeeProfile
            {
                UserGUID = userGuid,
                CreatedAt = now,
                CreatedBy = _currentUser.GetCurrentUsername(),
            };
            profile.AvatarUrl = kind == "avatar"
                ? _uploadService.GetPublicDownloadUrl(finalObjectKey)
                : profile.AvatarUrl;
            profile.IdentityPhotoObjectKey = kind == "identity"
                ? finalObjectKey
                : profile.IdentityPhotoObjectKey;
            if (kind == "identity")
            {
                profile.IdentityPhotoUrl = null;
            }
            profile.UpdatedAt = now;
            profile.UpdatedBy = _currentUser.GetCurrentUsername();
            profile.IsDeleted = false;

            if (profile.EmployeeInfoId == 0)
            {
                try
                {
                    await db.Insertable(profile).ExecuteCommandAsync(cancellationToken);
                }
                catch (Exception ex) when (EmployeeCashierBarcodeService.IsUniqueConstraintViolation(ex))
                {
                    var concurrent = await db.Queryable<EmployeeProfile>()
                        .FirstAsync(item => item.UserGUID == userGuid && !item.IsDeleted);
                    if (concurrent is null)
                    {
                        throw;
                    }
                    profile.EmployeeInfoId = concurrent.EmployeeInfoId;
                    await UpdateImageColumnsAsync(db, profile, kind, finalObjectKey, now);
                }
            }
            else
            {
                await UpdateImageColumnsAsync(db, profile, kind, finalObjectKey, now);
            }

            var completed = await db.Updateable<EmployeeImageUploadTicket>()
                .SetColumns(item => new EmployeeImageUploadTicket
                {
                    Status = EmployeeImageUploadStatus.Completed,
                    CompletedAt = now,
                    StageChangedAt = now,
                })
                .Where(item => item.PendingObjectKey == pendingObjectKey
                    && item.Status == EmployeeImageUploadStatus.Promoted)
                .ExecuteCommandAsync(cancellationToken);
            if (completed != 1)
            {
                return await ResolveTransitionFailureAsync(pendingObjectKey);
            }
            _ = await _uploadService.DeleteObjectAsync(pendingObjectKey, CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(oldObjectKey)
                && !string.Equals(oldObjectKey, finalObjectKey, StringComparison.Ordinal))
            {
                var cleanupResult = await _uploadService.DeleteObjectAsync(oldObjectKey, CancellationToken.None);
                if (!cleanupResult.Success)
                {
                    _logger.LogWarning(
                        "员工图片替换成功，但旧对象清理失败。UserGUID: {UserGUID}, ObjectKey: {ObjectKey}",
                        userGuid,
                        oldObjectKey
                    );
                }
                else
                {
                    await MarkPreviousObjectCleanedAsync(pendingObjectKey, oldObjectKey);
                }
            }
            return ApiResponse<bool>.OK(true, "图片保存成功");
        }

        private async Task<ApiResponse<bool>> ResolveTransitionFailureAsync(string pendingObjectKey)
        {
            var current = await _context.Db.Queryable<EmployeeImageUploadTicket>()
                .FirstAsync(item => item.PendingObjectKey == pendingObjectKey);
            return current?.Status == EmployeeImageUploadStatus.Completed
                ? ApiResponse<bool>.OK(true, "图片已保存")
                : ApiResponse<bool>.Error("图片状态已变更，请重试", "IMAGE_UPLOAD_STATE_CONFLICT");
        }

        private Task<int> MarkPreviousObjectCleanedAsync(
            string pendingObjectKey,
            string previousObjectKey
        ) => _context.Db.Updateable<EmployeeImageUploadTicket>()
            .SetColumns(item => new EmployeeImageUploadTicket
            {
                PreviousObjectCleanupStatus = EmployeeImageObjectCleanupStatus.Completed,
            })
            .Where(item => item.PendingObjectKey == pendingObjectKey
                && item.PreviousObjectKey == previousObjectKey
                && item.PreviousObjectCleanupStatus == EmployeeImageObjectCleanupStatus.Pending)
            .ExecuteCommandAsync();

        private async Task<ApiResponse<bool>?> ValidatePendingObjectAsync(
            EmployeeImageUploadTicket ticket,
            CancellationToken cancellationToken
        )
        {
            var metadataResult = await _uploadService.GetObjectMetadataAsync(
                ticket.PendingObjectKey,
                cancellationToken
            );
            var metadata = metadataResult.Data;
            if (!metadataResult.Success || metadata is null)
            {
                return ApiResponse<bool>.Error("无法验证已上传图片", "IMAGE_METADATA_INVALID");
            }
            var actualType = metadata.ContentType?.Trim().ToLowerInvariant();
            if (!EmployeeProfileImageRules.MatchesMetadata(
                    ticket.UserGUID,
                    ticket.Kind,
                    metadata.ContentLength ?? 0,
                    actualType,
                    metadata.Owner,
                    metadata.Kind,
                    metadata.DeclaredFileSize,
                    metadata.DeclaredContentType
                )
                || ticket.FileSize != metadata.ContentLength
                || !string.Equals(ticket.ContentType, actualType, StringComparison.OrdinalIgnoreCase))
            {
                return ApiResponse<bool>.Error("图片实际属性与上传签名不一致", "IMAGE_METADATA_MISMATCH");
            }
            var contentResult = await _uploadService.DownloadObjectBytesAsync(
                ticket.PendingObjectKey,
                (int)EmployeeProfileImageRules.MaximumFileSize,
                cancellationToken
            );
            return !contentResult.Success
                || contentResult.Data is null
                || contentResult.Data.LongLength != metadata.ContentLength
                || !EmployeeProfileImageRules.MatchesImageContent(contentResult.Data, actualType!, ticket.Kind)
                ? ApiResponse<bool>.Error("上传内容不是有效的图片文件", "IMAGE_CONTENT_INVALID")
                : null;
        }

        private async Task MarkFailedAndCleanupAsync(
            EmployeeImageUploadTicket ticket,
            bool deleteFinal
        )
        {
            await _context.Db.Updateable<EmployeeImageUploadTicket>()
                .SetColumns(item => new EmployeeImageUploadTicket
                {
                    Status = EmployeeImageUploadStatus.Failed,
                    FailedAt = DateTime.UtcNow,
                    StageChangedAt = DateTime.UtcNow,
                })
                .Where(item => item.PendingObjectKey == ticket.PendingObjectKey
                    && item.Status == ticket.Status)
                .ExecuteCommandAsync();
            _ = await _uploadService.DeleteObjectAsync(ticket.PendingObjectKey, CancellationToken.None);
            if (deleteFinal && !string.IsNullOrWhiteSpace(ticket.FinalObjectKey))
            {
                _ = await _uploadService.DeleteObjectAsync(ticket.FinalObjectKey, CancellationToken.None);
            }
        }

        public async Task<ApiResponse<bool>> DeleteAsync(
            string kind,
            CancellationToken cancellationToken = default
        )
        {
            var userGuid = _currentUser.GetCurrentUserGuid();
            var normalizedKind = kind.Trim().ToLowerInvariant();
            if (
                string.IsNullOrWhiteSpace(userGuid)
                || (normalizedKind != "avatar" && normalizedKind != "identity")
            )
            {
                return ApiResponse<bool>.Error("图片类型无效", "INVALID_IMAGE_KIND");
            }

            await using (await EmployeeProfileMediaLock.AcquireAsync(
                _context.Db,
                userGuid,
                normalizedKind,
                _logger,
                cancellationToken
            ))
            {
                if (normalizedKind == "identity" && _sensitiveChangeService is not null)
                {
                    var pending = await _sensitiveChangeService.DeletePendingIdentityPhotoAsync();
                    return pending.Success
                        ? ApiResponse<bool>.OK(true, "证件照删除已提交审批")
                        : ApiResponse<bool>.Error(pending.Message, pending.ErrorCode);
                }
                return await DeleteLockedAsync(userGuid, normalizedKind, cancellationToken);
            }
        }

        private async Task<ApiResponse<bool>> DeleteLockedAsync(
            string userGuid,
            string normalizedKind,
            CancellationToken cancellationToken
        )
        {
            var profile = await _context.Db.Queryable<EmployeeProfile>()
                .FirstAsync(item => item.UserGUID == userGuid && !item.IsDeleted);
            if (profile is null)
            {
                return ApiResponse<bool>.OK(true);
            }
            var objectKey = GetManagedObjectKey(profile, userGuid, normalizedKind);
            if (!string.IsNullOrWhiteSpace(objectKey))
            {
                // 显式删除必须先删私有对象；失败时保留数据库关联，允许用户重试。
                var deleteResult = await _uploadService.DeleteObjectAsync(
                    objectKey,
                    cancellationToken
                );
                if (!deleteResult.Success)
                {
                    return deleteResult;
                }
            }
            profile.UpdatedAt = DateTime.UtcNow;
            profile.UpdatedBy = _currentUser.GetCurrentUsername();
            if (normalizedKind == "avatar")
            {
                var existingAvatarUrl = profile.AvatarUrl;
                profile.AvatarUrl = null;
                await _context.Db.Updateable<EmployeeProfile>()
                    .SetColumns(item => new EmployeeProfile
                    {
                        AvatarUrl = null,
                        UpdatedAt = profile.UpdatedAt,
                        UpdatedBy = profile.UpdatedBy,
                    })
                    .Where(item => item.EmployeeInfoId == profile.EmployeeInfoId
                        && item.AvatarUrl == existingAvatarUrl)
                    .ExecuteCommandAsync();
            }
            else
            {
                profile.IdentityPhotoObjectKey = null;
                profile.IdentityPhotoUrl = null;
                await _context.Db.Updateable<EmployeeProfile>()
                    .SetColumns(item => new EmployeeProfile
                    {
                        IdentityPhotoObjectKey = null,
                        IdentityPhotoUrl = null,
                        UpdatedAt = profile.UpdatedAt,
                        UpdatedBy = profile.UpdatedBy,
                    })
                    .Where(item => item.EmployeeInfoId == profile.EmployeeInfoId
                        && item.IdentityPhotoObjectKey == objectKey)
                    .ExecuteCommandAsync();
            }
            return ApiResponse<bool>.OK(true, "图片已删除");
        }

        private static Task<int> UpdateImageColumnsAsync(
            SqlSugar.ISqlSugarClient db,
            EmployeeProfile profile,
            string kind,
            string finalObjectKey,
            DateTime now
        ) => kind == "avatar"
            ? db.Updateable<EmployeeProfile>()
                .SetColumns(item => new EmployeeProfile
                {
                    AvatarUrl = profile.AvatarUrl,
                    UpdatedAt = now,
                    UpdatedBy = profile.UpdatedBy,
                })
                .Where(item => item.EmployeeInfoId == profile.EmployeeInfoId)
                .ExecuteCommandAsync()
            : db.Updateable<EmployeeProfile>()
                .SetColumns(item => new EmployeeProfile
                {
                    IdentityPhotoObjectKey = finalObjectKey,
                    IdentityPhotoUrl = null,
                    UpdatedAt = now,
                    UpdatedBy = profile.UpdatedBy,
                })
                .Where(item => item.EmployeeInfoId == profile.EmployeeInfoId)
                .ExecuteCommandAsync();

        private string? GetManagedObjectKey(
            EmployeeProfile? profile,
            string userGuid,
            string kind
        )
        {
            if (profile is null)
            {
                return null;
            }
            string? objectKey = kind == "identity" ? profile.IdentityPhotoObjectKey : null;
            if (
                kind == "avatar"
                && _uploadService.TryGetPublicObjectKey(profile.AvatarUrl, out var avatarObjectKey)
            )
            {
                objectKey = avatarObjectKey;
            }
            return EmployeeProfileImageRules.OwnsObjectKey(objectKey, userGuid, kind)
                ? objectKey
                : null;
        }
    }
}
