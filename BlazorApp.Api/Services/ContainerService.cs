
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 货柜服务实现
    /// </summary>
    public class ContainerService : IContainerService
    {
        private readonly SqlSugarContext _localContext;
        private readonly IMapper _mapper;
        private readonly ILogger<ContainerService> _logger;
        private readonly ITranslationService _translationService;

        public ContainerService(
            SqlSugarContext localContext,
            IMapper mapper,
            ILogger<ContainerService> logger,
            ITranslationService translationService)
        {
            _localContext = localContext;
            _mapper = mapper;
            _logger = logger;
            _translationService = translationService;
        }

        /// <summary>
        /// 获取货柜列表
        /// </summary>
        public async Task<ContainerListResponse> GetContainersAsync(ContainerQueryRequest request)
        {
            try
            {
                var query = _localContext.Db.Queryable<Container>()
                    .Includes(x => x.Details);

                // 根据日期类型过滤
                if (request.StartDate.HasValue && request.EndDate.HasValue)
                {
                    if (request.DateType == "实际到货日期" || request.DateType == "Actual Arrival Date")
                    {
                        query = query.Where(x => x.ActualArrivalDate >= request.StartDate.Value && x.ActualArrivalDate <= request.EndDate.Value);
                    }
                    else
                    {
                        query = query.Where(x => x.EstimatedArrivalDate >= request.StartDate.Value && x.EstimatedArrivalDate <= request.EndDate.Value);
                    }
                }

                // 只查询有效状态的货柜
                query = query.Where(x => x.Status != null && x.Status != 0);

                // 排序 - 优先显示实际到货的，然后按日期排序
                if (request.DateType == "实际到货日期" || request.DateType == "Actual Arrival Date")
                {
                    query = query.OrderByDescending(x => x.ActualArrivalDate);
                }
                else
                {
                    query = query.OrderByDescending(x => x.EstimatedArrivalDate);
                }

                // 获取总数
                var totalCount = await query.CountAsync();

                // 分页查询
                var containers = await query
                    .Skip((request.Page - 1) * request.PageSize)
                    .Take(request.PageSize)
                    .ToListAsync();

                var result = new ContainerListResponse
                {
                    Containers = _mapper.Map<List<ContainerMainDto>>(containers),
                    TotalCount = totalCount,
                    Page = request.Page,
                    PageSize = request.PageSize
                };

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取货柜列表失败");
                throw;
            }
        }

        /// <summary>
        /// 获取货柜详情
        /// </summary>
        public async Task<ContainerMainDto?> GetContainerDetailAsync(string containerGuid)
        {
            try
            {
                var container = await _localContext.Db.Queryable<Container>()
                    .Includes(x => x.Details)
                    .Where(x => x.ContainerCode == containerGuid)
                    .FirstAsync();

                return _mapper.Map<ContainerMainDto>(container);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取货柜详情失败, ContainerGuid: {ContainerGuid}", containerGuid);
                throw;
            }
        }

        /// <summary>
        /// 获取货柜商品列表
        /// </summary>
        public async Task<List<ContainerDetailDto>> GetContainerProductsAsync(string containerGuid)
        {
            try
            {
                var products = await _localContext.Db.Queryable<ContainerDetail>()
                    .Includes(x => x.Product)
                    .Where(x => x.ContainerCode == containerGuid)
                    .Where(x => x.ProductCode != null)
                    .OrderBy(x => x.ProductCode)
                    .ToListAsync();

                return _mapper.Map<List<ContainerDetailDto>>(products);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取货柜商品列表失败, ContainerGuid: {ContainerGuid}", containerGuid);
                throw;
            }
        }

        /// <summary>
        /// 获取符合条件的所有货柜商品明细列表
        /// </summary>
        public async Task<List<ContainerDetailDto>> GetFilteredContainerProductsAsync(ContainerQueryRequest request)
        {
            try
            {
                var containerQuery = _localContext.Db.Queryable<Container>();

                // 根据日期类型过滤
                if (request.StartDate.HasValue && request.EndDate.HasValue)
                {
                    if (request.DateType == "实际到货日期" || request.DateType == "Actual Arrival Date")
                    {
                        containerQuery = containerQuery.Where(x => x.ActualArrivalDate >= request.StartDate.Value && x.ActualArrivalDate <= request.EndDate.Value);
                    }
                    else
                    {
                        containerQuery = containerQuery.Where(x => x.EstimatedArrivalDate >= request.StartDate.Value && x.EstimatedArrivalDate <= request.EndDate.Value);
                    }
                }

                // 只查询有效状态的货柜
                containerQuery = containerQuery.Where(x => x.Status != null && x.Status != 0);

                // 获取符合条件的货柜编码列表
                var containerCodes = await containerQuery.Select(x => x.ContainerCode).ToListAsync();

                // 获取这些货柜的所有商品明细
                var productsQuery = _localContext.Db.Queryable<ContainerDetail>()
                    .Includes(x => x.Product)
                    .Where(x => containerCodes.Contains(x.ContainerCode))
                    .Where(x => x.ProductCode != null);

                // 添加货号过滤
                if (!string.IsNullOrEmpty(request.ItemNumberFilter))
                {
                    productsQuery = productsQuery.Where(x =>
                        (x.Product != null && x.Product.HBProductNo != null && x.Product.HBProductNo.Contains(request.ItemNumberFilter))
                    );
                }

                // 添加排序
                var sortBy = request.SortBy ?? "货号";
                var sortDirection = request.SortDirection ?? "asc";

                switch (sortBy)
                {
                    case var s when string.Equals(s, "货号", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "ItemNumber", StringComparison.OrdinalIgnoreCase):
                        productsQuery = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase)
                            ? productsQuery.OrderByDescending(x => x.Product != null ? x.Product.HBProductNo : "")
                            : productsQuery.OrderBy(x => x.Product != null ? x.Product.HBProductNo : "");
                        break;
                    case var s when string.Equals(s, "商品编码", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "ProductCode", StringComparison.OrdinalIgnoreCase):
                        productsQuery = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase)
                            ? productsQuery.OrderByDescending(x => x.ProductCode)
                            : productsQuery.OrderBy(x => x.ProductCode);
                        break;
                    case var s when string.Equals(s, "商品名称", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "ProductName", StringComparison.OrdinalIgnoreCase):
                        productsQuery = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase)
                            ? productsQuery.OrderByDescending(x => x.Product != null ? x.Product.ProductName : "")
                            : productsQuery.OrderBy(x => x.Product != null ? x.Product.ProductName : "");
                        break;
                    default:
                        // 默认按货号排序
                        productsQuery = productsQuery.OrderBy(x => x.Product != null ? x.Product.HBProductNo : "");
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

        /// <summary>
        /// 获取日期过滤选项
        /// </summary>
        public async Task<List<DateFilterOption>> GetDateFilterOptionsAsync()
        {
            try
            {
                var now = DateTime.Now;
                var options = new List<DateFilterOption>();

                // 过去1周内 - 实际到货日期
                options.Add(new DateFilterOption
                {
                    Label = "Arrived in the Past Week",
                    Value = "past_2_week_actual",
                    StartDate = now.AddDays(-14),
                    EndDate = now,
                    DateType = "Actual Arrival Date"
                });

                // 将来5周内 - 预计到岸日期
                for (int i = 0; i < 5; i++)
                {
                    var weekStart = now.AddDays(i * 7);
                    var weekEnd = weekStart.AddDays(6);

                    options.Add(new DateFilterOption
                    {
                        Label = i == 0 ? "Estimated Arrival This Week" : $"Estimated Arrival in Week {i + 1}",
                        Value = $"future_week_{i + 1}",
                        StartDate = weekStart,
                        EndDate = weekEnd,
                        DateType = "Estimated Arrival Date"
                    });
                }

                // 获取数据库中的实际日期范围来动态生成更多选项
                var dateRanges = await _localContext.Db.Queryable<Container>()
                    .Where(x => x.Status != null && x.Status != 0)
                    .Where(x => x.EstimatedArrivalDate != null || x.ActualArrivalDate != null)
                    .Select(x => new { EstimatedArrivalDate = x.EstimatedArrivalDate, ActualArrivalDate = x.ActualArrivalDate })
                    .ToListAsync();

                // 添加基于实际数据的时间段选项
                var recentActualDates = dateRanges
                    .Where(x => x.ActualArrivalDate.HasValue && x.ActualArrivalDate.Value >= now.AddDays(-30))
                    .Select(x => x.ActualArrivalDate!.Value)
                    .Distinct()
                    .OrderByDescending(x => x)
                    .Take(10)
                    .ToList();

                var upcomingEstimatedDates = dateRanges
                    .Where(x => x.EstimatedArrivalDate.HasValue && x.EstimatedArrivalDate.Value >= now && x.EstimatedArrivalDate.Value <= now.AddDays(60))
                    .Select(x => x.EstimatedArrivalDate!.Value)
                    .Distinct()
                    .OrderBy(x => x)
                    .Take(10)
                    .ToList();

                return options;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取日期过滤选项失败");
                throw;
            }
        }

        /// <summary>
        /// 更新货柜信息
        /// </summary>
        public async Task<bool> UpdateContainerAsync(string containerGuid, UpdateContainerDto dto)
        {
            try
            {
                var container = await _localContext.Db.Queryable<Container>()
                    .Where(x => x.ContainerCode == containerGuid)
                    .FirstAsync();

                if (container == null)
                {
                    _logger.LogWarning("货柜不存在: {ContainerGuid}", containerGuid);
                    return false;
                }

                // 更新字段
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

                // 保存更改
                var rowsAffected = await _localContext.Db.Updateable(container)
                    .UpdateColumns(x => new { x.ActualArrivalDate, x.ExchangeRate, x.ShippingFee, x.Remarks })
                    .Where(x => x.ContainerCode == containerGuid)
                    .ExecuteCommandAsync();

                _logger.LogInformation("更新货柜信息成功: {ContainerGuid}, 影响行数: {RowsAffected}", containerGuid, rowsAffected);

                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新货柜信息失败, ContainerGuid: {ContainerGuid}", containerGuid);
                throw;
            }
        }

        /// <summary>
        /// 批量更新货柜明细
        /// </summary>
        public async Task<int> BatchUpdateDetailsAsync(List<UpdateContainerDetailDto> updates)
        {
            try
            {
                if (updates == null || !updates.Any())
                {
                    _logger.LogWarning("批量更新明细列表为空");
                    return 0;
                }

                _logger.LogInformation("开始批量更新货柜明细，数量: {Count}", updates.Count);

                int totalUpdated = 0;

                // 批量更新明细
                foreach (var update in updates)
                {
                    var detail = await _localContext.Db.Queryable<ContainerDetail>()
                        .Where(x => x.DetailCode == update.HGUID)
                        .FirstAsync();

                    if (detail == null)
                    {
                        _logger.LogWarning("明细不存在: {DetailGuid}", update.HGUID);
                        continue;
                    }

                    // 更新明细字段
                    if (update.调整浮率.HasValue)
                    {
                        detail.AdjustmentRate = update.调整浮率.Value;
                    }

                    if (update.进口价格.HasValue)
                    {
                        detail.ImportPrice = update.进口价格.Value;
                    }

                    if (update.运输成本.HasValue)
                    {
                        detail.TransportCost = update.运输成本.Value;
                    }

                    if (update.贴牌价格.HasValue)
                    {
                        detail.OEMPrice = update.贴牌价格.Value;
                    }

                    // 保存更改
                    var rowsAffected = await _localContext.Db.Updateable(detail)
                        .UpdateColumns(x => new { x.AdjustmentRate, x.ImportPrice, x.TransportCost, x.OEMPrice })
                        .Where(x => x.DetailCode == update.HGUID)
                        .ExecuteCommandAsync();

                    if (rowsAffected > 0)
                    {
                        totalUpdated++;
                    }

                    // 如果有商品名称或英文名称需要更新，更新关联的商品信息
                    if (!string.IsNullOrEmpty(update.商品名称) || !string.IsNullOrEmpty(update.英文名称))
                    {
                        if (!string.IsNullOrEmpty(detail.ProductCode))
                        {
                            var product = await _localContext.Db.Queryable<DomesticProduct>()
                                .Where(x => x.ProductCode == detail.ProductCode)
                                .FirstAsync();

                            if (product != null)
                            {
                                if (!string.IsNullOrEmpty(update.商品名称))
                                {
                                    product.ProductName = update.商品名称;
                                }

                                if (!string.IsNullOrEmpty(update.英文名称))
                                {
                                    product.EnglishProductName = update.英文名称;
                                }

                                await _localContext.Db.Updateable(product)
                                    .UpdateColumns(x => new { x.ProductName, x.EnglishProductName })
                                    .Where(x => x.ProductCode == detail.ProductCode)
                                    .ExecuteCommandAsync();
                            }
                        }
                    }
                }

                _logger.LogInformation("批量更新货柜明细完成，成功更新: {TotalUpdated}/{Total}", totalUpdated, updates.Count);

                return totalUpdated;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新货柜明细失败");
                throw;
            }
        }

        /// <summary>
        /// 创建新货柜
        /// </summary>
        public async Task<string> CreateContainerAsync(CreateContainerDto dto)
        {
            try
            {
                _logger.LogInformation("开始创建货柜: {ContainerNumber}", dto.货柜编号);

                // 检查货柜编号是否已存在
                var exists = await _localContext.Db.Queryable<Container>()
                    .Where(x => x.ContainerNumber == dto.货柜编号)
                    .AnyAsync();

                if (exists)
                {
                    throw new InvalidOperationException($"货柜编号 {dto.货柜编号} 已存在");
                }

                // 创建新货柜
                var container = new Container
                {
                    ContainerCode = Guid.NewGuid().ToString(), // 生成新的GUID
                    ContainerNumber = dto.货柜编号,
                    LoadingDate = dto.装柜日期,
                    EstimatedArrivalDate = dto.预计到岸日期,
                    ActualArrivalDate = null,
                    ExchangeRate = dto.汇率,
                    ShippingFee = dto.运费,
                    Remarks = dto.备注,
                    Status = 0, // 默认状态：已装柜
                    TotalPieces = 0,
                    TotalQuantity = 0,
                    TotalAmount = 0,
                    TotalVolume = 0,
                    CostFloatRate = null,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    IsDeleted = false
                };

                // 插入数据库
                var result = await _localContext.Db.Insertable(container)
                    .ExecuteReturnEntityAsync();

                _logger.LogInformation("创建货柜成功: {ContainerCode}, 货柜编号: {ContainerNumber}",
                    result.ContainerCode, result.ContainerNumber);

                return result.ContainerCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建货柜失败, 货柜编号: {ContainerNumber}", dto.货柜编号);
                throw;
            }
        }
    }
}