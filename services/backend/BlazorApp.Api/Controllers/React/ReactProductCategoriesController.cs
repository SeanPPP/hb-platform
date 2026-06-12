using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/product-categories")]
    [Authorize]
    public class ReactProductCategoriesController : ControllerBase
    {
        private readonly IProductCategoryReactService _service;
        private readonly ILogger<ReactProductCategoriesController> _logger;

        public ReactProductCategoriesController(
            IProductCategoryReactService service,
            ILogger<ReactProductCategoriesController> logger
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
                _logger.LogError(ex, "ProductCategory GetTree failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetList(
            [FromQuery] ProductCategoryFilterDto filter
        )
        {
            try
            {
                var result = await _service.GetListAsync(filter);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProductCategory GetList failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> Create([FromBody] CreateProductCategoryDto dto)
        {
            try
            {
                var created = await _service.CreateAsync(dto);
                return Ok(new { success = true, data = created });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProductCategory Create failed");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPut("{categoryGuid}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> Update(
            string categoryGuid,
            [FromBody] UpdateProductCategoryDto dto
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
                _logger.LogError(ex, "ProductCategory Update failed");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpDelete("{categoryGuid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(string categoryGuid)
        {
            try
            {
                var ok = await _service.DeleteAsync(categoryGuid);
                return Ok(new { success = ok });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "ProductCategory Delete failed due to business rule");
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProductCategory Delete failed");
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
                _logger.LogError(ex, "ProductCategory BatchMove failed");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("batch/toggle-active")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchToggleActive(
            [FromBody] BatchToggleActiveDto dto
        )
        {
            try
            {
                var count = await _service.BatchToggleActiveAsync(dto);
                return Ok(new { success = true, data = new { affected = count } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProductCategory BatchToggleActive failed");
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
                _logger.LogError(ex, "ProductCategory BatchSort failed");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }
    }
}
