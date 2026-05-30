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

            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var roleService = scope.ServiceProvider.GetRequiredService<IRoleService>();

                if (await UserHasAnyRoleAsync(roleService, userId, Permissions.SuperAdminRoleNames))
                {
                    context.Succeed(requirement);
                    return;
                }

                var result = await roleService.UserHasPermissionAsync(userId, requirement.Permission);
                if (result.Data)
                {
                    context.Succeed(requirement);
                    return;
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
    }
}
