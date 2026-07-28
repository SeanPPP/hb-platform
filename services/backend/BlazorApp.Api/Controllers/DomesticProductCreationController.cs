using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
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
        private readonly IDomesticSetTemplateService? _templateService;

        public DomesticProductCreationController(
            IDomesticProductCreationService service,
            ILogger<DomesticProductCreationController> logger,
            IDomesticSetTemplateService? templateService = null)
        {
            _service = service;
            _logger = logger;
            _templateService = templateService;
        }

        /// <summary>
        /// 批量创建国内商品
        /// </summary>
        /// <param name="request">批量创建请求</param>
        /// <returns>批量创建结果</returns>
        [HttpPost("batch")]
        [Authorize(Policy = Permissions.DomesticPurchase.ManageProducts)]
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
        [Authorize(Policy = Permissions.DomesticPurchase.ManageProducts)]
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
        [Authorize(Policy = Permissions.DomesticPurchase.ManageProducts)]
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
        [Authorize(Policy = Permissions.DomesticPurchase.ManageProducts)]
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
        [Authorize(Policy = Permissions.DomesticPurchase.ManageProducts)]
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

        /// <summary>
        /// 更新批次明细商品名称和零售价
        /// </summary>
        /// <param name="batchNumber">批次号</param>
        /// <param name="request">更新请求</param>
        /// <returns>更新结果</returns>
        [HttpPut("batch/{batchNumber}/items")]
        [Authorize(Policy = Permissions.DomesticPurchase.ManageProducts)]
        public async Task<IActionResult> UpdateBatchItems(
            string batchNumber,
            [FromBody] UpdateBatchItemsRequest request)
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

                if (request.Items == null || !request.Items.Any())
                {
                    return BadRequest(ApiResponse<object>.Error("商品列表不能为空", "VALIDATION_ERROR"));
                }

                var result = await _service.UpdateBatchItemsAsync(batchNumber, request);

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
                _logger.LogError(ex, "更新批次明细失败: {BatchNumber}", batchNumber);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 获取供应商可复用的国内套装模板。
        /// </summary>
        [HttpGet("templates")]
        [Authorize(Policy = Permissions.DomesticPurchase.ManageProducts)]
        public async Task<IActionResult> GetTemplates(
            [FromQuery] string supplierCode,
            [FromQuery] bool includeInactive = false)
        {
            if (_templateService == null)
            {
                return TemplateServiceUnavailable();
            }

            var result = await _templateService.GetTemplatesAsync(supplierCode, includeInactive);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// 获取单个国内套装模板详情，子项保持保存时的顺序。
        /// </summary>
        [HttpGet("templates/{templateId}")]
        [Authorize(Policy = Permissions.DomesticPurchase.ManageProducts)]
        public async Task<IActionResult> GetTemplate(
            string templateId,
            [FromQuery] string supplierCode)
        {
            if (_templateService == null)
            {
                return TemplateServiceUnavailable();
            }

            var result = await _templateService.GetTemplateAsync(templateId, supplierCode);
            return ToTemplateActionResult(result);
        }

        /// <summary>
        /// 保存国内套装模板快照。
        /// </summary>
        [HttpPost("templates")]
        [Authorize(Policy = Permissions.DomesticPurchase.ManageProducts)]
        public async Task<IActionResult> CreateTemplate([FromBody] SaveDomesticSetTemplateRequest request)
        {
            if (_templateService == null)
            {
                return TemplateServiceUnavailable();
            }
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse<object>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState));
            }

            var result = await _templateService.CreateAsync(request);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// 修改国内套装模板；已停用模板只有显式传入 isEnabled=true 才恢复启用。
        /// </summary>
        [HttpPut("templates/{templateId}")]
        [Authorize(Policy = Permissions.DomesticPurchase.ManageProducts)]
        public async Task<IActionResult> UpdateTemplate(
            string templateId,
            [FromQuery] string supplierCode,
            [FromBody] SaveDomesticSetTemplateRequest request)
        {
            if (_templateService == null)
            {
                return TemplateServiceUnavailable();
            }
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse<object>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState));
            }

            var result = await _templateService.UpdateAsync(templateId, supplierCode, request);
            return ToTemplateActionResult(result);
        }

        /// <summary>
        /// 停用国内套装模板，不删除模板及子项快照。
        /// </summary>
        [HttpPost("templates/{templateId}/deactivate")]
        [Authorize(Policy = Permissions.DomesticPurchase.ManageProducts)]
        public async Task<IActionResult> DeactivateTemplate(
            string templateId,
            [FromQuery] string supplierCode)
        {
            if (_templateService == null)
            {
                return TemplateServiceUnavailable();
            }

            var result = await _templateService.DeactivateAsync(templateId, supplierCode);
            return result.Success
                ? Ok(result)
                : result.ErrorCode == "DOMESTIC_SET_TEMPLATE_NOT_FOUND"
                    ? NotFound(result)
                    : BadRequest(result);
        }

        private IActionResult TemplateServiceUnavailable()
        {
            _logger.LogError("国内套装模板服务未注册");
            return StatusCode(
                500,
                ApiResponse<object>.Error("服务器内部错误", "DOMESTIC_SET_TEMPLATE_SERVICE_UNAVAILABLE")
            );
        }

        private IActionResult ToTemplateActionResult<T>(ApiResponse<T> result) =>
            result.Success
                ? Ok(result)
                : result.ErrorCode == "DOMESTIC_SET_TEMPLATE_NOT_FOUND"
                    ? NotFound(result)
                    : BadRequest(result);
    }
}
