using System.Security.Claims;
using BlazorApp.Api.Authentication;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace BlazorApp.Api.Authorization
{
    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<PermissionAuthorizationHandler> _logger;

        public PermissionAuthorizationHandler(
            IServiceScopeFactory serviceScopeFactory,
            IMemoryCache cache,
            ILogger<PermissionAuthorizationHandler> logger
        )
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement
        )
        {
            if (IsServiceApiToken(context.User))
            {
                // 关键位置：service token 只认认证 handler 写入的专用 scope，不能回落到用户角色或普通 permission claim。
                if (HasServiceApiScope(context.User, requirement.Permission))
                {
                    context.Succeed(requirement);
                }

                return;
            }

            var userId =
                context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.User.FindFirst("userId")?.Value
                ?? context.User.FindFirst("userGuid")?.Value
                ?? context.User.FindFirst(ClaimTypes.Name)?.Value
                ?? context.User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return;
            }

            if (Permissions.IsAttendanceSelfServiceGranted(requirement.Permission))
            {
                context.Succeed(requirement);
                return;
            }

            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var roleService = scope.ServiceProvider.GetRequiredService<IRoleService>();

                if (await UserHasAnyRoleAsync(roleService, userId, Permissions.SuperAdminRoleNames))
                {
                    context.Succeed(requirement);
                    return;
                }

                foreach (var permission in Permissions.GetEquivalentPermissionCodes(requirement.Permission))
                {
                    var result = await roleService.UserHasPermissionAsync(userId, permission);
                    if (result.Data)
                    {
                        context.Succeed(requirement);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "权限验证失败: User={UserId}, Permission={Permission}",
                    userId,
                    requirement.Permission
                );
            }
        }

        private static async Task<bool> UserHasAnyRoleAsync(
            IRoleService roleService,
            string userId,
            params string[] roleNames
        )
        {
            foreach (var roleName in roleNames)
            {
                var roleResult = await roleService.UserHasRoleAsync(userId, roleName);
                if (roleResult.Data)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsServiceApiToken(ClaimsPrincipal user)
        {
            return user.HasClaim(
                ServiceApiTokenAuthenticationDefaults.TokenTypeClaim,
                "true"
            );
        }

        private static bool HasServiceApiScope(ClaimsPrincipal user, string permission)
        {
            return user
                .FindAll(ServiceApiTokenAuthenticationDefaults.ScopeClaim)
                .Any(claim => string.Equals(claim.Value, permission, StringComparison.OrdinalIgnoreCase));
        }
    }
}
