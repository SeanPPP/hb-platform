using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Sync;

internal sealed class SyncMissingOrdersHandler(
    SyncMissingOrdersValidator validator,
    SyncMissingOrdersQuery query,
    SyncMissingOrdersCommand command,
    ILogger<SyncMissingOrdersHandler> logger
) : IStoreOrderMissingOrdersSyncExecutor
{
    public async Task<SyncMissingOrdersResultDto> SyncMissingOrdersFromHqAsync(
        SyncMissingOrdersRequestDto? request
    )
    {
        var result = new SyncMissingOrdersResultDto { Success = true, Message = string.Empty };
        var storeCodes = validator.NormalizeStoreCodes(request);

        try
        {
            var queryResult = await query.ExecuteAsync(storeCodes);
            if (queryResult.Preparation == null)
            {
                result.Message = queryResult.Message;
                return result;
            }

            var writeResult = await command.ExecuteAsync(queryResult.Preparation);
            result.OrdersSynced = writeResult.OrdersSynced;
            result.OrdersUpdated = writeResult.OrdersUpdated;
            result.DetailsSynced = writeResult.DetailsSynced;
            result.DetailsUpdated = writeResult.DetailsUpdated;

            logger.LogInformation(
                "分店订货同步完成：新增订单 {OrdersSynced}、更新订单 {OrdersUpdated}、新增明细 {DetailsSynced}、更新明细 {DetailsUpdated}",
                result.OrdersSynced,
                result.OrdersUpdated,
                result.DetailsSynced,
                result.DetailsUpdated
            );

            var hasChanges =
                result.OrdersSynced > 0
                || result.DetailsSynced > 0
                || result.OrdersUpdated > 0
                || result.DetailsUpdated > 0;
            result.Message = hasChanges
                ? $"同步成功：新增订单 {result.OrdersSynced} 条、详情 {result.DetailsSynced} 条；"
                    + $"更新订单 {result.OrdersUpdated} 条、详情 {result.DetailsUpdated} 条"
                : "所有订单已是最新，无需同步";

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"同步失败：{ex.Message}";
            logger.LogError(
                ex,
                "同步缺失订单失败，分店代码：{StoreCodes}",
                storeCodes.Count > 0 ? string.Join(",", storeCodes) : "全部"
            );
            return result;
        }
    }
}
