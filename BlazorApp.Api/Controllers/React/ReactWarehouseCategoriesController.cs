using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/warehouse-categories")]
    [Authorize]
    public class ReactWarehouseCategoriesController : ControllerBase
    {
        private readonly IWarehouseCategoryReactService _service;
        private readonly ILogger<ReactWarehouseCategoriesController> _logger;

        public ReactWarehouseCategoriesController(
            IWarehouseCategoryReactService service,
            ILogger<ReactWarehouseCategoriesController> logger
        )
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet("tree")]
        public async Task<IActionResult> GetTree()
        {
            try
            {
                var tree = await _service.GetTreeAsync();
                return Ok(new { success = true, data = tree });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetTree failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetList([FromQuery] WarehouseCategoryFilterDto filter)
        {
            try
            {
                var result = await _service.GetListAsync(filter);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetList failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> Create([FromBody] CreateWarehouseCategoryDto dto)
        {
            try
            {
                var created = await _service.CreateAsync(dto);
                return Ok(new { success = true, data = created });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Create failed");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPut("{categoryGuid}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> Update(
            string categoryGuid,
            [FromBody] UpdateWarehouseCategoryDto dto
        )
        {
            try
            {
                dto.CategoryGUID = categoryGuid;
                var updated = await _service.UpdateAsync(dto);
                return Ok(new { success = true, data = updated });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update failed");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpDelete("{categoryGuid}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> Delete(string categoryGuid)
        {
            try
            {
                var ok = await _service.DeleteAsync(categoryGuid);
                return Ok(new { success = ok });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Delete failed due to business rule");
                var msg = ex.Message?.ToLower();
                if (msg?.Contains("has children") == true)
                {
                    return BadRequest(
                        new { success = false, message = "该分类存在子分类，无法删除" }
                    );
                }
                if (msg?.Contains("has products") == true)
                {
                    return BadRequest(
                        new { success = false, message = "该分类下存在关联商品，请先取消关联" }
                    );
                }
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete failed");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("batch/move")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchMove([FromBody] BatchMoveCategoriesDto dto)
        {
            try
            {
                var ok = await _service.BatchMoveAsync(dto);
                return Ok(new { success = ok });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchMove failed");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("batch/toggle-active")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchToggleActive([FromBody] BatchToggleActiveDto dto)
        {
            try
            {
                var count = await _service.BatchToggleActiveAsync(dto);
                return Ok(new { success = true, data = new { affected = count } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchToggleActive failed");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("batch/sort")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchSort([FromBody] BatchSortRequestDto dto)
        {
            try
            {
                var ok = await _service.BatchSortAsync(dto);
                return Ok(new { success = ok });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchSort failed");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpGet("{categoryGuid}/products")]
        public async Task<IActionResult> GetProductsByCategory(
            string categoryGuid,
            [FromQuery] WarehouseProductFilterDto filter
        )
        {
            try
            {
                var result = await _service.GetProductsByCategoryAsync(categoryGuid, filter);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "GetProductsByCategory failed (categoryGuid={CategoryGuid}, PageNumber={PageNumber}, PageSize={PageSize})",
                    categoryGuid,
                    filter?.PageNumber,
                    filter?.PageSize
                );
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("{categoryGuid}/products/batch-assign")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchAssignProducts(
            string categoryGuid,
            [FromBody] BatchAssignProductsRequestDto dto
        )
        {
            try
            {
                dto.CategoryGuid = categoryGuid;
                var count = await _service.BatchAssignProductsAsync(dto);
                return Ok(new { success = true, data = new { affected = count } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchAssignProducts failed");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("products/batch-unassign")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchUnassignProducts(
            [FromBody] BatchUnassignProductsRequestDto dto
        )
        {
            try
            {
                var count = await _service.BatchUnassignProductsAsync(dto);
                return Ok(new { success = true, data = new { affected = count } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchUnassignProducts failed");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }
    }
}
