using BlazorApp.Api.Features.PromoPosters;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    /// <summary>
    /// 移动端「促销海报」接口（扫码查询页的海报入口）。
    /// 放在商品维护控制器的分部类里，复用其「登录账号 + 绑定设备」双轨鉴权与分店范围解析；
    /// 路由用 ~/ 覆盖类级前缀，对外是独立的 /api/react/v1/promo-posters。
    /// 海报只读商品与价格、不改任何数据，因此与查看商品详情同权限，不另设权限码。
    /// </summary>
    public partial class ReactStoreProductMaintenanceController
    {
        [HttpGet("~/api/react/v1/promo-posters/defaults")]
        public async Task<IActionResult> GetPromoPosterDefaults([FromQuery] string? storeCode, [FromQuery] string? productCode)
        {
            var guard = await GuardPromoPosterAccessAsync(storeCode);
            if (guard != null) return guard;
            if (string.IsNullOrWhiteSpace(productCode))
            {
                return BadRequest(ApiResponse<object>.Error("缺少商品编码"));
            }

            var defaults = await _promoPosterService!.GetDefaultsAsync(storeCode!.Trim(), productCode.Trim());
            if (defaults == null)
            {
                return NotFound(ApiResponse<object>.Error("商品不存在"));
            }
            return Ok(ApiResponse<PromoPosterDefaultsDto>.OK(defaults, "查询成功"));
        }

        [HttpPost("~/api/react/v1/promo-posters/pdf")]
        public async Task<IActionResult> CreatePromoPosterPdf([FromBody] PromoPosterPdfRequest? request)
        {
            var guard = await GuardPromoPosterAccessAsync(request?.StoreCode);
            if (guard != null) return guard;

            try
            {
                var result = _promoPosterService!.BuildPdf(request!, DateTime.Now);
                _logger.LogInformation(
                    "生成促销海报 PDF：门店 {StoreCode}，{PosterCount} 张，{PageCount} 页，{Bytes} 字节",
                    request!.StoreCode, result.PosterCount, result.PageCount, result.Content.Length);
                Response.Headers["X-Poster-Page-Count"] = result.PageCount.ToString();
                return File(result.Content, "application/pdf", result.FileName);
            }
            catch (PromoPosterValidationException ex)
            {
                return BadRequest(ApiResponse<object>.Error(ex.Message));
            }
        }

        /// <summary>与价格更新接口相同的分店范围校验；通过返回 null。</summary>
        private async Task<IActionResult?> GuardPromoPosterAccessAsync(string? storeCode)
        {
            if (_promoPosterService == null)
            {
                return StatusCode(503, ApiResponse<object>.Error("促销海报未启用"));
            }

            var access = await ResolveAccessContextAsync();
            if (!access.IsAllowed)
            {
                return Unauthorized(ApiResponse<object>.Error(access.Message));
            }
            if (string.IsNullOrWhiteSpace(storeCode))
            {
                return BadRequest(ApiResponse<object>.Error("缺少分店代码"));
            }
            if (access.StoreCodes != null && !access.StoreCodes.Contains(storeCode.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                return Forbid();
            }
            return null;
        }
    }
}
