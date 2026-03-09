using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/holiday-products")]
    [Authorize]
    public class HolidayProductController : ControllerBase
    {
        private readonly IHolidayProductReactService _holidayProductReactService;
        private readonly ILogger<HolidayProductController> _logger;

        public HolidayProductController(
          IHolidayProductReactService holidayProductReactService,
          ILogger<HolidayProductController> logger
        )
        {
            _holidayProductReactService = holidayProductReactService;
            _logger = logger;
        }

        [HttpPost("import")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ImportHolidayProducts(
          [FromBody] HolidayProductImportRequestDto request
        )
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage));
                    return BadRequest(
                      new { success = false, message = $"输入验证失败: {string.Join(", ", errors)}" }
                    );
                }

                _logger.LogInformation(
                  "导入节日商品: SupplierCode={SupplierCode}, HolidayType={HolidayType}, Year={Year}, Count={Count}",
                  request.SupplierCode,
                  request.HolidayType,
                  request.Year,
                  request.Products?.Count ?? 0
                );

                var result = await _holidayProductReactService.ImportHolidayProductsFromExcelAsync(request);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导入节日商品失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("analyze")]
        public async Task<IActionResult> GetHolidayProductsAnalysis(
          [FromBody] HolidayProductAnalysisRequestDto request
        )
        {
            try
            {
                _logger.LogInformation(
                  "查询节日商品分析: SupplierCode={SupplierCode}, HolidayType={HolidayType}, Year={Year}, StoreCount={StoreCount}",
                  request.SupplierCode,
                  request.HolidayType,
                  request.Year,
                  request.StoreCodes?.Count ?? 0
                );

                var result = await _holidayProductReactService.GetHolidayProductsAnalysisAsync(request);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询节日商品分析失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpGet("{productCode}/weekly-sales")]
        public async Task<IActionResult> GetProductWeeklySales(
          string productCode,
          [FromQuery] DateTime startDate,
          [FromQuery] DateTime endDate,
          [FromQuery] List<string>? storeCodes
        )
        {
            try
            {
                _logger.LogInformation(
                  "查询商品每周销量: ProductCode={ProductCode}, StartDate={StartDate}, EndDate={EndDate}",
                  productCode,
                  startDate,
                  endDate
                );

                var result = await _holidayProductReactService.GetProductWeeklySalesAsync(
                  productCode,
                  startDate,
                  endDate,
                  storeCodes
                );

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询商品每周销量失败: {ProductCode}", productCode);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("list")]
        public async Task<IActionResult> GetHolidayProductsList(
          [FromBody] HolidayProductListRequestDto request
        )
        {
            try
            {
                _logger.LogInformation(
                  "查询节日商品列表: SupplierCode={SupplierCode}, HolidayType={HolidayType}, Year={Year}",
                  request.SupplierCode,
                  request.HolidayType,
                  request.Year
                );

                var result = await _holidayProductReactService.GetHolidayProductsListAsync(request);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询节日商品列表失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("purchase-data")]
        public async Task<IActionResult> GetPurchaseData([FromBody] PurchaseDataRequestDto request)
        {
            try
            {
                _logger.LogInformation(
                  "查询进货数据: ProductCount={ProductCount}, Year={Year}",
                  request.ProductCodes?.Count ?? 0,
                  request.Year
                );

                var result = await _holidayProductReactService.GetPurchaseDataByProductCodesAsync(request);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询进货数据失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("sales-data")]
        public async Task<IActionResult> GetSalesData([FromBody] SalesDataRequestDto request)
        {
            try
            {
                _logger.LogInformation(
                  "查询销售数据: ProductCount={ProductCount}",
                  request.ProductCodes?.Count ?? 0
                );

                var result = await _holidayProductReactService.GetSalesDataByProductCodesAsync(request);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询销售数据失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("simple")]
        public async Task<IActionResult> GetHolidayProductsSimple(
          [FromBody] HolidayProductAnalysisRequestDto request
        )
        {
            try
            {
                _logger.LogInformation(
                  "查询节日商品简化分析: SupplierCode={SupplierCode}, HolidayType={HolidayType}, Year={Year}, StoreCount={StoreCount}",
                  request.SupplierCode,
                  request.HolidayType,
                  request.Year,
                  request.StoreCodes?.Count ?? 0
                );

                var result = await _holidayProductReactService.GetHolidayProductsSimpleAsync(request);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询节日商品简化分析失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("{productCode}/branch-details")]
        public async Task<IActionResult> GetProductBranchDetails(
          string productCode,
          [FromBody] ProductBranchDetailsRequestDto request
        )
        {
            try
            {
                _logger.LogInformation(
                  "查询商品分店明细: ProductCode={ProductCode}, StoreCount={StoreCount}",
                  productCode,
                  request.StoreCodes?.Count ?? 0
                );

                request.ProductCode = productCode;

                var result = await _holidayProductReactService.GetProductBranchDetailsAsync(request);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询商品分店明细失败: {ProductCode}", productCode);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }
    }
}
