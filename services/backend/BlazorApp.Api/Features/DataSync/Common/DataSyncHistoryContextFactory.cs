using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.DataSync.Common;

/// <summary>
/// 统一从服务器请求上下文生成审计身份，避免同步入口接受客户端操作人。
/// </summary>
internal sealed class DataSyncHistoryContextFactory
{
    private readonly ICurrentUserService _currentUserService;

    public DataSyncHistoryContextFactory(ICurrentUserService currentUserService) => _currentUserService = currentUserService;

    public WarehouseProductChangeHistoryContextDto Create(string source, Guid batchGuid, DateTime occurredAtUtc)
    {
        var actorUserGuid = _currentUserService.GetCurrentUserGuid();
        var actorName = _currentUserService.GetCurrentUsername();
        var hasActorUserGuid = !string.IsNullOrWhiteSpace(actorUserGuid);
        var isSystem = !hasActorUserGuid && (string.IsNullOrWhiteSpace(actorName) || string.Equals(actorName, "System", StringComparison.OrdinalIgnoreCase));
        return new WarehouseProductChangeHistoryContextDto { Action = "BatchUpdate", Source = source, BatchGuid = batchGuid, ActorUserGuid = hasActorUserGuid ? actorUserGuid : null, ActorName = isSystem ? "System" : actorName, ActorType = hasActorUserGuid || !isSystem ? "User" : "System", OccurredAtUtc = occurredAtUtc };
    }

    public string ResolveSetChildPurchasePriceActor()
    {
        var actor = _currentUserService.GetCurrentUsername();
        return string.IsNullOrWhiteSpace(actor) ? "System" : actor.Trim();
    }
}
