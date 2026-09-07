using BlazorApp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BlazorApp.Api.Controllers;

[ApiController]
[Route("api/StatisticsJobTrigger/cost-backfill")]
[Authorize(Roles = "Admin")]
public sealed class SalesCostBackfillController(SalesCostBackfillService service) : ControllerBase
{
    public sealed record PreviewRequest(DateTime StartDate, DateTime EndDate);

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] PreviewRequest request)
    {
        if (!service.SchemaReady()) return StatusCode(503, new { success = false, message = "成本回填审计表尚未完成受控迁移" });
        try
        {
            var id = await service.PreviewAsync(request.StartDate, request.EndDate, Actor());
            return Accepted(new { success = true, batchId = id, status = "Previewing" });
        }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
    }

    [HttpGet("{batchId:guid}")]
    public async Task<IActionResult> Get(Guid batchId, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 100)
    {
        if (!service.SchemaReady()) return StatusCode(503);
        var result = await service.GetAsync(batchId, pageIndex, pageSize);
        return result == null ? NotFound() : Ok(new { success = true, data = result });
    }

    [HttpPost("{batchId:guid}/apply")]
    public Task<IActionResult> Apply(Guid batchId) => RequestOperation(batchId, false);

    [HttpPost("{batchId:guid}/rollback")]
    public Task<IActionResult> Rollback(Guid batchId) => RequestOperation(batchId, true);

    [HttpPost("{batchId:guid}/retry-preview")]
    public async Task<IActionResult> RetryPreview(Guid batchId)
    {
        if (!service.SchemaReady()) return StatusCode(503);
        return await service.RetryPreviewAsync(batchId, Actor())
            ? Accepted(new { success = true, batchId, status = "Previewing" })
            : Conflict(new { success = false, message = "仅未冻结的预览失败或待处理日期可以重试" });
    }

    private async Task<IActionResult> RequestOperation(Guid batchId, bool rollback)
    {
        if (!service.SchemaReady()) return StatusCode(503);
        return await service.RequestAsync(batchId, rollback, Actor())
            ? Accepted(new { success = true, batchId, status = rollback ? "RollingBack" : "Applying" })
            : Conflict(new { success = false, message = "批次状态不支持此操作；来源变化需重新预览" });
    }

    private string Actor() => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("UserGuid") ?? User.Identity?.Name
        ?? throw new InvalidOperationException("管理员身份缺少稳定审计标识");
}
