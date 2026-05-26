using System.Security.Claims;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace BlazorApp.Api.Authorization
{
    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<PermissionAuthorizationHandler> _logger;

        public PermissionAuthorizationHandler(
            IServiceScopeFactory serviceScopeFactory,
            IMemoryCache cache,
            ILogger<PermissionAuthorizationHandler> logger
        )
        {
            _serviceScopeFactory = serviceScopeFactory;
            _cache = cache;
            _logger = logger;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement
        )
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return;
            }

            if (context.User.IsInRole("Admin") || context.User.IsInRole("管理员"))
            {
                context.Succeed(requirement);
                return;
            }

            if (Permissions.IsAttendanceSelfServiceGranted(requirement.Permission))
            {
                context.Succeed(requirement);
                return;
            }

            if (
                context.User.IsInRole("StoreManager")
                && Permissions.IsStoreManagerGranted(requirement.Permission)
            )
            {
                context.Succeed(requirement);
                return;
            }

            if (
                context.User.IsInRole("WarehouseManager")
                && Permissions.IsWarehouseManagerGranted(requirement.Permission)
            )
            {
                context.Succeed(requirement);
                return;
            }

            var equivalentPermissions = Permissions.GetEquivalentPermissionCodes(
                requirement.Permission
            );

            if (
                equivalentPermissions.Any(permission =>
                    context.User.HasClaim("permission", permission)
                )
            )
            {
                context.Succeed(requirement);
                return;
            }

            // 缓存键: user_permission_{userId}_{permission}
            var cacheKey = $"user_permission_{userId}_{requirement.Permission}";

            if (!_cache.TryGetValue(cacheKey, out bool hasPermission))
            {
                try
                {
                    // 创建新的 Scope 以解析 Scoped Service (IRoleService)
                    using var scope = _serviceScopeFactory.CreateScope();
                    var roleService = scope.ServiceProvider.GetRequiredService<IRoleService>();

                    foreach (var permission in equivalentPermissions)
                    {
                        var result = await roleService.UserHasPermissionAsync(userId, permission);
                        if (result.Data)
                        {
                            hasPermission = true;
                            break;
                        }
                    }

                    // 缓存结果 5 分钟
                    _cache.Set(cacheKey, hasPermission, TimeSpan.FromMinutes(5));
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "权限验证失败: User={UserId}, Permission={Permission}",
                        userId,
                        requirement.Permission
                    );
                    hasPermission = false;
                }
            }

            if (hasPermission)
            {
                context.Succeed(requirement);
            }
        }
    }
}
