using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace BlazorApp.Api.Controllers.React
{
    /// <summary>
    /// React 货柜管理控制器
    /// 专门为React前端提供的API接口
    /// </summary>
    [ApiController]
    [Route("api/react/v1/containers")]
    [Authorize]
    public class ReactContainerController : ControllerBase
    {
        private readonly IContainerReactService _containerReactService;
        private readonly IContainerHqSyncService _containerHqSyncService;
        private readonly ContainerExportService _containerExportService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ReactContainerController> _logger;
        private const string ExcelContentType =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        private const string PdfContentType = "application/pdf";
        public static readonly TimeSpan ComingSoonCacheDuration = TimeSpan.FromMinutes(30);

        public ReactContainerController(
            IContainerReactService containerReactService,
            IContainerHqSyncService containerHqSyncService,
            ContainerExportService containerExportService,
            IMemoryCache cache,
            ILogger<ReactContainerController> logger
        )
        {
            _containerReactService = containerReactService;
            _containerHqSyncService = containerHqSyncService;
            _containerExportService = containerExportService;
            _cache = cache;
            _logger = logger;
        }

        private static MemoryCacheEntryOptions CreateComingSoonCacheOptions()
        {
            return new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(ComingSoonCacheDuration);
        }

        private static ContainerQueryRequest CreateComingSoonContainerQuery(
            string dateType,
            DateTime startDate,
            DateTime endDate,
            string sortDirection
        )
        {
            return new ContainerQueryRequest
            {
                DateType = dateType,
                StartDate = startDate,
                EndDate = endDate,
                Page = 1,
                PageSize = 100,
                SortBy = dateType,
                SortDirection = sortDirection,
            };
        }

        private async Task<List<ContainerMainDto>> LoadComingSoonSummariesAsync()
        {
            var today = DateTime.Today;
            // SqlSugar 的 scoped 连接不能在同一个请求内并发打开，两段日期查询必须顺序执行。
            var upcomingResult = await _containerReactService.GetContainersAsync(
                CreateComingSoonContainerQuery(
                    "预计到岸日期",
                    today,
                    today.AddDays(56),
                    "asc"
                )
            );
            var arrivedResult = await _containerReactService.GetContainersAsync(
                CreateComingSoonContainerQuery(
                    "实际到货日期",
                    today.AddDays(-7),
                    today,
                    "desc"
                )
            );

            var containerMap = new Dictionary<string, ContainerMainDto>();
            foreach (var container in arrivedResult.Containers.Concat(upcomingResult.Containers))
            {
                if (!string.IsNullOrWhiteSpace(container.HGUID))
                {
                    containerMap[container.HGUID] = container;
                }
            }

            return containerMap.Values
                .OrderBy(container => container.实际到货日期 ?? container.预计到岸日期 ?? DateTime.MaxValue)
                .ToList();
        }

        /// <summary>
        /// 获取货柜列表（React专用）
        /// </summary>
        /// <param name="request">查询请求</param>
        /// <returns>货柜列表</returns>
        [HttpPost("list")]
        [Authorize(Policy = Permissions.Container.View)]
        public async Task<IActionResult> GetContainers([FromBody] ContainerQueryRequest request)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new { success = false, message = "请求参数不能为空" });
                }

                _logger.LogInformation(
                    "获取货柜列表: Page={Page}, PageSize={PageSize}, DateType={DateType}",
                    request.Page,
                    request.PageSize,
                    request.DateType
                );

                var result = await _containerReactService.GetContainersAsync(request);

                return Ok(
                    new
                    {
                        success = true,
                        data = new
                        {
                            items = result.Containers,
                            total = result.TotalCount,
                            page = result.Page,
                            pageSize = result.PageSize,
                        },
                        message = "获取货柜列表成功",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取货柜列表失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取货柜详情（React专用）
        /// </summary>
        /// <param name="containerGuid">货柜GUID</param>
        /// <returns>货柜详情</returns>
        [HttpGet("{containerGuid}")]
        [Authorize(Policy = Permissions.Container.View)]
        public async Task<IActionResult> GetContainerDetail(string containerGuid)
        {
            try
            {
                if (string.IsNullOrEmpty(containerGuid))
                {
                    return BadRequest(new { success = false, message = "货柜GUID不能为空" });
                }

                _logger.LogInformation(
                    "获取货柜详情: ContainerGuid={ContainerGuid}",
                    containerGuid
                );

                var result = await _containerReactService.GetContainerDetailAsync(containerGuid);

                if (result == null)
                {
                    return NotFound(new { success = false, message = "货柜不存在" });
                }

                return Ok(
                    new
                    {
                        success = true,
                        data = result,
                        message = "获取货柜详情成功",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "获取货柜详情失败, ContainerGuid: {ContainerGuid}",
                    containerGuid
                );
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 更新货柜信息（React专用）
        /// </summary>
        /// <param name="containerGuid">货柜GUID</param>
        /// <param name="dto">更新DTO</param>
        /// <returns>更新结果</returns>
        [HttpPut("{containerGuid}")]
        [Authorize(Policy = Permissions.Container.Edit)]
        public async Task<IActionResult> UpdateContainer(
            string containerGuid,
            [FromBody] UpdateContainerDto dto
        )
        {
            try
            {
                if (string.IsNullOrEmpty(containerGuid))
                {
                    return BadRequest(new { success = false, message = "货柜GUID不能为空" });
                }

                if (dto == null)
                {
                    return BadRequest(new { success = false, message = "更新数据不能为空" });
                }

                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values.SelectMany(v =>
                        v.Errors.Select(e => e.ErrorMessage)
                    );
                    return BadRequest(
                        new
                        {
                            success = false,
                            message = $"输入验证失败: {string.Join(", ", errors)}",
                        }
                    );
                }

                _logger.LogInformation(
                    "更新货柜信息: ContainerGuid={ContainerGuid}",
                    containerGuid
                );

                var result = await _containerReactService.UpdateContainerAsync(containerGuid, dto);

                if (result)
                {
                    return Ok(new { success = true, message = "更新成功" });
                }
                else
                {
                    return NotFound(new { success = false, message = "货柜不存在或更新失败" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "更新货柜信息失败, ContainerGuid: {ContainerGuid}",
                    containerGuid
                );
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取货柜商品列表（React专用）
        /// </summary>
        /// <param name="containerGuid">货柜GUID</param>
        /// <returns>商品列表</returns>
        [HttpGet("{containerGuid}/products")]
        [Authorize(Policy = Permissions.Container.View)]
        public async Task<IActionResult> GetContainerProducts(string containerGuid)
        {
            try
            {
                if (string.IsNullOrEmpty(containerGuid))
                {
                    return BadRequest(new { success = false, message = "货柜GUID不能为空" });
                }

                _logger.LogInformation(
                    "获取货柜商品列表: ContainerGuid={ContainerGuid}",
                    containerGuid
                );

                var result = await _containerReactService.GetContainerProductsAsync(containerGuid);

                return Ok(
                    new
                    {
                        success = true,
                        data = result,
                        message = "获取商品列表成功",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "获取货柜商品列表失败, ContainerGuid: {ContainerGuid}",
                    containerGuid
                );
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 服务端筛选、排序并按块返回货柜商品明细（React专用）
        /// </summary>
        [HttpPost("{containerGuid}/products/query")]
        [Authorize(Policy = Permissions.Container.View)]
        public async Task<IActionResult> QueryContainerProducts(
            string containerGuid,
            [FromBody] ContainerDetailQueryDto? request
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(containerGuid))
                {
                    return BadRequest(new { success = false, message = "货柜GUID不能为空" });
                }

                request ??= new ContainerDetailQueryDto();
                request.ContainerGuid = containerGuid;

                var result = await _containerReactService.QueryContainerDetailsAsync(request);

                return Ok(
                    new
                    {
                        success = true,
                        data = result,
                        message = "获取货柜商品明细成功",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "查询货柜商品明细失败, ContainerGuid: {ContainerGuid}",
                    containerGuid
                );
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 导出货柜商品明细（React专用）
        /// </summary>
        [HttpPost("{containerGuid}/products/export")]
        [Authorize(Policy = Permissions.Container.View)]
        public async Task<IActionResult> ExportContainerProducts(
            string containerGuid,
            [FromBody] ReactContainerDetailsExportRequest? request
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(containerGuid))
                {
                    return BadRequest(new { success = false, message = "货柜GUID不能为空" });
                }

                request ??= new ReactContainerDetailsExportRequest();
                if (!TryResolveExportFormat(request.Format, out var format))
                {
                    return BadRequest(new { success = false, message = "导出格式只支持 excel 或 pdf" });
                }

                var container = await _containerReactService.GetContainerDetailAsync(containerGuid);
                if (container == null)
                {
                    return NotFound(new { success = false, message = "货柜不存在" });
                }

                var details = await LoadReactContainerExportDetailsAsync(containerGuid, request);
                if (details.Count == 0)
                {
                    return BadRequest(new { success = false, message = "没有找到要导出的明细数据" });
                }

                // React 明细复用现有义乌导出引擎，只在入口处做 DTO 形状适配。
                var exportContainer = MapReactContainerForExport(container, containerGuid);
                var exportDetails = details
                    .Select(detail => MapReactContainerDetailForExport(detail, containerGuid))
                    .ToList();
                var exportColumns = ResolveReactExportColumns(request);
                var fileBytes = format == "pdf"
                    ? await _containerExportService.GeneratePdfFileAsync(
                        exportContainer,
                        exportDetails,
                        exportColumns
                    )
                    : await _containerExportService.GenerateExcelFileAsync(
                        exportContainer,
                        exportDetails,
                        exportColumns
                    );
                var extension = format == "pdf" ? "pdf" : "xlsx";
                var contentType = format == "pdf" ? PdfContentType : ExcelContentType;
                var fileName = BuildReactContainerExportFileName(
                    exportContainer.ContainerNumber,
                    containerGuid,
                    extension
                );

                return File(fileBytes, contentType, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "导出货柜商品明细失败, ContainerGuid: {ContainerGuid}",
                    containerGuid
                );
                return StatusCode(500, new { success = false, message = "导出失败，请稍后重试" });
            }
        }

        private async Task<List<ContainerDetailDto>> LoadReactContainerExportDetailsAsync(
            string containerGuid,
            ReactContainerDetailsExportRequest request
        )
        {
            var query = request.Query ?? new ContainerDetailQueryDto();
            var selectedHguids = (request.SelectedHguids ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var details = new List<ContainerDetailDto>();

            query.ContainerGuid = containerGuid;
            query.PageNumber = 1;
            query.PageSize = 500;
            query.IncludeTotal = false;
            query.IncludeStats = false;

            while (true)
            {
                var result = await _containerReactService.QueryContainerDetailsAsync(query);
                var items = selectedHguids.Count == 0
                    ? result.Items
                    : result.Items
                        .Where(item => item.HGUID != null && selectedHguids.Contains(item.HGUID))
                        .ToList();
                details.AddRange(items);

                // 导出按服务端查询分页拉全量；选中项找齐后提前结束，避免额外查询。
                if (
                    !result.HasMore
                    || result.Items.Count == 0
                    || (selectedHguids.Count > 0 && details.Count >= selectedHguids.Count)
                )
                {
                    break;
                }

                query.PageNumber++;
            }

            return details;
        }

        private static List<string>? ResolveReactExportColumns(ReactContainerDetailsExportRequest request)
        {
            if (request.ExportColumns?.Count > 0)
            {
                return request.ExportColumns;
            }

            return request.Columns;
        }

        private static bool TryResolveExportFormat(string? format, out string normalizedFormat)
        {
            normalizedFormat = string.IsNullOrWhiteSpace(format)
                ? "excel"
                : format.Trim().ToLowerInvariant();

            return normalizedFormat is "excel" or "pdf";
        }

        private static YiwuContainerDto MapReactContainerForExport(
            ContainerMainDto container,
            string containerGuid
        )
        {
            return new YiwuContainerDto
            {
                ContainerCode = container.HGUID ?? containerGuid,
                ContainerNumber = container.货柜编号 ?? containerGuid,
                LoadingDate = container.装柜日期,
                EstimatedArrivalDate = container.预计到岸日期,
                ActualArrivalDate = container.实际到货日期,
                TotalPieces = container.合计件数,
                TotalQuantity = container.合计数量,
                TotalAmount = container.合计金额,
                TotalVolume = container.总体积,
                CostFloatRate = container.成本浮率,
                ExchangeRate = container.汇率,
                ShippingFee = container.运费,
                Status = container.状态,
                Remarks = container.备注,
            };
        }

        private static YiwuContainerDetailDto MapReactContainerDetailForExport(
            ContainerDetailDto detail,
            string containerGuid
        )
        {
            var product = detail.商品信息;
            return new YiwuContainerDetailDto
            {
                DetailCode = detail.HGUID ?? string.Empty,
                ContainerCode = detail.主表GUID ?? containerGuid,
                ProductCode = detail.商品编码,
                LoadingType = detail.装柜类型,
                ProductType = detail.商品类型,
                SetQuantity = detail.套装数量,
                LoadingPieces = detail.装柜件数,
                LoadingQuantity = detail.装柜数量,
                DomesticPrice = detail.国内价格,
                AdjustmentRate = detail.调整浮率,
                ImportPrice = detail.进口价格,
                OEMPrice = detail.贴牌价格,
                PackingQuantity = detail.单件装箱数,
                UnitVolume = detail.单件体积,
                TotalAmount = detail.合计装柜金额,
                TotalVolume = detail.合计装柜体积,
                TransportCost = detail.运输成本,
                Remarks = detail.备注,
                Product = new ProductInfoDto
                {
                    ProductCode = product?.商品编码 ?? detail.商品编码,
                    ItemNumber = product?.货号 ?? detail.商品编码,
                    Barcode = product?.条形码,
                    ChineseName = product?.商品名称,
                    EnglishName = product?.英文名称,
                    ImageUrl = product?.商品图片,
                    Specification = product?.商品规格,
                    OEMPrice = detail.贴牌价格,
                    ImportPrice = detail.进口价格,
                },
            };
        }

        private static string BuildReactContainerExportFileName(
            string? containerNumber,
            string containerGuid,
            string extension
        )
        {
            var name = string.IsNullOrWhiteSpace(containerNumber) ? containerGuid : containerNumber;
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalidChar, '_');
            }

            return $"货柜明细_{name}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}";
        }

        /// <summary>
        /// 获取国内套装多码价格明细（货柜明细弹窗专用）
        /// </summary>
        [HttpGet("products/{productCode}/domestic-set-codes")]
        [Authorize(Policy = Permissions.Container.View)]
        public async Task<IActionResult> GetDomesticSetCodes(string productCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productCode))
                {
                    return BadRequest(new { success = false, message = "商品编码不能为空" });
                }

                var result = await _containerReactService.GetDomesticSetCodesAsync(productCode);
                return Ok(new { success = true, data = result, message = "获取国内套装明细成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取国内套装明细失败, ProductCode: {ProductCode}", productCode);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 回写国内套装多码价格（仅价格字段）
        /// </summary>
        [HttpPatch("products/{productCode}/domestic-set-codes/prices")]
        [Authorize(Policy = Permissions.Container.Edit)]
        public async Task<IActionResult> UpdateDomesticSetCodePrices(
            string productCode,
            [FromBody] UpdateContainerDomesticSetCodePricesRequestDto? request
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productCode))
                {
                    return BadRequest(new { success = false, message = "商品编码不能为空" });
                }

                request ??= new UpdateContainerDomesticSetCodePricesRequestDto();
                var updatedBy = User.Identity?.Name ?? "system";
                var updatedCount = await _containerReactService.UpdateDomesticSetCodePricesAsync(
                    productCode,
                    request,
                    updatedBy
                );

                return Ok(
                    new
                    {
                        success = true,
                        data = new { updatedCount },
                        message = "保存成功",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "回写国内套装多码价格失败, ProductCode: {ProductCode}", productCode);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取符合条件的所有货柜商品明细列表（React专用）
        /// </summary>
        /// <param name="request">查询请求</param>
        /// <returns>商品明细列表</returns>
        [HttpPost("filtered-products")]
        [Authorize(Policy = Permissions.Container.View)]
        public async Task<IActionResult> GetFilteredContainerProducts(
            [FromBody] ContainerQueryRequest request
        )
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new { success = false, message = "请求参数不能为空" });
                }

                _logger.LogInformation(
                    "获取货柜商品明细列表: DateType={DateType}, ItemNumberFilter={ItemNumberFilter}",
                    request.DateType,
                    request.ItemNumberFilter
                );

                var result = await _containerReactService.GetFilteredContainerProductsAsync(
                    request
                );

                return Ok(
                    new
                    {
                        success = true,
                        data = result,
                        message = "获取商品明细列表成功",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取货柜商品明细列表失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取 Coming Soon 货柜摘要（多用户共享 30 分钟缓存）。
        /// </summary>
        [HttpGet("coming-soon/summaries")]
        [Authorize(Roles = "Admin,WarehouseManager,WarehouseStaff,User")]
        public async Task<IActionResult> GetComingSoonContainerSummaries()
        {
            try
            {
                var cacheKey = $"ComingSoon:Summaries:{DateTime.Today:yyyy-MM-dd}";
                var result = await _cache.GetOrCreateAsync(
                    cacheKey,
                    async entry =>
                    {
                        entry.SetOptions(CreateComingSoonCacheOptions());
                        return await LoadComingSoonSummariesAsync();
                    }
                ) ?? new List<ContainerMainDto>();

                return Ok(
                    new
                    {
                        success = true,
                        data = result,
                        message = $"获取成功，共 {result.Count} 个货柜",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 Coming Soon 货柜摘要失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取 Coming Soon 单货柜商品明细（多用户共享 30 分钟缓存）。
        /// </summary>
        [HttpGet("coming-soon/{containerGuid}/products")]
        [Authorize(Roles = "Admin,WarehouseManager,WarehouseStaff,User")]
        public async Task<IActionResult> GetComingSoonContainerProducts(string containerGuid)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(containerGuid))
                {
                    return BadRequest(new { success = false, message = "货柜GUID不能为空" });
                }

                var cacheKey = $"ComingSoon:Products:{containerGuid}";
                var result = await _cache.GetOrCreateAsync(
                    cacheKey,
                    async entry =>
                    {
                        entry.SetOptions(CreateComingSoonCacheOptions());
                        return await _containerReactService.GetContainerProductsAsync(containerGuid);
                    }
                ) ?? new List<ContainerDetailDto>();

                return Ok(
                    new
                    {
                        success = true,
                        data = result,
                        message = "获取商品列表成功",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "获取 Coming Soon 货柜商品明细失败, ContainerGuid: {ContainerGuid}",
                    containerGuid
                );
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 批量更新货柜明细（React专用）
        /// </summary>
        /// <param name="updates">明细更新列表</param>
        /// <returns>更新结果</returns>
        [HttpPost("batch-update-details")]
        [Authorize(Policy = Permissions.Container.Edit)]
        public async Task<IActionResult> BatchUpdateDetails(
            [FromBody] List<UpdateContainerDetailDto> updates
        )
        {
            try
            {
                if (updates == null || !updates.Any())
                {
                    return BadRequest(new { success = false, message = "更新列表不能为空" });
                }

                var totalUpdated = await _containerReactService.BatchUpdateDetailsAsync(updates);

                return Ok(
                    new
                    {
                        success = true,
                        message = $"成功更新 {totalUpdated} 条明细",
                        data = new { totalUpdated, totalRequested = updates.Count },
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新货柜明细失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 人工确认后，将国内商品编码对齐到本地主档商品编码。
        /// </summary>
        [HttpPost("details/align-domestic-product-code")]
        [Authorize(Policy = Permissions.Container.Edit)]
        [Authorize(Policy = Permissions.Products.Edit)]
        public async Task<IActionResult> AlignDomesticProductCode(
            [FromBody] AlignDomesticProductCodeRequestDto request
        )
        {
            try
            {
                var result = await _containerReactService.AlignDomesticProductCodeAsync(request);
                return Ok(
                    new
                    {
                        success = true,
                        message = "国内商品编码已对齐",
                        data = result,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "对齐国内商品编码失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 按当前筛选范围批量调浮率（React专用）
        /// </summary>
        [HttpPost("{containerGuid}/actions/apply-float-rate")]
        [Authorize(Policy = Permissions.Container.Edit)]
        public async Task<IActionResult> ApplyFloatRateByScope(
            string containerGuid,
            [FromBody] ContainerDetailApplyFloatRateRequestDto request
        )
        {
            try
            {
                if (request == null || !request.FloatRate.HasValue)
                {
                    return BadRequest(new { success = false, message = "调整浮率不能为空" });
                }

                var totalUpdated = await _containerReactService.ApplyFloatRateByScopeAsync(
                    containerGuid,
                    request
                );
                return Ok(
                    new
                    {
                        success = true,
                        message = $"成功更新 {totalUpdated} 条明细",
                        data = new { totalUpdated },
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "按筛选范围批量调浮率失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 按当前筛选范围批量改价（React专用）
        /// </summary>
        [HttpPost("{containerGuid}/actions/apply-prices")]
        [Authorize(Policy = Permissions.Container.Edit)]
        public async Task<IActionResult> ApplyPricesByScope(
            string containerGuid,
            [FromBody] ContainerDetailApplyPricesRequestDto request
        )
        {
            try
            {
                if (request == null || (!request.ImportPrice.HasValue && !request.OemPrice.HasValue))
                {
                    return BadRequest(new { success = false, message = "进口价格或零售价不能为空" });
                }

                var totalUpdated = await _containerReactService.ApplyPricesByScopeAsync(
                    containerGuid,
                    request
                );
                return Ok(
                    new
                    {
                        success = true,
                        message = $"成功更新 {totalUpdated} 条明细",
                        data = new { totalUpdated },
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "按筛选范围批量改价失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 按当前筛选范围重算成本（React专用）
        /// </summary>
        [HttpPost("{containerGuid}/actions/recalculate-costs")]
        [Authorize(Policy = Permissions.Container.Edit)]
        public async Task<IActionResult> RecalculateCostsByScope(
            string containerGuid,
            [FromBody] ContainerDetailBatchScopeDto request
        )
        {
            try
            {
                request ??= new ContainerDetailBatchScopeDto();
                var totalUpdated = await _containerReactService.RecalculateCostsByScopeAsync(
                    containerGuid,
                    request
                );
                return Ok(
                    new
                    {
                        success = true,
                        message = $"成功更新 {totalUpdated} 条明细",
                        data = new { totalUpdated },
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "按筛选范围重算成本失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 按当前筛选范围回填上次价格快照（React专用）
        /// </summary>
        [HttpPost("{containerGuid}/actions/backfill-last-prices")]
        [Authorize(Policy = Permissions.Container.Edit)]
        public async Task<IActionResult> BackfillLastPricesByScope(
            string containerGuid,
            [FromBody] ContainerDetailBatchScopeDto request
        )
        {
            try
            {
                request ??= new ContainerDetailBatchScopeDto();
                var totalUpdated = await _containerReactService.BackfillLastPricesByScopeAsync(
                    containerGuid,
                    request
                );
                return Ok(
                    new
                    {
                        success = true,
                        message = $"成功更新 {totalUpdated} 条明细",
                        data = new { totalUpdated },
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "按筛选范围回填上次价格失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 批量删除货柜明细（React专用）
        /// </summary>
        /// <param name="request">包含待删除的 HGUID 列表</param>
        /// <returns>删除结果</returns>
        [HttpPost("batch-delete-details")]
        [Authorize(Policy = Permissions.Container.Delete)]
        public async Task<IActionResult> BatchDeleteDetails(
            [FromBody] BatchDeleteDetailsRequestDto request
        )
        {
            try
            {
                if (request == null || request.Hguids == null || !request.Hguids.Any())
                {
                    return BadRequest(new { success = false, message = "删除列表不能为空" });
                }

                var totalDeleted = await _containerReactService.BatchDeleteDetailsAsync(
                    request.Hguids
                );

                return Ok(
                    new
                    {
                        success = true,
                        message = $"成功删除 {totalDeleted} 条明细",
                        data = new { totalDeleted, totalRequested = request.Hguids.Count },
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除货柜明细失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取日期过滤选项（React专用）
        /// </summary>
        /// <returns>日期选项列表</returns>
        [HttpGet("date-filter-options")]
        [Authorize(Policy = Permissions.Container.View)]
        public async Task<IActionResult> GetDateFilterOptions()
        {
            try
            {
                _logger.LogInformation("获取日期过滤选项");

                var result = await _containerReactService.GetDateFilterOptionsAsync();

                return Ok(
                    new
                    {
                        success = true,
                        data = result,
                        message = "获取日期过滤选项成功",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取日期过滤选项失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 创建新货柜（React专用）
        /// </summary>
        /// <param name="dto">创建货柜DTO</param>
        /// <returns>创建结果</returns>
        [HttpPost]
        [Authorize(Policy = Permissions.Container.Create)]
        public async Task<IActionResult> CreateContainer([FromBody] CreateContainerDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return BadRequest(new { success = false, message = "创建数据不能为空" });
                }

                if (string.IsNullOrWhiteSpace(dto.货柜编号))
                {
                    return BadRequest(new { success = false, message = "货柜编号不能为空" });
                }

                var containerGuid = await _containerReactService.CreateContainerAsync(dto);

                return Ok(
                    new
                    {
                        success = true,
                        message = "创建成功",
                        data = new { containerGuid },
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "创建货柜失败: {Message}", ex.Message);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建货柜失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 检查货柜明细冲突（按 ProductCode）
        /// </summary>
        [HttpPost("check-conflicts")]
        [Authorize(Policy = Permissions.Container.View)]
        public async Task<IActionResult> CheckConflicts([FromBody] CheckConflictsRequestDto request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.ContainerId))
                {
                    return BadRequest(new { success = false, message = "ContainerId 不能为空" });
                }
                var codes =
                    request
                        .Items?.Select(i => i.ProductCode)
                        ?.Where(c => !string.IsNullOrWhiteSpace(c))
                        .Distinct()
                        .ToList() ?? new List<string>();
                if (!codes.Any())
                {
                    return Ok(
                        new
                        {
                            success = true,
                            data = Array.Empty<ContainerConflictItemDto>(),
                            message = "无待检查的编码",
                        }
                    );
                }

                var conflicts = await _containerReactService.CheckConflictsAsync(
                    request.ContainerId,
                    codes
                );
                return Ok(
                    new
                    {
                        success = true,
                        data = conflicts,
                        message = "检查完成",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查货柜明细冲突失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 批量分配商品到货柜（支持覆盖/增加数量）
        /// </summary>
        [HttpPost("assign-products")]
        [Authorize(Policy = Permissions.Container.Edit)]
        public async Task<IActionResult> AssignProducts([FromBody] AssignProductsRequestDto request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.ContainerId))
                {
                    return BadRequest(new { success = false, message = "ContainerId 不能为空" });
                }
                if (request.Items == null || !request.Items.Any())
                {
                    return BadRequest(new { success = false, message = "Items 不能为空" });
                }
                var resolution = string.Equals(
                    request.Resolution,
                    "override",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "override"
                    : "increase";
                var result = await _containerReactService.AssignProductsAsync(
                    request.ContainerId,
                    request.Items,
                    resolution,
                    request.Notes
                );
                return Ok(
                    new
                    {
                        success = true,
                        data = result,
                        message = "分配完成",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量分配商品到货柜失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取即将到港的货柜及商品列表（Coming Soon 页面专用）
        /// 返回：未来8周内预计到港 + 最近一周内实际到港的货柜及其商品
        /// </summary>
        [HttpGet("coming-soon")]
        [Authorize(Roles = "Admin,WarehouseManager,WarehouseStaff,User")]
        public async Task<IActionResult> GetComingSoonContainers()
        {
            try
            {
                _logger.LogInformation("获取即将到港货柜列表 (Coming Soon)");

                var result = await _containerReactService.GetComingSoonContainersAsync();

                return Ok(
                    new
                    {
                        success = true,
                        data = result,
                        message = $"获取成功，共 {result.Count} 个货柜",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取即将到港货柜列表失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("sync-from-hq")]
        [Authorize(Policy = Permissions.Container.Edit)]
        public async Task<IActionResult> SyncContainersFromHq(
            [FromBody] SyncFromHqRequestDto? request
        )
        {
            try
            {
                _logger.LogInformation("从HQ同步货柜（增量+明细）");

                var result = await _containerHqSyncService.SyncIncrementalAsync(request?.StartDate);

                if (result.IsSuccess)
                {
                    return Ok(CreateSyncResponse(true, result.Message, result));
                }

                return CreateSyncFailureResponse(result);
            }
            catch (ContainerSyncConflictException ex)
            {
                _logger.LogWarning(ex, "从HQ同步货柜发生并发冲突");
                var result = CreateFailedSyncResult(ex.Message, ContainerHqSyncErrorCodes.Conflict);
                return Conflict(CreateSyncResponse(false, result.Message, result));
            }
            catch (ContainerSyncInvalidSourceDataException ex)
            {
                _logger.LogWarning(ex, "从HQ同步货柜时发现HQ源数据质量问题");
                var result = CreateFailedSyncResult(
                    ex.Message,
                    ContainerHqSyncErrorCodes.InvalidSourceData
                );
                return UnprocessableEntity(CreateSyncResponse(false, result.Message, result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从HQ同步货柜失败");
                var result = CreateFailedSyncResult("服务器内部错误", ContainerHqSyncErrorCodes.InternalError);
                return StatusCode(500, CreateSyncResponse(false, result.Message, result));
            }
        }

        /// <summary>
        /// 将核心同步结果映射为 HTTP 语义，保证 data 始终是 SyncResult。
        /// </summary>
        private static IActionResult CreateSyncFailureResponse(SyncResult result)
        {
            return result.ErrorCode switch
            {
                ContainerHqSyncErrorCodes.Conflict => new ConflictObjectResult(
                    CreateSyncResponse(false, result.Message, result)
                ),
                ContainerHqSyncErrorCodes.InvalidSourceData => new UnprocessableEntityObjectResult(
                    CreateSyncResponse(false, result.Message, result)
                ),
                _ => new ObjectResult(CreateSyncResponse(false, result.Message, result))
                {
                    StatusCode = 500,
                },
            };
        }

        /// <summary>
        /// 为异常路径补齐标准同步结果，方便前端复用同一 data 契约。
        /// </summary>
        private static SyncResult CreateFailedSyncResult(string message, string errorCode)
        {
            var now = DateTime.UtcNow;
            return new SyncResult
            {
                StartTime = now,
                EndTime = now,
                Duration = TimeSpan.Zero,
                IsSuccess = false,
                Message = message,
                ErrorCode = errorCode,
                ErrorCount = 1,
            };
        }

        /// <summary>
        /// 统一返回前端兼容的成功/错误结构，避免同步泳道响应分叉。
        /// </summary>
        private static object CreateSyncResponse(bool success, string message, object? data)
        {
            return new
            {
                success,
                message,
                data,
            };
        }

        [HttpPost("push-to-hbsales")]
        [Authorize(Policy = Permissions.Container.Edit)]
        public async Task<IActionResult> PushContainersToHbSales(
            [FromBody] PushToHbSalesRequestDto request
        )
        {
            try
            {
                if (request?.ContainerGuids == null || !request.ContainerGuids.Any())
                {
                    return BadRequest(new { success = false, message = "请选择要推送的货柜" });
                }

                _logger.LogInformation(
                    "推送 {Count} 个货柜到HBSales",
                    request.ContainerGuids.Count
                );

                var result = await _containerReactService.PushContainersToHbSalesAsync(
                    request.ContainerGuids
                );

                return Ok(
                    new
                    {
                        success = result.IsSuccess,
                        data = result,
                        message = result.Message,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "推送货柜到HBSales失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }
    }

    public class SyncFromHqRequestDto
    {
        public DateTime? StartDate { get; set; }
    }

    public class PushToHbSalesRequestDto
    {
        public List<string> ContainerGuids { get; set; } = new();
    }
}
