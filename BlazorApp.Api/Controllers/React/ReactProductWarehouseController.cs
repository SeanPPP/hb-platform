using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
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
        private readonly IDeviceRegistrationService _deviceRegistrationService;
        private readonly IMapper _mapper;
        private readonly TencentCloudUploadService _uploadService;

        public ReactProductWarehouseController(
            IProductWarehouseReactService service,
            ILogger<ReactProductWarehouseController> logger,
            IDeviceRegistrationService deviceRegistrationService,
            IMapper mapper,
            TencentCloudUploadService uploadService
        )
        {
            _service = service;
            _logger = logger;
            _deviceRegistrationService = deviceRegistrationService;
            _mapper = mapper;
            _uploadService = uploadService;
        }

        [HttpGet("mobile/lookup")]
        [AllowAnonymous]
        public async Task<IActionResult> LookupMobile([FromQuery] string keyword)
        {
            var access = await ResolveReadAccessAsync();
            if (!access.IsAllowed)
            {
                return Unauthorized(new { success = false, message = access.Message });
            }

            if (string.IsNullOrWhiteSpace(keyword))
            {
                return BadRequest(new { success = false, message = "查询关键字不能为空" });
            }

            try
            {
                var items = await _service.LookupMobileProductsAsync(keyword);
                return Ok(new { success = true, data = items, message = "查询成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "移动端仓库商品查询失败: {Keyword}", keyword);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpGet("mobile/{productCode}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetMobileProduct(string productCode)
        {
            var access = await ResolveReadAccessAsync();
            if (!access.IsAllowed)
            {
                return Unauthorized(new { success = false, message = access.Message });
            }

            try
            {
                var item = await _service.GetMobileProductAsync(productCode);
                if (item == null)
                {
                    return NotFound(new { success = false, message = "商品不存在" });
                }

                return Ok(new { success = true, data = item, message = "获取成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取移动端仓库商品详情失败: {ProductCode}", productCode);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPatch("mobile/{productCode}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> PatchMobileProduct(
            string productCode,
            [FromBody] WarehouseMobileProductPatchDto dto
        )
        {
            try
            {
                if (dto == null)
                {
                    return BadRequest(new { success = false, message = "请求参数不能为空" });
                }

                var item = await _service.PatchMobileProductAsync(productCode, dto);
                if (item == null)
                {
                    return NotFound(new { success = false, message = "商品不存在" });
                }

                return Ok(new { success = true, data = item, message = "保存成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新移动端仓库商品失败: {ProductCode}", productCode);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPut("mobile/{productCode}/location")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> SetMobileProductLocation(
            string productCode,
            [FromBody] SetWarehouseProductLocationDto dto
        )
        {
            try
            {
                if (dto == null)
                {
                    return BadRequest(new { success = false, message = "请求参数不能为空" });
                }

                var item = await _service.SetMobileProductLocationAsync(productCode, dto.LocationGuid);
                if (item == null)
                {
                    return NotFound(new { success = false, message = "商品不存在" });
                }

                return Ok(new { success = true, data = item, message = "货位更新成功" });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "更新移动端商品货位参数无效: {ProductCode}", productCode);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新移动端商品货位失败: {ProductCode}", productCode);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("mobile/{productCode}/image-upload-signature")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public IActionResult GetMobileImageUploadSignature(
            string productCode,
            [FromBody] DirectUploadRequest request
        )
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.FileName))
                {
                    return BadRequest(new { success = false, message = "文件名不能为空" });
                }

                var objectKey =
                    request.ObjectKey
                    ?? $"warehouse/mobile/{productCode}/{Path.GetFileNameWithoutExtension(request.FileName)}_{DateTime.Now:yyMMddHHmmss}{Path.GetExtension(request.FileName)}";

                var signature = _uploadService.GetDirectUploadSignature(
                    objectKey,
                    request.ContentType,
                    request.FileSize
                );

                return Ok(new { success = true, data = signature, message = "签名生成成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成仓库商品图片上传签名失败: {ProductCode}", productCode);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpGet("mobile/{productCode}/print-payload")]
        [AllowAnonymous]
        public async Task<IActionResult> GetMobileProductPrintPayload(
            string productCode,
            [FromQuery] string type = "product"
        )
        {
            var access = await ResolveReadAccessAsync();
            if (!access.IsAllowed)
            {
                return Unauthorized(new { success = false, message = access.Message });
            }

            try
            {
                if (string.Equals(type, "location", StringComparison.OrdinalIgnoreCase))
                {
                    var locationPayload = await _service.GetMobileLocationPrintPayloadAsync(productCode);
                    if (locationPayload == null)
                    {
                        return NotFound(new { success = false, message = "货位标签数据不存在" });
                    }

                    return Ok(new { success = true, data = locationPayload, message = "获取成功" });
                }

                var productPayload = await _service.GetMobileProductPrintPayloadAsync(productCode);
                if (productPayload == null)
                {
                    return NotFound(new { success = false, message = "商品标签数据不存在" });
                }

                return Ok(new { success = true, data = productPayload, message = "获取成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取仓库标签打印数据失败: {ProductCode}", productCode);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
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

        private async Task<WarehouseReadAccessContext> ResolveReadAccessAsync()
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                if (HasAnyRole("Admin", "WarehouseManager", "WarehouseStaff"))
                {
                    return new WarehouseReadAccessContext { IsAllowed = true };
                }

                return new WarehouseReadAccessContext { IsAllowed = false, Message = "当前账号没有仓库访问权限" };
            }

            var hardwareId = Request.Headers["X-Device-Id"].FirstOrDefault();
            var authCode = Request.Headers["X-Auth-Code"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(hardwareId) || string.IsNullOrWhiteSpace(authCode))
            {
                return new WarehouseReadAccessContext { IsAllowed = false, Message = "未登录且缺少设备授权信息" };
            }

            var isValid = await _deviceRegistrationService.ValidateDeviceAuthCodeAsync(hardwareId, authCode);
            if (!isValid)
            {
                return new WarehouseReadAccessContext { IsAllowed = false, Message = "设备授权无效" };
            }

            var deviceEntity = await _deviceRegistrationService.GetDeviceByHardwareIdAsync(hardwareId);
            if (deviceEntity == null)
            {
                return new WarehouseReadAccessContext { IsAllowed = false, Message = "设备不存在" };
            }

            var device = _mapper.Map<DeviceDataDto>(deviceEntity);
            if (device.Status != 1)
            {
                return new WarehouseReadAccessContext { IsAllowed = false, Message = "设备未启用" };
            }

            return new WarehouseReadAccessContext { IsAllowed = true };
        }

        private bool HasAnyRole(params string[] roles)
        {
            return roles.Any(role =>
                User?.Claims.Any(claim =>
                    claim.Type == ClaimTypes.Role
                    && claim.Value.Equals(role, StringComparison.OrdinalIgnoreCase)
                ) == true
            );
        }

        private sealed class WarehouseReadAccessContext
        {
            public bool IsAllowed { get; set; }
            public string Message { get; set; } = "未授权";
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

        [HttpPost("batch-toggle-active")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchToggleActive(
            [FromBody] BatchToggleWarehouseProductsActiveRequestDto request
        )
        {
            try
            {
                if (request == null || request.ProductCodes == null || !request.ProductCodes.Any())
                    return BadRequest(new { success = false, message = "商品编码不能为空" });

                var resp = await _service.BatchToggleActiveAsync(request);
                if (resp.Success)
                {
                    return Ok(
                        new
                        {
                            success = true,
                            message = resp.Message,
                            successCount = resp.SuccessCount,
                            failedCount = resp.FailedCount,
                            errors = resp.Errors,
                        }
                    );
                }

                return BadRequest(
                    new
                    {
                        success = false,
                        message = resp.Message,
                        successCount = resp.SuccessCount,
                        failedCount = resp.FailedCount,
                        errors = resp.Errors,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "仓库商品批量上下架失败");
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
