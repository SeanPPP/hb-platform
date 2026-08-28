using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BlazorApp.Api.Features.StoreOrders.Common;

internal interface IStoreOrderActorContext
{
    ClaimsPrincipal? User { get; }

    string ActorName { get; }

    bool HasRole(string role);
}

internal sealed class StoreOrderActorContext(IHttpContextAccessor httpContextAccessor)
    : IStoreOrderActorContext
{
    public ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public string ActorName => User?.Identity?.Name ?? "System";

    public bool HasRole(string role)
    {
        return User?.Claims.Any(claim =>
                claim.Type == ClaimTypes.Role
                && claim.Value.Equals(role, StringComparison.OrdinalIgnoreCase)
            )
            == true;
    }
}
