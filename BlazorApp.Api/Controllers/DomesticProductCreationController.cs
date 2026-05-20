using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// 国内商品货号条码批量创建控制器
    /// </summary>
    [ApiController]
    [Route("api/v1/domestic-product-creation")]
    [Authorize]
    public class DomesticProductCreationController : ControllerBase
    {
        private readonly IDomesticProductCreationService _service;
        private readonly ILogger<DomesticProductCreationController> _logger;

        public DomesticProductCreationController(
            IDomesticProductCreationService service,
            ILogger<DomesticProductCreationController> logger)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>
        /// 批量创建国内商品
        /// </summary>
        /// <param name="request">批量创建请求</param>
        /// <returns>批量创建结果</returns>
        [HttpPost("batch")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> CreateBatch([FromBody] CreateDomesticProductBatchRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse<object>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState));
                }

                if (request.Items == null || !request.Items.Any())
                {
                    return BadRequest(ApiResponse<object>.Error("商品列表不能为空", "VALIDATION_ERROR"));
                }

                var result = await _service.CreateBatchAsync(request);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建国内商品失败");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 获取批次列表（分页）
        /// </summary>
        /// <param name="page">页码</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="supplierCode">供应商编码（可选）</param>
        /// <param name="startDate">开始日期（可选）</param>
        /// <param name="endDate">结束日期（可选）</param>
        /// <returns>批次列表</returns>
        [HttpGet("batches")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetBatchList(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? supplierCode = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                var result = await _service.GetBatchListAsync(page, pageSize, supplierCode, startDate, endDate);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取批次列表失败");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 获取批次详情
        /// </summary>
        /// <param name="batchNumber">批次号</param>
        /// <returns>批次详情</returns>
        [HttpGet("batch/{batchNumber}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetBatchDetail(string batchNumber)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(batchNumber))
                {
                    return BadRequest(ApiResponse<object>.Error("批次号不能为空", "VALIDATION_ERROR"));
                }

                var result = await _service.GetBatchDetailAsync(batchNumber);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "BATCH_NOT_FOUND")
                {
                    return NotFound(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取批次详情失败: {BatchNumber}", batchNumber);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 导出批次创建结果
        /// </summary>
        /// <param name="batchNumber">批次号</param>
        /// <returns>Excel文件</returns>
        [HttpGet("batch/{batchNumber}/export")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> ExportBatch(string batchNumber)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(batchNumber))
                {
                    return BadRequest(ApiResponse<object>.Error("批次号不能为空", "VALIDATION_ERROR"));
                }

                var result = await _service.ExportBatchAsync(batchNumber);

                if (result.Success && result.Data != null)
                {
                    return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
                }

                if (result.ErrorCode == "BATCH_NOT_FOUND")
                {
                    return NotFound(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出批次创建结果失败: {BatchNumber}", batchNumber);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 批量更新私牌价格
        /// </summary>
        /// <param name="batchNumber">批次号</param>
        /// <param name="request">更新请求</param>
        /// <returns>更新结果</returns>
        [HttpPut("batch/{batchNumber}/prices")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> UpdatePrivateLabelPrice(
            string batchNumber,
            [FromBody] UpdatePrivateLabelPriceRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(batchNumber))
                {
                    return BadRequest(ApiResponse<object>.Error("批次号不能为空", "VALIDATION_ERROR"));
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse<object>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState));
                }

                var result = await _service.UpdatePrivateLabelPriceAsync(batchNumber, request);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "BATCH_NOT_FOUND")
                {
                    return NotFound(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新私牌价格失败: {BatchNumber}", batchNumber);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }
    }
}
