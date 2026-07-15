using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services
{
    public class EmployeeProfileService : IEmployeeProfileService
    {
        private readonly SqlSugarContext _context;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<EmployeeProfileService> _logger;
        private readonly TencentCloudUploadService? _uploadService;
        private readonly EmployeeProfileSensitiveChangeService? _sensitiveChangeService;

        public EmployeeProfileService(
            SqlSugarContext context,
            ICurrentUserService currentUserService,
            ILogger<EmployeeProfileService> logger,
            TencentCloudUploadService? uploadService = null,
            EmployeeProfileSensitiveChangeService? sensitiveChangeService = null
        )
        {
            _context = context;
            _currentUserService = currentUserService;
            _logger = logger;
            _uploadService = uploadService;
            _sensitiveChangeService = sensitiveChangeService;
        }

        public async Task<ApiResponse<PagedResult<EmployeeProfileListItemDto>>> GetAdminListAsync(
            EmployeeProfileQueryDto query
        )
        {
            try
            {
                var db = _context.Db;
                var baseQuery = db.Queryable<User>()
                    .LeftJoin<EmployeeProfile>(
                        (user, profile) =>
                            user.UserGUID == profile.UserGUID && !profile.IsDeleted
                    )
                    .Where((user, profile) => !user.IsDeleted);

                if (!string.IsNullOrWhiteSpace(query.Search))
                {
                    var keyword = query.Search.Trim();
                    baseQuery = baseQuery.Where(
                        (user, profile) =>
                            user.Username.Contains(keyword)
                            || user.Email.Contains(keyword)
                            || (user.FullName != null && user.FullName.Contains(keyword))
                            || (profile.Address != null && profile.Address.Contains(keyword))
                            || (profile.IdentityId != null && profile.IdentityId.Contains(keyword))
                    );
                }

                if (query.HasProfile.HasValue)
                {
                    if (query.HasProfile.Value)
                    {
                        baseQuery = baseQuery.Where((user, profile) => profile.EmployeeInfoId > 0);
                    }
                    else
                    {
                        baseQuery = baseQuery.Where((user, profile) => profile.EmployeeInfoId == 0);
                    }
                }

                var employeeType = ParseEmployeeType(query.EmployeeType);
                if (employeeType.HasValue)
                {
                    baseQuery = baseQuery.Where(
                        (user, profile) => profile.EmployeeType == employeeType.Value
                    );
                }

                var total = await baseQuery.CountAsync();
                var rows = await baseQuery
                    .OrderBy((user, profile) => user.Username)
                    .Select(
                        (user, profile) =>
                            new
                            {
                                profile.EmployeeInfoId,
                                user.UserGUID,
                                user.Username,
                                user.Email,
                                user.FullName,
                                profile.Phone,
                                profile.BankBSB,
                                profile.BankACC,
                                profile.SuperannuationCompanyName,
                                profile.SuperannuationCompanyCode,
                                profile.SuperannuationAccount,
                                profile.Gender,
                                profile.EmployeeType,
                                profile.Birthday,
                                profile.AvatarUrl,
                                profile.UpdatedAt,
                            }
                    )
                    .Skip((query.Page - 1) * query.PageSize)
                    .Take(query.PageSize)
                    .ToListAsync();

                var items = rows
                    .Select(row => new EmployeeProfileListItemDto
                    {
                        EmployeeInfoId = row.EmployeeInfoId > 0 ? row.EmployeeInfoId : null,
                        UserGUID = row.UserGUID,
                        Username = row.Username,
                        Email = row.Email,
                        FullName = row.FullName,
                        HasProfile = row.EmployeeInfoId > 0,
                        Phone = row.Phone,
                        BankBsb = row.BankBSB,
                        BankAccountNumber = row.BankACC,
                        SuperannuationCompanyName = row.SuperannuationCompanyName,
                        SuperannuationCompanyCode = row.SuperannuationCompanyCode,
                        SuperannuationAccountNumber = row.SuperannuationAccount,
                        Gender = FormatGender(row.Gender),
                        EmployeeType = FormatEmployeeType(row.EmployeeType),
                        Birthday = row.Birthday,
                        AvatarUrl = row.AvatarUrl,
                        UpdatedAt = row.UpdatedAt,
                    })
                    .ToList();

                return ApiResponse<PagedResult<EmployeeProfileListItemDto>>.OK(
                    new PagedResult<EmployeeProfileListItemDto>
                    {
                        Items = items,
                        Total = total,
                        Page = query.Page,
                        PageSize = query.PageSize,
                    },
                    "获取员工个人信息列表成功"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取员工个人信息列表失败");
                return ApiResponse<PagedResult<EmployeeProfileListItemDto>>.Error(
                    "获取员工个人信息列表失败",
                    "GET_EMPLOYEE_PROFILES_FAILED"
                );
            }
        }

        public Task<ApiResponse<EmployeeProfileDetailDto>> GetAdminDetailAsync(string userGuid)
        {
            return GetByUserGuidAsync(userGuid);
        }

        public async Task<ApiResponse<EmployeeProfileDetailDto>> UpsertAdminAsync(
            string userGuid,
            EmployeeProfileUpsertDto dto
        )
        {
            return await UpsertForUserAsync(userGuid, dto, allowLegacyImageUrls: true, isAdmin: true);
        }

        public Task<ApiResponse<EmployeeProfileDetailDto>> GetSelfAsync()
        {
            var userGuid = _currentUserService.GetCurrentUserGuid();
            return GetByUserGuidAsync(userGuid);
        }

        public async Task<ApiResponse<EmployeeProfileDetailDto>> UpsertSelfAsync(EmployeeProfileUpsertDto dto)
        {
            var userGuid = _currentUserService.GetCurrentUserGuid();
            return await UpsertForUserAsync(userGuid, dto, allowLegacyImageUrls: false, isAdmin: false);
        }

        private async Task<ApiResponse<EmployeeProfileDetailDto>> GetByUserGuidAsync(string userGuid)
        {
            if (string.IsNullOrWhiteSpace(userGuid))
            {
                return ApiResponse<EmployeeProfileDetailDto>.Error("未找到当前用户", "CURRENT_USER_NOT_FOUND");
            }

            try
            {
                var db = _context.Db;
                var user = await db.Queryable<User>()
                    .FirstAsync(item => item.UserGUID == userGuid && !item.IsDeleted);

                if (user == null)
                {
                    return ApiResponse<EmployeeProfileDetailDto>.Error("用户不存在", "USER_NOT_FOUND");
                }

                var profile = await db.Queryable<EmployeeProfile>()
                    .FirstAsync(item => item.UserGUID == userGuid && !item.IsDeleted);

                return ApiResponse<EmployeeProfileDetailDto>.OK(MapDetail(user, profile), "获取员工个人信息成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取员工个人信息失败，UserGUID: {UserGUID}", userGuid);
                return ApiResponse<EmployeeProfileDetailDto>.Error(
                    "获取员工个人信息失败",
                    "GET_EMPLOYEE_PROFILE_FAILED"
                );
            }
        }

        private async Task<ApiResponse<EmployeeProfileDetailDto>> UpsertForUserAsync(
            string userGuid,
            EmployeeProfileUpsertDto dto,
            bool allowLegacyImageUrls,
            bool isAdmin
        )
        {
            if (string.IsNullOrWhiteSpace(userGuid))
            {
                return ApiResponse<EmployeeProfileDetailDto>.Error("未找到当前用户", "CURRENT_USER_NOT_FOUND");
            }

            try
            {
                var db = _context.Db;
                var user = await db.Queryable<User>()
                    .FirstAsync(item => item.UserGUID == userGuid && !item.IsDeleted);

                if (user == null)
                {
                    return ApiResponse<EmployeeProfileDetailDto>.Error("用户不存在", "USER_NOT_FOUND");
                }

                var actor = _currentUserService.GetCurrentUsername();
                var now = DateTime.UtcNow;
                var profile = await db.Queryable<EmployeeProfile>()
                    .FirstAsync(item => item.UserGUID == userGuid && !item.IsDeleted);

                var sensitiveChanged = isAdmin && HasSensitiveChanges(profile, dto);
                var legacySensitiveDto = !isAdmin && HasLegacySensitivePayload(dto)
                    ? BuildLegacySensitiveSnapshot(profile, dto)
                    : null;
                var supersededKeys = new List<string>();
                IAsyncDisposable? adminSensitiveLock = null;

                if (isAdmin)
                {
                    adminSensitiveLock = await EmployeeProfileMediaLock.AcquireAsync(
                        db,
                        userGuid,
                        "sensitive-change",
                        _logger
                    );
                }

                try
                {
                    if (isAdmin)
                    {
                        await db.Ado.BeginTranAsync();
                    }
                    if (profile == null)
                    {
                        profile = new EmployeeProfile
                        {
                            UserGUID = userGuid,
                            CreatedAt = now,
                            CreatedBy = actor,
                            UpdatedAt = now,
                            UpdatedBy = actor,
                        };

                        ApplyChanges(profile, dto, userGuid, actor, now, isCreate: true, allowLegacyImageUrls, isAdmin);
                        if (sensitiveChanged)
                        {
                            profile.SensitiveRevision = 1;
                        }
                        await db.Insertable(profile).ExecuteCommandAsync();
                    }
                    else
                    {
                        ApplyChanges(profile, dto, userGuid, actor, now, isCreate: false, allowLegacyImageUrls, isAdmin);
                        if (sensitiveChanged)
                        {
                            profile.SensitiveRevision++;
                        }
                        await db.Updateable(profile).ExecuteCommandAsync();
                    }

                    if (isAdmin && sensitiveChanged && _sensitiveChangeService is not null)
                    {
                        // 关键逻辑：管理员直改与待审申请失效必须处在同一事务内。
                        supersededKeys = await _sensitiveChangeService
                            .SupersedePendingWithinTransactionAsync(userGuid, actor);
                    }
                    if (isAdmin)
                    {
                        await db.Ado.CommitTranAsync();
                    }
                }
                catch
                {
                    if (isAdmin)
                    {
                        await db.Ado.RollbackTranAsync();
                    }
                    throw;
                }
                finally
                {
                    if (adminSensitiveLock is not null)
                    {
                        await adminSensitiveLock.DisposeAsync();
                    }
                }

                if (isAdmin && supersededKeys.Count > 0 && _sensitiveChangeService is not null)
                {
                    await _sensitiveChangeService.CleanupSupersededObjectsAsync(userGuid, supersededKeys);
                }
                if (legacySensitiveDto is not null && _sensitiveChangeService is not null)
                {
                    // 旧客户端仍可提交同一 DTO，但敏感字段只能进入审批快照。
                    await _sensitiveChangeService.UpsertSelfAsync(legacySensitiveDto);
                }

                return ApiResponse<EmployeeProfileDetailDto>.OK(
                    MapDetail(user, profile),
                    "保存员工个人信息成功"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存员工个人信息失败，UserGUID: {UserGUID}", userGuid);
                return ApiResponse<EmployeeProfileDetailDto>.Error(
                    "保存员工个人信息失败",
                    "UPSERT_EMPLOYEE_PROFILE_FAILED"
                );
            }
        }

        private static void ApplyChanges(
            EmployeeProfile profile,
            EmployeeProfileUpsertDto dto,
            string userGuid,
            string actor,
            DateTime now,
            bool isCreate,
            bool allowLegacyImageUrls,
            bool allowSensitiveChanges
        )
        {
            profile.UserGUID = userGuid;
            profile.Phone = Normalize(dto.Phone);
            if (allowSensitiveChanges)
            {
                profile.BankBSB = Normalize(dto.BankBsb);
                profile.BankACC = Normalize(dto.BankAccountNumber);
                profile.SuperannuationCompanyName = Normalize(dto.SuperannuationCompanyName);
                profile.SuperannuationCompanyCode = Normalize(dto.SuperannuationCompanyCode);
                profile.SuperannuationAccount = Normalize(dto.SuperannuationAccountNumber);
            }
            profile.Birthday = dto.Birthday?.Date;
            profile.Gender = ParseGender(dto.Gender);
            profile.EmployeeType = ParseEmployeeType(dto.EmploymentType);
            if (allowLegacyImageUrls)
            {
                profile.AvatarUrl = Normalize(dto.AvatarUrl);
                if (string.IsNullOrWhiteSpace(profile.IdentityPhotoObjectKey))
                {
                    // 历史记录仍允许管理端维护 URL；托管私有图只能通过专用媒体接口更改。
                    profile.IdentityPhotoUrl = Normalize(dto.IdentityPhotoUrl);
                }
            }
            if (allowSensitiveChanges)
            {
                profile.IdentityType = Normalize(dto.IdentityType);
                profile.IdentityId = Normalize(dto.IdentityId);
            }
            profile.Address = Normalize(dto.Address);
            profile.UpdatedAt = now;
            profile.UpdatedBy = actor;
            profile.IsDeleted = false;

            if (isCreate)
            {
                profile.CreatedAt = now;
                profile.CreatedBy = actor;
            }
        }

        private EmployeeProfileDetailDto MapDetail(User user, EmployeeProfile? profile)
        {
            string? identityPhotoUrl = profile?.IdentityPhotoUrl;
            DateTime? identityPhotoUrlExpiresAt = null;
            if (!string.IsNullOrWhiteSpace(profile?.IdentityPhotoObjectKey) && _uploadService is not null)
            {
                var signed = _uploadService.GetSignedDownload(profile.IdentityPhotoObjectKey, 300);
                identityPhotoUrl = signed.Url;
                identityPhotoUrlExpiresAt = signed.ExpiresAtUtc;
            }
            return new EmployeeProfileDetailDto
            {
                EmployeeInfoId = profile?.EmployeeInfoId,
                UserGUID = user.UserGUID,
                Username = user.Username,
                Email = user.Email,
                FullName = user.FullName,
                Phone = profile?.Phone,
                BankBsb = profile?.BankBSB,
                BankAccountNumber = profile?.BankACC,
                SuperannuationCompanyName = profile?.SuperannuationCompanyName,
                SuperannuationCompanyCode = profile?.SuperannuationCompanyCode,
                SuperannuationAccountNumber = profile?.SuperannuationAccount,
                Birthday = profile?.Birthday,
                Gender = FormatGender(profile?.Gender),
                EmploymentType = FormatEmployeeType(profile?.EmployeeType),
                AvatarUrl = profile?.AvatarUrl,
                IdentityId = profile?.IdentityId,
                IdentityType = profile?.IdentityType,
                IdentityPhotoUrl = identityPhotoUrl,
                IdentityPhotoUrlExpiresAt = identityPhotoUrlExpiresAt,
                Address = profile?.Address,
                CreatedAt = profile?.CreatedAt,
                CreatedBy = profile?.CreatedBy,
                UpdatedAt = profile?.UpdatedAt,
                UpdatedBy = profile?.UpdatedBy,
            };
        }

        private static string? Normalize(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool HasLegacySensitivePayload(EmployeeProfileUpsertDto dto) =>
            dto.BankBsb is not null
            || dto.BankAccountNumber is not null
            || dto.SuperannuationCompanyName is not null
            || dto.SuperannuationCompanyCode is not null
            || dto.SuperannuationAccountNumber is not null
            || dto.IdentityType is not null
            || dto.IdentityId is not null;

        private static bool HasSensitiveChanges(EmployeeProfile? profile, EmployeeProfileUpsertDto dto) =>
            !string.Equals(profile?.BankBSB, Normalize(dto.BankBsb), StringComparison.Ordinal)
            || !string.Equals(profile?.BankACC, Normalize(dto.BankAccountNumber), StringComparison.Ordinal)
            || !string.Equals(profile?.SuperannuationCompanyName, Normalize(dto.SuperannuationCompanyName), StringComparison.Ordinal)
            || !string.Equals(profile?.SuperannuationCompanyCode, Normalize(dto.SuperannuationCompanyCode), StringComparison.Ordinal)
            || !string.Equals(profile?.SuperannuationAccount, Normalize(dto.SuperannuationAccountNumber), StringComparison.Ordinal)
            || !string.Equals(profile?.IdentityType, Normalize(dto.IdentityType), StringComparison.Ordinal)
            || !string.Equals(profile?.IdentityId, Normalize(dto.IdentityId), StringComparison.Ordinal)
            || (string.IsNullOrWhiteSpace(profile?.IdentityPhotoObjectKey)
                && !string.Equals(profile?.IdentityPhotoUrl, Normalize(dto.IdentityPhotoUrl), StringComparison.Ordinal));

        private static EmployeeProfileSensitiveChangeUpsertDto BuildLegacySensitiveSnapshot(
            EmployeeProfile? profile,
            EmployeeProfileUpsertDto dto
        ) => new()
        {
            BankBsb = dto.BankBsb ?? profile?.BankBSB,
            BankAccountNumber = dto.BankAccountNumber ?? profile?.BankACC,
            SuperannuationCompanyName = dto.SuperannuationCompanyName ?? profile?.SuperannuationCompanyName,
            SuperannuationCompanyCode = dto.SuperannuationCompanyCode ?? profile?.SuperannuationCompanyCode,
            SuperannuationAccountNumber = dto.SuperannuationAccountNumber ?? profile?.SuperannuationAccount,
            IdentityType = dto.IdentityType ?? profile?.IdentityType,
            IdentityId = dto.IdentityId ?? profile?.IdentityId,
        };

        private static EmployeeGender? ParseGender(string? value)
        {
            return Normalize(value)?.ToLowerInvariant() switch
            {
                null => null,
                "" => null,
                "unknown" => EmployeeGender.Unknown,
                "male" => EmployeeGender.Male,
                "female" => EmployeeGender.Female,
                "other" => EmployeeGender.Other,
                _ => null,
            };
        }

        private static EmployeeType? ParseEmployeeType(string? value)
        {
            return Normalize(value)?.ToLowerInvariant() switch
            {
                null => null,
                "" => null,
                "fulltime" => EmployeeType.FullTime,
                "full_time" => EmployeeType.FullTime,
                "full-time" => EmployeeType.FullTime,
                "parttime" => EmployeeType.PartTime,
                "part_time" => EmployeeType.PartTime,
                "part-time" => EmployeeType.PartTime,
                "casual" => EmployeeType.Temporary,
                "temporary" => EmployeeType.Temporary,
                _ => null,
            };
        }

        private static string? FormatGender(EmployeeGender? value)
        {
            return value switch
            {
                EmployeeGender.Unknown => "unknown",
                EmployeeGender.Male => "male",
                EmployeeGender.Female => "female",
                EmployeeGender.Other => "other",
                _ => null,
            };
        }

        private static string? FormatEmployeeType(EmployeeType? value)
        {
            return value switch
            {
                EmployeeType.FullTime => "fullTime",
                EmployeeType.PartTime => "partTime",
                EmployeeType.Temporary => "casual",
                _ => null,
            };
        }
    }
}
