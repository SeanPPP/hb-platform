using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/product-grades")]
    [Authorize]
    public class ReactProductGradesController : ControllerBase
    {
        private readonly IProductGradeReactService _productGradeReactService;
        private readonly ILogger<ReactProductGradesController> _logger;

        public ReactProductGradesController(
            IProductGradeReactService productGradeReactService,
            ILogger<ReactProductGradesController> logger)
        {
            _productGradeReactService = productGradeReactService;
            _logger = logger;
        }

        [HttpGet]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetProductGrades(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? search = null,
            [FromQuery] string? grade = null,
            [FromQuery] string? supplierCode = null,
            [FromQuery] string? sortField = null,
            [FromQuery] string? sortDirection = null)
        {
            try
            {
                var query = new ProductGradeListQueryDto
                {
                    Page = page,
                    PageSize = pageSize,
                    Search = search,
                    Grade = grade,
                    SupplierCode = supplierCode,
                    SortField = sortField,
                    SortDirection = sortDirection,
                };

                var result = await _productGradeReactService.GetProductGradesAsync(query);

                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data, message = "获取商品等级列表成功" });
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品等级列表失败");
                return StatusCode(500, new { success = false, message = "获取商品等级列表失败" });
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateOrUpdateProductGrade([FromBody] CreateProductGradeDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage));
                    return BadRequest(new { success = false, message = $"输入验证失败: {string.Join(", ", errors)}" });
                }

                var result = await _productGradeReactService.CreateOrUpdateProductGradeAsync(dto);

                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data, message = "商品等级保存成功" });
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建/更新商品等级失败");
                return StatusCode(500, new { success = false, message = "商品等级保存失败" });
            }
        }

        [HttpPut("batch")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> BatchUpdateGrades([FromBody] BatchUpdateGradeDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage));
                    return BadRequest(new { success = false, message = $"输入验证失败: {string.Join(", ", errors)}" });
                }

                var result = await _productGradeReactService.BatchUpdateGradesAsync(dto);

                if (result.Success)
                {
                    return Ok(new { success = true, message = "批量更新商品等级成功" });
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新商品等级失败");
                return StatusCode(500, new { success = false, message = "批量更新商品等级失败" });
            }
        }

        [HttpPost("paste-import")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> PasteImportGrades([FromBody] PasteImportGradeDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage));
                    return BadRequest(new { success = false, message = $"输入验证失败: {string.Join(", ", errors)}" });
                }

                var result = await _productGradeReactService.PasteImportGradesAsync(dto);

                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data, message = "粘贴导入商品等级成功" });
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "粘贴导入商品等级失败");
                return StatusCode(500, new { success = false, message = "粘贴导入商品等级失败" });
            }
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteProductGrade(string id)
        {
            try
            {
                var result = await _productGradeReactService.DeleteProductGradeAsync(id);

                if (result.Success)
                {
                    return Ok(new { success = true, message = "商品等级删除成功" });
                }

                if (result.ErrorCode == "GRADE_NOT_FOUND")
                {
                    return NotFound(new { success = false, message = result.Message });
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除商品等级失败，Id: {Id}", id);
                return StatusCode(500, new { success = false, message = "删除商品等级失败" });
            }
        }

        [HttpPut("batch-price")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> BatchUpdateGradePrice([FromBody] BatchUpdateGradePriceDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage));
                    return BadRequest(new { success = false, message = $"输入验证失败: {string.Join(", ", errors)}" });
                }

                var result = await _productGradeReactService.BatchUpdateGradePriceAsync(dto);

                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data, message = result.Data?.Message ?? "批量修改价格成功" });
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量修改商品等级价格失败");
                return StatusCode(500, new { success = false, message = "批量修改价格失败" });
            }
        }

        [HttpPost("by-codes")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetGradesByProductCodes([FromBody] List<string> productCodes)
        {
            try
            {
                var result = await _productGradeReactService.GetProductGradesByProductCodesAsync(productCodes);

                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data, message = "获取商品等级成功" });
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量查询商品等级失败");
                return StatusCode(500, new { success = false, message = "批量查询商品等级失败" });
            }
        }
    }
}
