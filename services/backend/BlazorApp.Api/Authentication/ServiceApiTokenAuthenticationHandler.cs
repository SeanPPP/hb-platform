using System.Security.Claims;
using System.Text.Encodings.Web;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Authentication
{
    public sealed class ServiceApiTokenAuthenticationHandler
        : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly IServiceApiTokenService _serviceApiTokenService;
        private readonly IClientIpResolver _clientIpResolver;

        public ServiceApiTokenAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IServiceApiTokenService serviceApiTokenService,
            IClientIpResolver clientIpResolver
        )
            : base(options, logger, encoder)
        {
            _serviceApiTokenService = serviceApiTokenService;
            _clientIpResolver = clientIpResolver;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var token = ServiceApiTokenAuthenticationDefaults.GetBearerToken(Request);
            if (string.IsNullOrWhiteSpace(token))
            {
                return AuthenticateResult.NoResult();
            }

            if (!token.StartsWith(ServiceApiTokenAuthenticationDefaults.TokenPrefix, StringComparison.Ordinal))
            {
                return AuthenticateResult.NoResult();
            }

            var clientIp = _clientIpResolver.Resolve(Context);
            var validation = await _serviceApiTokenService.ValidateAsync(token, clientIp);
            if (validation == null)
            {
                return AuthenticateResult.Fail("Service API token 无效或已撤销");
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, validation.Id.ToString()),
                new(ClaimTypes.Name, validation.Name),
                new(ServiceApiTokenAuthenticationDefaults.TokenTypeClaim, "true"),
                new(ServiceApiTokenAuthenticationDefaults.TokenIdClaim, validation.Id.ToString()),
                new(ServiceApiTokenAuthenticationDefaults.TokenNameClaim, validation.Name),
                new(ServiceApiTokenAuthenticationDefaults.TokenPrefixClaim, validation.TokenPrefix),
            };

            if (validation.ExpiresAt.HasValue)
            {
                claims.Add(
                    new(
                        ServiceApiTokenAuthenticationDefaults.ExpiresAtClaim,
                        validation.ExpiresAt.Value.ToString("O")
                    )
                );
            }

            if (validation.LastUsedAt.HasValue)
            {
                claims.Add(
                    new(
                        ServiceApiTokenAuthenticationDefaults.LastUsedAtClaim,
                        validation.LastUsedAt.Value.ToString("O")
                    )
                );
            }

            // 关键位置：service token 的授权只依赖专用 scope claim，避免复用普通 JWT 的 permission claim。
            foreach (var scope in validation.Scopes)
            {
                claims.Add(new Claim(ServiceApiTokenAuthenticationDefaults.ScopeClaim, scope));
            }

            var identity = new ClaimsIdentity(
                claims,
                ServiceApiTokenAuthenticationDefaults.AuthenticationScheme
            );
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }
    }
}
