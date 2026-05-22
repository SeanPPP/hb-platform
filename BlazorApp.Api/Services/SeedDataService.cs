using BlazorApp.Api.Data;
using BlazorApp.Api.Utils;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 种子数据服务
    /// </summary>
    public class SeedDataService
    {
        private readonly SqlSugarContext _dbContext;
        private readonly ILogger<SeedDataService> _logger;

        public SeedDataService(SqlSugarContext dbContext, ILogger<SeedDataService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// 初始化所有种子数据
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                // 1. 首先创建角色（必须在用户之前）
                await CreateRoleSeedDataAsync();

                // 2. 创建管理员用户
                await CreateAdminUserAsync();

                // 3. 立即为管理员分配Admin角色（确保默认角色）
                await AssignAdminRoleAsync();

                // 4. 创建默认店铺
                // await CreateDefaultStoreAsync();

                // 5. 为admin用户分配默认店铺
                // await AssignDefaultStoreToAdminAsync();

                // 6. 初始化权限数据并分配给Admin
                await InitializePermissionSeedsAsync();

                // 7. 验证管理员角色分配
                await VerifyAdminRoleAssignmentAsync();

                _logger.LogInformation("种子数据初始化完成 - admin用户已分配Admin角色和默认店铺");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "种子数据初始化失败");
                throw;
            }
        }

        /// <summary>
        /// 创建角色种子数据
        /// </summary>
        private async Task CreateRoleSeedDataAsync()
        {
            var db = _dbContext.Db;

            // 检查是否已存在角色
            var existingRoles = await db.Queryable<Role>().ToListAsync();

            var rolesToCreate = new List<Role>();

            if (!existingRoles.Any(r => r.RoleName == "Admin"))
            {
                rolesToCreate.Add(
                    new Role
                    {
                        RoleGUID = Guid.NewGuid().ToString(),
                        RoleName = "Admin",
                        Description = "System Administrator with full permissions",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                    }
                );
            }

            if (!existingRoles.Any(r => r.RoleName == "Manager"))
            {
                rolesToCreate.Add(
                    new Role
                    {
                        RoleGUID = Guid.NewGuid().ToString(),
                        RoleName = "Manager",
                        Description = "Manager role with business management permissions",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                    }
                );
            }

            if (!existingRoles.Any(r => r.RoleName == "User"))
            {
                rolesToCreate.Add(
                    new Role
                    {
                        RoleGUID = Guid.NewGuid().ToString(),
                        RoleName = "User",
                        Description = "Regular user with basic viewing permissions",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                    }
                );
            }

            if (!existingRoles.Any(r => r.RoleName == "Order"))
            {
                rolesToCreate.Add(
                    new Role
                    {
                        RoleGUID = Guid.NewGuid().ToString(),
                        RoleName = "Order",
                        Description = "订货员",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                    }
                );
            }

            if (!existingRoles.Any(r => r.RoleName == "StoreManager"))
            {
                rolesToCreate.Add(
                    new Role
                    {
                        RoleGUID = Guid.NewGuid().ToString(),
                        RoleName = "StoreManager",
                        Description = "Store manager role for branch staff maintenance",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                    }
                );
            }

            if (!existingRoles.Any(r => r.RoleName == "StoreStaff"))
            {
                rolesToCreate.Add(
                    new Role
                    {
                        RoleGUID = Guid.NewGuid().ToString(),
                        RoleName = "StoreStaff",
                        Description = "Store staff role for branch employees",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                    }
                );
            }

            if (rolesToCreate.Any())
            {
                await db.Insertable(rolesToCreate).ExecuteCommandAsync();
                _logger.LogInformation(
                    $"已创建 {rolesToCreate.Count} 个角色: {string.Join(", ", rolesToCreate.Select(r => r.RoleName))}"
                );
            }
            else
            {
                _logger.LogInformation("角色数据已存在，跳过创建");
            }
        }

        /// <summary>
        /// 创建管理员用户
        /// </summary>
        private async Task CreateAdminUserAsync()
        {
            var db = _dbContext.Db;

            var adminUser = await db.Queryable<User>().FirstAsync(u => u.Username == "admin");

            if (adminUser == null)
            {
                var seedUser = new User
                {
                    UserGUID = Guid.NewGuid().ToString(),
                    Username = "admin",
                    Email = "admin@example.com",
                    FullName = "Administrator",
                    // 默认密码 "admin" 先通过SHA256哈希（模拟前端），再加盐哈希存储
                    PasswordHash = PasswordHasher.HashPassword(
                        PasswordHasher.ComputeSha256("admin")
                    ),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    LastLoginAt = null,
                };

                await db.Insertable(seedUser).ExecuteCommandAsync();
                _logger.LogInformation("管理员用户已创建，用户名: admin, 密码: admin");
            }
            else
            {
                // 确保管理员用户是激活状态
                if (!adminUser.IsActive)
                {
                    adminUser.IsActive = true;
                    adminUser.UpdatedAt = DateTime.UtcNow;
                    await db.Updateable(adminUser)
                        .UpdateColumns(u => new { u.IsActive, u.UpdatedAt })
                        .ExecuteCommandAsync();
                }
                _logger.LogInformation("管理员用户已存在");
            }
        }

        /// <summary>
        /// 为管理员分配Admin角色
        /// </summary>
        private async Task AssignAdminRoleAsync()
        {
            var db = _dbContext.Db;

            // 获取Admin角色和admin用户
            var adminRole = await db.Queryable<Role>().FirstAsync(r => r.RoleName == "Admin");

            var adminUser = await db.Queryable<User>().FirstAsync(u => u.Username == "admin");

            if (adminRole == null)
            {
                _logger.LogError("Admin角色不存在，无法分配给管理员用户");
                return;
            }

            if (adminUser == null)
            {
                _logger.LogError("管理员用户不存在，无法分配角色");
                return;
            }

            // 检查是否已存在角色关联
            var existingUserRole = await db.Queryable<UserRole>()
                .FirstAsync(ur =>
                    ur.UserGUID == adminUser.UserGUID && ur.RoleGUID == adminRole.RoleGUID
                );

            if (existingUserRole == null)
            {
                var userRole = new UserRole
                {
                    UserRoleGUID = Guid.NewGuid().ToString(),
                    UserGUID = adminUser.UserGUID,
                    RoleGUID = adminRole.RoleGUID,
                    AssignedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                };

                await db.Insertable(userRole).ExecuteCommandAsync();
                _logger.LogInformation("管理员用户已成功关联Admin角色");
            }
            else
            {
                _logger.LogInformation("管理员用户角色关联已存在");
            }
        }

        /// <summary>
        /// 创建默认店铺
        /// </summary>
        private async Task CreateDefaultStoreAsync()
        {
            var db = _dbContext.Db;

            // 检查是否已存在默认店铺
            var existingStore = await db.Queryable<Store>()
                .FirstAsync(s => s.StoreCode == "DEFAULT" || s.StoreName == "Default Store");

            if (existingStore == null)
            {
                var defaultStore = new Store
                {
                    StoreGUID = "default", // 使用固定的GUID，方便购物车API fallback使用
                    StoreCode = "DEFAULT",
                    StoreName = "Default Store",
                    Address = "Default Store Address",
                    Phone = "000-0000-0000",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                };

                await db.Insertable(defaultStore).ExecuteCommandAsync();
                _logger.LogInformation(
                    "已创建默认店铺: {StoreName} (GUID: {StoreGUID})",
                    defaultStore.StoreName,
                    defaultStore.StoreGUID
                );
            }
            else
            {
                _logger.LogInformation("默认店铺已存在，跳过创建");
            }
        }

        /// <summary>
        /// 为admin用户分配默认店铺
        /// </summary>
        private async Task AssignDefaultStoreToAdminAsync()
        {
            var db = _dbContext.Db;

            // 获取admin用户
            var adminUser = await db.Queryable<User>().FirstAsync(u => u.Username == "admin");

            if (adminUser == null)
            {
                _logger.LogError("admin用户不存在，无法分配店铺");
                return;
            }

            // 获取默认店铺
            var defaultStore = await db.Queryable<Store>()
                .FirstAsync(s => s.StoreGUID == "default" || s.StoreCode == "DEFAULT");

            if (defaultStore == null)
            {
                _logger.LogError("默认店铺不存在，无法分配给admin用户");
                return;
            }

            // 检查是否已存在用户店铺关联
            var existingUserStore = await db.Queryable<UserStore>()
                .FirstAsync(us =>
                    us.UserGUID == adminUser.UserGUID && us.StoreGUID == defaultStore.StoreGUID
                );

            if (existingUserStore == null)
            {
                var userStore = new UserStore
                {
                    UserStoreGUID = Guid.NewGuid().ToString(),
                    UserGUID = adminUser.UserGUID,
                    StoreGUID = defaultStore.StoreGUID,
                    CreatedAt = DateTime.UtcNow,
                };

                await db.Insertable(userStore).ExecuteCommandAsync();
                _logger.LogInformation(
                    "✅ 已为admin用户分配默认店铺: {StoreName}",
                    defaultStore.StoreName
                );
            }
            else
            {
                _logger.LogInformation("admin用户已关联默认店铺，跳过分配");
            }
        }

        /// <summary>
        /// 验证管理员角色分配是否正确
        /// </summary>
        private async Task VerifyAdminRoleAssignmentAsync()
        {
            var db = _dbContext.Db;

            try
            {
                // 获取admin用户及其角色
                var adminUser = await db.Queryable<User>()
                    .Includes(u => u.Roles)
                    .FirstAsync(u => u.Username == "admin");

                if (adminUser == null)
                {
                    _logger.LogError("验证失败：admin用户不存在");
                    return;
                }

                // 检查是否拥有Admin角色
                var hasAdminRole = adminUser.Roles?.Any(r => r.RoleName == "Admin") ?? false;

                if (hasAdminRole)
                {
                    _logger.LogInformation("✅ 验证成功：admin用户拥有Admin角色");
                }
                else
                {
                    _logger.LogWarning("⚠️ 验证警告：admin用户未拥有Admin角色，正在重新分配...");

                    // 重新分配Admin角色
                    var adminRole = await db.Queryable<Role>()
                        .FirstAsync(r => r.RoleName == "Admin");

                    if (adminRole != null)
                    {
                        var userRole = new UserRole
                        {
                            UserRoleGUID = Guid.NewGuid().ToString(),
                            UserGUID = adminUser.UserGUID,
                            RoleGUID = adminRole.RoleGUID,
                            AssignedAt = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow,
                        };

                        await db.Insertable(userRole).ExecuteCommandAsync();
                        _logger.LogInformation("✅ Admin角色重新分配成功");
                    }
                }

                // 记录用户的所有角色
                var roleNames =
                    adminUser.Roles?.Select(r => r.RoleName).ToList() ?? new List<string>();
                _logger.LogInformation($"admin用户当前角色：{string.Join(", ", roleNames)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证管理员角色分配时发生错误");
            }
        }

        /// <summary>
        /// 初始化权限种子数据
        /// </summary>
        public async Task InitializePermissionSeedsAsync()
        {
            var db = _dbContext.Db;

            // 1. 同步权限定义
            var allPermissions = PermissionSeedData.AllPermissions.ToList();
            var existingPermissions = await db.Queryable<SysPermission>().ToListAsync();
            var existingPermissionCodes = existingPermissions
                .Select(permission => permission.Code)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var newPermissions = new List<SysPermission>();
            foreach (var seed in allPermissions)
            {
                if (!existingPermissionCodes.Contains(seed.Code))
                {
                    newPermissions.Add(
                        new SysPermission
                        {
                            Id = UuidHelper.GenerateUuid7(),
                            Code = seed.Code,
                            Name = seed.Name,
                            Category = seed.Category,
                            Description = seed.Description,
                        }
                    );
                }
            }

            if (newPermissions.Any())
            {
                await db.Insertable(newPermissions).ExecuteCommandAsync();
                _logger.LogInformation($"已新增 {newPermissions.Count} 个权限定义");
            }

            // 2. 为 Admin 角色分配所有权限
            var adminRole = await db.Queryable<Role>().FirstAsync(r => r.RoleName == "Admin");
            if (adminRole != null)
            {
                // 获取Admin已有的权限
                var currentRolePermissions = await db.Queryable<SysRolePermission>()
                    .Where(rp => rp.RoleGuid == adminRole.RoleGUID)
                    .Select(rp => rp.PermissionCode)
                    .ToListAsync();
                var currentRolePermissionCodes = currentRolePermissions.ToHashSet(
                    StringComparer.OrdinalIgnoreCase
                );

                // 找出需要新增的关联
                var missingCodes = allPermissions
                    .Select(permission => permission.Code)
                    .Where(code => !currentRolePermissionCodes.Contains(code))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (missingCodes.Any())
                {
                    var newRolePermissions = missingCodes
                        .Select(code => new SysRolePermission
                        {
                            Id = UuidHelper.GenerateUuid7(),
                            RoleGuid = adminRole.RoleGUID,
                            PermissionCode = code,
                        })
                        .ToList();

                    await db.Insertable(newRolePermissions).ExecuteCommandAsync();
                    _logger.LogInformation(
                        $"已为 Admin 角色自动分配 {newRolePermissions.Count} 个新权限"
                    );
                }
            }
        }
    }
}
