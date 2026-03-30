using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BlazorApp.Api.Services
{
    public interface ICurrentUserService
    {
        string GetCurrentUsername();
        string GetCurrentUserGuid();
    }

    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string GetCurrentUsername()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null)
                return "System";

            return user.Identity?.Name ?? user.FindFirst("fullName")?.Value ?? "System";
        }

        public string GetCurrentUserGuid()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null)
                return string.Empty;

            return user.FindFirst("userId")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? string.Empty;
        }
    }
}
