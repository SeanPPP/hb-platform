using System.Security.Claims;
using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services
{
    public interface IAuthSessionValidator
    {
        Task<bool> IsAccessSessionActiveAsync(string userGuid, ClaimsPrincipal principal);
    }

    public sealed class AuthSessionValidator(SqlSugarContext dbContext) : IAuthSessionValidator
    {
        public async Task<bool> IsAccessSessionActiveAsync(string userGuid, ClaimsPrincipal principal)
        {
            var sessionId = principal.FindFirst("sessionId")?.Value;
            if (string.IsNullOrWhiteSpace(userGuid) || string.IsNullOrWhiteSpace(sessionId))
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
