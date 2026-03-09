using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    /// <summary>
    /// React 前端专用：仅限 Product 与 WarehouseProduct 的商品检测/更新/新建控制器
    /// </summary>
    [ApiController]
    [Route("api/react/v1/product-warehouse")]
    [Authorize]
    public class ReactProductWarehouseController : ControllerBase
    {
        private readonly IProductWarehouseReactService _service;
        private readonly ILogger<ReactProductWarehouseController> _logger;

        public ReactProductWarehouseController(
            IProductWarehouseReactService service,
            ILogger<ReactProductWarehouseController> logger
        )
        {
            _service = service;
            _logger = logger;
        }

        [HttpPost("detect")]
        [Authorize(Roles = "Admin,WarehouseManager,User")]
        public async Task<IActionResult> Detect([FromBody] DetectRequest request)
        {
            try
            {
                if (request == null || request.Items == null || !request.Items.Any())
                    return BadRequest(new { success = false, message = "请求数据不能为空" });

                var data = await _service.DetectAsync(request.Items);
                return Ok(
                    new
                    {
                        success = true,
                        data,
                        message = "检测完成",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检测商品失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("batch-update")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchUpdate([FromBody] BatchUpdateRequest request)
        {
            try
            {
                if (request == null || request.Items == null || !request.Items.Any())
                    return BadRequest(new { success = false, message = "请求数据不能为空" });

                var resp = await _service.BatchUpdateAsync(request.Items);
                return Ok(
                    new
                    {
                        success = resp.Success,
                        message = resp.Message,
                        successCount = resp.SuccessCount,
                        failedCount = resp.FailedCount,
                        errors = resp.Errors,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("batch-create")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchCreate([FromBody] BatchCreateRequest request)
        {
            try
            {
                if (request == null || request.Items == null || !request.Items.Any())
                    return BadRequest(new { success = false, message = "请求数据不能为空" });

                var resp = await _service.BatchCreateAsync(request.Items);
                return Ok(
                    new
                    {
                        success = resp.Success,
                        message = resp.Message,
                        successCount = resp.SuccessCount,
                        failedCount = resp.FailedCount,
                        skippedCount = resp.SkippedCount,
                        errors = resp.Errors,
                        skippedItems = resp.SkippedItems,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("table")]
        [Authorize(Roles = "Admin,WarehouseManager,User")]
        public async Task<IActionResult> Table([FromBody] ReactTableRequestDto request)
        {
            var user = HttpContext.User;
            var authHeader = Request.Headers["Authorization"];
            Console.WriteLine($"=== 请求头 Authorization: {authHeader} ===");
            Console.WriteLine($"=== 用户已认证: {user?.Identity?.IsAuthenticated} ===");
            var roles =
                user?.Claims?.Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
                    ?.Select(c => c.Value)
                    ?.ToList() ?? new List<string>();
            Console.WriteLine($"=== Table接口访问 - 用户角色: {string.Join(", ", roles)} ===");

            try
            {
                var data = await _service.GetAntdTableDataAsync(request);
                return Ok(
                    new
                    {
                        success = true,
                        data = data.Items,
                        total = data.Total,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "表格数据获取失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("create-single")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> CreateSingle(
            [FromBody] CreateSingleProductRequestDto request
        )
        {
            try
            {
                if (request == null)
                    return BadRequest(new { success = false, message = "请求数据不能为空" });

                var resp = await _service.CreateSingleProductAsync(request);
                return Ok(
                    new
                    {
                        success = resp.Success,
                        message = resp.Message,
                        productCode = resp.ProductCode,
                        itemNumber = resp.ItemNumber,
                        barcode = resp.Barcode,
                        barcodeExists = resp.BarcodeExists,
                        warnings = resp.Warnings,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "新建单个商品失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("domestic-not-in-warehouse")]
        [Authorize(Roles = "Admin,WarehouseManager,User")]
        public async Task<IActionResult> DomesticNotInWarehouse(
            [FromBody] GetDomesticProductsNotInWarehouseRequestDto request
        )
        {
            try
            {
                if (request == null)
                    return BadRequest(new { success = false, message = "请求数据不能为空" });

                var data = await _service.GetDomesticProductsNotInWarehouseAsync(request);
                return Ok(
                    new
                    {
                        success = true,
                        data = data.Items,
                        total = data.Total,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取国内商品不在仓库列表失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("import-from-domestic")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> ImportFromDomestic(
            [FromBody] ImportFromDomesticRequestDto request
        )
        {
            try
            {
                if (request == null)
                    return BadRequest(new { success = false, message = "请求数据不能为空" });

                var resp = await _service.ImportFromDomesticAsync(request);
                return Ok(
                    new
                    {
                        success = resp.Success,
                        message = resp.Message,
                        successCount = resp.SuccessCount,
                        failedCount = resp.FailedCount,
                        results = resp.Results,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从国内商品导入失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("non-hb-not-in-warehouse")]
        [Authorize(Roles = "Admin,WarehouseManager,User")]
        public async Task<IActionResult> NonHotbargainNotInWarehouse(
            [FromBody] GetNonHotbargainProductsNotInWarehouseRequestDto request
        )
        {
            try
            {
                if (request == null)
                    return BadRequest(new { success = false, message = "请求数据不能为空" });

                var data = await _service.GetNonHotbargainProductsNotInWarehouseAsync(request);
                return Ok(
                    new
                    {
                        success = true,
                        data = data.Items,
                        total = data.Total,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取非Hotbargain商品不在仓库列表失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("import-non-hb")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> ImportNonHotbargain(
            [FromBody] ImportNonHotbargainRequestDto request
        )
        {
            try
            {
                if (request == null)
                    return BadRequest(new { success = false, message = "请求数据不能为空" });

                var resp = await _service.ImportNonHotbargainProductsAsync(request);
                return Ok(
                    new
                    {
                        success = resp.Success,
                        message = resp.Message,
                        successCount = resp.SuccessCount,
                        failedCount = resp.FailedCount,
                        results = resp.Results,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导入非Hotbargain商品失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 仓库商品完整更新（六表 + 国内商品联动）
        /// </summary>
        [HttpPut("{productCode}/full-update")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> FullUpdate(
            string productCode,
            [FromBody] WarehouseProductFullUpdateDto dto
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productCode))
                    return BadRequest(new { success = false, message = "商品编码不能为空" });
                if (dto == null)
                    return BadRequest(new { success = false, message = "请求数据不能为空" });

                var resp = await _service.FullUpdateAsync(productCode, dto);
                if (resp.Success)
                    return Ok(new { success = true, message = resp.Message });
                return BadRequest(new { success = false, message = resp.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "仓库商品完整更新失败 ProductCode={ProductCode}", productCode);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取商品条码对应套装价/进货价列表（商品类型≠0 时编辑弹窗用）
        /// </summary>
        [HttpGet("{productCode}/barcode-prices")]
        [Authorize(Roles = "Admin,WarehouseManager,User")]
        public async Task<IActionResult> GetBarcodePrices(string productCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productCode))
                    return BadRequest(new { success = false, message = "商品编码不能为空" });
                var list = await _service.GetBarcodePricesAsync(productCode);
                return Ok(new { success = true, data = list });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取条码价列表失败 ProductCode={ProductCode}", productCode);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        #region 请求包装类
        public class DetectRequest
        {
            public List<DetectionItemDto> Items { get; set; } = new();
        }

        public class BatchUpdateRequest
        {
            public List<UpdateItemDto> Items { get; set; } = new();
        }

        public class BatchCreateRequest
        {
            public List<CreateItemDto> Items { get; set; } = new();
        }
        #endregion
    }
}
