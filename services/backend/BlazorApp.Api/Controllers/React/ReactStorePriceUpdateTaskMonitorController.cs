using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    /// <summary>
    /// Web 端「价格变更任务」监控页：只读地查看各分店的执行情况。
    /// 执行动作（改价、处理标签）只发生在移动端，保证"谁处理的"记录真实。
    /// </summary>
    [ApiController]
    [Route("api/react/v1/store-price-update-tasks")]
    [Authorize]
    public class ReactStorePriceUpdateTaskMonitorController : ControllerBase
    {
        private readonly IStorePriceUpdateTaskService _service;

        public ReactStorePriceUpdateTaskMonitorController(IStorePriceUpdateTaskService service)
        {
            _service = service;
        }

        [HttpGet("summary")]
        [Authorize(Policy = Permissions.Warehouse.ManageProducts)]
        public async Task<IActionResult> GetSummary([FromQuery] StorePriceUpdateTaskQueryDto query)
        {
            var summary = await _service.GetSummaryAsync(query, HttpContext.RequestAborted);
            return Ok(ApiResponse<StorePriceUpdateTaskSummaryDto>.OK(summary, "查询成功"));
        }

        [HttpGet("by-store")]
        [Authorize(Policy = Permissions.Warehouse.ManageProducts)]
        public async Task<IActionResult> GetByStore([FromQuery] StorePriceUpdateTaskQueryDto query)
        {
            var rows = await _service.GetByStoreAsync(query, HttpContext.RequestAborted);
            return Ok(ApiResponse<List<StorePriceUpdateTaskStoreRowDto>>.OK(rows, "查询成功"));
        }

        [HttpGet("by-product")]
        [Authorize(Policy = Permissions.Warehouse.ManageProducts)]
        public async Task<IActionResult> GetByProduct(
            [FromQuery] StorePriceUpdateTaskQueryDto query,
            [FromQuery] bool onlyIncomplete = true
        )
        {
            var page = await _service.GetByProductAsync(query, onlyIncomplete, HttpContext.RequestAborted);
            return Ok(ApiResponse<StorePriceUpdateTaskProductPageDto>.OK(page, "查询成功"));
        }

        /// <summary>任务明细。status 传 All 可同时查看未完成、已完成与已取消。</summary>
        [HttpGet("tasks")]
        [Authorize(Policy = Permissions.Warehouse.ManageProducts)]
        public async Task<IActionResult> GetTasks([FromQuery] StorePriceUpdateTaskQueryDto query)
        {
            // 监控页面向管理者，不按分店范围裁剪（accessibleStoreCodes = null）。
            var page = await _service.GetPageAsync(query, null, HttpContext.RequestAborted);
            return Ok(ApiResponse<StorePriceUpdateTaskPageDto>.OK(page, "查询成功"));
        }

        /// <summary>
        /// 改价保存前的预告：当前有多少分店会收到通知。只读、只返回计数，
        /// Web 仓库商品编辑与移动端仓库维护共用，因此仅要求已登录。
        /// </summary>
        [HttpGet("preview")]
        public async Task<IActionResult> Preview(
            [FromQuery] string productCode,
            [FromQuery] decimal? retailPrice,
            [FromQuery] bool suggestedDiscountSpecified = false,
            [FromQuery] decimal? suggestedDiscountRate = null
        )
        {
            var preview = await _service.PreviewAsync(
                productCode,
                retailPrice,
                suggestedDiscountSpecified,
                suggestedDiscountRate,
                HttpContext.RequestAborted
            );
            return Ok(ApiResponse<PriceNotificationPreviewDto>.OK(preview, "查询成功"));
        }

        // ---------- 仓库商品建议折扣 ----------
        // 独立成接口而不是塞进十几个仓库改价入口各自的 DTO：
        // 这里自己走"快照 → 写入 → 审计"，审计收口上的挂钩会同步生成分店通知，
        // 既不需要改动任何现有写入链路的事务逻辑，行为又与它们完全一致。

        [HttpPost("suggested-discounts/lookup")]
        public async Task<IActionResult> LookupSuggestedDiscounts([FromBody] SuggestedDiscountLookupRequestDto request)
        {
            var codes = (request.ProductCodes ?? new List<string>()).Take(2000).ToList();
            var discounts = await _service.GetSuggestedDiscountsAsync(codes, HttpContext.RequestAborted);
            var items = codes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(code => new SuggestedDiscountItemDto
                {
                    ProductCode = code,
                    SuggestedDiscountRate = discounts.TryGetValue(code, out var rate) ? rate : null,
                })
                .ToList();
            return Ok(ApiResponse<List<SuggestedDiscountItemDto>>.OK(items, "查询成功"));
        }

        [HttpPut("suggested-discounts")]
        [Authorize(Roles = "Admin,WarehouseManager,WarehouseStaff")]
        public async Task<IActionResult> SetSuggestedDiscounts([FromBody] SetSuggestedDiscountsRequestDto request)
        {
            var codes = (request.ProductCodes ?? new List<string>())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (codes.Count == 0)
            {
                return BadRequest(ApiResponse<object>.Error("请选择商品"));
            }
            if (codes.Count > 2000)
            {
                return BadRequest(ApiResponse<object>.Error("单次最多设置 2000 个商品"));
            }
            if (request.SuggestedDiscountRate is < 0m or > 1m)
            {
                return BadRequest(ApiResponse<object>.Error("建议折扣必须在 0 到 1 之间"));
            }

            var updatedBy = User.Identity?.Name ?? "system";
            var changed = await _service.SetSuggestedDiscountsWithHistoryAsync(
                codes,
                request.SuggestedDiscountRate,
                updatedBy,
                string.IsNullOrWhiteSpace(request.Source) ? "WarehouseProducts" : request.Source.Trim(),
                HttpContext.RequestAborted
            );
            return Ok(
                ApiResponse<SetSuggestedDiscountsResultDto>.OK(
                    new SetSuggestedDiscountsResultDto { ChangedCount = changed },
                    changed == 0 ? "建议折扣未变化" : "保存成功"
                )
            );
        }
    }
}
