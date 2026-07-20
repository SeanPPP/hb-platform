using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React;

[ApiController]
[Route("api/react/v1/preorders/admin")]
[Authorize(Policy = Permissions.Warehouse.ManageOrders)]
public sealed class PreorderAdminController : ControllerBase
{
    private readonly IPreorderReactService _service;

    public PreorderAdminController(IPreorderReactService service)
    {
        _service = service;
    }

    [HttpGet("templates")]
    public Task<IActionResult> GetTemplates([FromQuery] PreorderTemplateQueryDto query) =>
        ExecuteAsync(() => _service.GetTemplatesAsync(query));

    [HttpGet("templates/{templateGuid}")]
    public Task<IActionResult> GetTemplate(string templateGuid) =>
        ExecuteAsync(() => _service.GetTemplateAsync(templateGuid));

    [HttpPost("templates")]
    public Task<IActionResult> CreateTemplate([FromBody] SavePreorderTemplateDto request) =>
        ExecuteAsync(() => _service.CreateTemplateAsync(request), "Preorder 模板已创建");

    [HttpPut("templates/{templateGuid}")]
    public Task<IActionResult> UpdateTemplate(
        string templateGuid,
        [FromBody] SavePreorderTemplateDto request
    ) => ExecuteAsync(() => _service.UpdateTemplateAsync(templateGuid, request), "Preorder 模板已更新");

    [HttpPost("resolve-items")]
    public Task<IActionResult> ResolveItems([FromBody] ResolvePreorderItemsRequestDto request) =>
        ExecuteAsync(() => _service.ResolveItemsAsync(request));

    [HttpGet("templates/{templateGuid}/activations")]
    public Task<IActionResult> GetActivations(string templateGuid) =>
        ExecuteAsync(() => _service.GetTemplateActivationsAsync(templateGuid));

    [HttpPost("templates/{templateGuid}/activate")]
    public Task<IActionResult> Activate(
        string templateGuid,
        [FromBody] ActivatePreorderTemplateDto request
    ) => ExecuteAsync(() => _service.ActivateAsync(templateGuid, request), "Preorder 新一期已激活");

    [HttpGet("activations/{activationGuid}")]
    public Task<IActionResult> GetActivation(string activationGuid) =>
        ExecuteAsync(() => _service.GetActivationAsync(activationGuid));

    [HttpPut("activations/{activationGuid}/stores")]
    public Task<IActionResult> UpdateActivationStores(
        string activationGuid,
        [FromBody] UpdatePreorderActivationStoresDto request
    ) => ExecuteAsync(
        () => _service.UpdateActivationStoresAsync(activationGuid, request),
        "激活批次分店已更新"
    );

    [HttpPut("activations/{activationGuid}/estimated-arrival-date")]
    public Task<IActionResult> UpdateActivationEstimatedArrivalDate(
        string activationGuid,
        [FromBody] UpdatePreorderActivationEstimatedArrivalDateDto request
    ) => ExecuteAsync(
        () => _service.UpdateActivationEstimatedArrivalDateAsync(activationGuid, request),
        "预计到货日期已更新"
    );

    [HttpPost("activations/{activationGuid}/close")]
    public Task<IActionResult> CloseActivation(
        string activationGuid,
        [FromBody] ClosePreorderActivationDto request
    ) => ExecuteAsync(() => _service.CloseActivationAsync(activationGuid, request), "激活批次已更新");

    [HttpPost("activations/{activationGuid}/cancel")]
    public Task<IActionResult> CancelActivation(string activationGuid) =>
        ExecuteAsync(() => _service.CancelActivationAsync(activationGuid), "激活批次已取消");

    [HttpGet("activations/{activationGuid}/orders")]
    public Task<IActionResult> GetOrders(string activationGuid) =>
        ExecuteAsync(() => _service.GetActivationOrdersAsync(activationGuid));

    [HttpGet("activations/{activationGuid}/statistics")]
    public Task<IActionResult> GetStatistics(string activationGuid) =>
        ExecuteAsync(() => _service.GetStatisticsAsync(activationGuid));

    [HttpGet("activations/{activationGuid}/export")]
    public async Task<IActionResult> Export(string activationGuid)
    {
        try
        {
            var file = await _service.ExportAsync(activationGuid);
            return File(file.Content, file.ContentType, file.FileName);
        }
        catch (PreorderBusinessException ex)
        {
            return StatusCode(
                ex.StatusCode,
                ApiResponse<object>.Error(ex.Message, ex.ErrorCode, ex.Details)
            );
        }
    }

    [HttpPatch("orders/{orderGuid}/status")]
    public Task<IActionResult> UpdateOrderStatus(
        string orderGuid,
        [FromBody] UpdatePreorderOrderStatusDto request
    ) => ExecuteAsync(() => _service.UpdateOrderStatusAsync(orderGuid, request), "Preorder 订单状态已更新");

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<T>> action, string message = "操作成功")
    {
        try
        {
            return Ok(ApiResponse<T>.OK(await action(), message));
        }
        catch (PreorderBusinessException ex)
        {
            return StatusCode(
                ex.StatusCode,
                ApiResponse<T>.Error(ex.Message, ex.ErrorCode, ex.Details)
            );
        }
    }
}
