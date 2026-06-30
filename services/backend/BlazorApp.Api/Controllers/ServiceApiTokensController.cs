using System.Security.Claims;
using BlazorApp.Api.Authentication;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/service-api-tokens")]
    public sealed class ServiceApiTokensController : ControllerBase
    {
        private readonly IServiceApiTokenService _serviceApiTokenService;

        public ServiceApiTokensController(IServiceApiTokenService serviceApiTokenService)
        {
            _serviceApiTokenService = serviceApiTokenService;
        }

        [HttpGet]
        [Authorize(
            AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
            Policy = Permissions.System.ManageAppDownloads
        )]
        public async Task<IActionResult> List()
        {
            var result = await _serviceApiTokenService.ListAsync();
            return Ok(result);
        }

        [HttpPost]
        [Authorize(
            AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
            Policy = Permissions.System.ManageAppDownloads
        )]
        public async Task<IActionResult> Create([FromBody] ServiceApiTokenCreateRequestDto request)
        {
            var result = await _serviceApiTokenService.CreateAsync(request, ResolveActor());
            return Ok(result);
        }

        [HttpPost("{id:guid}/revoke")]
        [Authorize(
            AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
            Policy = Permissions.System.ManageAppDownloads
        )]
        public async Task<IActionResult> Revoke(Guid id)
        {
            var result = await _serviceApiTokenService.RevokeAsync(id, ResolveActor());
            return Ok(result);
        }

        [HttpGet("current")]
        [Authorize(
            AuthenticationSchemes = ServiceApiTokenAuthenticationDefaults.AuthenticationScheme,
            Policy = Permissions.System.ManageAppDownloads
        )]
        public IActionResult Current()
        {
            var scopes = User
                .FindAll(ServiceApiTokenAuthenticationDefaults.ScopeClaim)
                .Select(claim => claim.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var idClaim = User.FindFirst(ServiceApiTokenAuthenticationDefaults.TokenIdClaim)?.Value;
            Guid.TryParse(idClaim, out var id);

            var response = new ServiceApiTokenCurrentDto
            {
                Id = id,
                Name =
                    User.FindFirst(ServiceApiTokenAuthenticationDefaults.TokenNameClaim)?.Value
                    ?? User.Identity?.Name
                    ?? string.Empty,
                TokenPrefix =
                    User.FindFirst(ServiceApiTokenAuthenticationDefaults.TokenPrefixClaim)?.Value
                    ?? string.Empty,
                Scopes = scopes,
                ExpiresAt = ParseDateTimeClaim(
                    User.FindFirst(ServiceApiTokenAuthenticationDefaults.ExpiresAtClaim)?.Value
                ),
                LastUsedAt = ParseDateTimeClaim(
                    User.FindFirst(ServiceApiTokenAuthenticationDefaults.LastUsedAtClaim)?.Value
                ),
            };

            return Ok(ApiResponse<ServiceApiTokenCurrentDto>.OK(response, "Service API Token 有效"));
        }

        private string ResolveActor()
        {
            return User.Identity?.Name
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("userId")?.Value
                ?? "System";
        }

        private static DateTime? ParseDateTimeClaim(string? value)
        {
            return DateTime.TryParse(value, out var parsed) ? parsed : null;
        }
    }
}
