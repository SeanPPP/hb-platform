using System.Security.Claims;
using BlazorApp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers;

[ApiController]
[Route("api/StatisticsJobTrigger/hourly-backfill")]
[Authorize(Roles = "Admin")]
public sealed class HourlySalesBackfillController(
    HourlySalesBackfillService service,
    IConfiguration configuration) : ControllerBase
{
    public sealed record PreviewRequest(DateTime StartDate, DateTime EndDate);

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] PreviewRequest request)
    {
        if (!service.SchemaReady())
            return StatusCode(503, new { success = false, message = "分时发布表或统一读取视图尚未完成受控迁移" });
        try
        {
            var id = await service.PreviewAsync(request.StartDate, request.EndDate, Actor());
            return Accepted(new { success = true, batchId = id, status = "Previewing" });
        }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
    }

    [HttpGet("{batchId:guid}")]
    public async Task<IActionResult> Get(Guid batchId)
    {
        if (!service.SchemaReady()) return StatusCode(503);
        var result = await service.GetAsync(batchId);
        return result == null ? NotFound() : Ok(new { success = true, data = result });
    }

    [HttpPost("{batchId:guid}/apply")]
    public Task<IActionResult> Apply(Guid batchId) => RequestChange(batchId, false);

    [HttpPost("{batchId:guid}/rollback")]
    public Task<IActionResult> Rollback(Guid batchId) => RequestChange(batchId, true);

    [HttpPost("{batchId:guid}/days/{date:datetime}/revalidate")]
    public async Task<IActionResult> Revalidate(Guid batchId, DateTime date, CancellationToken token)
    {
        if (!service.SchemaReady()) return StatusCode(503);
        var valid = await service.RevalidateAsync(batchId, date, token);
        return valid.HasValue
            ? Ok(new { success = valid.Value, batchId, date = date.Date,
                status = valid.Value ? "Applied" : "SourceDrift" })
            : Conflict(new { success = false, message = "该日期不存在可复核的Applied认证" });
    }

    private async Task<IActionResult> RequestChange(Guid batchId, bool rollback)
    {
        if (!service.SchemaReady())
            return StatusCode(503, new { success = false, message = "分时发布表或统一读取视图尚未完成受控迁移" });
        if (!rollback && !configuration.GetValue<bool>("SalesStatistics:HourlyBackfillApplyEnabled"))
            return Conflict(new
            {
                success = false,
                message = "分时发布 Apply 门禁未开启；须先完成只读预览和逐日审批",
            });
        return await service.RequestAsync(batchId, rollback, Actor())
            ? Accepted(new { success = true, batchId, status = rollback ? "RollingBack" : "Applying" })
            : Conflict(new { success = false, message = "批次未完成安全预览或状态不支持此操作" });
    }

    private string Actor() => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("UserGuid") ?? User.Identity?.Name
        ?? throw new InvalidOperationException("管理员身份缺少稳定审计标识");
}
