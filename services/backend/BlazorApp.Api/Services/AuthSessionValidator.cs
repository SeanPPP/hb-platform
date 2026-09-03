using System.Security.Claims;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.MobileDeviceActivation;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services
{
    public interface IAuthSessionValidator
    {
        Task<bool> IsAccessSessionActiveAsync(string userGuid, ClaimsPrincipal principal);
    }

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
    }
}
