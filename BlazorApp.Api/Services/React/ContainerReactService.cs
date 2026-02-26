using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// React 货柜服务（独立实现，不委托原服务）
    /// </summary>
    public class ContainerReactService : IContainerReactService
    {
        private readonly SqlSugarContext _context;
        private readonly IMapper _mapper;
        private readonly ILogger<ContainerReactService> _logger;

        public ContainerReactService(
            SqlSugarContext context,
            IMapper mapper,
            ILogger<ContainerReactService> logger
        )
        {
            _context = context;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<ContainerListResponse> GetContainersAsync(ContainerQueryRequest request)
        {
            try
            {
                var query = _context.Db.Queryable<Container>().Includes(x => x.Details);

                if (request.StartDate.HasValue && request.EndDate.HasValue)
                {
                    if (
                        request.DateType == "实际到货日期"
                        || request.DateType == "Actual Arrival Date"
                    )
                    {
                        query = query.Where(x =>
                            x.ActualArrivalDate >= request.StartDate.Value
                            && x.ActualArrivalDate <= request.EndDate.Value
                        );
                    }
                    else
                    {
                        query = query.Where(x =>
                            x.EstimatedArrivalDate >= request.StartDate.Value
                            && x.EstimatedArrivalDate <= request.EndDate.Value
                        );
                    }
                }

                query = query.Where(x => x.Status != null);

                if (!string.IsNullOrEmpty(request.ItemNumberFilter))
                {
                    var containerCodesWithItem = await _context
                        .Db.Queryable<ContainerDetail>()
                        .LeftJoin<DomesticProduct>((cd, p) => cd.ProductCode == p.ProductCode)
                        .Where(
                            (cd, p) =>
                                !cd.IsDeleted
                                && p.HBProductNo != null
                                && p.HBProductNo.Contains(request.ItemNumberFilter)
                        )
                        .Select((cd, p) => cd.ContainerCode)
                        .ToListAsync();

                    if (containerCodesWithItem.Any())
                    {
                        query = query.Where(c => containerCodesWithItem.Contains(c.ContainerCode));
                    }
                    else
                    {
                        return new ContainerListResponse
                        {
                            Containers = new List<ContainerMainDto>(),
                            TotalCount = 0,
                            Page = request.Page,
                            PageSize = request.PageSize,
                        };
                    }
                }

                if (request.DateType == "实际到货日期" || request.DateType == "Actual Arrival Date")
                {
                    query = query.OrderByDescending(x => x.ActualArrivalDate);
                }
                else
                {
                    query = query.OrderByDescending(x => x.EstimatedArrivalDate);
                }

                var totalCount = await query.CountAsync();

                var containers = await query
                    .Skip((request.Page - 1) * request.PageSize)
                    .Take(request.PageSize)
                    .ToListAsync();

                var result = new ContainerListResponse
                {
                    Containers = _mapper.Map<List<ContainerMainDto>>(containers),
                    TotalCount = totalCount,
                    Page = request.Page,
                    PageSize = request.PageSize,
                };

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取货柜列表失败");
                throw;
            }
        }

        public async Task<ContainerMainDto?> GetContainerDetailAsync(string containerGuid)
        {
            try
            {
                var container = await _context
                    .Db.Queryable<Container>()
                    .Includes(x => x.Details)
                    .Where(x => x.ContainerCode == containerGuid)
                    .FirstAsync();

                return _mapper.Map<ContainerMainDto>(container);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "获取货柜详情失败, ContainerGuid: {ContainerGuid}",
                    containerGuid
                );
                throw;
            }
        }

        public async Task<bool> UpdateContainerAsync(string containerGuid, UpdateContainerDto dto)
        {
            try
            {
                var container = await _context
                    .Db.Queryable<Container>()
                    .Where(x => x.ContainerCode == containerGuid)
                    .FirstAsync();

                if (container == null)
                {
                    _logger.LogWarning("货柜不存在: {ContainerGuid}", containerGuid);
                    return false;
                }

                // 根据 DTO 的中文字段更新
                if (dto.实际到货日期.HasValue)
                {
                    container.ActualArrivalDate = dto.实际到货日期.Value;
                }

                if (dto.汇率.HasValue)
                {
                    container.ExchangeRate = dto.汇率.Value;
                }

                if (dto.运费.HasValue)
                {
                    container.ShippingFee = dto.运费.Value;
                }

                if (dto.备注 != null)
                {
                    container.Remarks = dto.备注;
                }

                // 保存并限制更新列
                var rowsAffected = await _context
                    .Db.Updateable(container)
                    .UpdateColumns(x => new
                    {
                        x.ActualArrivalDate,
                        x.ExchangeRate,
                        x.ShippingFee,
                        x.Remarks,
                    })
                    .Where(x => x.ContainerCode == containerGuid)
                    .ExecuteCommandAsync();

                _logger.LogInformation(
                    "更新货柜信息成功: {ContainerGuid}, 影响行数: {RowsAffected}",
                    containerGuid,
                    rowsAffected
                );
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "更新货柜信息失败, ContainerGuid: {ContainerGuid}",
                    containerGuid
                );
                throw;
            }
        }

        public async Task<List<ContainerDetailDto>> GetContainerProductsAsync(string containerGuid)
        {
            try
            {
                var products = await _context
                    .Db.Queryable<ContainerDetail>()
                    .Includes(x => x.LocalProduct)
                    .Includes(x => x.Product)
                    .Where(x => x.ContainerCode == containerGuid)
                    .Where(x => x.ProductCode != null)
                    .OrderBy(x => x.ProductCode)
                    .ToListAsync();

                return _mapper.Map<List<ContainerDetailDto>>(products);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "获取货柜商品列表失败, ContainerGuid: {ContainerGuid}",
                    containerGuid
                );
                throw;
            }
        }

        public async Task<List<ContainerDetailDto>> GetFilteredContainerProductsAsync(
            ContainerQueryRequest request
        )
        {
            try
            {
                var containerQuery = _context.Db.Queryable<Container>();

                if (request.StartDate.HasValue && request.EndDate.HasValue)
                {
                    if (
                        request.DateType == "实际到货日期"
                        || request.DateType == "Actual Arrival Date"
                    )
                    {
                        containerQuery = containerQuery.Where(x =>
                            x.ActualArrivalDate >= request.StartDate.Value
                            && x.ActualArrivalDate <= request.EndDate.Value
                        );
                    }
                    else
                    {
                        containerQuery = containerQuery.Where(x =>
                            x.EstimatedArrivalDate >= request.StartDate.Value
                            && x.EstimatedArrivalDate <= request.EndDate.Value
                        );
                    }
                }

                containerQuery = containerQuery.Where(x => x.Status != null);

                var containerCodes = await containerQuery
                    .Select(x => x.ContainerCode)
                    .ToListAsync();

                var productsQuery = _context
                    .Db.Queryable<ContainerDetail>()
                    .Includes(x => x.Product)
                    .Where(x => containerCodes.Contains(x.ContainerCode))
                    .Where(x => x.ProductCode != null);

                if (!string.IsNullOrEmpty(request.ItemNumberFilter))
                {
                    productsQuery = productsQuery.Where(x =>
                        (
                            x.Product != null
                            && x.Product.HBProductNo != null
                            && x.Product.HBProductNo.Contains(request.ItemNumberFilter)
                        )
                    );
                }

                var sortBy = request.SortBy ?? "货号";
                var sortDirection = request.SortDirection ?? "asc";

                switch (sortBy)
                {
                    case var s
                        when string.Equals(s, "货号", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(s, "ItemNumber", StringComparison.OrdinalIgnoreCase):
                        productsQuery = string.Equals(
                            sortDirection,
                            "desc",
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? productsQuery.OrderByDescending(x =>
                                x.Product != null ? x.Product.HBProductNo : ""
                            )
                            : productsQuery.OrderBy(x =>
                                x.Product != null ? x.Product.HBProductNo : ""
                            );
                        break;
                    case var s
                        when string.Equals(s, "商品编码", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(s, "ProductCode", StringComparison.OrdinalIgnoreCase):
                        productsQuery = string.Equals(
                            sortDirection,
                            "desc",
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? productsQuery.OrderByDescending(x => x.ProductCode)
                            : productsQuery.OrderBy(x => x.ProductCode);
                        break;
                    case var s
                        when string.Equals(s, "商品名称", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(s, "ProductName", StringComparison.OrdinalIgnoreCase):
                        productsQuery = string.Equals(
                            sortDirection,
                            "desc",
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? productsQuery.OrderByDescending(x =>
                                x.Product != null ? x.Product.ProductName : ""
                            )
                            : productsQuery.OrderBy(x =>
                                x.Product != null ? x.Product.ProductName : ""
                            );
                        break;
                    default:
                        productsQuery = productsQuery.OrderBy(x =>
                            x.Product != null ? x.Product.HBProductNo : ""
                        );
                        break;
                }

                var products = await productsQuery.ToListAsync();
                var containerDetails = _mapper.Map<List<ContainerDetailDto>>(products);

                return containerDetails;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取货柜商品明细列表失败");
                throw;
            }
        }

        public Task<List<DateFilterOption>> GetDateFilterOptionsAsync()
        {
            try
            {
                var now = DateTime.Now;
                var options = new List<DateFilterOption>();

                options.Add(
                    new DateFilterOption
                    {
                        Label = "Arrived in the Past Week",
                        Value = "past_2_week_actual",
                        StartDate = now.AddDays(-14),
                        EndDate = now,
                        DateType = "Actual Arrival Date",
                    }
                );

                for (int i = 0; i < 5; i++)
                {
                    var weekStart = now.AddDays(i * 7);
                    var weekEnd = weekStart.AddDays(6);
                    options.Add(
                        new DateFilterOption
                        {
                            Label =
                                i == 0
                                    ? "Estimated Arrival This Week"
                                    : $"Estimated Arrival in Week {i + 1}",
                            Value = $"future_week_{i + 1}",
                            StartDate = weekStart,
                            EndDate = weekEnd,
                            DateType = "Estimated Arrival Date",
                        }
                    );
                }

                // 如果需要从数据库动态生成更多选项，可以在此补充逻辑（保持与现有实现一致）
                return Task.FromResult(options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取日期过滤选项失败");
                throw;
            }
        }

        public async Task<int> BatchUpdateDetailsAsync(List<UpdateContainerDetailDto> updates)
        {
            try
            {
                if (updates == null || !updates.Any())
                {
                    _logger.LogWarning("批量更新明细列表为空");
                    return 0;
                }

                _logger.LogInformation(
                    "[React] 开始批量更新货柜明细，数量: {Count}",
                    updates.Count
                );

                var hguids = updates.Select(u => u.HGUID).Distinct().ToList();
                var details = await _context
                    .Db.Queryable<ContainerDetail>()
                    .Where(d => hguids.Contains(d.DetailCode))
                    .ToListAsync();

                var detailMap = details.ToDictionary(d => d.DetailCode, d => d);
                var changedDetails = new List<ContainerDetail>();

                foreach (var update in updates)
                {
                    if (!detailMap.TryGetValue(update.HGUID, out var detail))
                    {
                        _logger.LogWarning("[React] 明细不存在: {DetailGuid}", update.HGUID);
                        continue;
                    }

                    var changed = false;
                    if (update.调整浮率.HasValue && detail.AdjustmentRate != update.调整浮率.Value)
                    {
                        detail.AdjustmentRate = update.调整浮率.Value;
                        changed = true;
                    }
                    if (update.进口价格.HasValue && detail.ImportPrice != update.进口价格.Value)
                    {
                        detail.ImportPrice = update.进口价格.Value;
                        changed = true;
                    }
                    if (update.运输成本.HasValue && detail.TransportCost != update.运输成本.Value)
                    {
                        detail.TransportCost = update.运输成本.Value;
                        changed = true;
                    }
                    if (update.贴牌价格.HasValue && detail.OEMPrice != update.贴牌价格.Value)
                    {
                        detail.OEMPrice = update.贴牌价格.Value;
                        changed = true;
                    }
                    if (changed)
                    {
                        changedDetails.Add(detail);
                    }
                }

                var totalUpdated = 0;

                if (changedDetails.Count > 0)
                {
                    await _context.Db.Ado.BeginTranAsync();
                    try
                    {
                        var rows = await _context
                            .Db.Updateable(changedDetails)
                            .UpdateColumns(x => new
                            {
                                x.AdjustmentRate,
                                x.ImportPrice,
                                x.TransportCost,
                                x.OEMPrice,
                            })
                            .WhereColumns(x => new { x.DetailCode })
                            .ExecuteCommandAsync();
                        totalUpdated = rows;

                        var productUpdates = updates
                            .Select(u =>
                            {
                                if (detailMap.TryGetValue(u.HGUID, out var d))
                                {
                                    return new
                                    {
                                        d.ProductCode,
                                        u.商品名称,
                                        u.英文名称,
                                    };
                                }
                                return null;
                            })
                            .Where(x => x != null && !string.IsNullOrEmpty(x.ProductCode))
                            .ToList();

                        var productCodes = productUpdates
                            .Select(x => x!.ProductCode!)
                            .Distinct()
                            .ToList();

                        if (productCodes.Count > 0)
                        {
                            var products = await _context
                                .Db.Queryable<DomesticProduct>()
                                .Where(p => productCodes.Contains(p.ProductCode))
                                .ToListAsync();

                            var productMap = products.ToDictionary(p => p.ProductCode, p => p);
                            var changedProducts = new List<DomesticProduct>();

                            foreach (var u in productUpdates)
                            {
                                if (u == null)
                                    continue;
                                if (!productMap.TryGetValue(u.ProductCode!, out var p))
                                    continue;

                                var pChanged = false;
                                if (
                                    !string.IsNullOrEmpty(u.商品名称)
                                    && p.ProductName != u.商品名称
                                )
                                {
                                    p.ProductName = u.商品名称;
                                    pChanged = true;
                                }
                                if (
                                    !string.IsNullOrEmpty(u.英文名称)
                                    && p.EnglishProductName != u.英文名称
                                )
                                {
                                    p.EnglishProductName = u.英文名称;
                                    pChanged = true;
                                }
                                if (pChanged)
                                {
                                    changedProducts.Add(p);
                                }
                            }

                            if (changedProducts.Count > 0)
                            {
                                await _context
                                    .Db.Updateable(changedProducts)
                                    .UpdateColumns(x => new { x.ProductName, x.EnglishProductName })
                                    .WhereColumns(x => new { x.ProductCode })
                                    .ExecuteCommandAsync();
                            }
                        }

                        await _context.Db.Ado.CommitTranAsync();
                    }
                    catch
                    {
                        await _context.Db.Ado.RollbackTranAsync();
                        throw;
                    }
                }

                _logger.LogInformation(
                    "[React] 批量更新货柜明细完成，成功更新: {TotalUpdated}/{Total}",
                    totalUpdated,
                    updates.Count
                );

                return totalUpdated;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[React] 批量更新货柜明细失败");
                throw;
            }
        }

        public async Task<string> CreateContainerAsync(CreateContainerDto dto)
        {
            try
            {
                _logger.LogInformation("[React] 开始创建货柜: {ContainerNumber}", dto.货柜编号);

                // 检查货柜编号是否已存在
                var exists = await _context
                    .Db.Queryable<Container>()
                    .Where(x => x.ContainerNumber == dto.货柜编号)
                    .AnyAsync();

                if (exists)
                {
                    throw new InvalidOperationException($"货柜编号 {dto.货柜编号} 已存在");
                }

                // 创建新货柜
                var container = new Container
                {
                    ContainerCode = Guid.NewGuid().ToString(),
                    ContainerNumber = dto.货柜编号,
                    LoadingDate = dto.装柜日期,
                    EstimatedArrivalDate = dto.预计到岸日期,
                    ActualArrivalDate = null,
                    ExchangeRate = dto.汇率,
                    ShippingFee = dto.运费,
                    Remarks = dto.备注,
                    Status = 0, // 默认状态：已装柜/草稿
                    TotalPieces = 0,
                    TotalQuantity = 0,
                    TotalAmount = 0,
                    TotalVolume = 0,
                    CostFloatRate = null,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    IsDeleted = false,
                };

                var result = await _context.Db.Insertable(container).ExecuteReturnEntityAsync();

                _logger.LogInformation(
                    "[React] 创建货柜成功: {ContainerCode}, 货柜编号: {ContainerNumber}",
                    result.ContainerCode,
                    result.ContainerNumber
                );

                return result.ContainerCode;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[React] 创建货柜失败, 货柜编号: {ContainerNumber}",
                    dto.货柜编号
                );
                throw;
            }
        }

        /// <summary>
        /// 查找货柜（兼容 ContainerNumber 或 ContainerCode）
        /// </summary>
        private async Task<Container?> FindContainerByIdAsync(string containerId)
        {
            if (string.IsNullOrWhiteSpace(containerId))
                return null;
            // 优先按业务编号匹配
            var container = await _context
                .Db.Queryable<Container>()
                .Where(x => x.ContainerNumber == containerId)
                .FirstAsync();
            if (container != null)
                return container;
            // 退化为按编码匹配
            container = await _context
                .Db.Queryable<Container>()
                .Where(x => x.ContainerCode == containerId)
                .FirstAsync();
            return container;
        }

        public async Task<List<ContainerConflictItemDto>> CheckConflictsAsync(
            string containerId,
            List<string> productCodes
        )
        {
            try
            {
                var container = await FindContainerByIdAsync(containerId);
                if (container == null)
                {
                    _logger.LogWarning("货柜不存在: {ContainerId}", containerId);
                    return new List<ContainerConflictItemDto>();
                }

                var codes = productCodes
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();
                if (!codes.Any())
                    return new List<ContainerConflictItemDto>();

                var details = await _context
                    .Db.Queryable<ContainerDetail>()
                    .Where(x =>
                        x.ContainerCode == container.ContainerCode
                        && x.ProductCode != null
                        && codes.Contains(x.ProductCode)
                    )
                    .Select(x => new ContainerConflictItemDto
                    {
                        ProductCode = x.ProductCode!,
                        ExistingPieces = x.LoadingPieces,
                        ExistingPackingQuantity = x.PackingQuantity,
                        ExistingUnitVolume = x.UnitVolume,
                    })
                    .ToListAsync();

                return details;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查货柜明细冲突失败");
                throw;
            }
        }

        public async Task<AssignProductsResultDto> AssignProductsAsync(
            string containerId,
            List<AssignProductItemDto> items,
            string resolution,
            string? notes
        )
        {
            var result = new AssignProductsResultDto();
            try
            {
                var container = await FindContainerByIdAsync(containerId);
                if (container == null)
                {
                    _logger.LogWarning("货柜不存在: {ContainerId}", containerId);
                    // 全部失败
                    result.Failed.AddRange(
                        items.Select(i => new AssignProductFailedItemDto
                        {
                            ProductCode = i.ProductCode,
                            Error = "货柜不存在",
                        })
                    );
                    return result;
                }

                var isOverride = string.Equals(
                    resolution,
                    "override",
                    StringComparison.OrdinalIgnoreCase
                );

                foreach (var item in items)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(item.ProductCode))
                        {
                            result.Failed.Add(
                                new AssignProductFailedItemDto
                                {
                                    ProductCode = "",
                                    Error = "ProductCode 不能为空",
                                }
                            );
                            continue;
                        }

                        // 查找是否存在明细
                        var detail = await _context
                            .Db.Queryable<ContainerDetail>()
                            .Where(x =>
                                x.ContainerCode == container.ContainerCode
                                && x.ProductCode == item.ProductCode
                            )
                            .FirstAsync();

                        if (detail == null)
                        {
                            // 新建明细
                            detail = new ContainerDetail
                            {
                                ContainerCode = container.ContainerCode,
                                ProductCode = item.ProductCode,
                                LoadingPieces = item.Quantity,
                                PackingQuantity = item.PackingQuantity,
                                UnitVolume = item.UnitVolume,
                                DomesticPrice = item.DomesticPrice,
                                OEMPrice = item.OEMPrice,
                                Remarks = string.IsNullOrWhiteSpace(item.Notes)
                                    ? notes
                                    : item.Notes,
                            };

                            // 计算装柜数量与总体积
                            detail.LoadingQuantity =
                                (detail.PackingQuantity ?? 0m) * (detail.LoadingPieces ?? 0m);
                            // 使用模型计算方法，统一精度（金额2位、体积3位）
                            detail.TotalVolume =
                                (detail.LoadingPieces ?? 0m) * (detail.UnitVolume ?? 0m);
                            detail.UpdateCalculatedFields();

                            await _context.Db.Insertable(detail).ExecuteCommandAsync();
                            result.Created++;
                        }
                        else
                        {
                            // 更新明细
                            var currentPieces = detail.LoadingPieces ?? 0m;
                            detail.LoadingPieces = isOverride
                                ? item.Quantity
                                : (currentPieces + item.Quantity);

                            // 更新 PackingQuantity / UnitVolume（如提供）
                            if (item.PackingQuantity.HasValue)
                            {
                                detail.PackingQuantity = item.PackingQuantity.Value;
                            }
                            if (item.UnitVolume.HasValue)
                            {
                                detail.UnitVolume = item.UnitVolume.Value;
                            }
                            // 更新 DomesticPrice / OEMPrice（如提供）
                            if (item.DomesticPrice.HasValue)
                            {
                                detail.DomesticPrice = item.DomesticPrice.Value;
                            }
                            if (item.OEMPrice.HasValue)
                            {
                                detail.OEMPrice = item.OEMPrice.Value;
                            }
                            // 备注追加
                            var noteText = string.IsNullOrWhiteSpace(item.Notes)
                                ? notes
                                : item.Notes;
                            if (!string.IsNullOrWhiteSpace(noteText))
                            {
                                detail.Remarks = string.IsNullOrWhiteSpace(detail.Remarks)
                                    ? noteText
                                    : ($"{detail.Remarks}; {noteText}");
                            }

                            // 重新计算装柜数量与总体积
                            detail.LoadingQuantity =
                                (detail.PackingQuantity ?? 0m) * (detail.LoadingPieces ?? 0m);
                            detail.TotalVolume =
                                (detail.LoadingPieces ?? 0m) * (detail.UnitVolume ?? 0m);
                            // 统一调用计算方法更新总金额与总体积
                            detail.UpdateCalculatedFields();

                            await _context
                                .Db.Updateable(detail)
                                .UpdateColumns(x => new
                                {
                                    x.LoadingPieces,
                                    x.PackingQuantity,
                                    x.UnitVolume,
                                    x.DomesticPrice,
                                    x.OEMPrice,
                                    x.LoadingQuantity,
                                    x.TotalVolume,
                                    x.TotalAmount,
                                    x.Remarks,
                                })
                                .ExecuteCommandAsync();
                            result.Updated++;
                        }
                    }
                    catch (Exception exItem)
                    {
                        _logger.LogError(
                            exItem,
                            "分配商品失败: ProductCode={ProductCode}",
                            item.ProductCode
                        );
                        result.Failed.Add(
                            new AssignProductFailedItemDto
                            {
                                ProductCode = item.ProductCode,
                                Error = exItem.Message,
                            }
                        );
                    }
                }

                // 可选：更新主表汇总字段（TotalPieces/TotalQuantity/TotalVolume/TotalAmount）
                try
                {
                    var detailsAll = await _context
                        .Db.Queryable<ContainerDetail>()
                        .Where(x => x.ContainerCode == container.ContainerCode)
                        .ToListAsync();

                    container.TotalPieces = detailsAll.Sum(d => d.LoadingPieces ?? 0m);
                    container.TotalQuantity = detailsAll.Sum(d => d.LoadingQuantity ?? 0m);
                    container.TotalVolume = detailsAll.Sum(d => d.TotalVolume ?? 0m);
                    container.TotalAmount = detailsAll.Sum(d => d.TotalAmount ?? 0m);

                    await _context
                        .Db.Updateable(container)
                        .UpdateColumns(x => new
                        {
                            x.TotalPieces,
                            x.TotalQuantity,
                            x.TotalVolume,
                            x.TotalAmount,
                        })
                        .ExecuteCommandAsync();
                }
                catch (Exception exSum)
                {
                    _logger.LogWarning(exSum, "更新主表汇总字段失败，但不影响分配结果");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量分配商品到货柜失败");
                throw;
            }
        }

        public async Task<int> BatchDeleteDetailsAsync(List<string> hguids)
        {
            try
            {
                if (hguids == null || !hguids.Any())
                {
                    _logger.LogWarning("[React] 批量删除明细列表为空");
                    return 0;
                }

                _logger.LogInformation("[React] 开始批量删除货柜明细，数量: {Count}", hguids.Count);

                // 找出要删除的明细以及涉及的货柜编码
                var detailsToDelete = await _context
                    .Db.Queryable<ContainerDetail>()
                    .Where(d => hguids.Contains(d.DetailCode))
                    .Select(d => new { d.DetailCode, d.ContainerCode })
                    .ToListAsync();

                if (!detailsToDelete.Any())
                {
                    _logger.LogWarning("[React] 未找到待删除的明细");
                    return 0;
                }

                var containerCodes = detailsToDelete
                    .Select(x => x.ContainerCode)
                    .Where(c => c != null)
                    .Distinct()
                    .ToList();

                // 执行删除
                var deletedRows = await _context
                    .Db.Deleteable<ContainerDetail>()
                    .Where(d => hguids.Contains(d.DetailCode))
                    .ExecuteCommandAsync();

                // 更新相关货柜的汇总字段（不影响删除结果）
                foreach (var code in containerCodes)
                {
                    try
                    {
                        var container = await _context
                            .Db.Queryable<Container>()
                            .Where(c => c.ContainerCode == code)
                            .FirstAsync();
                        if (container == null)
                            continue;

                        var detailsAll = await _context
                            .Db.Queryable<ContainerDetail>()
                            .Where(x => x.ContainerCode == code)
                            .ToListAsync();

                        container.TotalPieces = detailsAll.Sum(d => d.LoadingPieces ?? 0m);
                        container.TotalQuantity = detailsAll.Sum(d => d.LoadingQuantity ?? 0m);
                        container.TotalVolume = detailsAll.Sum(d => d.TotalVolume ?? 0m);
                        container.TotalAmount = detailsAll.Sum(d => d.TotalAmount ?? 0m);

                        await _context
                            .Db.Updateable(container)
                            .UpdateColumns(x => new
                            {
                                x.TotalPieces,
                                x.TotalQuantity,
                                x.TotalVolume,
                                x.TotalAmount,
                            })
                            .ExecuteCommandAsync();
                    }
                    catch (Exception exSum)
                    {
                        _logger.LogWarning(exSum, "更新主表汇总字段失败，但不影响删除结果");
                    }
                }

                _logger.LogInformation(
                    "[React] 批量删除货柜明细完成，成功删除: {Deleted}/{Total}",
                    deletedRows,
                    hguids.Count
                );
                return deletedRows;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[React] 批量删除货柜明细失败");
                throw;
            }
        }
    }
}
