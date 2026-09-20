using System.Diagnostics;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Api.Services.React.OfflineCatalog;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    /// <summary>
    /// 移动端离线商品目录同步（设备注册绑定会话专用）。
    /// 协议参考 POS 的 catalog sync-plan / page / delta page；鉴权与商品维护接口一致：
    /// 登录用户按分店范围，匿名绑定设备按设备所属分店。
    /// </summary>
    [ApiController]
    [Route("api/react/v1/store-product-maintenance/offline-catalog")]
    [AllowAnonymous]
    public class ReactStoreProductOfflineCatalogController : ControllerBase
    {
        public const string SnapshotExpiredErrorCode = "OFFLINE_CATALOG_SNAPSHOT_EXPIRED";
        public const string CapacityBusyErrorCode = "OFFLINE_CATALOG_CAPACITY_BUSY";
        public const string StoreForbiddenErrorCode = "OFFLINE_CATALOG_STORE_FORBIDDEN";

        private readonly IStoreProductOfflineCatalogService _service;
        private readonly StoreAccessContextResolver _accessResolver;
        private readonly ILogger<ReactStoreProductOfflineCatalogController> _logger;

        public ReactStoreProductOfflineCatalogController(
            IStoreProductOfflineCatalogService service,
            StoreAccessContextResolver accessResolver,
            ILogger<ReactStoreProductOfflineCatalogController> logger)
        {
            _service = service;
            _accessResolver = accessResolver;
            _logger = logger;
        }

        [HttpGet("sync-plan")]
        public async Task<IActionResult> GetSyncPlan(
            [FromQuery] string storeCode,
            [FromQuery] string? baseCatalogVersion,
            CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            var scope = await AuthorizeStoreAsync<OfflineCatalogSyncPlanDto>(storeCode);
            if (scope.Failure != null)
            {
                return scope.Failure;
            }

            try
            {
                var plan = await _service.GetSyncPlanAsync(storeCode.Trim(), baseCatalogVersion, cancellationToken);
                _logger.LogInformation(
                    "OfflineCatalog sync-plan store={StoreCode} actor={Actor} base={Base} mode={Mode} target={Target} total={Total} deltaOps={DeltaOps} total_ms={TotalMs}",
                    storeCode,
                    scope.Access!.ActorLabel,
                    baseCatalogVersion,
                    plan?.Mode,
                    plan?.TargetCatalogVersion,
                    plan?.TargetTotal,
                    plan?.DeltaOperationCount,
                    sw.ElapsedMilliseconds);
                return plan is null
                    ? NotFound(ApiResponse<OfflineCatalogSyncPlanDto>.Error("分店不存在或不可用", "STORE_NOT_FOUND"))
                    : Ok(ApiResponse<OfflineCatalogSyncPlanDto>.OK(plan));
            }
            catch (OfflineCatalogCapacityBusyException)
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    ApiResponse<OfflineCatalogSyncPlanDto>.Error("离线目录服务繁忙，请稍后重试", CapacityBusyErrorCode));
            }
        }

        [HttpGet("page")]
        public async Task<IActionResult> GetPage(
            [FromQuery] string storeCode,
            [FromQuery] string? cursor,
            [FromQuery] int pageSize = 5000,
            [FromQuery] string? catalogVersion = null,
            [FromQuery] string? downloadLeaseId = null,
            CancellationToken cancellationToken = default)
        {
            if (pageSize <= 0 || pageSize > OfflineCatalogIndex.MaxPageSize)
            {
                return BadRequest(ApiResponse<OfflineCatalogPageDto>.Error(
                    $"pageSize 必须在 1 到 {OfflineCatalogIndex.MaxPageSize} 之间",
                    "PAGE_SIZE_INVALID"));
            }

            if (!string.IsNullOrWhiteSpace(cursor) && string.IsNullOrWhiteSpace(catalogVersion) && string.IsNullOrWhiteSpace(downloadLeaseId))
            {
                return BadRequest(ApiResponse<OfflineCatalogPageDto>.Error(
                    "续页必须携带 catalogVersion 或 downloadLeaseId",
                    "CATALOG_VERSION_REQUIRED"));
            }

            var scope = await AuthorizeStoreAsync<OfflineCatalogPageDto>(storeCode);
            if (scope.Failure != null)
            {
                return scope.Failure;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                var page = await _service.GetPageAsync(storeCode.Trim(), cursor, pageSize, catalogVersion, downloadLeaseId, cancellationToken);
                _logger.LogInformation(
                    "OfflineCatalog page store={StoreCode} continuation={Continuation} items={Items} hasMore={HasMore} total_ms={TotalMs}",
                    storeCode,
                    !string.IsNullOrWhiteSpace(cursor),
                    page?.Items.Count,
                    page?.HasMore,
                    sw.ElapsedMilliseconds);
                return page is null
                    ? NotFound(ApiResponse<OfflineCatalogPageDto>.Error("分店不存在或不可用", "STORE_NOT_FOUND"))
                    : Ok(ApiResponse<OfflineCatalogPageDto>.OK(page));
            }
            catch (OfflineCatalogSnapshotExpiredException)
            {
                return Conflict(ApiResponse<OfflineCatalogPageDto>.Error("离线目录快照已过期，请重新开始下载", SnapshotExpiredErrorCode));
            }
            catch (OfflineCatalogCapacityBusyException)
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    ApiResponse<OfflineCatalogPageDto>.Error("离线目录服务繁忙，请稍后重试", CapacityBusyErrorCode));
            }
        }

        [HttpGet("delta/page")]
        public async Task<IActionResult> GetDeltaPage(
            [FromQuery] string storeCode,
            [FromQuery] string baseCatalogVersion,
            [FromQuery] string targetCatalogVersion,
            [FromQuery] string? cursor,
            [FromQuery] int pageSize = 5000,
            [FromQuery] string? downloadLeaseId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(baseCatalogVersion) || string.IsNullOrWhiteSpace(targetCatalogVersion))
            {
                return BadRequest(ApiResponse<OfflineCatalogDeltaPageDto>.Error(
                    "baseCatalogVersion 与 targetCatalogVersion 必填",
                    "CATALOG_VERSION_REQUIRED"));
            }

            if (pageSize <= 0 || pageSize > OfflineCatalogIndex.MaxPageSize)
            {
                return BadRequest(ApiResponse<OfflineCatalogDeltaPageDto>.Error(
                    $"pageSize 必须在 1 到 {OfflineCatalogIndex.MaxPageSize} 之间",
                    "PAGE_SIZE_INVALID"));
            }

            var scope = await AuthorizeStoreAsync<OfflineCatalogDeltaPageDto>(storeCode);
            if (scope.Failure != null)
            {
                return scope.Failure;
            }

            try
            {
                var page = await _service.GetDeltaPageAsync(
                    storeCode.Trim(),
                    baseCatalogVersion,
                    targetCatalogVersion,
                    cursor,
                    pageSize,
                    downloadLeaseId,
                    cancellationToken);
                return Ok(ApiResponse<OfflineCatalogDeltaPageDto>.OK(page));
            }
            catch (OfflineCatalogSnapshotExpiredException)
            {
                // 基线或目标任一过期都无法保证删除项完整，客户端必须回退全量。
                return Conflict(ApiResponse<OfflineCatalogDeltaPageDto>.Error("离线目录快照已过期，请改为全量下载", SnapshotExpiredErrorCode));
            }
        }

        private async Task<(StoreAccessContext? Access, IActionResult? Failure)> AuthorizeStoreAsync<T>(string? storeCode)
        {
            if (string.IsNullOrWhiteSpace(storeCode))
            {
                return (null, BadRequest(ApiResponse<T>.Error("storeCode 必填", "STORE_CODE_REQUIRED")));
            }

            var access = await _accessResolver.ResolveAsync(User, Request.Headers);
            if (!access.IsAllowed)
            {
                _logger.LogWarning("OfflineCatalog unauthorized store={StoreCode} message={Message}", storeCode, access.Message);
                return (access, Unauthorized(ApiResponse<T>.Error(access.Message)));
            }

            if (!access.CanAccessStore(storeCode))
            {
                _logger.LogWarning(
                    "OfflineCatalog store forbidden store={StoreCode} actor={Actor} scope={Scope}",
                    storeCode,
                    access.ActorLabel,
                    StoreAccessContextResolver.FormatStoreScope(access.StoreCodes));
                return (access, StatusCode(
                    StatusCodes.Status403Forbidden,
                    ApiResponse<T>.Error("当前账号或设备无权访问该分店", StoreForbiddenErrorCode)));
            }

            return (access, null);
        }
    }
}
