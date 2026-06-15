using System.Security.Cryptography;
using System.Text;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Utils;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 用户管理服务实现
    /// 🧑‍💼 提供用户数据的CRUD操作和权限管理功能
    /// </summary>
    public class UserService : IUserService
    {
        private readonly SqlSugarContext _context;
        private readonly ILogger<UserService> _logger;
        private readonly ICurrentUserManageableStoreScopeService? _manageableStoreScopeService;

        public UserService(
            SqlSugarContext context,
            ILogger<UserService> logger,
            ICurrentUserManageableStoreScopeService? manageableStoreScopeService = null
        )
        {
            _context = context;
            _logger = logger;
            _manageableStoreScopeService = manageableStoreScopeService;
        }

        /// <summary>
        /// 获取用户列表（分页）
        /// </summary>
        public async Task<ApiResponse<PagedResult<UserDto>>> GetUsersAsync(UserQueryDto query)
        {
            try
            {
                var db = _context.Db;
                var storeScope = await ResolveUserListStoreScopeAsync(db);
                if (storeScope.ReturnEmptyPage)
                {
                    return ApiResponse<PagedResult<UserDto>>.OK(
                        CreateEmptyPagedResult(query),
                        "获取用户列表成功"
                    );
                }
                if (IsOutOfScopedStoreQuery(storeScope, query.StoreGuid))
                {
                    return ApiResponse<PagedResult<UserDto>>.OK(
                        CreateEmptyPagedResult(query),
                        "获取用户列表成功"
                    );
                }

                //使用多对多导航查询
                var userQuery = db.Queryable<User>().Includes(u => u.Roles).Includes(u => u.Stores);

                if (storeScope.StoreGuids.Count > 0)
                {
                    var scopedStoreGuids = storeScope.StoreGuids.ToArray();
                    var scopedUserGuids = await db.Queryable<UserStore>()
                        .Where(us => !us.IsDeleted && scopedStoreGuids.Contains(us.StoreGUID))
                        .Select(us => us.UserGUID)
                        .Distinct()
                        .ToListAsync();

                    if (scopedUserGuids.Count == 0)
                    {
                        return ApiResponse<PagedResult<UserDto>>.OK(
                            CreateEmptyPagedResult(query),
                            "获取用户列表成功"
                        );
                    }

                    userQuery = userQuery.Where(u => scopedUserGuids.Contains(u.UserGUID));
                }

                // 搜索条件
                if (!string.IsNullOrEmpty(query.SearchKeyword))
                {
                    userQuery = userQuery.Where(u =>
                        u.Username.Contains(query.SearchKeyword)
                        || u.Email.Contains(query.SearchKeyword)
                        || (u.FullName != null && u.FullName.Contains(query.SearchKeyword))
                    );
                }

                // 状态筛选
                if (query.IsActive.HasValue)
                {
                    userQuery = userQuery.Where(u => u.IsActive == query.IsActive.Value);
                }

                // 角色筛选 - 优化为子查询
                if (!string.IsNullOrEmpty(query.RoleGuid))
                {
                    userQuery = userQuery.Where(u =>
                        db.Queryable<UserRole>()
                            .Where(ur => ur.RoleGUID == query.RoleGuid && ur.UserGUID == u.UserGUID)
                            .Any()
                    );
                }

                // 分店筛选 - 优化为子查询
                if (!string.IsNullOrEmpty(query.StoreGuid))
                {
                    userQuery = userQuery.Where(u =>
                        db.Queryable<UserStore>()
                            .Where(us =>
                                us.StoreGUID == query.StoreGuid && us.UserGUID == u.UserGUID
                            )
                            .Any()
                    );
                }

                // 排序
                if (!string.IsNullOrEmpty(query.SortBy))
                {
                    if (query.SortDirection?.ToLower() == "asc")
                    {
                        userQuery = query.SortBy.ToLower() switch
                        {
                            "username" => userQuery.OrderBy(u => u.Username),
                            "email" => userQuery.OrderBy(u => u.Email),
                            "fullname" => userQuery.OrderBy(u => u.FullName),
                            "lastloginat" => userQuery.OrderBy(u => u.LastLoginAt),
                            "createdat" => userQuery.OrderBy(u => u.CreatedAt),
                            _ => userQuery.OrderBy(u => u.CreatedAt),
                        };
                    }
                    else
                    {
                        userQuery = query.SortBy.ToLower() switch
                        {
                            "username" => userQuery.OrderByDescending(u => u.Username),
                            "email" => userQuery.OrderByDescending(u => u.Email),
                            "fullname" => userQuery.OrderByDescending(u => u.FullName),
                            "lastloginat" => userQuery.OrderByDescending(u => u.LastLoginAt),
                            "createdat" => userQuery.OrderByDescending(u => u.CreatedAt),
                            _ => userQuery.OrderByDescending(u => u.CreatedAt),
                        };
                    }
                }
                else
                {
                    userQuery = userQuery.OrderByDescending(u => u.CreatedAt);
                }

                // 获取总数
                var total = await userQuery.CountAsync();

                // 分页查询
                var users = await userQuery
                    .Skip((query.Page - 1) * query.PageSize)
                    .Take(query.PageSize)
                    .ToListAsync();
                //内存中将用户转换为UserDto
                var userDtos = new List<UserDto>();

                foreach (var user in users)
                {
                    var userDto = new UserDto
                    {
                        UserGUID = user.UserGUID,
                        Username = user.Username,
                        Email = user.Email,
                        FullName = user.FullName,
                        LastLoginAt = user.LastLoginAt,
                        LastLoginIp = user.LastLoginIp,
                        IsActive = user.IsActive,
                        CreatedAt = user.CreatedAt,
                        UpdatedAt = user.UpdatedAt ?? user.CreatedAt,
                        RoleNames = (user.Roles ?? new List<Role>())
                            .Select(r => r.RoleName)
                            .ToList(),
                        StoreNames = (user.Stores ?? new List<Store>())
                            .Where(s =>
                                storeScope.StoreGuids.Count == 0
                                || storeScope.StoreGuids.Contains(s.StoreGUID)
                            )
                            .Select(s => s.StoreName)
                            .ToList(),
                        Roles = (user.Roles ?? new List<Role>())
                            .Select(r => new RoleDto
                            {
                                RoleGUID = r.RoleGUID,
                                RoleName = r.RoleName,
                                Description = r.Description,
                                IsActive = r.IsActive,
                                CreatedAt = r.CreatedAt,
                                UpdatedAt = r.UpdatedAt ?? r.CreatedAt,
                            })
                            .ToList(),
                        Stores = (user.Stores ?? new List<Store>())
                            .Where(s =>
                                storeScope.StoreGuids.Count == 0
                                || storeScope.StoreGuids.Contains(s.StoreGUID)
                            )
                            .Select(s => new UserStoreDto
                            {
                                StoreGUID = s.StoreGUID,
                                StoreName = s.StoreName,
                                StoreCode = s.StoreCode,
                                IsActive = s.IsActive,
                                IsPrimary = false,
                                AssignedAt = DateTime.UtcNow,
                            })
                            .ToList(),
                    };
                    userDtos.Add(userDto);
                }

                var result = new PagedResult<UserDto>
                {
                    Items = userDtos,
                    Total = total,
                    Page = query.Page,
                    PageSize = query.PageSize,
                };

                return ApiResponse<PagedResult<UserDto>>.OK(result, "获取用户列表成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户列表失败");
                return ApiResponse<PagedResult<UserDto>>.Error(
                    "获取用户列表失败",
                    "GET_USERS_FAILED"
                );
            }
        }

        /// <summary>
        /// 获取用户列表（高性能版本 - 使用单一复合查询）
        /// </summary>
        public async Task<ApiResponse<PagedResult<UserDto>>> GetUsersOptimizedAsync(
            UserQueryDto query
        )
        {
            try
            {
                var db = _context.Db;
                var storeScope = await ResolveUserListStoreScopeAsync(db);
                if (storeScope.ReturnEmptyPage)
                {
                    return ApiResponse<PagedResult<UserDto>>.OK(
                        CreateEmptyPagedResult(query),
                        "获取用户列表成功"
                    );
                }
                if (IsOutOfScopedStoreQuery(storeScope, query.StoreGuid))
                {
                    return ApiResponse<PagedResult<UserDto>>.OK(
                        CreateEmptyPagedResult(query),
                        "获取用户列表成功"
                    );
                }

                // 构建复合查询，一次性获取所有需要的数据
                var baseQuery = db.Queryable<User>()
                    .LeftJoin<UserRole>((u, ur) => u.UserGUID == ur.UserGUID)
                    .LeftJoin<Role>((u, ur, r) => ur.RoleGUID == r.RoleGUID && r.IsActive)
                    .LeftJoin<UserStore>((u, ur, r, us) => u.UserGUID == us.UserGUID && !us.IsDeleted)
                    // 用户关联分店用于身份范围展示，停用分店也必须保留；只排除已删除分店。
                    .LeftJoin<Store>(
                        (u, ur, r, us, s) => us.StoreGUID == s.StoreGUID && !s.IsDeleted
                    );

                if (storeScope.StoreGuids.Count > 0)
                {
                    var scopedStoreGuids = storeScope.StoreGuids.ToArray();
                    baseQuery = baseQuery.Where(
                        (u, ur, r, us, s) =>
                            !us.IsDeleted && scopedStoreGuids.Contains(us.StoreGUID)
                    );
                }

                // 搜索条件
                if (!string.IsNullOrEmpty(query.SearchKeyword))
                {
                    baseQuery = baseQuery.Where(
                        (u, ur, r, us, s) =>
                            u.Username.Contains(query.SearchKeyword)
                            || u.Email.Contains(query.SearchKeyword)
                            || (u.FullName != null && u.FullName.Contains(query.SearchKeyword))
                    );
                }

                // 状态筛选
                if (query.IsActive.HasValue)
                {
                    baseQuery = baseQuery.Where(
                        (u, ur, r, us, s) => u.IsActive == query.IsActive.Value
                    );
                }

                // 角色筛选
                if (!string.IsNullOrEmpty(query.RoleGuid))
                {
                    baseQuery = baseQuery.Where((u, ur, r, us, s) => ur.RoleGUID == query.RoleGuid);
                }

                // 分店筛选
                if (!string.IsNullOrEmpty(query.StoreGuid))
                {
                    baseQuery = baseQuery.Where(
                        (u, ur, r, us, s) => us.StoreGUID == query.StoreGuid
                    );
                }

                // 获取去重的用户数据和关联信息
                var userDataList = await baseQuery
                    .Select(
                        (u, ur, r, us, s) =>
                            new
                            {
                                User = new UserDto
                                {
                                    UserGUID = u.UserGUID,
                                    Username = u.Username,
                                    Email = u.Email,
                                    FullName = u.FullName,
                                    LastLoginAt = u.LastLoginAt,
                                    LastLoginIp = u.LastLoginIp,
                                    IsActive = u.IsActive,
                                    CreatedAt = u.CreatedAt,
                                    UpdatedAt = u.UpdatedAt ?? u.CreatedAt,
                                },
                                RoleName = r.RoleName,
                                StoreName = s.StoreName,
                            }
                    )
                    .ToListAsync();

                // 合并相同用户的角色和分店信息
                var userDict = new Dictionary<string, UserDto>();
                var roleDict = new Dictionary<string, HashSet<string>>();
                var storeDict = new Dictionary<string, HashSet<string>>();

                foreach (var item in userDataList)
                {
                    var userGuid = item.User.UserGUID;

                    // 添加用户基本信息（只添加一次）
                    if (!userDict.ContainsKey(userGuid))
                    {
                        userDict[userGuid] = item.User;
                        roleDict[userGuid] = new HashSet<string>();
                        storeDict[userGuid] = new HashSet<string>();
                    }

                    // 添加角色名称
                    if (!string.IsNullOrEmpty(item.RoleName))
                    {
                        roleDict[userGuid].Add(item.RoleName);
                    }

                    // 添加分店名称
                    if (!string.IsNullOrEmpty(item.StoreName))
                    {
                        storeDict[userGuid].Add(item.StoreName);
                    }
                }

                // 将去重后的用户转换为列表
                var distinctUsers = userDict.Values.ToList();

                // 排序
                if (!string.IsNullOrEmpty(query.SortBy))
                {
                    if (query.SortDirection?.ToLower() == "asc")
                    {
                        distinctUsers = query.SortBy.ToLower() switch
                        {
                            "username" => distinctUsers.OrderBy(u => u.Username).ToList(),
                            "email" => distinctUsers.OrderBy(u => u.Email).ToList(),
                            "fullname" => distinctUsers.OrderBy(u => u.FullName).ToList(),
                            "lastloginat" => distinctUsers.OrderBy(u => u.LastLoginAt).ToList(),
                            "createdat" => distinctUsers.OrderBy(u => u.CreatedAt).ToList(),
                            _ => distinctUsers.OrderBy(u => u.CreatedAt).ToList(),
                        };
                    }
                    else
                    {
                        distinctUsers = query.SortBy.ToLower() switch
                        {
                            "username" => distinctUsers.OrderByDescending(u => u.Username).ToList(),
                            "email" => distinctUsers.OrderByDescending(u => u.Email).ToList(),
                            "fullname" => distinctUsers.OrderByDescending(u => u.FullName).ToList(),
                            "lastloginat" => distinctUsers
                                .OrderByDescending(u => u.LastLoginAt)
                                .ToList(),
                            "createdat" => distinctUsers
                                .OrderByDescending(u => u.CreatedAt)
                                .ToList(),
                            _ => distinctUsers.OrderByDescending(u => u.CreatedAt).ToList(),
                        };
                    }
                }
                else
                {
                    distinctUsers = distinctUsers.OrderByDescending(u => u.CreatedAt).ToList();
                }

                // 分页
                var total = distinctUsers.Count;
                var pagedUsers = distinctUsers
                    .Skip((query.Page - 1) * query.PageSize)
                    .Take(query.PageSize)
                    .ToList();

                // 为分页后的用户添加角色和分店信息
                foreach (var user in pagedUsers)
                {
                    user.RoleNames = roleDict
                        .GetValueOrDefault(user.UserGUID, new HashSet<string>())
                        .ToList();
                    user.StoreNames = storeDict
                        .GetValueOrDefault(user.UserGUID, new HashSet<string>())
                        .ToList();
                }

                var result = new PagedResult<UserDto>
                {
                    Items = pagedUsers,
                    Total = total,
                    Page = query.Page,
                    PageSize = query.PageSize,
                };

                return ApiResponse<PagedResult<UserDto>>.OK(result, "获取用户列表成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户列表失败");
                return ApiResponse<PagedResult<UserDto>>.Error(
                    "获取用户列表失败",
                    "GET_USERS_FAILED"
                );
            }
        }

        private async Task<UserListStoreScope> ResolveUserListStoreScopeAsync(ISqlSugarClient db)
        {
            if (_manageableStoreScopeService == null)
            {
                return UserListStoreScope.Unrestricted;
            }

            var scope = await _manageableStoreScopeService.GetScopeAsync();
            if (scope.IsAdmin)
            {
                return UserListStoreScope.Unrestricted;
            }

            if (scope.IsAllowed)
            {
                return new UserListStoreScope(
                    scope.StoreGuids
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    false
                );
            }

            if (!scope.IsAuthenticated || string.IsNullOrWhiteSpace(scope.UserGuid))
            {
                return UserListStoreScope.Unrestricted;
            }

            var isStoreManager = await CurrentUserHasRoleAliasAsync(
                db,
                scope.UserGuid,
                CurrentUserManageableStoreScopeService.StoreManagerRoleAliases
            );

            return isStoreManager
                ? new UserListStoreScope(Array.Empty<string>(), true)
                : UserListStoreScope.Unrestricted;
        }

        private static bool IsOutOfScopedStoreQuery(
            UserListStoreScope storeScope,
            string? requestedStoreGuid
        )
        {
            return storeScope.StoreGuids.Count > 0
                && !string.IsNullOrWhiteSpace(requestedStoreGuid)
                && !storeScope.StoreGuids.Contains(
                    requestedStoreGuid,
                    StringComparer.OrdinalIgnoreCase
                );
        }

        private async Task<UserStoreMutationScope> ResolveUserStoreMutationScopeAsync(
            ISqlSugarClient db
        )
        {
            if (_manageableStoreScopeService == null)
            {
                return UserStoreMutationScope.Unrestricted;
            }

            var scope = await _manageableStoreScopeService.GetScopeAsync();
            if (scope.IsAdmin)
            {
                return UserStoreMutationScope.Unrestricted;
            }

            if (scope.IsAllowed)
            {
                return new UserStoreMutationScope(
                    scope.StoreGuids
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    true,
                    false
                );
            }

            if (!scope.IsAuthenticated || string.IsNullOrWhiteSpace(scope.UserGuid))
            {
                return UserStoreMutationScope.Unrestricted;
            }

            var isStoreManager = await CurrentUserHasRoleAliasAsync(
                db,
                scope.UserGuid,
                CurrentUserManageableStoreScopeService.StoreManagerRoleAliases
            );

            return isStoreManager
                ? UserStoreMutationScope.Forbidden
                : UserStoreMutationScope.Unrestricted;
        }

        private async Task<bool> CurrentUserHasRoleAliasAsync(
            ISqlSugarClient db,
            string userGuid,
            IReadOnlyCollection<string> roleAliases
        )
        {
            var aliases = roleAliases.ToArray();
            var roleGuids = await db.Queryable<Role>()
                .Where(role => !role.IsDeleted && aliases.Contains(role.RoleName))
                .Select(role => role.RoleGUID)
                .ToListAsync();

            return roleGuids.Count > 0
                && await db.Queryable<UserRole>()
                    .AnyAsync(userRole =>
                        userRole.UserGUID == userGuid
                        && !userRole.IsDeleted
                        && roleGuids.Contains(userRole.RoleGUID)
                    );
        }

        private static PagedResult<UserDto> CreateEmptyPagedResult(UserQueryDto query)
        {
            return new PagedResult<UserDto>
            {
                Items = new List<UserDto>(),
                Total = 0,
                Page = query.Page,
                PageSize = query.PageSize,
            };
        }

        private sealed record UserListStoreScope(IReadOnlyCollection<string> StoreGuids, bool ReturnEmptyPage)
        {
            public static UserListStoreScope Unrestricted { get; } =
                new(Array.Empty<string>(), false);
        }

        private sealed record UserStoreMutationScope(
            IReadOnlyCollection<string> StoreGuids,
            bool IsRestricted,
            bool IsForbidden
        )
        {
            public static UserStoreMutationScope Unrestricted { get; } =
                new(Array.Empty<string>(), false, false);

            public static UserStoreMutationScope Forbidden { get; } =
                new(Array.Empty<string>(), false, true);
        }

        /// <summary>
        /// 性能测试方法 - 比较不同查询方式的性能
        /// </summary>
        /// <param name="query">查询参数</param>
        /// <returns>性能测试结果</returns>
        public async Task<ApiResponse<object>> PerformanceTestAsync(UserQueryDto query)
        {
            try
            {
                var results = new List<object>();

                // 测试原始方法
                var sw1 = System.Diagnostics.Stopwatch.StartNew();
                var result1 = await GetUsersAsync(query);
                sw1.Stop();

                // 测试优化方法
                var sw2 = System.Diagnostics.Stopwatch.StartNew();
                var result2 = await GetUsersOptimizedAsync(query);
                sw2.Stop();

                var performanceReport = new
                {
                    OriginalMethod = new
                    {
                        ExecutionTimeMs = sw1.ElapsedMilliseconds,
                        RecordsReturned = result1.Data?.Items?.Count ?? 0,
                        TotalRecords = result1.Data?.Total ?? 0,
                        PerformanceRating = "Baseline",
                    },
                    OptimizedMethod = new
                    {
                        ExecutionTimeMs = sw2.ElapsedMilliseconds,
                        RecordsReturned = result2.Data?.Items?.Count ?? 0,
                        TotalRecords = result2.Data?.Total ?? 0,
                        PerformanceRating = "High Performance",
                    },
                    PerformanceImprovement = new
                    {
                        TimeReductionMs = sw1.ElapsedMilliseconds - sw2.ElapsedMilliseconds,
                        SpeedupFactor = sw1.ElapsedMilliseconds > 0
                            ? Math.Round(
                                (double)sw1.ElapsedMilliseconds / sw2.ElapsedMilliseconds,
                                2
                            )
                            : 1.0,
                        PercentageImprovement = sw1.ElapsedMilliseconds > 0
                            ? Math.Round(
                                (
                                    (double)(sw1.ElapsedMilliseconds - sw2.ElapsedMilliseconds)
                                    / sw1.ElapsedMilliseconds
                                ) * 100,
                                2
                            )
                            : 0.0,
                    },
                    TestParameters = new
                    {
                        PageSize = query.PageSize,
                        Page = query.Page,
                        SearchKeyword = query.SearchKeyword,
                        HasRoleFilter = !string.IsNullOrEmpty(query.RoleGuid),
                        HasStoreFilter = !string.IsNullOrEmpty(query.StoreGuid),
                        TestTimestamp = DateTime.UtcNow,
                    },
                };

                return ApiResponse<object>.OK(performanceReport, "性能测试完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "性能测试失败");
                return ApiResponse<object>.Error("性能测试失败", "PERFORMANCE_TEST_FAILED");
            }
        }

        /// <summary>
        /// 根据GUID获取用户详情
        /// </summary>
        public async Task<ApiResponse<UserDetailDto>> GetUserByGuidAsync(string userGuid)
        {
            try
            {
                var db = _context.Db;
                var user = await db.Queryable<User>()
                    .Where(u => u.UserGUID == userGuid)
                    .FirstAsync();

                if (user == null)
                {
                    return ApiResponse<UserDetailDto>.Error("用户不存在", "USER_NOT_FOUND");
                }

                var userDetail = new UserDetailDto
                {
                    UserGUID = user.UserGUID,
                    Username = user.Username,
                    Email = user.Email,
                    FullName = user.FullName,
                    LastLoginAt = user.LastLoginAt,
                    LastLoginIp = user.LastLoginIp,
                    IsActive = user.IsActive,
                    CreatedAt = user.CreatedAt,
                    UpdatedAt = user.UpdatedAt ?? user.CreatedAt,
                };

                // 获取用户角色详情
                var roles = await db.Queryable<UserRole>()
                    .InnerJoin<Role>((ur, r) => ur.RoleGUID == r.RoleGUID)
                    .Where((ur, r) => ur.UserGUID == userGuid)
                    .Select(
                        (ur, r) =>
                            new RoleDto
                            {
                                RoleGUID = r.RoleGUID,
                                RoleName = r.RoleName,
                                Description = r.Description,
                                IsActive = r.IsActive,
                                CreatedAt = r.CreatedAt,
                                UpdatedAt = r.UpdatedAt ?? r.CreatedAt,
                            }
                    )
                    .ToListAsync();
                userDetail.Roles = roles;

                // 获取用户分店详情
                var stores = await db.Queryable<UserStore>()
                    .InnerJoin<Store>((us, s) => us.StoreGUID == s.StoreGUID)
                    // 用户详情也要展示历史关联的停用分店，只过滤软删除关系和软删除分店。
                    .Where((us, s) => us.UserGUID == userGuid && !us.IsDeleted && !s.IsDeleted)
                    .Select(
                        (us, s) =>
                            new UserStoreDto
                            {
                                StoreGUID = s.StoreGUID,
                                StoreName = s.StoreName,
                                StoreCode = s.StoreCode,
                                IsActive = s.IsActive,
                                IsPrimary = us.IsPrimary,
                                AssignedAt = us.CreatedAt,
                            }
                    )
                    .ToListAsync();
                userDetail.Stores = stores;

                // 填充角色名和分店名
                userDetail.RoleNames = roles.Select(r => r.RoleName).ToList();
                userDetail.StoreNames = stores.Select(s => s.StoreName).ToList();

                // 获取用户的权限（通过关联的角色）
                var roleGuids = roles.Select(r => r.RoleGUID).ToList();
                var permissions = new List<string>();
                if (roleGuids.Any())
                {
                    permissions = await db.Queryable<SysRolePermission>()
                        .Where(srp => roleGuids.Contains(srp.RoleGuid) && !srp.IsDeleted)
                        .Select(srp => srp.PermissionCode)
                        .ToListAsync();
                }
                userDetail.Permissions = permissions;

                return ApiResponse<UserDetailDto>.OK(userDetail, "获取用户详情成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户详情失败，UserGUID: {UserGUID}", userGuid);
                return ApiResponse<UserDetailDto>.Error(
                    "获取用户详情失败",
                    "GET_USER_DETAIL_FAILED"
                );
            }
        }

        /// <summary>
        /// 获取用户登录记录
        /// </summary>
        public async Task<ApiResponse<PagedResult<UserLoginRecordDto>>> GetUserLoginRecordsAsync(
            string userGuid,
            UserLoginRecordQueryDto query
        )
        {
            try
            {
                var db = _context.Db;
                var page = Math.Max(query.Page, 1);
                var pageSize = Math.Clamp(query.PageSize, 1, 100);

                var userExists = await db.Queryable<User>()
                    .AnyAsync(user => user.UserGUID == userGuid && !user.IsDeleted);
                if (!userExists)
                {
                    return ApiResponse<PagedResult<UserLoginRecordDto>>.Error(
                        "用户不存在",
                        "USER_NOT_FOUND"
                    );
                }

                var tokenQuery = db.Queryable<RefreshToken>()
                    .Where(token => token.UserGUID == userGuid && !token.IsDeleted)
                    .OrderByDescending(token => token.CreatedAt);

                var total = await tokenQuery.CountAsync();
                var tokens = await tokenQuery
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
                var now = DateTime.UtcNow;

                var items = tokens
                    .Select(token =>
                    {
                        var isExpired = token.ExpiresAt < now;
                        var status = token.IsRevoked
                            ? "revoked"
                            : isExpired
                                ? "expired"
                                : "active";

                        return new UserLoginRecordDto
                        {
                            SessionId = token.RefreshTokenGUID,
                            LoginAt = token.CreatedAt,
                            IpAddress = token.IpAddress,
                            UserAgent = token.UserAgent,
                            ExpiresAt = token.ExpiresAt,
                            IsRevoked = token.IsRevoked,
                            IsExpired = isExpired,
                            Status = status,
                        };
                    })
                    .ToList();

                var result = new PagedResult<UserLoginRecordDto>
                {
                    Items = items,
                    Total = total,
                    Page = page,
                    PageSize = pageSize,
                };

                return ApiResponse<PagedResult<UserLoginRecordDto>>.OK(
                    result,
                    "获取用户登录记录成功"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户登录记录失败，UserGUID: {UserGUID}", userGuid);
                return ApiResponse<PagedResult<UserLoginRecordDto>>.Error(
                    "获取用户登录记录失败",
                    "GET_USER_LOGIN_RECORDS_FAILED"
                );
            }
        }

        /// <summary>
        /// 根据用户名获取用户
        /// </summary>
        public async Task<ApiResponse<UserDto>> GetUserByUsernameAsync(string username)
        {
            try
            {
                var db = _context.Db;
                var usernameLower = (username ?? string.Empty).Trim().ToLowerInvariant();
                var user = await db.Queryable<User>()
                    .Where(u => u.Username.ToLower() == usernameLower)
                    .Select(u => new UserDto
                    {
                        UserGUID = u.UserGUID,
                        Username = u.Username,
                        Email = u.Email,
                        FullName = u.FullName,
                        LastLoginAt = u.LastLoginAt,
                        LastLoginIp = u.LastLoginIp,
                        IsActive = u.IsActive,
                        CreatedAt = u.CreatedAt,
                        UpdatedAt = u.UpdatedAt ?? u.CreatedAt,
                    })
                    .FirstAsync();

                if (user == null)
                {
                    return ApiResponse<UserDto>.Error("用户不存在", "USER_NOT_FOUND");
                }

                return ApiResponse<UserDto>.OK(user, "获取用户成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据用户名获取用户失败，Username: {Username}", username);
                return ApiResponse<UserDto>.Error("获取用户失败", "GET_USER_BY_USERNAME_FAILED");
            }
        }

        /// <summary>
        /// 根据邮箱获取用户
        /// </summary>
        public async Task<ApiResponse<UserDto>> GetUserByEmailAsync(string email)
        {
            try
            {
                var db = _context.Db;
                var user = await db.Queryable<User>()
                    .Where(u => u.Email == email)
                    .Select(u => new UserDto
                    {
                        UserGUID = u.UserGUID,
                        Username = u.Username,
                        Email = u.Email,
                        FullName = u.FullName,
                        LastLoginAt = u.LastLoginAt,
                        LastLoginIp = u.LastLoginIp,
                        IsActive = u.IsActive,
                        CreatedAt = u.CreatedAt,
                        UpdatedAt = u.UpdatedAt ?? u.CreatedAt,
                    })
                    .FirstAsync();

                if (user == null)
                {
                    return ApiResponse<UserDto>.Error("用户不存在", "USER_NOT_FOUND");
                }

                return ApiResponse<UserDto>.OK(user, "获取用户成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据邮箱获取用户失败，Email: {Email}", email);
                return ApiResponse<UserDto>.Error("获取用户失败", "GET_USER_BY_EMAIL_FAILED");
            }
        }

        /// <summary>
        /// 创建新用户
        /// </summary>
        public async Task<ApiResponse<UserDto>> CreateUserAsync(CreateUserDto dto)
        {
            try
            {
                var db = _context.Db;
                var storeMutationScope = await ResolveUserStoreMutationScopeAsync(db);
                if (storeMutationScope.IsForbidden)
                {
                    return ApiResponse<UserDto>.Error(
                        "当前店长未分配任何可管理分店",
                        "STORE_SCOPE_DENIED"
                    );
                }

                if (
                    storeMutationScope.IsRestricted
                    && dto.StoreGuids.Any(storeGuid =>
                        !storeMutationScope.StoreGuids.Contains(
                            storeGuid,
                            StringComparer.OrdinalIgnoreCase
                        )
                    )
                )
                {
                    return ApiResponse<UserDto>.Error("不能分配非管辖分店", "STORE_SCOPE_DENIED");
                }

                // 检查用户名是否已存在（用户名大小写不敏感）
                var usernameLower = (dto.Username ?? string.Empty).Trim().ToLowerInvariant();
                var existingUser = await db.Queryable<User>()
                    .Where(u => u.Username.ToLower() == usernameLower || u.Email == dto.Email)
                    .FirstAsync();

                if (existingUser != null)
                {
                    if (existingUser.Username.ToLower() == usernameLower)
                        return ApiResponse<UserDto>.Error("用户名已存在", "USERNAME_EXISTS");
                    if (existingUser.Email == dto.Email)
                        return ApiResponse<UserDto>.Error("邮箱已存在", "EMAIL_EXISTS");
                }

                // 创建用户（用户名统一写入小写）
                var user = new User
                {
                    UserGUID = Guid.NewGuid().ToString(),
                    Username = usernameLower,
                    Email = dto.Email,
                    PasswordHash = PasswordHasher.HashSubmittedPassword(dto.Password, dto.PasswordFormat),
                    FullName = dto.FullName,
                    IsActive = dto.IsActive,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };

                // 开启事务
                await db.Ado.BeginTranAsync();

                try
                {
                    // 插入用户
                    await db.Insertable(user).ExecuteCommandAsync();

                    // 分配角色
                    if (dto.RoleGuids.Any())
                    {
                        var userRoles = dto
                            .RoleGuids.Select(roleGuid => new UserRole
                            {
                                UserRoleGUID = Guid.NewGuid().ToString(),
                                UserGUID = user.UserGUID,
                                RoleGUID = roleGuid,
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow,
                            })
                            .ToList();

                        await db.Insertable(userRoles).ExecuteCommandAsync();
                    }

                    // 分配分店
                    if (dto.StoreGuids.Any())
                    {
                        var userStores = dto
                            .StoreGuids.Select(storeGuid => new UserStore
                            {
                                UserStoreGUID = Guid.NewGuid().ToString(),
                                UserGUID = user.UserGUID,
                                StoreGUID = storeGuid,
                                IsPrimary = false, // 默认只是普通分店关联
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow,
                            })
                            .ToList();

                        await db.Insertable(userStores).ExecuteCommandAsync();
                    }

                    await db.Ado.CommitTranAsync();

                    var result = new UserDto
                    {
                        UserGUID = user.UserGUID,
                        Username = user.Username,
                        Email = user.Email,
                        FullName = user.FullName,
                        LastLoginAt = user.LastLoginAt,
                        LastLoginIp = user.LastLoginIp,
                        IsActive = user.IsActive,
                        CreatedAt = user.CreatedAt,
                        UpdatedAt = user.UpdatedAt ?? user.CreatedAt,
                    };

                    _logger.LogInformation(
                        "创建用户成功，UserGUID: {UserGUID}, Username: {Username}",
                        user.UserGUID,
                        user.Username
                    );
                    return ApiResponse<UserDto>.OK(result, "创建用户成功");
                }
                catch
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建用户失败，Username: {Username}", dto.Username);
                return ApiResponse<UserDto>.Error("创建用户失败", "CREATE_USER_FAILED");
            }
        }

        /// <summary>
        /// 批量创建用户（按分店代码、用户名、密码，分配默认角色与对应分店）
        /// </summary>
        public async Task<ApiResponse<BatchCreateUserResultDto>> BatchCreateUsersAsync(
            BatchCreateUserRequestDto dto
        )
        {
            var result = new BatchCreateUserResultDto();
            try
            {
                var db = _context.Db;

                // 校验默认角色存在且启用
                var role = await db.Queryable<Role>()
                    .Where(r => r.RoleGUID == dto.DefaultRoleGuid && r.IsActive)
                    .FirstAsync();
                if (role == null)
                {
                    return ApiResponse<BatchCreateUserResultDto>.Error(
                        "所选默认角色不存在或已禁用，请选择有效角色",
                        "INVALID_DEFAULT_ROLE"
                    );
                }

                if (dto.Items == null || !dto.Items.Any())
                {
                    return ApiResponse<BatchCreateUserResultDto>.Error(
                        "请至少提供一条用户数据",
                        "EMPTY_ITEMS"
                    );
                }

                for (var i = 0; i < dto.Items.Count; i++)
                {
                    var item = dto.Items[i];
                    var rowIndex = i + 1;
                    try
                    {
                        // 按分店代码解析 StoreGUID
                        var store = await db.Queryable<Store>()
                            .Where(s => s.StoreCode == item.StoreCode.Trim())
                            .FirstAsync();
                        if (store == null)
                        {
                            result.FailureCount++;
                            result.Failures.Add(
                                new BatchCreateUserFailureItemDto
                                {
                                    RowIndex = rowIndex,
                                    Username = item.Username,
                                    Reason = $"分店代码不存在: {item.StoreCode}",
                                }
                            );
                            continue;
                        }

                        // 生成邮箱（唯一性用 storeCode 辅助）；用户名统一小写
                        var usernameLower = item.Username.Trim().ToLowerInvariant();
                        var email = $"{usernameLower}@{item.StoreCode.Trim()}.store.local";

                        var createDto = new CreateUserDto
                        {
                            Username = usernameLower,
                            Email = email,
                            Password = item.Password,
                            IsActive = true,
                            RoleGuids = new List<string> { dto.DefaultRoleGuid },
                            StoreGuids = new List<string> { store.StoreGUID },
                        };

                        var createResult = await CreateUserAsync(createDto);
                        if (createResult.Success)
                        {
                            result.SuccessCount++;
                        }
                        else
                        {
                            result.FailureCount++;
                            result.Failures.Add(
                                new BatchCreateUserFailureItemDto
                                {
                                    RowIndex = rowIndex,
                                    Username = item.Username,
                                    Reason = createResult.Message ?? "创建失败",
                                }
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        result.FailureCount++;
                        result.Failures.Add(
                            new BatchCreateUserFailureItemDto
                            {
                                RowIndex = rowIndex,
                                Username = item.Username,
                                Reason = ex.Message,
                            }
                        );
                        _logger.LogWarning(
                            ex,
                            "批量创建用户第 {Row} 行失败: {Username}",
                            rowIndex,
                            item.Username
                        );
                    }
                }

                _logger.LogInformation(
                    "批量创建用户完成，成功: {Success}, 失败: {Failure}",
                    result.SuccessCount,
                    result.FailureCount
                );
                return ApiResponse<BatchCreateUserResultDto>.OK(result, "批量创建完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建用户失败");
                return ApiResponse<BatchCreateUserResultDto>.Error(
                    "批量创建用户失败",
                    "BATCH_CREATE_USERS_FAILED"
                );
            }
        }

        /// <summary>
        /// 根据GUID更新用户
        /// </summary>
        public async Task<ApiResponse<UserDto>> UpdateUserByGuidAsync(
            string userGuid,
            UpdateUserDto dto
        )
        {
            try
            {
                var db = _context.Db;

                // 检查用户是否存在
                var user = await db.Queryable<User>()
                    .Where(u => u.UserGUID == userGuid)
                    .FirstAsync();

                if (user == null)
                {
                    return ApiResponse<UserDto>.Error("用户不存在", "USER_NOT_FOUND");
                }

                // 检查用户名和邮箱是否被其他用户使用（用户名大小写不敏感）
                var usernameLower = (dto.Username ?? string.Empty).Trim().ToLowerInvariant();
                var existingUser = await db.Queryable<User>()
                    .Where(u =>
                        u.UserGUID != userGuid
                        && (u.Username.ToLower() == usernameLower || u.Email == dto.Email)
                    )
                    .FirstAsync();

                if (existingUser != null)
                {
                    if (existingUser.Username.ToLower() == usernameLower)
                        return ApiResponse<UserDto>.Error(
                            "用户名已被其他用户使用",
                            "USERNAME_EXISTS"
                        );
                    if (existingUser.Email == dto.Email)
                        return ApiResponse<UserDto>.Error("邮箱已被其他用户使用", "EMAIL_EXISTS");
                }

                // 更新用户信息（用户名统一写入小写）
                user.Username = usernameLower;
                user.Email = dto.Email;
                user.FullName = dto.FullName;
                user.IsActive = dto.IsActive;
                user.UpdatedAt = DateTime.UtcNow;

                await db.Updateable(user).ExecuteCommandAsync();

                var result = new UserDto
                {
                    UserGUID = user.UserGUID,
                    Username = user.Username,
                    Email = user.Email,
                    FullName = user.FullName,
                    LastLoginAt = user.LastLoginAt,
                    LastLoginIp = user.LastLoginIp,
                    IsActive = user.IsActive,
                    CreatedAt = user.CreatedAt,
                    UpdatedAt = user.UpdatedAt ?? user.CreatedAt,
                };

                _logger.LogInformation("更新用户成功，UserGUID: {UserGUID}", userGuid);
                return ApiResponse<UserDto>.OK(result, "更新用户成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新用户失败，UserGUID: {UserGUID}", userGuid);
                return ApiResponse<UserDto>.Error("更新用户失败", "UPDATE_USER_FAILED");
            }
        }

        /// <summary>
        /// 根据GUID删除用户
        /// </summary>
        public async Task<ApiResponse<bool>> DeleteUserByGuidAsync(string userGuid)
        {
            try
            {
                var db = _context.Db;

                // 检查用户是否存在
                var user = await db.Queryable<User>()
                    .Where(u => u.UserGUID == userGuid)
                    .FirstAsync();

                if (user == null)
                {
                    return ApiResponse<bool>.Error("用户不存在", "USER_NOT_FOUND");
                }

                // 开启事务
                await db.Ado.BeginTranAsync();

                try
                {
                    // 删除用户角色关联
                    await db.Deleteable<UserRole>()
                        .Where(ur => ur.UserGUID == userGuid)
                        .ExecuteCommandAsync();

                    // 删除用户分店关联
                    await db.Deleteable<UserStore>()
                        .Where(us => us.UserGUID == userGuid)
                        .ExecuteCommandAsync();

                    // 删除用户刷新令牌
                    await db.Deleteable<RefreshToken>()
                        .Where(rt => rt.UserGUID == userGuid)
                        .ExecuteCommandAsync();

                    // 删除用户
                    await db.Deleteable<User>()
                        .Where(u => u.UserGUID == userGuid)
                        .ExecuteCommandAsync();

                    await db.Ado.CommitTranAsync();

                    _logger.LogInformation("删除用户成功，UserGUID: {UserGUID}", userGuid);
                    return ApiResponse<bool>.OK(true, "删除用户成功");
                }
                catch
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除用户失败，UserGUID: {UserGUID}", userGuid);
                return ApiResponse<bool>.Error("删除用户失败", "DELETE_USER_FAILED");
            }
        }

        /// <summary>
        /// 根据GUID更新用户状态
        /// </summary>
        public async Task<ApiResponse<bool>> UpdateUserStatusByGuidAsync(
            string userGuid,
            bool isActive
        )
        {
            try
            {
                var db = _context.Db;

                var result = await db.Updateable<User>()
                    .SetColumns(u => new User { IsActive = isActive, UpdatedAt = DateTime.UtcNow })
                    .Where(u => u.UserGUID == userGuid)
                    .ExecuteCommandAsync();

                if (result == 0)
                {
                    return ApiResponse<bool>.Error("用户不存在", "USER_NOT_FOUND");
                }

                _logger.LogInformation(
                    "更新用户状态成功，UserGUID: {UserGUID}, IsActive: {IsActive}",
                    userGuid,
                    isActive
                );
                return ApiResponse<bool>.OK(
                    true,
                    $"用户状态已更新为{(isActive ? "激活" : "禁用")}"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新用户状态失败，UserGUID: {UserGUID}", userGuid);
                return ApiResponse<bool>.Error("更新用户状态失败", "UPDATE_USER_STATUS_FAILED");
            }
        }

        /// <summary>
        /// 更新用户密码
        /// </summary>
        public async Task<ApiResponse<bool>> UpdateUserPasswordByGuidAsync(
            string userGuid,
            UpdateUserPasswordDto dto
        )
        {
            try
            {
                var db = _context.Db;

                var hashedPassword = PasswordHasher.HashSubmittedPassword(dto.NewPassword, dto.PasswordFormat);

                var result = await db.Updateable<User>()
                    .SetColumns(u => new User
                    {
                        PasswordHash = hashedPassword,
                        UpdatedAt = DateTime.UtcNow,
                    })
                    .Where(u => u.UserGUID == userGuid)
                    .ExecuteCommandAsync();

                if (result == 0)
                {
                    return ApiResponse<bool>.Error("用户不存在", "USER_NOT_FOUND");
                }

                _logger.LogInformation("更新用户密码成功，UserGUID: {UserGUID}", userGuid);
                return ApiResponse<bool>.OK(true, "密码更新成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新用户密码失败，UserGUID: {UserGUID}", userGuid);
                return ApiResponse<bool>.Error("更新用户密码失败", "UPDATE_USER_PASSWORD_FAILED");
            }
        }

        /// <summary>
        /// 为用户分配角色
        /// </summary>
        public async Task<ApiResponse<bool>> AssignRolesToUserAsync(
            string userGuid,
            UserRoleAssignmentDto dto
        )
        {
            try
            {
                var db = _context.Db;

                // 检查用户是否存在
                var userExists = await db.Queryable<User>()
                    .Where(u => u.UserGUID == userGuid)
                    .AnyAsync();

                if (!userExists)
                {
                    return ApiResponse<bool>.Error("用户不存在", "USER_NOT_FOUND");
                }

                // 开启事务
                await db.Ado.BeginTranAsync();

                try
                {
                    // 删除现有的角色分配
                    await db.Deleteable<UserRole>()
                        .Where(ur => ur.UserGUID == userGuid)
                        .ExecuteCommandAsync();

                    // 添加新的角色分配
                    if (dto.RoleGuids.Any())
                    {
                        var userRoles = dto
                            .RoleGuids.Select(roleGuid => new UserRole
                            {
                                UserRoleGUID = Guid.NewGuid().ToString(), // ✅ 必须设置主键
                                UserGUID = userGuid,
                                RoleGUID = roleGuid,
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow,
                            })
                            .ToList();

                        await db.Insertable(userRoles).ExecuteCommandAsync();
                    }

                    await db.Ado.CommitTranAsync();

                    _logger.LogInformation(
                        "用户角色分配成功，UserGUID: {UserGUID}, RoleCount: {RoleCount}",
                        userGuid,
                        dto.RoleGuids.Count
                    );
                    return ApiResponse<bool>.OK(true, "角色分配成功");
                }
                catch
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "用户角色分配失败，UserGUID: {UserGUID}", userGuid);
                return ApiResponse<bool>.Error("角色分配失败", "ASSIGN_ROLES_FAILED");
            }
        }

        /// <summary>
        /// 为用户分配分店
        /// </summary>
        public async Task<ApiResponse<bool>> AssignStoresToUserAsync(
            string userGuid,
            List<UserStoreAssignmentDto> storeAssignments
        )
        {
            try
            {
                var db = _context.Db;

                // 检查用户是否存在
                var userExists = await db.Queryable<User>()
                    .Where(u => u.UserGUID == userGuid)
                    .AnyAsync();

                if (!userExists)
                {
                    return ApiResponse<bool>.Error("用户不存在", "USER_NOT_FOUND");
                }

                var storeMutationScope = await ResolveUserStoreMutationScopeAsync(db);
                if (storeMutationScope.IsForbidden)
                {
                    return ApiResponse<bool>.Error(
                        "当前店长未分配任何可管理分店",
                        "STORE_SCOPE_DENIED"
                    );
                }

                var effectiveStoreAssignments = storeAssignments;
                if (storeMutationScope.IsRestricted)
                {
                    var scopedStoreGuids = new HashSet<string>(
                        storeMutationScope.StoreGuids,
                        StringComparer.OrdinalIgnoreCase
                    );
                    var existingUserStores = await db.Queryable<UserStore>()
                        .Where(us => us.UserGUID == userGuid && !us.IsDeleted)
                        .ToListAsync();
                    var existingInScope = existingUserStores.Any(us =>
                        scopedStoreGuids.Contains(us.StoreGUID)
                    );

                    if (!existingInScope)
                    {
                        return ApiResponse<bool>.Error("无权管理该用户的分店", "USER_SCOPE_DENIED");
                    }

                    var hiddenExistingStoreGuids = existingUserStores
                        .Where(us => !scopedStoreGuids.Contains(us.StoreGUID))
                        .Select(us => us.StoreGUID)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var outOfScopeAssignments = storeAssignments
                        .Where(assignment => !scopedStoreGuids.Contains(assignment.StoreGUID))
                        .ToList();

                    if (
                        outOfScopeAssignments.Any(assignment =>
                            !hiddenExistingStoreGuids.Contains(assignment.StoreGUID)
                        )
                    )
                    {
                        return ApiResponse<bool>.Error("不能分配非管辖分店", "STORE_SCOPE_DENIED");
                    }

                    effectiveStoreAssignments = storeAssignments
                        .Where(assignment => scopedStoreGuids.Contains(assignment.StoreGUID))
                        .ToList();
                }

                // 开启事务
                await db.Ado.BeginTranAsync();

                try
                {
                    // 删除现有的分店分配
                    if (storeMutationScope.IsRestricted)
                    {
                        var scopedStoreGuids = storeMutationScope.StoreGuids.ToArray();
                        await db.Deleteable<UserStore>()
                            .Where(us =>
                                us.UserGUID == userGuid && scopedStoreGuids.Contains(us.StoreGUID)
                            )
                            .ExecuteCommandAsync();
                    }
                    else
                    {
                        await db.Deleteable<UserStore>()
                            .Where(us => us.UserGUID == userGuid)
                            .ExecuteCommandAsync();
                    }

                    // 添加新的分店分配
                    if (effectiveStoreAssignments.Any())
                    {
                        var userStores = effectiveStoreAssignments
                            .Select(
                                (assignment, index) =>
                                    new UserStore
                                    {
                                        UserStoreGUID = Guid.NewGuid().ToString(), // ✅ 必须设置主键
                                        UserGUID = userGuid,
                                        StoreGUID = assignment.StoreGUID,
                                        IsPrimary = assignment.IsPrimary,
                                        CreatedAt = DateTime.UtcNow,
                                        UpdatedAt = DateTime.UtcNow,
                                    }
                            )
                            .ToList();

                        await db.Insertable(userStores).ExecuteCommandAsync();
                    }

                    await db.Ado.CommitTranAsync();

                    _logger.LogInformation(
                        "用户分店分配成功，UserGUID: {UserGUID}, StoreCount: {StoreCount}",
                        userGuid,
                        effectiveStoreAssignments.Count
                    );
                    return ApiResponse<bool>.OK(true, "分店分配成功");
                }
                catch
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "用户分店分配失败，UserGUID: {UserGUID}", userGuid);
                return ApiResponse<bool>.Error("分店分配失败", "ASSIGN_STORES_FAILED");
            }
        }

        /// <summary>
        /// 获取用户的角色列表
        /// </summary>
        public async Task<ApiResponse<List<RoleDto>>> GetUserRolesAsync(string userGuid)
        {
            try
            {
                var db = _context.Db;

                var roles = await db.Queryable<UserRole>()
                    .InnerJoin<Role>((ur, r) => ur.RoleGUID == r.RoleGUID)
                    .Where((ur, r) => ur.UserGUID == userGuid)
                    .Select(
                        (ur, r) =>
                            new RoleDto
                            {
                                RoleGUID = r.RoleGUID,
                                RoleName = r.RoleName,
                                Description = r.Description,
                                IsActive = r.IsActive,
                                CreatedAt = r.CreatedAt,
                                UpdatedAt = r.UpdatedAt ?? r.CreatedAt,
                            }
                    )
                    .ToListAsync();

                return ApiResponse<List<RoleDto>>.OK(roles, "获取用户角色成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户角色失败，UserGUID: {UserGUID}", userGuid);
                return ApiResponse<List<RoleDto>>.Error(
                    "获取用户角色失败",
                    "GET_USER_ROLES_FAILED"
                );
            }
        }

        /// <summary>
        /// 获取用户的分店列表
        /// </summary>
        public async Task<ApiResponse<List<UserStoreDto>>> GetUserStoresAsync(string userGuid)
        {
            try
            {
                var db = _context.Db;

                var userRoles = await db.Queryable<UserRole>()
                    .InnerJoin<Role>((ur, r) => ur.RoleGUID == r.RoleGUID)
                    .Where((ur, r) => ur.UserGUID == userGuid)
                    .Select((ur, r) => r.RoleName)
                    .ToListAsync();

                var isAdminOrWarehouse =
                    userRoles.Contains("Admin") || userRoles.Contains("Warehouse");

                List<UserStoreDto> stores;

                if (isAdminOrWarehouse)
                {
                    stores = await db.Queryable<Store>()
                        // 当前用户身份范围需要展示已关联/可见分店，停用分店不能影响登录后的用户信息。
                        .Where(s => !s.IsDeleted)
                        .Select(s => new UserStoreDto
                        {
                            StoreGUID = s.StoreGUID,
                            StoreName = s.StoreName,
                            StoreCode = s.StoreCode,
                            IsActive = s.IsActive,
                            IsPrimary = false,
                            AssignedAt = DateTime.UtcNow,
                        })
                        .ToListAsync();
                }
                else
                {
                    stores = await db.Queryable<UserStore>()
                        .InnerJoin<Store>((us, s) => us.StoreGUID == s.StoreGUID)
                        // 用户登录和用户信息展示只要求用户分店关系存在，停用分店也要返回。
                        .Where((us, s) => us.UserGUID == userGuid && !us.IsDeleted && !s.IsDeleted)
                        .Select(
                            (us, s) =>
                                new UserStoreDto
                                {
                                    StoreGUID = s.StoreGUID,
                                    StoreName = s.StoreName,
                                    StoreCode = s.StoreCode,
                                    IsActive = s.IsActive,
                                    IsPrimary = us.IsPrimary,
                                    AssignedAt = us.CreatedAt,
                                }
                        )
                        .ToListAsync();
                }

                return ApiResponse<List<UserStoreDto>>.OK(stores, "获取用户分店成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户分店失败，UserGUID: {UserGUID}", userGuid);
                return ApiResponse<List<UserStoreDto>>.Error(
                    "获取用户分店失败",
                    "GET_USER_STORES_FAILED"
                );
            }
        }

        /// <summary>
        /// 从用户移除角色
        /// </summary>
        public async Task<ApiResponse<bool>> RemoveRoleFromUserAsync(
            string userGuid,
            string roleGuid
        )
        {
            try
            {
                var db = _context.Db;

                var result = await db.Deleteable<UserRole>()
                    .Where(ur => ur.UserGUID == userGuid && ur.RoleGUID == roleGuid)
                    .ExecuteCommandAsync();

                if (result == 0)
                {
                    return ApiResponse<bool>.Error("用户角色关联不存在", "USER_ROLE_NOT_FOUND");
                }

                _logger.LogInformation(
                    "移除用户角色成功，UserGUID: {UserGUID}, RoleGUID: {RoleGUID}",
                    userGuid,
                    roleGuid
                );
                return ApiResponse<bool>.OK(true, "移除角色成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "移除用户角色失败，UserGUID: {UserGUID}, RoleGUID: {RoleGUID}",
                    userGuid,
                    roleGuid
                );
                return ApiResponse<bool>.Error("移除角色失败", "REMOVE_ROLE_FAILED");
            }
        }

        /// <summary>
        /// 从用户移除分店
        /// </summary>
        public async Task<ApiResponse<bool>> RemoveStoreFromUserAsync(
            string userGuid,
            string storeGuid
        )
        {
            try
            {
                var db = _context.Db;

                var result = await db.Deleteable<UserStore>()
                    .Where(us => us.UserGUID == userGuid && us.StoreGUID == storeGuid)
                    .ExecuteCommandAsync();

                if (result == 0)
                {
                    return ApiResponse<bool>.Error("用户分店关联不存在", "USER_STORE_NOT_FOUND");
                }

                _logger.LogInformation(
                    "移除用户分店成功，UserGUID: {UserGUID}, StoreGUID: {StoreGUID}",
                    userGuid,
                    storeGuid
                );
                return ApiResponse<bool>.OK(true, "移除分店成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "移除用户分店失败，UserGUID: {UserGUID}, StoreGUID: {StoreGUID}",
                    userGuid,
                    storeGuid
                );
                return ApiResponse<bool>.Error("移除分店失败", "REMOVE_STORE_FAILED");
            }
        }

        /// <summary>
        /// 批量管理用户
        /// </summary>
        public async Task<ApiResponse<bool>> BatchManageUsersAsync(BatchUserOperationDto dto)
        {
            try
            {
                var db = _context.Db;

                switch (dto.Operation.ToLower())
                {
                    case "activate":
                        await db.Updateable<User>()
                            .SetColumns(u => new User
                            {
                                IsActive = true,
                                UpdatedAt = DateTime.UtcNow,
                            })
                            .Where(u => dto.UserGuids.Contains(u.UserGUID))
                            .ExecuteCommandAsync();
                        break;

                    case "deactivate":
                        await db.Updateable<User>()
                            .SetColumns(u => new User
                            {
                                IsActive = false,
                                UpdatedAt = DateTime.UtcNow,
                            })
                            .Where(u => dto.UserGuids.Contains(u.UserGUID))
                            .ExecuteCommandAsync();
                        break;

                    case "delete":
                        // 开启事务
                        await db.Ado.BeginTranAsync();
                        try
                        {
                            // 删除相关数据
                            await db.Deleteable<UserRole>()
                                .Where(ur => dto.UserGuids.Contains(ur.UserGUID))
                                .ExecuteCommandAsync();

                            await db.Deleteable<UserStore>()
                                .Where(us => dto.UserGuids.Contains(us.UserGUID))
                                .ExecuteCommandAsync();

                            await db.Deleteable<RefreshToken>()
                                .Where(rt => dto.UserGuids.Contains(rt.UserGUID))
                                .ExecuteCommandAsync();

                            await db.Deleteable<User>()
                                .Where(u => dto.UserGuids.Contains(u.UserGUID))
                                .ExecuteCommandAsync();

                            await db.Ado.CommitTranAsync();
                        }
                        catch
                        {
                            await db.Ado.RollbackTranAsync();
                            throw;
                        }
                        break;

                    default:
                        return ApiResponse<bool>.Error("不支持的操作类型", "UNSUPPORTED_OPERATION");
                }

                _logger.LogInformation(
                    "批量管理用户成功，Operation: {Operation}, UserCount: {UserCount}",
                    dto.Operation,
                    dto.UserGuids.Count
                );
                return ApiResponse<bool>.OK(true, $"批量{dto.Operation}操作成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量管理用户失败，Operation: {Operation}", dto.Operation);
                return ApiResponse<bool>.Error("批量操作失败", "BATCH_OPERATION_FAILED");
            }
        }

        /// <summary>
        /// 导入用户（简化实现）
        /// </summary>
        public async Task<ApiResponse<ImportUserResultDto>> ImportUsersAsync(
            List<ImportUserDto> users
        )
        {
            var result = new ImportUserResultDto { TotalCount = users.Count };

            try
            {
                foreach (var importUser in users)
                {
                    try
                    {
                        var createDto = new CreateUserDto
                        {
                            Username = importUser.Username,
                            Email = importUser.Email,
                            FullName = importUser.FullName,
                            // 导入密码按原始密码交给后端统一慢哈希，保持与新登录协议一致。
                            Password = importUser.Password,
                            PasswordFormat = PasswordHasher.PasswordFormatRaw,
                            IsActive = true,
                        };

                        var createResult = await CreateUserAsync(createDto);
                        if (createResult.IsSuccess)
                        {
                            result.SuccessCount++;
                            result.ImportedUsers.Add(createResult.Data!);
                        }
                        else
                        {
                            result.FailureCount++;
                            result.Errors.Add(
                                $"用户 {importUser.Username}: {createResult.Message}"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        result.FailureCount++;
                        result.Errors.Add($"用户 {importUser.Username}: {ex.Message}");
                    }
                }

                return ApiResponse<ImportUserResultDto>.OK(
                    result,
                    $"导入完成，成功: {result.SuccessCount}, 失败: {result.FailureCount}"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导入用户失败");
                return ApiResponse<ImportUserResultDto>.Error(
                    "导入用户失败",
                    "IMPORT_USERS_FAILED"
                );
            }
        }

        /// <summary>
        /// 导出用户
        /// </summary>
        public async Task<ApiResponse<List<UserDto>>> ExportUsersAsync(UserQueryDto query)
        {
            try
            {
                query.PageSize = int.MaxValue; // 获取所有数据
                query.PageNumber = 1;

                var result = await GetUsersAsync(query);
                if (result.IsSuccess)
                {
                    return ApiResponse<List<UserDto>>.OK(
                        result.Data?.Items ?? new List<UserDto>(),
                        "导出用户数据成功"
                    );
                }

                return ApiResponse<List<UserDto>>.Error(result.Message, result.ErrorCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出用户失败");
                return ApiResponse<List<UserDto>>.Error("导出用户失败", "EXPORT_USERS_FAILED");
            }
        }

        /// <summary>
        /// 获取用户统计信息
        /// </summary>
        public async Task<ApiResponse<UserStatisticsDto>> GetUserStatisticsAsync()
        {
            try
            {
                var db = _context.Db;

                // 获取基本统计
                var totalUsers = await db.Queryable<User>().CountAsync();
                var activeUsers = await db.Queryable<User>().Where(u => u.IsActive).CountAsync();
                var inactiveUsers = await db.Queryable<User>().Where(u => !u.IsActive).CountAsync();

                // 获取有角色的用户
                var usersWithRoles = await db.Queryable<UserRole>()
                    .Select(ur => ur.UserGUID)
                    .Distinct()
                    .ToListAsync();
                var usersWithoutRoles = totalUsers - usersWithRoles.Count;

                // 获取有分店的用户
                var usersWithStores = await db.Queryable<UserStore>()
                    .Select(us => us.UserGUID)
                    .Distinct()
                    .ToListAsync();
                var usersWithoutStores = totalUsers - usersWithStores.Count;

                var statistics = new UserStatisticsDto
                {
                    TotalUsers = totalUsers,
                    ActiveUsers = activeUsers,
                    InactiveUsers = inactiveUsers,
                    UsersWithoutRoles = usersWithoutRoles,
                    UsersWithoutStores = usersWithoutStores,
                };

                return ApiResponse<UserStatisticsDto>.OK(statistics, "获取用户统计成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户统计失败");
                return ApiResponse<UserStatisticsDto>.Error(
                    "获取用户统计失败",
                    "GET_USER_STATISTICS_FAILED"
                );
            }
        }

        /// <summary>
        /// 检查用户名是否可用
        /// </summary>
        public async Task<ApiResponse<bool>> IsUsernameAvailableAsync(
            string username,
            string? excludeUserGuid = null
        )
        {
            try
            {
                var db = _context.Db;
                var usernameLower = (username ?? string.Empty).Trim().ToLowerInvariant();
                var query = db.Queryable<User>().Where(u => u.Username.ToLower() == usernameLower);

                if (!string.IsNullOrEmpty(excludeUserGuid))
                {
                    query = query.Where(u => u.UserGUID != excludeUserGuid);
                }

                var exists = await query.AnyAsync();
                return ApiResponse<bool>.OK(!exists, exists ? "用户名已存在" : "用户名可用");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查用户名可用性失败，Username: {Username}", username);
                return ApiResponse<bool>.Error("检查用户名失败", "CHECK_USERNAME_FAILED");
            }
        }

        /// <summary>
        /// 检查邮箱是否可用
        /// </summary>
        public async Task<ApiResponse<bool>> IsEmailAvailableAsync(
            string email,
            string? excludeUserGuid = null
        )
        {
            try
            {
                var db = _context.Db;
                var query = db.Queryable<User>().Where(u => u.Email == email);

                if (!string.IsNullOrEmpty(excludeUserGuid))
                {
                    query = query.Where(u => u.UserGUID != excludeUserGuid);
                }

                var exists = await query.AnyAsync();
                return ApiResponse<bool>.OK(!exists, exists ? "邮箱已存在" : "邮箱可用");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查邮箱可用性失败，Email: {Email}", email);
                return ApiResponse<bool>.Error("检查邮箱失败", "CHECK_EMAIL_FAILED");
            }
        }

        /// <summary>
        /// 重置用户密码
        /// </summary>
        public async Task<ApiResponse<string>> ResetUserPasswordAsync(string userGuid)
        {
            try
            {
                var db = _context.Db;

                // 生成新密码
                var newPassword = GenerateRandomPassword();
                var hashedPassword = PasswordHasher.HashPassword(newPassword);

                var result = await db.Updateable<User>()
                    .SetColumns(u => new User
                    {
                        PasswordHash = hashedPassword,
                        UpdatedAt = DateTime.UtcNow,
                    })
                    .Where(u => u.UserGUID == userGuid)
                    .ExecuteCommandAsync();

                if (result == 0)
                {
                    return ApiResponse<string>.Error("用户不存在", "USER_NOT_FOUND");
                }

                _logger.LogInformation("重置用户密码成功，UserGUID: {UserGUID}", userGuid);
                return ApiResponse<string>.OK(newPassword, "密码重置成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重置用户密码失败，UserGUID: {UserGUID}", userGuid);
                return ApiResponse<string>.Error("重置密码失败", "RESET_PASSWORD_FAILED");
            }
        }

        /// <summary>
        /// 锁定/解锁用户
        /// </summary>
        public async Task<ApiResponse<bool>> LockUserAsync(string userGuid, bool isLocked)
        {
            try
            {
                // 这里可以实现用户锁定逻辑，暂时使用IsActive字段代替
                return await UpdateUserStatusByGuidAsync(userGuid, !isLocked);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "锁定/解锁用户失败，UserGUID: {UserGUID}, IsLocked: {IsLocked}",
                    userGuid,
                    isLocked
                );
                return ApiResponse<bool>.Error("锁定/解锁用户失败", "LOCK_USER_FAILED");
            }
        }

        #region 私有方法


        /// <summary>
        /// 生成随机密码
        /// </summary>
        private string GenerateRandomPassword()
        {
            const string chars =
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
            var random = new Random();
            return new string(
                Enumerable.Repeat(chars, 12).Select(s => s[random.Next(s.Length)]).ToArray()
            );
        }

        #endregion
    }
}
