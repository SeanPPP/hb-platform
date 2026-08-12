using System.Security.Claims;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React;

[ApiController]
[Route("api/react/v1/product-warehouse/store-price-sync")]
[Authorize]
public sealed class ReactProductWarehouseStorePriceSyncController : ControllerBase
{
    private const string AllowedRoles =
        "Admin,管理员,SuperAdmin,超级管理员,WarehouseManager,仓库经理";

    private readonly IWarehouseStorePriceSyncService _syncService;
    private readonly IWarehouseStorePriceSyncJobService _jobService;
    private readonly ILogger<ReactProductWarehouseStorePriceSyncController> _logger;

    public ReactProductWarehouseStorePriceSyncController(
        IWarehouseStorePriceSyncService syncService,
        IWarehouseStorePriceSyncJobService jobService,
        ILogger<ReactProductWarehouseStorePriceSyncController> logger
    )
    {
        _syncService = syncService;
        _jobService = jobService;
        _logger = logger;
    }

    [HttpGet("target-stores")]
    [Authorize(Roles = AllowedRoles)]
    public async Task<IActionResult> GetTargetStores(
        CancellationToken cancellationToken = default
    )
    {
        var stores = await _syncService.GetTargetStoresAsync(cancellationToken);
        return Ok(ApiResponse<List<WarehouseStorePriceSyncTargetStoreDto>>.OK(
            stores,
            "获取目标分店成功"
        ));
    }

    [HttpGet("product-count")]
    [Authorize(Roles = AllowedRoles)]
    public async Task<IActionResult> GetProductCount(
        CancellationToken cancellationToken = default
    )
    {
        var count = await _syncService.GetAllProductCountAsync(cancellationToken);
        return Ok(ApiResponse<int>.OK(count, "获取仓库商品总数成功"));
    }

    [HttpPost("jobs")]
    [Authorize(Roles = AllowedRoles)]
    public async Task<IActionResult> StartJob(
        [FromBody] WarehouseStorePriceSyncRequestDto request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var updatedBy = User.FindFirstValue(ClaimTypes.Name)
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? "system";
            var job = await _jobService.StartJobAsync(request, updatedBy, cancellationToken);
            return Ok(ApiResponse<WarehouseStorePriceSyncJobDto>.OK(
                job,
                job.Message ?? "仓库价格同步任务已提交"
            ));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<WarehouseStorePriceSyncJobDto>.Error(
                ex.Message,
                "INVALID_REQUEST"
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "提交仓库价格同步任务失败");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResponse<WarehouseStorePriceSyncJobDto>.Error(
                    "提交仓库价格同步任务失败",
                    "INTERNAL_ERROR"
                )
            );
        }
    }

    [HttpGet("jobs/{jobId}")]
    [Authorize(Roles = AllowedRoles)]
    public async Task<IActionResult> GetJob(
        string jobId,
        CancellationToken cancellationToken = default
    )
    {
        var job = await _jobService.GetJobAsync(jobId, cancellationToken);
        if (job == null)
        {
            return NotFound(ApiResponse<WarehouseStorePriceSyncJobDto>.Error(
                "任务不存在或已过期",
                "JOB_NOT_FOUND"
            ));
        }

        return Ok(ApiResponse<WarehouseStorePriceSyncJobDto>.OK(job, "获取仓库价格同步任务成功"));
    }
}
