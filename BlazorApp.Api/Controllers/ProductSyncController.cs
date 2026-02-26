using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// 商品同步API控制器
    /// 提供商品检测、批量创建、批量更新等API端点
    /// </summary>
    [ApiController]
    [Route("api/v1/product-sync")]
    [Authorize]
    public class ProductSyncController : ControllerBase
    {
        private readonly IProductSyncService _productSyncService;
        private readonly ILogger<ProductSyncController> _logger;

        public ProductSyncController(
            IProductSyncService productSyncService,
            ILogger<ProductSyncController> logger)
        {
            _productSyncService = productSyncService;
            _logger = logger;
        }

        /// <summary>
        /// 批量检测商品是否存在
        /// POST /api/v1/product-sync/detect
        /// </summary>
        /// <param name="request">检测请求，包含商品编码和货号列表</param>
        /// <returns>检测结果，包含商品是否存在以及仓库商品信息</returns>
        [HttpPost("detect")]
        [ProducesResponseType(typeof(BatchProductOperationResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> DetectProducts([FromBody] BatchProductDetectionRequest request)
        {
            try
            {
                // 验证请求参数
                if (request == null || !request.Items.Any())
                {
                    _logger.LogWarning("检测商品请求为空或商品列表为空");
                    return BadRequest(new { success = false, message = "请求数据不能为空" });
                }

                _logger.LogInformation("接收到批量检测商品请求，商品数量: {Count}", request.Items.Count);

                // 调用服务层执行检测
                var response = await _productSyncService.DetectProductsAsync(request);

                if (response.Success)
                {
                    _logger.LogInformation("批量检测商品成功");
                    return Ok(new { success = true, data = response.Data, message = response.Message });
                }
                else
                {
                    _logger.LogWarning("批量检测商品失败: {Message}", response.Message);
                    return StatusCode(500, new { success = false, message = response.Message, errors = response.Errors });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量检测商品时发生异常");
                return StatusCode(500, new { success = false, message = "服务器内部错误：" + ex.Message });
            }
        }

        /// <summary>
        /// 批量更新仓库商品信息
        /// POST /api/v1/product-sync/batch-update
        /// </summary>
        /// <param name="request">更新请求，包含要更新的商品信息</param>
        /// <returns>更新结果，包含成功/失败数量和错误信息</returns>
        [HttpPost("batch-update")]
        [ProducesResponseType(typeof(BatchProductOperationResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> BatchUpdateWarehouseProducts([FromBody] BatchProductUpdateRequest request)
        {
            try
            {
                // 验证请求参数
                if (request == null || !request.Items.Any())
                {
                    _logger.LogWarning("批量更新商品请求为空或商品列表为空");
                    return BadRequest(new { success = false, message = "请求数据不能为空" });
                }

                _logger.LogInformation("接收到批量更新商品请求，商品数量: {Count}", request.Items.Count);

                // 调用服务层执行更新
                var response = await _productSyncService.BatchUpdateWarehouseProductsAsync(request);

                if (response.Success)
                {
                    _logger.LogInformation("批量更新商品成功，成功: {SuccessCount}，失败: {FailedCount}",
                        response.SuccessCount, response.FailedCount);
                    return Ok(new
                    {
                        success = true,
                        message = response.Message,
                        successCount = response.SuccessCount,
                        failedCount = response.FailedCount,
                        errors = response.Errors
                    });
                }
                else
                {
                    _logger.LogWarning("批量更新商品失败: {Message}", response.Message);
                    return StatusCode(500, new
                    {
                        success = false,
                        message = response.Message,
                        successCount = response.SuccessCount,
                        failedCount = response.FailedCount,
                        errors = response.Errors
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新商品时发生异常");
                return StatusCode(500, new { success = false, message = "服务器内部错误：" + ex.Message });
            }
        }

        /// <summary>
        /// 批量创建商品信息（含二次检查和套装商品处理）
        /// POST /api/v1/product-sync/batch-create
        /// </summary>
        /// <param name="request">创建请求，包含要创建的商品信息</param>
        /// <returns>创建结果，包含成功/失败/跳过数量和详细信息</returns>
        [HttpPost("batch-create")]
        [ProducesResponseType(typeof(BatchProductOperationResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> BatchCreateProducts([FromBody] BatchProductCreateRequest request)
        {
            try
            {
                // 验证请求参数
                if (request == null || !request.Items.Any())
                {
                    _logger.LogWarning("批量创建商品请求为空或商品列表为空");
                    return BadRequest(new { success = false, message = "请求数据不能为空" });
                }

                // 验证所有商品的贴牌价格
                var invalidItems = request.Items.Where(x => x.OEMPrice <= 0).Select(x => x.ItemNumber).ToList();
                if (invalidItems.Any())
                {
                    _logger.LogWarning("发现贴牌价格无效的商品: {Items}", string.Join(", ", invalidItems));
                    return BadRequest(new
                    {
                        success = false,
                        message = $"以下商品的贴牌价格无效（必须大于0）：{string.Join(", ", invalidItems)}"
                    });
                }

                _logger.LogInformation("接收到批量创建商品请求，商品数量: {Count}", request.Items.Count);

                // 调用服务层执行创建
                var response = await _productSyncService.BatchCreateProductsAsync(request);

                if (response.Success)
                {
                    _logger.LogInformation("批量创建商品成功，成功: {SuccessCount}，跳过: {SkippedCount}，失败: {FailedCount}",
                        response.SuccessCount, response.SkippedCount, response.FailedCount);
                    return Ok(new
                    {
                        success = true,
                        message = response.Message,
                        successCount = response.SuccessCount,
                        failedCount = response.FailedCount,
                        skippedCount = response.SkippedCount,
                        errors = response.Errors,
                        skippedItems = response.SkippedItems
                    });
                }
                else
                {
                    _logger.LogWarning("批量创建商品失败: {Message}", response.Message);
                    return StatusCode(500, new
                    {
                        success = false,
                        message = response.Message,
                        successCount = response.SuccessCount,
                        failedCount = response.FailedCount,
                        skippedCount = response.SkippedCount,
                        errors = response.Errors,
                        skippedItems = response.SkippedItems
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建商品时发生异常");
                return StatusCode(500, new { success = false, message = "服务器内部错误：" + ex.Message });
            }
        }
    }
}

