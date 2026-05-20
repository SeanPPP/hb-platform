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

        public EmployeeProfileService(
            SqlSugarContext context,
            ICurrentUserService currentUserService,
            ILogger<EmployeeProfileService> logger
        )
        {
            _context = context;
            _currentUserService = currentUserService;
            _logger = logger;
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

        public Task<ApiResponse<EmployeeProfileDetailDto>> UpsertAdminAsync(
            string userGuid,
            EmployeeProfileUpsertDto dto
        )
        {
            return UpsertForUserAsync(userGuid, dto);
        }

        public Task<ApiResponse<EmployeeProfileDetailDto>> GetSelfAsync()
        {
            var userGuid = _currentUserService.GetCurrentUserGuid();
            return GetByUserGuidAsync(userGuid);
        }

        public Task<ApiResponse<EmployeeProfileDetailDto>> UpsertSelfAsync(EmployeeProfileUpsertDto dto)
        {
            var userGuid = _currentUserService.GetCurrentUserGuid();
            return UpsertForUserAsync(userGuid, dto);
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
            EmployeeProfileUpsertDto dto
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

                    ApplyChanges(profile, dto, userGuid, actor, now, isCreate: true);
                    await db.Insertable(profile).ExecuteCommandAsync();
                }
                else
                {
                    ApplyChanges(profile, dto, userGuid, actor, now, isCreate: false);
                    await db.Updateable(profile).ExecuteCommandAsync();
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
            bool isCreate
        )
        {
            profile.UserGUID = userGuid;
            profile.BankBSB = Normalize(dto.BankBsb);
            profile.BankACC = Normalize(dto.BankAccountNumber);
            profile.SuperannuationCompanyName = Normalize(dto.SuperannuationCompanyName);
            profile.SuperannuationCompanyCode = Normalize(dto.SuperannuationCompanyCode);
            profile.SuperannuationAccount = Normalize(dto.SuperannuationAccountNumber);
            profile.Birthday = dto.Birthday?.Date;
            profile.Gender = ParseGender(dto.Gender);
            profile.EmployeeType = ParseEmployeeType(dto.EmploymentType);
            profile.AvatarUrl = Normalize(dto.AvatarUrl);
            profile.IdentityId = Normalize(dto.IdentityId);
            profile.IdentityPhotoUrl = Normalize(dto.IdentityPhotoUrl);
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

        private static EmployeeProfileDetailDto MapDetail(User user, EmployeeProfile? profile)
        {
            return new EmployeeProfileDetailDto
            {
                EmployeeInfoId = profile?.EmployeeInfoId,
                UserGUID = user.UserGUID,
                Username = user.Username,
                Email = user.Email,
                FullName = user.FullName,
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
                IdentityPhotoUrl = profile?.IdentityPhotoUrl,
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
