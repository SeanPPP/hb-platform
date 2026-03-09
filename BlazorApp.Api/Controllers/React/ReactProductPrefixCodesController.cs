using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    /// <summary>
    /// React 商品前缀管理控制器
    /// 专门为React前端提供的API接口
    /// </summary>
    [ApiController]
    [Route("api/react/v1/product-prefix-codes")]
    [Authorize]
    public class ReactProductPrefixCodesController : ControllerBase
    {
        private readonly IProductPrefixCodeReactService _productPrefixCodeReactService;
        private readonly ILogger<ReactProductPrefixCodesController> _logger;

        public ReactProductPrefixCodesController(
            IProductPrefixCodeReactService productPrefixCodeReactService,
            ILogger<ReactProductPrefixCodesController> logger)
        {
            _productPrefixCodeReactService = productPrefixCodeReactService;
            _logger = logger;
        }

        /// <summary>
        /// 根据供应商编码获取前缀列表
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <returns>前缀列表</returns>
        [HttpGet("by-supplier/{supplierCode}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetPrefixesBySupplierCode(string supplierCode)
        {
            try
            {
                var result = await _productPrefixCodeReactService.GetPrefixesBySupplierCodeAsync(supplierCode);

                if (result.Success)
                {
                    return Ok(new
                    {
                        success = true,
                        data = result.Data,
                        message = "获取前缀列表成功"
                    });
                }

                return BadRequest(new
                {
                    success = false,
                    message = result.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取供应商前缀列表失败，SupplierCode: {SupplierCode}", supplierCode);
                return StatusCode(500, new
                {
                    success = false,
                    message = "获取前缀列表失败"
                });
            }
        }

        /// <summary>
        /// 创建商品前缀
        /// </summary>
        /// <param name="dto">创建商品前缀DTO</param>
        /// <returns>创建的商品前缀</returns>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateProductPrefixCode([FromBody] CreateProductPrefixCodeDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage));
                    return BadRequest(new
                    {
                        success = false,
                        message = $"输入验证失败: {string.Join(", ", errors)}"
                    });
                }

                var result = await _productPrefixCodeReactService.CreateProductPrefixCodeAsync(dto);

                if (result.Success)
                {
                    return Ok(new
                    {
                        success = true,
                        data = result.Data,
                        message = "前缀创建成功"
                    });
                }

                if (result.ErrorCode == "PREFIX_NAME_EXISTS")
                {
                    return Conflict(new
                    {
                        success = false,
                        message = result.Message,
                        errorCode = result.ErrorCode
                    });
                }

                return BadRequest(new
                {
                    success = false,
                    message = result.Message,
                    errorCode = result.ErrorCode
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建商品前缀失败");
                return StatusCode(500, new
                {
                    success = false,
                    message = "前缀创建失败"
                });
            }
        }

        /// <summary>
        /// 更新商品前缀
        /// </summary>
        /// <param name="prefixCode">前缀编码</param>
        /// <param name="dto">更新商品前缀DTO</param>
        /// <returns>更新的商品前缀</returns>
        [HttpPut("{prefixCode}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateProductPrefixCode(string prefixCode, [FromBody] UpdateProductPrefixCodeDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "请求参数验证失败"
                    });
                }

                var result = await _productPrefixCodeReactService.UpdateProductPrefixCodeAsync(prefixCode, dto);

                if (result.Success)
                {
                    return Ok(new
                    {
                        success = true,
                        data = result.Data,
                        message = "前缀更新成功"
                    });
                }

                if (result.ErrorCode == "PREFIX_NOT_FOUND")
                {
                    return NotFound(new
                    {
                        success = false,
                        message = result.Message
                    });
                }

                return BadRequest(new
                {
                    success = false,
                    message = result.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新商品前缀失败，PrefixCode: {PrefixCode}", prefixCode);
                return StatusCode(500, new
                {
                    success = false,
                    message = "前缀更新失败"
                });
            }
        }

        /// <summary>
        /// 删除商品前缀
        /// </summary>
        /// <param name="prefixCode">前缀编码</param>
        /// <returns>删除结果</returns>
        [HttpDelete("{prefixCode}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteProductPrefixCode(string prefixCode)
        {
            try
            {
                var result = await _productPrefixCodeReactService.DeleteProductPrefixCodeAsync(prefixCode);

                if (result.Success)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "前缀删除成功"
                    });
                }

                if (result.ErrorCode == "PREFIX_NOT_FOUND")
                {
                    return NotFound(new
                    {
                        success = false,
                        message = result.Message
                    });
                }

                if (result.ErrorCode == "PREFIX_IN_USE")
                {
                    return Conflict(new
                    {
                        success = false,
                        message = result.Message,
                        errorCode = result.ErrorCode
                    });
                }

                return BadRequest(new
                {
                    success = false,
                    message = result.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除商品前缀失败，PrefixCode: {PrefixCode}", prefixCode);
                return StatusCode(500, new
                {
                    success = false,
                    message = "前缀删除失败"
                });
            }
        }
    }
}

