using BlazorApp.Api.Services.OperationAudits;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React;

[ApiController]
[Route("api/react/pos-operation-audits")]
[Authorize(Policy = Permissions.PosTerminal.Audit.View)]
public sealed class PosOperationAuditController : ControllerBase
{
    private readonly OperationAuditQueryService _service;

    public PosOperationAuditController(OperationAuditQueryService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedListReactDto<OperationAuditListItemDto>>>> GetList(
        [FromQuery] OperationAuditQueryDto request
    )
    {
        var result = await _service.QueryAsync(request);
        return Ok(ApiResponse<PagedListReactDto<OperationAuditListItemDto>>.OK(result));
    }

    /// <summary>
    /// 汇总计数接口：沿用列表的门店权限与基础筛选，但忽略 Outcome / IsEmergencyOverride / IsOfflineCached，
    /// 供移动端展示“快捷过滤”入口的基数。
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<OperationAuditSummaryDto>>> GetSummary(
        [FromQuery] OperationAuditQueryDto request
    )
    {
        var result = await _service.GetSummaryAsync(request);
        return Ok(ApiResponse<OperationAuditSummaryDto>.OK(result));
    }

    [HttpGet("{eventId:guid}")]
    public async Task<ActionResult<ApiResponse<OperationAuditDetailDto>>> GetDetail(Guid eventId)
    {
        var result = await _service.GetDetailAsync(eventId);
        return result.Status switch
        {
            OperationAuditDetailAccessStatus.Found =>
                Ok(ApiResponse<OperationAuditDetailDto>.OK(result.Data!)),
            OperationAuditDetailAccessStatus.Forbidden => Forbid(),
            _ => NotFound(ApiResponse<OperationAuditDetailDto>.Error("操作日志不存在", "NOT_FOUND")),
        };
    }
}
