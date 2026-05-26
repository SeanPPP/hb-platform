using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 角色管理服务实现
    /// 🔐 提供角色数据的CRUD操作和权限管理功能
    /// </summary>
    public class RoleService : IRoleService
    {
        private readonly SqlSugarContext _context;
        private readonly ILogger<RoleService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        // 系统预定义权限
        private readonly List<PermissionCategoryDto> _systemPermissions = new()
        {
            new PermissionCategoryDto
            {
                Category = "user_management",
                DisplayName = "用户管理",
                Description = "用户相关的权限",
                Permissions = new List<PermissionDto>
                {
                    new()
                    {
                        Name = "user.view",
                        DisplayName = "查看用户",
                        Description = "查看用户列表和详情",
                        Category = "user_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "user.create",
                        DisplayName = "创建用户",
                        Description = "创建新用户",
                        Category = "user_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "user.edit",
                        DisplayName = "编辑用户",
                        Description = "修改用户信息",
                        Category = "user_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "user.delete",
                        DisplayName = "删除用户",
                        Description = "删除用户",
                        Category = "user_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "user.manage_roles",
                        DisplayName = "管理用户角色",
                        Description = "分配和移除用户角色",
                        Category = "user_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "user.manage_stores",
                        DisplayName = "管理用户分店",
                        Description = "分配和移除用户分店",
                        Category = "user_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "user.reset_password",
                        DisplayName = "重置密码",
                        Description = "重置用户密码",
                        Category = "user_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                },
            },
            new PermissionCategoryDto
            {
                Category = "role_management",
                DisplayName = "角色管理",
                Description = "角色相关的权限",
                Permissions = new List<PermissionDto>
                {
                    new()
                    {
                        Name = "role.view",
                        DisplayName = "查看角色",
                        Description = "查看角色列表和详情",
                        Category = "role_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "role.create",
                        DisplayName = "创建角色",
                        Description = "创建新角色",
                        Category = "role_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "role.edit",
                        DisplayName = "编辑角色",
                        Description = "修改角色信息",
                        Category = "role_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "role.delete",
                        DisplayName = "删除角色",
                        Description = "删除角色",
                        Category = "role_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "role.manage_permissions",
                        DisplayName = "管理角色权限",
                        Description = "分配和移除角色权限",
                        Category = "role_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "role.manage_users",
                        DisplayName = "管理角色用户",
                        Description = "为角色添加和移除用户",
                        Category = "role_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                },
            },
            new PermissionCategoryDto
            {
                Category = "store_management",
                DisplayName = "分店管理",
                Description = "分店相关的权限",
                Permissions = new List<PermissionDto>
                {
                    new()
                    {
                        Name = "store.view",
                        DisplayName = "查看分店",
                        Description = "查看分店列表和详情",
                        Category = "store_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "store.create",
                        DisplayName = "创建分店",
                        Description = "创建新分店",
                        Category = "store_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "store.edit",
                        DisplayName = "编辑分店",
                        Description = "修改分店信息",
                        Category = "store_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "store.delete",
                        DisplayName = "删除分店",
                        Description = "删除分店",
                        Category = "store_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "store.manage_users",
                        DisplayName = "管理分店用户",
                        Description = "为分店添加和移除用户",
                        Category = "store_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "store.sync",
                        DisplayName = "同步分店数据",
                        Description = "从HQ同步分店数据",
                        Category = "store_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                },
            },
            new PermissionCategoryDto
            {
                Category = "system_management",
                DisplayName = "系统管理",
                Description = "系统相关的权限",
                Permissions = new List<PermissionDto>
                {
                    new()
                    {
                        Name = "system.view_logs",
                        DisplayName = "查看日志",
                        Description = "查看系统日志",
                        Category = "system_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "system.manage_settings",
                        DisplayName = "管理设置",
                        Description = "管理系统设置",
                        Category = "system_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "system.backup",
                        DisplayName = "备份数据",
                        Description = "备份系统数据",
                        Category = "system_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                    new()
                    {
                        Name = "system.restore",
                        DisplayName = "恢复数据",
                        Description = "恢复系统数据",
                        Category = "system_management",
                        IsSystemPermission = true,
                        CreatedAt = DateTime.Now,
                        CreatedBy = "System",
                    },
                },
            },
        };

        public RoleService(
            SqlSugarContext context,
            ILogger<RoleService> logger,
            IHttpContextAccessor httpContextAccessor
        )
        {
            _context = context;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        private string GetCurrentUsername()
        {
            return _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
        }

        private static bool IsSuperAdminRoleName(string? roleName)
        {
            return Permissions.IsSuperAdminRole(roleName);
        }

        /// <summary>
        /// 获取角色列表（分页）
        /// </summary>
        public async Task<ApiResponse<PagedResult<RoleDto>>> GetRolesAsync(RoleQueryDto query)
        {
            try
            {
                var db = _context.Db;
                var roleQuery = db.Queryable<Role>();

                // 搜索条件
                if (!string.IsNullOrEmpty(query.SearchKeyword))
                {
                    roleQuery = roleQuery.Where(r =>
                        r.RoleName.Contains(query.SearchKeyword)
                        || (r.Description != null && r.Description.Contains(query.SearchKeyword))
                    );
                }

                // 状态筛选
                if (query.IsActive.HasValue)
                {
                    roleQuery = roleQuery.Where(r => r.IsActive == query.IsActive.Value);
                }

                // 排序
                if (!string.IsNullOrEmpty(query.SortBy))
                {
                    if (query.SortDirection?.ToLower() == "asc")
                    {
                        roleQuery = query.SortBy.ToLower() switch
                        {
                            "rolename" => roleQuery.OrderBy(r => r.RoleName),
                            "description" => roleQuery.OrderBy(r => r.Description),
                            "createdat" => roleQuery.OrderBy(r => r.CreatedAt),
                            _ => roleQuery.OrderBy(r => r.CreatedAt),
                        };
                    }
                    else
                    {
                        roleQuery = query.SortBy.ToLower() switch
                        {
                            "rolename" => roleQuery.OrderByDescending(r => r.RoleName),
                            "description" => roleQuery.OrderByDescending(r => r.Description),
                            "createdat" => roleQuery.OrderByDescending(r => r.CreatedAt),
                            _ => roleQuery.OrderByDescending(r => r.CreatedAt),
                        };
                    }
                }
                else
                {
                    roleQuery = roleQuery.OrderByDescending(r => r.CreatedAt);
                }

                // 获取总数
                var total = await roleQuery.CountAsync();

                // 分页查询
                var roles = await roleQuery
                    .Skip((query.Page - 1) * query.PageSize)
                    .Take(query.PageSize)
                    .Select(r => new RoleDto
                    {
                        UserCount = SqlFunc
                            .Subqueryable<UserRole>()
                            .Where(ur => ur.RoleGUID == r.RoleGUID)
                            .Count(),
                        RoleGUID = r.RoleGUID,
                        RoleName = r.RoleName,
                        Description = r.Description,
                        IsActive = r.IsActive,
                        CreatedAt = r.CreatedAt,
                        UpdatedAt = r.UpdatedAt ?? r.CreatedAt, // 处理可空DateTime
                    })
                    .ToListAsync();

                var result = new PagedResult<RoleDto>
                {
                    Items = roles,
                    Total = total,
                    Page = query.Page,
                    PageSize = query.PageSize,
                };

                return ApiResponse<PagedResult<RoleDto>>.OK(result, "获取角色列表成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取角色列表失败");
                return ApiResponse<PagedResult<RoleDto>>.Error(
                    "获取角色列表失败",
                    "GET_ROLES_FAILED"
                );
            }
        }

        /// <summary>
        /// 获取角色列表（高性能版本 - 使用复合查询）
        /// ⚡ 优化查询性能，解决N+1问题，使用单一复合查询
        /// </summary>
        public async Task<ApiResponse<PagedResult<RoleDto>>> GetRolesOptimizedAsync(
            RoleQueryDto query
        )
        {
            try
            {
                var db = _context.Db;

                // 构建复合查询，一次性获取角色和用户数量信息
                var baseQuery = db.Queryable<Role>()
                    .LeftJoin<UserRole>((r, ur) => r.RoleGUID == ur.RoleGUID);

                // 搜索条件
                if (!string.IsNullOrEmpty(query.SearchKeyword))
                {
                    baseQuery = baseQuery.Where(
                        (r, ur) =>
                            r.RoleName.Contains(query.SearchKeyword)
                            || (
                                r.Description != null && r.Description.Contains(query.SearchKeyword)
                            )
                    );
                }

                // 状态筛选
                if (query.IsActive.HasValue)
                {
                    baseQuery = baseQuery.Where((r, ur) => r.IsActive == query.IsActive.Value);
                }

                // 获取所有角色数据和用户统计
                var roleDataList = await baseQuery
                    .Select(
                        (r, ur) =>
                            new
                            {
                                Role = new RoleDto
                                {
                                    RoleGUID = r.RoleGUID,
                                    RoleName = r.RoleName,
                                    Description = r.Description,
                                    IsActive = r.IsActive,
                                    CreatedAt = r.CreatedAt,
                                    UpdatedAt = r.UpdatedAt ?? r.CreatedAt,
                                },
                                HasUser = ur.UserGUID != null,
                            }
                    )
                    .ToListAsync();

                // 合并相同角色的数据并统计用户数量
                var roleDict = new Dictionary<string, RoleDto>();
                var userCounts = new Dictionary<string, int>();

                foreach (var item in roleDataList)
                {
                    var roleGuid = item.Role.RoleGUID;

                    // 添加角色基本信息（只添加一次）
                    if (!roleDict.ContainsKey(roleGuid))
                    {
                        roleDict[roleGuid] = item.Role;
                        userCounts[roleGuid] = 0;
                    }

                    // 统计用户数量
                    if (item.HasUser)
                    {
                        userCounts[roleGuid]++;
                    }
                }

                // 将去重后的角色转换为列表并设置用户数量
                var distinctRoles = roleDict.Values.ToList();
                foreach (var role in distinctRoles)
                {
                    role.UserCount = userCounts.GetValueOrDefault(role.RoleGUID, 0);
                }

                // 排序
                if (!string.IsNullOrEmpty(query.SortBy))
                {
                    if (query.SortDirection?.ToLower() == "asc")
                    {
                        distinctRoles = query.SortBy.ToLower() switch
                        {
                            "rolename" => distinctRoles.OrderBy(r => r.RoleName).ToList(),
                            "createdat" => distinctRoles.OrderBy(r => r.CreatedAt).ToList(),
                            "usercount" => distinctRoles.OrderBy(r => r.UserCount).ToList(),
                            _ => distinctRoles.OrderBy(r => r.CreatedAt).ToList(),
                        };
                    }
                    else
                    {
                        distinctRoles = query.SortBy.ToLower() switch
                        {
                            "rolename" => distinctRoles.OrderByDescending(r => r.RoleName).ToList(),
                            "createdat" => distinctRoles
                                .OrderByDescending(r => r.CreatedAt)
                                .ToList(),
                            "usercount" => distinctRoles
                                .OrderByDescending(r => r.UserCount)
                                .ToList(),
                            _ => distinctRoles.OrderByDescending(r => r.CreatedAt).ToList(),
                        };
                    }
                }
                else
                {
                    distinctRoles = distinctRoles.OrderByDescending(r => r.CreatedAt).ToList();
                }

                // 分页
                var total = distinctRoles.Count;
                var pagedRoles = distinctRoles
                    .Skip((query.Page - 1) * query.PageSize)
                    .Take(query.PageSize)
                    .ToList();

                var result = new PagedResult<RoleDto>
                {
                    Items = pagedRoles,
                    Total = total,
                    Page = query.Page,
                    PageSize = query.PageSize,
                };

                return ApiResponse<PagedResult<RoleDto>>.OK(result, "获取角色列表成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取角色列表失败");
                return ApiResponse<PagedResult<RoleDto>>.Error(
                    "获取角色列表失败",
                    "GET_ROLES_FAILED"
                );
            }
        }

        /// <summary>
        /// 角色服务性能测试方法 - 比较不同查询方式的性能
        /// </summary>
        public async Task<ApiResponse<object>> PerformanceTestAsync(RoleQueryDto query)
        {
            try
            {
                // 测试原始方法
                var sw1 = System.Diagnostics.Stopwatch.StartNew();
                var result1 = await GetRolesAsync(query);
                sw1.Stop();

                // 测试优化方法
                var sw2 = System.Diagnostics.Stopwatch.StartNew();
                var result2 = await GetRolesOptimizedAsync(query);
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
                        HasStatusFilter = query.IsActive.HasValue,
                        TestTimestamp = DateTime.Now,
                    },
                };

                return ApiResponse<object>.OK(performanceReport, "角色服务性能测试完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "角色服务性能测试失败");
                return ApiResponse<object>.Error(
                    "角色服务性能测试失败",
                    "ROLE_PERFORMANCE_TEST_FAILED"
                );
            }
        }

        /// <summary>
        /// 获取所有活跃角色（不分页）
        /// </summary>
        public async Task<ApiResponse<List<RoleDto>>> GetActiveRolesAsync()
        {
            try
            {
                var db = _context.Db;

                var roles = await db.Queryable<Role>()
                    .Where(r => r.IsActive)
                    .OrderBy(r => r.RoleName)
                    .Select(r => new RoleDto
                    {
                        RoleGUID = r.RoleGUID,
                        RoleName = r.RoleName,
                        Description = r.Description,
                        IsActive = r.IsActive,
                        CreatedAt = r.CreatedAt,
                        UpdatedAt = r.UpdatedAt ?? r.CreatedAt,
                    })
                    .ToListAsync();

                return ApiResponse<List<RoleDto>>.OK(roles, "获取活跃角色成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取活跃角色失败");
                return ApiResponse<List<RoleDto>>.Error(
                    "获取活跃角色失败",
                    "GET_ACTIVE_ROLES_FAILED"
                );
            }
        }

        /// <summary>
        /// 根据GUID获取角色详情
        /// </summary>
        public async Task<ApiResponse<RoleDetailDto>> GetRoleByGuidAsync(string roleGuid)
        {
            try
            {
                var db = _context.Db;
                var role = await db.Queryable<Role>()
                    .Where(r => r.RoleGUID == roleGuid)
                    .FirstAsync();

                if (role == null)
                {
                    return ApiResponse<RoleDetailDto>.Error("角色不存在", "ROLE_NOT_FOUND");
                }

                var roleDetail = new RoleDetailDto
                {
                    RoleGUID = role.RoleGUID,
                    RoleName = role.RoleName,
                    Description = role.Description,
                    IsActive = role.IsActive,
                    CreatedAt = role.CreatedAt,
                    UpdatedAt = role.UpdatedAt ?? role.CreatedAt,
                };

                // 获取角色用户详情
                var users = await db.Queryable<UserRole>()
                    .InnerJoin<User>((ur, u) => ur.UserGUID == u.UserGUID)
                    .Where((ur, u) => ur.RoleGUID == roleGuid)
                    .Select(
                        (ur, u) =>
                            new RoleUserDto
                            {
                                UserGUID = u.UserGUID,
                                Username = u.Username,
                                Email = u.Email,
                                FullName = u.FullName,
                                IsActive = u.IsActive,
                                AssignedAt = ur.CreatedAt,
                            }
                    )
                    .ToListAsync();
                roleDetail.Users = users;

                // 获取用户数量
                roleDetail.UserCount = users.Count;

                // 从 SysRolePermission 表读取角色的权限代码列表
                var rolePermissions = await db.Queryable<SysRolePermission>()
                    .Where(rp => rp.RoleGuid == roleGuid && rp.IsDeleted == false)
                    .Select(rp => rp.PermissionCode)
                    .ToListAsync();
                roleDetail.Permissions = rolePermissions;

                return ApiResponse<RoleDetailDto>.OK(roleDetail, "获取角色详情成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取角色详情失败，RoleGUID: {RoleGUID}", roleGuid);
                return ApiResponse<RoleDetailDto>.Error(
                    "获取角色详情失败",
                    "GET_ROLE_DETAIL_FAILED"
                );
            }
        }

        /// <summary>
        /// 根据角色名获取角色
        /// </summary>
        public async Task<ApiResponse<RoleDto>> GetRoleByNameAsync(string roleName)
        {
            try
            {
                var db = _context.Db;
                var role = await db.Queryable<Role>()
                    .Where(r => r.RoleName == roleName)
                    .Select(r => new RoleDto
                    {
                        RoleGUID = r.RoleGUID,
                        RoleName = r.RoleName,
                        Description = r.Description,
                        IsActive = r.IsActive,
                        CreatedAt = r.CreatedAt,
                        UpdatedAt = r.UpdatedAt ?? r.CreatedAt,
                    })
                    .FirstAsync();

                if (role == null)
                {
                    return ApiResponse<RoleDto>.Error("角色不存在", "ROLE_NOT_FOUND");
                }

                return ApiResponse<RoleDto>.OK(role, "获取角色成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据角色名获取角色失败，RoleName: {RoleName}", roleName);
                return ApiResponse<RoleDto>.Error("获取角色失败", "GET_ROLE_BY_NAME_FAILED");
            }
        }

        /// <summary>
        /// 创建新角色
        /// </summary>
        public async Task<ApiResponse<RoleDto>> CreateRoleAsync(CreateRoleDto dto)
        {
            try
            {
                var db = _context.Db;

                // 检查角色名是否已存在
                var existingRole = await db.Queryable<Role>()
                    .Where(r => r.RoleName == dto.RoleName)
                    .FirstAsync();

                if (existingRole != null)
                {
                    return ApiResponse<RoleDto>.Error("角色名已存在", "ROLE_NAME_EXISTS");
                }

                // 创建角色
                var role = new Role
                {
                    RoleGUID = Guid.NewGuid().ToString(),
                    RoleName = dto.RoleName,
                    Description = dto.Description,
                    IsActive = dto.IsActive,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                };

                await db.Insertable(role).ExecuteCommandAsync();

                var result = new RoleDto
                {
                    RoleGUID = role.RoleGUID,
                    RoleName = role.RoleName,
                    Description = role.Description,
                    IsActive = role.IsActive,
                    CreatedAt = role.CreatedAt,
                    UpdatedAt = role.UpdatedAt ?? role.CreatedAt,
                    UserCount = 0,
                };

                _logger.LogInformation(
                    "创建角色成功，RoleGUID: {RoleGUID}, RoleName: {RoleName}",
                    role.RoleGUID,
                    role.RoleName
                );
                return ApiResponse<RoleDto>.OK(result, "创建角色成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建角色失败，RoleName: {RoleName}", dto.RoleName);
                return ApiResponse<RoleDto>.Error("创建角色失败", "CREATE_ROLE_FAILED");
            }
        }

        /// <summary>
        /// 根据GUID更新角色
        /// </summary>
        public async Task<ApiResponse<RoleDto>> UpdateRoleByGuidAsync(
            string roleGuid,
            UpdateRoleDto dto
        )
        {
            try
            {
                var db = _context.Db;

                // 检查角色是否存在
                var role = await db.Queryable<Role>()
                    .Where(r => r.RoleGUID == roleGuid)
                    .FirstAsync();

                if (role == null)
                {
                    return ApiResponse<RoleDto>.Error("角色不存在", "ROLE_NOT_FOUND");
                }

                // 检查角色名是否被其他角色使用
                var existingRole = await db.Queryable<Role>()
                    .Where(r => r.RoleGUID != roleGuid && r.RoleName == dto.RoleName)
                    .FirstAsync();

                if (existingRole != null)
                {
                    return ApiResponse<RoleDto>.Error("角色名已被其他角色使用", "ROLE_NAME_EXISTS");
                }

                // 更新角色信息
                role.RoleName = dto.RoleName;
                role.Description = dto.Description;
                role.IsActive = dto.IsActive;
                role.UpdatedAt = DateTime.UtcNow;

                await db.Updateable(role).ExecuteCommandAsync();

                var result = new RoleDto
                {
                    RoleGUID = role.RoleGUID,
                    RoleName = role.RoleName,
                    Description = role.Description,
                    IsActive = role.IsActive,
                    CreatedAt = role.CreatedAt,
                    UpdatedAt = role.UpdatedAt ?? role.CreatedAt,
                };

                _logger.LogInformation("更新角色成功，RoleGUID: {RoleGUID}", roleGuid);
                return ApiResponse<RoleDto>.OK(result, "更新角色成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新角色失败，RoleGUID: {RoleGUID}", roleGuid);
                return ApiResponse<RoleDto>.Error("更新角色失败", "UPDATE_ROLE_FAILED");
            }
        }

        /// <summary>
        /// 根据GUID删除角色
        /// </summary>
        public async Task<ApiResponse<bool>> DeleteRoleByGuidAsync(string roleGuid)
        {
            try
            {
                var db = _context.Db;

                // 检查角色是否存在
                var role = await db.Queryable<Role>()
                    .Where(r => r.RoleGUID == roleGuid)
                    .FirstAsync();

                if (role == null)
                {
                    return ApiResponse<bool>.Error("角色不存在", "ROLE_NOT_FOUND");
                }

                // 检查是否有用户正在使用该角色
                var hasUsers = await db.Queryable<UserRole>()
                    .Where(ur => ur.RoleGUID == roleGuid)
                    .AnyAsync();

                if (hasUsers)
                {
                    return ApiResponse<bool>.Error("角色正在被使用，无法删除", "ROLE_IN_USE");
                }

                // 开启事务
                await db.Ado.BeginTranAsync();

                try
                {
                    // 删除角色用户关联（如果有的话）
                    await db.Deleteable<UserRole>()
                        .Where(ur => ur.RoleGUID == roleGuid)
                        .ExecuteCommandAsync();

                    // 删除角色
                    await db.Deleteable<Role>()
                        .Where(r => r.RoleGUID == roleGuid)
                        .ExecuteCommandAsync();

                    await db.Ado.CommitTranAsync();

                    _logger.LogInformation("删除角色成功，RoleGUID: {RoleGUID}", roleGuid);
                    return ApiResponse<bool>.OK(true, "删除角色成功");
                }
                catch
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除角色失败，RoleGUID: {RoleGUID}", roleGuid);
                return ApiResponse<bool>.Error("删除角色失败", "DELETE_ROLE_FAILED");
            }
        }

        /// <summary>
        /// 根据GUID更新角色状态
        /// </summary>
        public async Task<ApiResponse<bool>> UpdateRoleStatusByGuidAsync(
            string roleGuid,
            bool isActive
        )
        {
            try
            {
                var db = _context.Db;

                var result = await db.Updateable<Role>()
                    .SetColumns(r => new Role { IsActive = isActive, UpdatedAt = DateTime.UtcNow })
                    .Where(r => r.RoleGUID == roleGuid)
                    .ExecuteCommandAsync();

                if (result == 0)
                {
                    return ApiResponse<bool>.Error("角色不存在", "ROLE_NOT_FOUND");
                }

                _logger.LogInformation(
                    "更新角色状态成功，RoleGUID: {RoleGUID}, IsActive: {IsActive}",
                    roleGuid,
                    isActive
                );
                return ApiResponse<bool>.OK(
                    true,
                    $"角色状态已更新为{(isActive ? "激活" : "禁用")}"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新角色状态失败，RoleGUID: {RoleGUID}", roleGuid);
                return ApiResponse<bool>.Error("更新角色状态失败", "UPDATE_ROLE_STATUS_FAILED");
            }
        }

        /// <summary>
        /// 获取角色的用户列表
        /// </summary>
        public async Task<ApiResponse<PagedResult<RoleUserDto>>> GetRoleUsersAsync(
            string roleGuid,
            RoleQueryDto query
        )
        {
            try
            {
                var db = _context.Db;
                var userQuery = db.Queryable<UserRole>()
                    .InnerJoin<User>((ur, u) => ur.UserGUID == u.UserGUID)
                    .Where((ur, u) => ur.RoleGUID == roleGuid);

                // 搜索条件
                if (!string.IsNullOrEmpty(query.SearchKeyword))
                {
                    userQuery = userQuery.Where(
                        (ur, u) =>
                            u.Username.Contains(query.SearchKeyword)
                            || u.Email.Contains(query.SearchKeyword)
                            || (u.FullName != null && u.FullName.Contains(query.SearchKeyword))
                    );
                }

                // 状态筛选
                if (query.IsActive.HasValue)
                {
                    userQuery = userQuery.Where((ur, u) => u.IsActive == query.IsActive.Value);
                }

                // 获取总数
                var total = await userQuery.CountAsync();

                // 分页查询
                var users = await userQuery
                    .OrderByDescending((ur, u) => ur.CreatedAt)
                    .Select(
                        (ur, u) =>
                            new RoleUserDto
                            {
                                UserGUID = u.UserGUID,
                                Username = u.Username,
                                Email = u.Email,
                                FullName = u.FullName,
                                IsActive = u.IsActive,
                                AssignedAt = ur.CreatedAt,
                            }
                    )
                    .Skip((query.Page - 1) * query.PageSize)
                    .Take(query.PageSize)
                    .ToListAsync();

                var result = new PagedResult<RoleUserDto>
                {
                    Items = users,
                    Total = total,
                    Page = query.Page,
                    PageSize = query.PageSize,
                };

                return ApiResponse<PagedResult<RoleUserDto>>.OK(result, "获取角色用户列表成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取角色用户列表失败，RoleGUID: {RoleGUID}", roleGuid);
                return ApiResponse<PagedResult<RoleUserDto>>.Error(
                    "获取角色用户列表失败",
                    "GET_ROLE_USERS_FAILED"
                );
            }
        }

        /// <summary>
        /// 为角色添加用户
        /// </summary>
        public async Task<ApiResponse<bool>> AddUsersToRoleAsync(
            string roleGuid,
            List<string> userGuids
        )
        {
            try
            {
                var db = _context.Db;

                // 检查角色是否存在
                var roleExists = await db.Queryable<Role>()
                    .Where(r => r.RoleGUID == roleGuid)
                    .AnyAsync();

                if (!roleExists)
                {
                    return ApiResponse<bool>.Error("角色不存在", "ROLE_NOT_FOUND");
                }

                // 获取已存在的用户角色关联
                var existingUserRoles = await db.Queryable<UserRole>()
                    .Where(ur => ur.RoleGUID == roleGuid && userGuids.Contains(ur.UserGUID))
                    .Select(ur => ur.UserGUID)
                    .ToListAsync();

                // 筛选出需要添加的用户
                var newUserGuids = userGuids.Except(existingUserRoles).ToList();

                if (newUserGuids.Any())
                {
                    var userRoles = newUserGuids
                        .Select(userGuid => new UserRole
                        {
                            UserGUID = userGuid,
                            RoleGUID = roleGuid,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                        })
                        .ToList();

                    await db.Insertable(userRoles).ExecuteCommandAsync();
                }

                _logger.LogInformation(
                    "为角色添加用户成功，RoleGUID: {RoleGUID}, AddedCount: {AddedCount}",
                    roleGuid,
                    newUserGuids.Count
                );
                return ApiResponse<bool>.OK(true, $"成功添加 {newUserGuids.Count} 个用户到角色");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "为角色添加用户失败，RoleGUID: {RoleGUID}", roleGuid);
                return ApiResponse<bool>.Error("添加用户失败", "ADD_USERS_TO_ROLE_FAILED");
            }
        }

        /// <summary>
        /// 从角色移除用户
        /// </summary>
        public async Task<ApiResponse<bool>> RemoveUserFromRoleAsync(
            string roleGuid,
            string userGuid
        )
        {
            try
            {
                var db = _context.Db;

                var result = await db.Deleteable<UserRole>()
                    .Where(ur => ur.RoleGUID == roleGuid && ur.UserGUID == userGuid)
                    .ExecuteCommandAsync();

                if (result == 0)
                {
                    return ApiResponse<bool>.Error("用户角色关联不存在", "USER_ROLE_NOT_FOUND");
                }

                _logger.LogInformation(
                    "从角色移除用户成功，RoleGUID: {RoleGUID}, UserGUID: {UserGUID}",
                    roleGuid,
                    userGuid
                );
                return ApiResponse<bool>.OK(true, "移除用户成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "从角色移除用户失败，RoleGUID: {RoleGUID}, UserGUID: {UserGUID}",
                    roleGuid,
                    userGuid
                );
                return ApiResponse<bool>.Error("移除用户失败", "REMOVE_USER_FROM_ROLE_FAILED");
            }
        }

        /// <summary>
        /// 批量管理角色
        /// </summary>
        public async Task<ApiResponse<bool>> BatchManageRolesAsync(BatchRoleOperationDto dto)
        {
            try
            {
                var db = _context.Db;

                switch (dto.Operation.ToLower())
                {
                    case "activate":
                        await db.Updateable<Role>()
                            .SetColumns(r => new Role
                            {
                                IsActive = true,
                                UpdatedAt = DateTime.Now,
                            })
                            .Where(r => dto.RoleGuids.Contains(r.RoleGUID))
                            .ExecuteCommandAsync();
                        break;

                    case "deactivate":
                        await db.Updateable<Role>()
                            .SetColumns(r => new Role
                            {
                                IsActive = false,
                                UpdatedAt = DateTime.Now,
                            })
                            .Where(r => dto.RoleGuids.Contains(r.RoleGUID))
                            .ExecuteCommandAsync();
                        break;

                    case "delete":
                        // 检查是否有角色正在被使用
                        var rolesInUse = await db.Queryable<UserRole>()
                            .Where(ur => dto.RoleGuids.Contains(ur.RoleGUID))
                            .Select(ur => ur.RoleGUID)
                            .ToListAsync();

                        if (rolesInUse.Any())
                        {
                            return ApiResponse<bool>.Error(
                                "部分角色正在被使用，无法删除",
                                "ROLES_IN_USE"
                            );
                        }

                        // 开启事务
                        await db.Ado.BeginTranAsync();
                        try
                        {
                            // 删除角色用户关联
                            await db.Deleteable<UserRole>()
                                .Where(ur => dto.RoleGuids.Contains(ur.RoleGUID))
                                .ExecuteCommandAsync();

                            // 删除角色
                            await db.Deleteable<Role>()
                                .Where(r => dto.RoleGuids.Contains(r.RoleGUID))
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
                    "批量管理角色成功，Operation: {Operation}, RoleCount: {RoleCount}",
                    dto.Operation,
                    dto.RoleGuids.Count
                );
                return ApiResponse<bool>.OK(true, $"批量{dto.Operation}操作成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量管理角色失败，Operation: {Operation}", dto.Operation);
                return ApiResponse<bool>.Error("批量操作失败", "BATCH_OPERATION_FAILED");
            }
        }

        /// <summary>
        /// 获取角色统计信息
        /// </summary>
        public async Task<ApiResponse<RoleStatisticsDto>> GetRoleStatisticsAsync()
        {
            try
            {
                var db = _context.Db;

                var statistics = new RoleStatisticsDto
                {
                    TotalRoles = await db.Queryable<Role>().CountAsync(),
                    ActiveRoles = await db.Queryable<Role>().Where(r => r.IsActive).CountAsync(),
                    InactiveRoles = await db.Queryable<Role>().Where(r => !r.IsActive).CountAsync(),
                };

                // 获取角色使用情况
                var roleUsage = await db.Queryable<Role>()
                    .LeftJoin<UserRole>((r, ur) => r.RoleGUID == ur.RoleGUID)
                    .GroupBy((r, ur) => new { r.RoleGUID, r.RoleName })
                    .Select(
                        (r, ur) =>
                            new RoleUsageDto
                            {
                                RoleGUID = r.RoleGUID,
                                RoleName = r.RoleName,
                                UserCount = SqlFunc.AggregateCount(ur.UserGUID),
                                IsSystemRole = false, // 暂时设为false，可以根据实际需求调整
                            }
                    )
                    .ToListAsync();

                statistics.RoleUsage = roleUsage;

                return ApiResponse<RoleStatisticsDto>.OK(statistics, "获取角色统计成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取角色统计失败");
                return ApiResponse<RoleStatisticsDto>.Error(
                    "获取角色统计失败",
                    "GET_ROLE_STATISTICS_FAILED"
                );
            }
        }

        /// <summary>
        /// 检查角色名是否可用
        /// </summary>
        public async Task<ApiResponse<bool>> IsRoleNameAvailableAsync(
            string roleName,
            string? excludeRoleGuid = null
        )
        {
            try
            {
                var db = _context.Db;
                var query = db.Queryable<Role>().Where(r => r.RoleName == roleName);

                if (!string.IsNullOrEmpty(excludeRoleGuid))
                {
                    query = query.Where(r => r.RoleGUID != excludeRoleGuid);
                }

                var exists = await query.AnyAsync();
                return ApiResponse<bool>.OK(!exists, exists ? "角色名已存在" : "角色名可用");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查角色名可用性失败，RoleName: {RoleName}", roleName);
                return ApiResponse<bool>.Error("检查角色名失败", "CHECK_ROLE_NAME_FAILED");
            }
        }

        /// <summary>
        /// 获取所有权限列表
        /// </summary>
        public async Task<ApiResponse<List<PermissionCategoryDto>>> GetPermissionsAsync()
        {
            try
            {
                var db = _context.Db;
                var permissions = await db.Queryable<SysPermission>().Where(p => !p.IsDeleted).ToListAsync();

                var result = permissions
                    .GroupBy(p => p.Category)
                    .Select(g => new PermissionCategoryDto
                    {
                        Category = g.Key,
                        DisplayName = g.Key, // 暂时使用Category作为显示名，实际可以加字典表
                        Description = $"{g.Key}相关权限",
                        Permissions = g.Select(p => new PermissionDto
                        {
                            Name = p.Code,
                            DisplayName = p.Name,
                            Description = p.Description,
                            Category = p.Category,
                            IsSystemPermission = false, // 从数据库加载的权限默认为自定义权限
                            CreatedAt = p.CreatedAt,
                            CreatedBy = p.CreatedBy,
                            UpdatedAt = p.UpdatedAt,
                            UpdatedBy = p.UpdatedBy,
                        })
                            .ToList(),
                    })
                    .ToList();

                return ApiResponse<List<PermissionCategoryDto>>.OK(result, "获取权限列表成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取权限列表失败");
                return ApiResponse<List<PermissionCategoryDto>>.Error(
                    "获取权限列表失败",
                    "GET_PERMISSIONS_FAILED"
                );
            }
        }

        public async Task<ApiResponse<PermissionCatalogDto>> GetPermissionCatalogAsync()
        {
            try
            {
                var permissions = await GetPermissionsAsync();
                var catalog = new PermissionCatalogDto
                {
                    Categories = permissions.Data ?? new List<PermissionCategoryDto>(),
                    PermissionAliases = Permissions.GetPermissionAliases()
                        .Select(item => new PermissionAliasDto
                        {
                            CanonicalCode = item.Key,
                            AliasCodes = item.Value.ToList(),
                        })
                        .ToList(),
                    RoleTemplates = PermissionSeedData.RolePermissionTemplates
                        .Select(item => new RolePermissionTemplateDto
                        {
                            RoleName = item.RoleName,
                            PermissionCodes = item.PermissionCodes.ToList(),
                        })
                        .ToList(),
                    SuperAdminRoleNames = Permissions.SuperAdminRoleNames.ToList(),
                };

                return ApiResponse<PermissionCatalogDto>.OK(catalog, "获取权限目录成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取权限目录失败");
                return ApiResponse<PermissionCatalogDto>.Error(
                    "获取权限目录失败",
                    "GET_PERMISSION_CATALOG_FAILED"
                );
            }
        }

        /// <summary>
        /// 获取角色的权限列表
        /// </summary>
        public async Task<ApiResponse<List<string>>> GetRolePermissionsAsync(string roleGuid)
        {
            try
            {
                var db = _context.Db;
                var role = await db.Queryable<Role>()
                    .Where(r => r.RoleGUID == roleGuid && !r.IsDeleted)
                    .FirstAsync();

                if (role == null)
                {
                    return ApiResponse<List<string>>.OK(new List<string>(), "获取角色权限成功");
                }

                if (IsSuperAdminRoleName(role.RoleName))
                {
                    var allPermissions = await db.Queryable<SysPermission>()
                        .Where(p => !p.IsDeleted)
                        .Select(p => p.Code)
                        .ToListAsync();

                    return ApiResponse<List<string>>.OK(
                        allPermissions.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        "Admin 默认拥有所有权限"
                    );
                }

                var permissions = await db.Queryable<SysRolePermission>()
                    .Where(rp => rp.RoleGuid == roleGuid && rp.IsDeleted == false)
                    .Select(rp => rp.PermissionCode)
                    .ToListAsync();

                return ApiResponse<List<string>>.OK(permissions, "获取角色权限成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取角色权限失败，RoleGUID: {RoleGUID}", roleGuid);
                return ApiResponse<List<string>>.Error(
                    "获取角色权限失败",
                    "GET_ROLE_PERMISSIONS_FAILED"
                );
            }
        }

        public async Task<ApiResponse<RolePermissionStateDto>> GetRolePermissionStateAsync(
            string roleGuid
        )
        {
            try
            {
                var db = _context.Db;
                var role = await db.Queryable<Role>()
                    .Where(item => item.RoleGUID == roleGuid && !item.IsDeleted)
                    .FirstAsync();

                if (role == null)
                {
                    return ApiResponse<RolePermissionStateDto>.Error(
                        "角色不存在",
                        "ROLE_NOT_FOUND"
                    );
                }

                var explicitCodes = await db.Queryable<SysRolePermission>()
                    .Where(item => item.RoleGuid == roleGuid && !item.IsDeleted)
                    .Select(item => item.PermissionCode)
                    .ToListAsync();

                var effectivePermissions = await GetRolePermissionsAsync(roleGuid);
                var isSuperAdmin = IsSuperAdminRoleName(role.RoleName);

                return ApiResponse<RolePermissionStateDto>.OK(
                    new RolePermissionStateDto
                    {
                        RoleGuid = role.RoleGUID,
                        RoleName = role.RoleName,
                        IsSuperAdmin = isSuperAdmin,
                        ImplicitAllPermissions = isSuperAdmin,
                        ExplicitPermissionCodes = explicitCodes
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                        EffectivePermissionCodes =
                            effectivePermissions.Data ?? new List<string>(),
                    },
                    "获取角色权限状态成功"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取角色权限状态失败，RoleGUID: {RoleGUID}", roleGuid);
                return ApiResponse<RolePermissionStateDto>.Error(
                    "获取角色权限状态失败",
                    "GET_ROLE_PERMISSION_STATE_FAILED"
                );
            }
        }

        /// <summary>
        /// 为角色分配权限
        /// </summary>
        public async Task<ApiResponse<bool>> AssignPermissionsToRoleAsync(
            string roleGuid,
            RolePermissionAssignmentDto dto
        )
        {
            try
            {
                var db = _context.Db;
                var role = await db.Queryable<Role>()
                    .Where(r => r.RoleGUID == roleGuid && !r.IsDeleted)
                    .FirstAsync();

                await db.Ado.BeginTranAsync();
                try
                {
                    if (role != null && IsSuperAdminRoleName(role.RoleName))
                    {
                        await db.Deleteable<SysRolePermission>()
                            .Where(rp => rp.RoleGuid == roleGuid)
                            .ExecuteCommandAsync();

                        await db.Ado.CommitTranAsync();

                        _logger.LogInformation(
                            "跳过 Admin 显式权限写入，RoleGUID: {RoleGUID}",
                            roleGuid
                        );
                        return ApiResponse<bool>.OK(true, "Admin 默认拥有所有权限，无需分配");
                    }

                    // 1. 删除旧权限
                    await db.Deleteable<SysRolePermission>()
                        .Where(rp => rp.RoleGuid == roleGuid)
                        .ExecuteCommandAsync();

                    // 2. 添加新权限
                    if (dto.Permissions != null && dto.Permissions.Any())
                    {
                        var newPermissions = dto
                            .Permissions.Select(code => new SysRolePermission
                            {
                                Id = UuidHelper.GenerateUuid7(),
                                RoleGuid = roleGuid,
                                PermissionCode = code,
                                CreatedAt = DateTime.Now,
                                CreatedBy = GetCurrentUsername(),
                                UpdatedAt = DateTime.Now,
                                UpdatedBy = GetCurrentUsername(),
                                IsDeleted = false
                            })
                            .ToList();

                        await db.Insertable(newPermissions).ExecuteCommandAsync();
                    }

                    await db.Ado.CommitTranAsync();

                    _logger.LogInformation(
                        "为角色分配权限成功，RoleGUID: {RoleGUID}, PermissionCount: {PermissionCount}",
                        roleGuid,
                        dto.Permissions?.Count ?? 0
                    );
                    return ApiResponse<bool>.OK(true, "权限分配成功");
                }
                catch
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "为角色分配权限失败，RoleGUID: {RoleGUID}", roleGuid);
                return ApiResponse<bool>.Error("权限分配失败", "ASSIGN_PERMISSIONS_FAILED");
            }
        }

        /// <summary>
        /// 检查用户是否有指定角色
        /// </summary>
        public async Task<ApiResponse<bool>> UserHasRoleAsync(string userGuid, string roleName)
        {
            try
            {
                var db = _context.Db;

                var hasRole = await db.Queryable<UserRole>()
                    .InnerJoin<User>((ur, u) => ur.UserGUID == u.UserGUID)
                    .InnerJoin<Role>((ur, u, r) => ur.RoleGUID == r.RoleGUID)
                    .Where(
                        (ur, u, r) =>
                            ur.UserGUID == userGuid
                            && !ur.IsDeleted
                            && u.IsActive
                            && !u.IsDeleted
                            && r.RoleName == roleName
                            && r.IsActive
                            && !r.IsDeleted
                    )
                    .AnyAsync();

                return ApiResponse<bool>.OK(hasRole, hasRole ? "用户拥有该角色" : "用户没有该角色");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "检查用户角色失败，UserGUID: {UserGUID}, RoleName: {RoleName}",
                    userGuid,
                    roleName
                );
                return ApiResponse<bool>.Error("检查用户角色失败", "CHECK_USER_ROLE_FAILED");
            }
        }

        /// <summary>
        /// 检查用户是否有指定权限
        /// </summary>
        public async Task<ApiResponse<bool>> UserHasPermissionAsync(
            string userGuid,
            string permission
        )
        {
            try
            {
                var db = _context.Db;

                var superAdminRoleNames = Permissions.SuperAdminRoleNames.ToList();
                var hasSuperAdminRole = await db.Queryable<UserRole>()
                    .InnerJoin<User>((ur, u) => ur.UserGUID == u.UserGUID)
                    .InnerJoin<Role>((ur, u, r) => ur.RoleGUID == r.RoleGUID)
                    .Where(
                        (ur, u, r) =>
                            ur.UserGUID == userGuid
                            && !ur.IsDeleted
                            && u.IsActive
                            && !u.IsDeleted
                            && r.IsActive
                            && !r.IsDeleted
                            && superAdminRoleNames.Contains(r.RoleName)
                    )
                    .AnyAsync();

                if (hasSuperAdminRole)
                {
                    return ApiResponse<bool>.OK(true, "Admin 默认拥有所有权限");
                }

                var equivalentPermissionCodes = Permissions.GetEquivalentPermissionCodes(permission)
                    .ToList();
                if (!equivalentPermissionCodes.Any())
                {
                    return ApiResponse<bool>.OK(false, "用户没有该权限");
                }

                // 链路: User -> UserRole -> Role -> SysRolePermission
                // 只要有一个角色拥有该权限即可
                var hasPermission = await db.Queryable<UserRole>()
                    .InnerJoin<User>((ur, u) => ur.UserGUID == u.UserGUID)
                    .InnerJoin<Role>((ur, u, r) => ur.RoleGUID == r.RoleGUID)
                    .InnerJoin<SysRolePermission>((ur, u, r, rp) => ur.RoleGUID == rp.RoleGuid)
                    .Where(
                        (ur, u, r, rp) =>
                            ur.UserGUID == userGuid
                            && !ur.IsDeleted
                            && u.IsActive
                            && !u.IsDeleted
                            && r.IsActive
                            && !r.IsDeleted
                            && !rp.IsDeleted
                            && equivalentPermissionCodes.Contains(rp.PermissionCode)
                    )
                    .AnyAsync();

                return ApiResponse<bool>.OK(
                    hasPermission,
                    hasPermission ? "用户拥有该权限" : "用户没有该权限"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "检查用户权限失败，UserGUID: {UserGUID}, Permission: {Permission}",
                    userGuid,
                    permission
                );
                return ApiResponse<bool>.Error("检查用户权限失败", "CHECK_USER_PERMISSION_FAILED");
            }
        }

        public async Task<ApiResponse<UserPermissionSnapshotDto>> GetUserPermissionSnapshotAsync(
            string userGuid
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userGuid))
                {
                    return ApiResponse<UserPermissionSnapshotDto>.OK(
                        new UserPermissionSnapshotDto(),
                        "用户权限快照为空"
                    );
                }

                var db = _context.Db;
                var roleEntries = await db.Queryable<UserRole>()
                    .InnerJoin<User>((ur, u) => ur.UserGUID == u.UserGUID)
                    .InnerJoin<Role>((ur, u, r) => ur.RoleGUID == r.RoleGUID)
                    .Where(
                        (ur, u, r) =>
                            ur.UserGUID == userGuid
                            && !ur.IsDeleted
                            && u.IsActive
                            && !u.IsDeleted
                            && r.IsActive
                            && !r.IsDeleted
                    )
                    .Select((ur, u, r) => new { r.RoleGUID, r.RoleName })
                    .ToListAsync();

                var roleNames = roleEntries
                    .Select(item => item.RoleName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var isSuperAdmin = roleNames.Any(IsSuperAdminRoleName);

                List<string> permissionCodes;
                if (isSuperAdmin)
                {
                    permissionCodes = await db.Queryable<SysPermission>()
                        .Where(item => !item.IsDeleted)
                        .Select(item => item.Code)
                        .ToListAsync();
                }
                else
                {
                    var roleGuids = roleEntries
                        .Select(item => item.RoleGUID)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var explicitCodes = roleGuids.Count == 0
                        ? new List<string>()
                        : await db.Queryable<SysRolePermission>()
                            .Where(item => roleGuids.Contains(item.RoleGuid) && !item.IsDeleted)
                            .Select(item => item.PermissionCode)
                            .ToListAsync();

                    permissionCodes = Permissions.ExpandPermissionCodes(explicitCodes).ToList();
                }

                return ApiResponse<UserPermissionSnapshotDto>.OK(
                    new UserPermissionSnapshotDto
                    {
                        UserGuid = userGuid,
                        IsSuperAdmin = isSuperAdmin,
                        RoleNames = roleNames,
                        PermissionCodes = permissionCodes
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                    },
                    isSuperAdmin ? "Admin 默认拥有所有权限" : "获取用户权限快照成功"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户权限快照失败，UserGUID: {UserGUID}", userGuid);
                return ApiResponse<UserPermissionSnapshotDto>.Error(
                    "获取用户权限快照失败",
                    "GET_USER_PERMISSION_SNAPSHOT_FAILED"
                );
            }
        }

        /// <summary>
        /// 复制角色
        /// </summary>
        public async Task<ApiResponse<RoleDto>> DuplicateRoleAsync(
            string sourceRoleGuid,
            string newRoleName,
            string? newDescription = null
        )
        {
            try
            {
                var db = _context.Db;

                // 获取源角色
                var sourceRole = await db.Queryable<Role>()
                    .Where(r => r.RoleGUID == sourceRoleGuid)
                    .FirstAsync();

                if (sourceRole == null)
                {
                    return ApiResponse<RoleDto>.Error("源角色不存在", "SOURCE_ROLE_NOT_FOUND");
                }

                // 检查新角色名是否已存在
                var existingRole = await db.Queryable<Role>()
                    .Where(r => r.RoleName == newRoleName)
                    .FirstAsync();

                if (existingRole != null)
                {
                    return ApiResponse<RoleDto>.Error("角色名已存在", "ROLE_NAME_EXISTS");
                }

                // 创建新角色
                var newRole = new Role
                {
                    RoleGUID = Guid.NewGuid().ToString(),
                    RoleName = newRoleName,
                    Description = newDescription ?? sourceRole.Description,
                    IsActive = sourceRole.IsActive,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                };

                await db.Insertable(newRole).ExecuteCommandAsync();

                var result = new RoleDto
                {
                    RoleGUID = newRole.RoleGUID,
                    RoleName = newRole.RoleName,
                    Description = newRole.Description,
                    IsActive = newRole.IsActive,
                    CreatedAt = newRole.CreatedAt,
                    UpdatedAt = newRole.UpdatedAt ?? newRole.CreatedAt,
                    UserCount = 0,
                };

                _logger.LogInformation(
                    "复制角色成功，SourceRoleGUID: {SourceRoleGUID}, NewRoleGUID: {NewRoleGUID}",
                    sourceRoleGuid,
                    newRole.RoleGUID
                );
                return ApiResponse<RoleDto>.OK(result, "复制角色成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "复制角色失败，SourceRoleGUID: {SourceRoleGUID}, NewRoleName: {NewRoleName}",
                    sourceRoleGuid,
                    newRoleName
                );
                return ApiResponse<RoleDto>.Error("复制角色失败", "DUPLICATE_ROLE_FAILED");
            }
        }

        /// <summary>
        /// 获取所有系统权限（扁平列表）
        /// </summary>
        public async Task<ApiResponse<List<SysPermission>>> GetSysPermissionsAsync()
        {
            try
            {
                var db = _context.Db;
                var list = await db.Queryable<SysPermission>()
                    .Where(p => p.IsDeleted == false)
                    .OrderBy(p => p.Category)
                    .OrderBy(p => p.Code)
                    .ToListAsync();
                return ApiResponse<List<SysPermission>>.OK(list, "获取系统权限列表成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取系统权限列表失败");
                return ApiResponse<List<SysPermission>>.Error(
                    "获取系统权限列表失败",
                    "GET_SYS_PERMISSIONS_FAILED"
                );
            }
        }

        /// <summary>
        /// 获取拥有指定权限的角色列表
        /// </summary>
        public async Task<ApiResponse<List<RoleDto>>> GetPermissionRolesAsync(string permissionCode)
        {
            try
            {
                var db = _context.Db;
                var roles = await db.Queryable<Role>()
                    .InnerJoin<SysRolePermission>((r, rp) => r.RoleGUID == rp.RoleGuid)
                    .Where((r, rp) => rp.PermissionCode == permissionCode && rp.IsDeleted == false)
                    .Select(r => new RoleDto
                    {
                        RoleGUID = r.RoleGUID,
                        RoleName = r.RoleName,
                        Description = r.Description,
                        IsActive = r.IsActive,
                        CreatedAt = r.CreatedAt,
                        UpdatedAt = r.UpdatedAt ?? r.CreatedAt,
                    })
                    .ToListAsync();

                return ApiResponse<List<RoleDto>>.OK(roles, "获取权限角色列表成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "获取权限角色列表失败，PermissionCode: {PermissionCode}",
                    permissionCode
                );
                return ApiResponse<List<RoleDto>>.Error(
                    "获取权限角色列表失败",
                    "GET_PERMISSION_ROLES_FAILED"
                );
            }
        }

        /// <summary>
        /// 为权限分配角色
        /// </summary>
        public async Task<ApiResponse<bool>> AssignRolesToPermissionAsync(
            string permissionCode,
            List<string> roleGuids
        )
        {
            try
            {
                var db = _context.Db;

                await db.Ado.BeginTranAsync();
                try
                {
                    // 1. 删除该权限的所有角色关联
                    await db.Deleteable<SysRolePermission>()
                        .Where(rp => rp.PermissionCode == permissionCode)
                        .ExecuteCommandAsync();

                    // 2. 添加新的角色关联
                    if (roleGuids != null && roleGuids.Any())
                    {
                        var superAdminRoleNames = Permissions.SuperAdminRoleNames.ToList();
                        var assignableRoleGuids = await db.Queryable<Role>()
                            .Where(r =>
                                roleGuids.Contains(r.RoleGUID)
                                && !r.IsDeleted
                                && !superAdminRoleNames.Contains(r.RoleName)
                            )
                            .Select(r => r.RoleGUID)
                            .ToListAsync();

                        var newPermissions = assignableRoleGuids
                            .Select(roleGuid => new SysRolePermission
                            {
                                Id = UuidHelper.GenerateUuid7(),
                                RoleGuid = roleGuid,
                                PermissionCode = permissionCode,
                                CreatedAt = DateTime.Now,
                                CreatedBy = GetCurrentUsername(),
                                UpdatedAt = DateTime.Now,
                                UpdatedBy = GetCurrentUsername(),
                                IsDeleted = false
                            })
                            .ToList();

                        await db.Insertable(newPermissions).ExecuteCommandAsync();
                    }

                    await db.Ado.CommitTranAsync();

                    _logger.LogInformation(
                        "为权限分配角色成功，PermissionCode: {PermissionCode}, RoleCount: {RoleCount}",
                        permissionCode,
                        roleGuids?.Count ?? 0
                    );
                    return ApiResponse<bool>.OK(true, "权限角色分配成功");
                }
                catch
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "为权限分配角色失败，PermissionCode: {PermissionCode}",
                    permissionCode
                );
                return ApiResponse<bool>.Error(
                    "权限角色分配失败",
                    "ASSIGN_ROLES_TO_PERMISSION_FAILED"
                );
            }
        }

        /// <summary>
        /// 创建新权限
        /// </summary>
        public async Task<ApiResponse<List<SysPermission>>> CreatePermissionAsync(CreateSysPermissionDto dto)
        {
            try
            {
                var db = _context.Db;
                var createdPermissions = new List<SysPermission>();
                var permissionDtos = new List<SysPermission>();

                if (dto.Actions != null && dto.Actions.Any())
                {
                    // 批量创建模式
                    foreach (var action in dto.Actions)
                    {
                        var suffix = action switch
                        {
                            "Create" => "创建",
                            "Delete" => "删除",
                            "Edit" => "编辑",
                            "View" => "查看",
                            _ => action
                        };

                        var newCode = $"{dto.Code}.{action}";
                        var newName = $"{dto.Name} - {suffix}";
                        var newDescription = !string.IsNullOrWhiteSpace(dto.Description)
                            ? $"{dto.Description} - {suffix}"
                            : $"{newName}";

                        permissionDtos.Add(new SysPermission
                        {
                            Id = UuidHelper.GenerateUuid7(),
                            Code = newCode,
                            Name = newName,
                            Category = dto.Category,
                            Description = newDescription,
                            CreatedAt = DateTime.Now,
                            CreatedBy = GetCurrentUsername(),
                            UpdatedAt = DateTime.Now,
                            UpdatedBy = GetCurrentUsername(),
                            IsDeleted = false
                        });
                    }
                }
                else
                {
                    // 单个创建模式
                    permissionDtos.Add(new SysPermission
                    {
                        Id = UuidHelper.GenerateUuid7(),
                        Code = dto.Code,
                        Name = dto.Name,
                        Category = dto.Category,
                        Description = dto.Description,
                        CreatedAt = DateTime.Now,
                        CreatedBy = GetCurrentUsername(),
                        UpdatedAt = DateTime.Now,
                        UpdatedBy = GetCurrentUsername(),
                        IsDeleted = false
                    });
                }

                // 检查重复并插入
                foreach (var permission in permissionDtos)
                {
                    var exists = await db.Queryable<SysPermission>()
                        .Where(p => p.Code == permission.Code && !p.IsDeleted)
                        .AnyAsync();

                    if (!exists)
                    {
                        await db.Insertable(permission).ExecuteCommandAsync();
                        createdPermissions.Add(permission);
                        _logger.LogInformation("创建权限成功，Code: {Code}", permission.Code);
                    }
                    else
                    {
                        _logger.LogWarning("权限代码已存在，跳过创建: {Code}", permission.Code);
                    }
                }

                if (!createdPermissions.Any())
                {
                    return ApiResponse<List<SysPermission>>.Error("未创建任何权限（可能代码已存在）", "NO_PERMISSION_CREATED");
                }

                return ApiResponse<List<SysPermission>>.OK(createdPermissions, $"成功创建 {createdPermissions.Count} 个权限");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建权限失败，BaseCode: {Code}", dto.Code);
                return ApiResponse<List<SysPermission>>.Error("创建权限失败", "CREATE_PERMISSION_FAILED");
            }
        }

        /// <summary>
        /// 软删除权限（按 code 唯一标识），同时清理 SysRolePermission 关联
        /// </summary>
        public async Task<ApiResponse<bool>> DeletePermissionAsync(string code)
        {
            try
            {
                var db = _context.Db;

                var permission = await db.Queryable<SysPermission>()
                    .Where(p => p.Code == code && !p.IsDeleted)
                    .FirstAsync();

                if (permission == null)
                {
                    return ApiResponse<bool>.Error("权限不存在或已删除", "PERMISSION_NOT_FOUND");
                }

                // 删除角色-权限关联
                await db.Deleteable<SysRolePermission>()
                    .Where(rp => rp.PermissionCode == code)
                    .ExecuteCommandAsync();

                // 软删除权限
                permission.IsDeleted = true;
                permission.UpdatedAt = DateTime.Now;
                permission.UpdatedBy = GetCurrentUsername();
                await db.Updateable(permission).ExecuteCommandAsync();

                _logger.LogInformation("删除权限成功，Code: {Code}", code);
                return ApiResponse<bool>.OK(true, "权限已删除");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除权限失败，Code: {Code}", code);
                return ApiResponse<bool>.Error("删除权限失败", "DELETE_PERMISSION_FAILED");
            }
        }
    }
}
