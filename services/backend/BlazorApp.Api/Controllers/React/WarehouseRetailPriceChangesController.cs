using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React;

[ApiController]
[Route("api/react/v1/warehouse-retail-price-changes")]
public sealed class WarehouseRetailPriceChangesController : ControllerBase
{
    private readonly IWarehouseRetailPriceChangeService _service;
    private readonly ILogger<WarehouseRetailPriceChangesController> _logger;

    public WarehouseRetailPriceChangesController(
        IWarehouseRetailPriceChangeService service,
        ILogger<WarehouseRetailPriceChangesController> logger
    )
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Warehouse.ManageProducts)]
    public async Task<IActionResult> Get(
        [FromQuery] WarehouseRetailPriceChangeQuery query,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var page = await _service.GetAsync(query, cancellationToken);
            return Ok(page);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<WarehouseRetailPriceChangePage>.Error(ex.Message, "BAD_REQUEST"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询仓库商品零售价月度变化失败");
            return StatusCode(500, ApiResponse<WarehouseRetailPriceChangePage>.Error("服务器内部错误"));
        }
    }
}
