using Microsoft.AspNetCore.Http;

namespace BlazorApp.Api.Authentication
{
    public static class ServiceApiTokenAuthenticationDefaults
    {
        public const string AuthenticationScheme = "ServiceApiToken";
        public const string PolicyScheme = "BearerOrServiceApiToken";
        public const string TokenPrefix = "hbsvc_";
        public const string TokenTypeClaim = "hb_service_api_token";
        public const string TokenIdClaim = "hb_service_api_token_id";
        public const string TokenNameClaim = "hb_service_api_token_name";
        public const string TokenPrefixClaim = "hb_service_api_token_prefix";
        public const string ScopeClaim = "hb_service_api_scope";
        public const string ExpiresAtClaim = "hb_service_api_expires_at";
        public const string LastUsedAtClaim = "hb_service_api_last_used_at";

        public static bool RequestHasServiceApiToken(HttpRequest request)
        {
            var token = GetBearerToken(request);
            return token?.StartsWith(TokenPrefix, StringComparison.Ordinal) == true;
        }

        public static string? GetBearerToken(HttpRequest request)
        {
            var authorization = request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authorization))
            {
                return null;
            }

            const string bearerPrefix = "Bearer ";
            if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return authorization[bearerPrefix.Length..].Trim();
        }
    }
}
