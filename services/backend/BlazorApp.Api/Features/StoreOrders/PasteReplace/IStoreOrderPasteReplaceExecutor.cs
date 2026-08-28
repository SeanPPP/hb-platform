using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.PasteReplace;

/// <summary>
/// 供 singleton 后台任务在新建 scope 内解析的窄执行端口。
/// </summary>
public interface IStoreOrderPasteReplaceExecutor
{
    Task<ApiResponse<bool>> PasteReplaceOrderLinesAsync(PasteReplaceOrderLinesDto request);
}
