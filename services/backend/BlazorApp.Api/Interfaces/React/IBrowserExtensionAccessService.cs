using System.Security.Claims;

namespace BlazorApp.Api.Interfaces.React;

public interface IBrowserExtensionAccessService
{
    Task<bool> CanAccessAsync(ClaimsPrincipal user, string? storeCode = null);
    Task<IReadOnlyList<string>> GetRelatedStoreCodesAsync(ClaimsPrincipal user);
}
