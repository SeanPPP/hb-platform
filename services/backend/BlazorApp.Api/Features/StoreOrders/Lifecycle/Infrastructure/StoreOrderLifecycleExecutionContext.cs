using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Application.Ports;
using BlazorApp.Shared.Constants;

namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Infrastructure;

internal sealed class StoreOrderLifecycleExecutionContext(
    IStoreOrderActorContext actorContext,
    IStoreOrderAccessPolicy? accessPolicy = null
) : IStoreOrderLifecycleExecutionContext
{
    public string ActorName => actorContext.ActorName;

    public bool IsWarehouseStaffOnly =>
        HasAnyRole("WarehouseStaff", "仓库员工")
        && !HasAnyRole(Permissions.SuperAdminRoleNames)
        && !HasAnyRole(Permissions.WarehouseManagerRoleNames);

    public DateTime LocalNow => DateTime.Now;

    public Task<bool> CanBypassPreorderCompletionAsync() =>
        accessPolicy?.CanBypassPreorderCompletionAsync() ?? Task.FromResult(false);

    private bool HasAnyRole(params string[] roles)
    {
        return roles.Any(actorContext.HasRole);
    }
}
