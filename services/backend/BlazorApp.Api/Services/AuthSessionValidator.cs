using System.Security.Claims;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.MobileDeviceActivation;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    public interface IAuthSessionValidator
    {
        Task<bool> IsAccessSessionActiveAsync(string userGuid, ClaimsPrincipal principal);

        Task<AuthWebSessionValidationResult> ValidateWebAccessSessionAsync(
            string userGuid,
            string? sessionId,
            CancellationToken cancellationToken = default);
    }

    public sealed record AuthWebSessionValidationResult(
        bool IsValid,
        IReadOnlyList<string> ActiveRoleNames);

    public sealed class AuthSessionValidator(
        SqlSugarContext dbContext,
        IMobileDeviceActivationService? mobileDeviceActivationService = null
    ) : IAuthSessionValidator
    {
        public async Task<bool> IsAccessSessionActiveAsync(string userGuid, ClaimsPrincipal principal)
        {
            if (string.IsNullOrWhiteSpace(userGuid))
            {
                return false;
            }

            if (string.Equals(
                    principal.FindFirst("token_use")?.Value,
                    MobileDeviceAccountTokenIssuer.TokenUse,
                    StringComparison.Ordinal))
            {
                if (mobileDeviceActivationService == null
                    || !MobileDeviceBindingContextResolver.TryResolve(principal, out var binding)
                    || !string.Equals(binding.UserGuid, userGuid, StringComparison.Ordinal))
                {
                    return false;
                }

                var validation = await mobileDeviceActivationService.ValidateTokenBindingAsync(
                    binding,
                    CancellationToken.None);

                // 设备账号令牌不依赖 RefreshToken，但每次请求都必须实时命中同一有效绑定。
                return validation.IsValid
                    && string.Equals(validation.UserGuid, userGuid, StringComparison.Ordinal);
            }

            var sessionId = principal.FindFirst("sessionId")?.Value;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return false;
            }

            var now = DateTime.UtcNow;
            var activeSession = await dbContext.Db.Queryable<RefreshToken>()
                .FirstAsync(token =>
                    token.RefreshTokenGUID == sessionId
                    && token.UserGUID == userGuid
                    && !token.IsRevoked
                    && !token.IsDeleted
                    && token.ExpiresAt >= now
                );

            // access token 必须绑定仍有效的 RefreshToken 会话；被挤下线后这里立即失效。
            return activeSession != null;
        }

        public async Task<AuthWebSessionValidationResult> ValidateWebAccessSessionAsync(
            string userGuid,
            string? sessionId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userGuid) || string.IsNullOrWhiteSpace(sessionId))
            {
                return new AuthWebSessionValidationResult(false, Array.Empty<string>());
            }

            var now = DateTime.UtcNow;
            // 一次查询实时校验用户、会话和角色；角色条件留在 LEFT JOIN 内，无角色不等于会话失效。
            var rows = await dbContext.Db
                .Queryable<User, RefreshToken, UserRole, Role>(
                    (user, session, userRole, role) => new JoinQueryInfos(
                        JoinType.Inner,
                        user.UserGUID == session.UserGUID
                            && session.RefreshTokenGUID == sessionId
                            && !session.IsRevoked
                            && !session.IsDeleted
                            && session.ExpiresAt >= now,
                        JoinType.Left,
                        user.UserGUID == userRole.UserGUID
                            && !userRole.IsDeleted,
                        JoinType.Left,
                        userRole.RoleGUID == role.RoleGUID
                            && role.IsActive
                            && !role.IsDeleted))
                .Where((user, session, userRole, role) =>
                    user.UserGUID == userGuid
                    && user.IsActive
                    && !user.IsDeleted)
                .Select((user, session, userRole, role) => new AuthWebSessionRoleRow
                {
                    UserGuid = user.UserGUID,
                    RoleName = role.RoleName,
                })
                .ToListAsync(cancellationToken);

            if (rows.Count == 0)
            {
                return new AuthWebSessionValidationResult(false, Array.Empty<string>());
            }

            var activeRoleNames = rows
                .Select(row => row.RoleName)
                .Where(roleName => !string.IsNullOrWhiteSpace(roleName))
                .Select(roleName => roleName!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return new AuthWebSessionValidationResult(true, activeRoleNames);
        }

        private sealed class AuthWebSessionRoleRow
        {
            public string UserGuid { get; set; } = string.Empty;

            public string? RoleName { get; set; }
        }
    }
}
