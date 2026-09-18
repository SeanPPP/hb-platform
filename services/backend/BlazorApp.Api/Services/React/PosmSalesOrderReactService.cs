using System.Linq;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class PosmSalesOrderReactService : IPosmSalesOrderReactService
    {
        private readonly POSMSqlSugarContext _posmContext;
        private readonly SqlSugarContext _context;
        private readonly IMapper _mapper;
        private readonly ILogger<PosmSalesOrderReactService> _logger;

        public PosmSalesOrderReactService(
            POSMSqlSugarContext posmContext,
            SqlSugarContext context,
            IMapper mapper,
            ILogger<PosmSalesOrderReactService> logger
        )
        {
            _posmContext = posmContext;
            _context = context;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<PagedListReactDto<PosmSalesOrderDto>> GetSalesOrderListAsync(
            PosmSalesOrderQueryParams queryParams
        )
        {
            var pageNumber = Math.Max(1, queryParams.PageNumber);
            // 限制单页上限，避免异常请求生成过大的 SQL Take 和响应体。
            var pageSize = queryParams.PageSize > 0 ? Math.Min(queryParams.PageSize, 1000) : 20;
            // POS 写入的 OrderTime 是门店本地墙钟时间（非 UTC），日期与时段边界一律按墙钟直接比较，
            // 不能再按客户端时区平移，否则"今天"会整体前移十小时。
            var baseQuery = _posmContext
                .Db.Queryable<SalesOrder>()
                .LeftJoin<SalesOrderDetail>((o, d) => o.OrderGuid == d.OrderGuid);

            if (queryParams.StartDate.HasValue)
            {
                // 边界用常量参数，保持 OrderTime 列可直接走索引。
                var start = queryParams.StartDate.Value.Date;
                baseQuery = baseQuery.Where(o => o.OrderTime >= start);
            }
            if (queryParams.EndDate.HasValue)
            {
                var endExclusive = queryParams.EndDate.Value.Date.AddDays(1);
                baseQuery = baseQuery.Where(o => o.OrderTime < endExclusive);
            }

            if (queryParams.TimeStart.HasValue)
            {
                var startSeconds = (int)queryParams.TimeStart.Value.TotalSeconds;
                baseQuery = baseQuery.Where(o =>
                    o.OrderTime.HasValue
                    && o.OrderTime.Value.Hour * 3600
                        + o.OrderTime.Value.Minute * 60
                        + o.OrderTime.Value.Second
                        >= startSeconds
                );
            }
            if (queryParams.TimeEnd.HasValue)
            {
                var endSeconds = (int)queryParams.TimeEnd.Value.TotalSeconds;
                baseQuery = baseQuery.Where(o =>
                    o.OrderTime.HasValue
                    && o.OrderTime.Value.Hour * 3600
                        + o.OrderTime.Value.Minute * 60
                        + o.OrderTime.Value.Second
                        <= endSeconds
                );
            }

            if (!string.IsNullOrWhiteSpace(queryParams.BranchCode))
            {
                baseQuery = baseQuery.Where(o => o.BranchCode == queryParams.BranchCode);
            }

            if (queryParams.BranchCodes != null && queryParams.BranchCodes.Any())
            {
                baseQuery = baseQuery.Where(o =>
                    o.BranchCode != null && queryParams.BranchCodes.Contains(o.BranchCode)
                );
            }

            if (!string.IsNullOrWhiteSpace(queryParams.DeviceCode))
            {
                baseQuery = baseQuery.Where(o => o.DeviceCode == queryParams.DeviceCode);
            }

            if (!string.IsNullOrWhiteSpace(queryParams.OrderGuidKeyword))
            {
                var orderGuidKeyword = queryParams.OrderGuidKeyword.Trim();
                baseQuery = baseQuery.Where(o =>
                    o.OrderGuid != null && o.OrderGuid.Contains(orderGuidKeyword)
                );
            }

            if (!string.IsNullOrWhiteSpace(queryParams.DeviceCodeKeyword))
            {
                var deviceCodeKeyword = queryParams.DeviceCodeKeyword.Trim();
                baseQuery = baseQuery.Where(o =>
                    o.DeviceCode != null && o.DeviceCode.Contains(deviceCodeKeyword)
                );
            }

            if (queryParams.OrderType.HasValue && queryParams.OrderType.Value != OrderType.All)
            {
                baseQuery = baseQuery.Where(o => o.Status == (int)queryParams.OrderType.Value);
            }

            if (queryParams.ItemCountMin.HasValue)
                baseQuery = baseQuery.Where(o => o.ItemCount >= queryParams.ItemCountMin.Value);
            if (queryParams.ItemCountMax.HasValue)
                baseQuery = baseQuery.Where(o => o.ItemCount <= queryParams.ItemCountMax.Value);
            if (queryParams.TotalAmountMin.HasValue)
                baseQuery = baseQuery.Where(o => o.TotalAmount >= queryParams.TotalAmountMin.Value);
            if (queryParams.TotalAmountMax.HasValue)
                baseQuery = baseQuery.Where(o => o.TotalAmount <= queryParams.TotalAmountMax.Value);
            if (queryParams.DiscountAmountMin.HasValue)
                baseQuery = baseQuery.Where(o =>
                    o.DiscountAmount >= queryParams.DiscountAmountMin.Value
                );
            if (queryParams.DiscountAmountMax.HasValue)
                baseQuery = baseQuery.Where(o =>
                    o.DiscountAmount <= queryParams.DiscountAmountMax.Value
                );
            if (queryParams.ActualPayMin.HasValue)
            {
                if (_posmContext.Db.CurrentConnectionConfig.DbType == DbType.Sqlite)
                {
                    // SQLite 的 decimal 表达式参数会按文本绑定；仅测试 provider 转 double，SQL Server 保持 decimal 精度。
                    var actualPayMin = (double)queryParams.ActualPayMin.Value;
                    baseQuery = baseQuery.Where(o =>
                        SqlFunc.ToDouble(o.TotalAmount) - SqlFunc.ToDouble(o.DiscountAmount)
                            >= actualPayMin
                    );
                }
                else
                {
                    baseQuery = baseQuery.Where(o =>
                        o.TotalAmount - o.DiscountAmount >= queryParams.ActualPayMin.Value
                    );
                }
            }
            if (queryParams.ActualPayMax.HasValue)
            {
                if (_posmContext.Db.CurrentConnectionConfig.DbType == DbType.Sqlite)
                {
                    var actualPayMax = (double)queryParams.ActualPayMax.Value;
                    baseQuery = baseQuery.Where(o =>
                        SqlFunc.ToDouble(o.TotalAmount) - SqlFunc.ToDouble(o.DiscountAmount)
                            <= actualPayMax
                    );
                }
                else
                {
                    baseQuery = baseQuery.Where(o =>
                        o.TotalAmount - o.DiscountAmount <= queryParams.ActualPayMax.Value
                    );
                }
            }

            if (!string.IsNullOrWhiteSpace(queryParams.Keyword))
            {
                var keyword = queryParams.Keyword.Trim();
                // POSM 明细没有货号列，货号 / 主档条码先在商品主档解析成商品编码再参与匹配。
                var masterProductCodes = await ResolveMasterProductCodesAsync(keyword);
                if (masterProductCodes.Count > 0)
                {
                    baseQuery = baseQuery.Where((o, d) =>
                        (o.OrderGuid != null && o.OrderGuid.Contains(keyword))
                        || (o.DeviceCode != null && o.DeviceCode.Contains(keyword))
                        || SqlFunc
                            .Subqueryable<SalesOrderDetail>()
                            .Where(detail =>
                                detail.OrderGuid == o.OrderGuid
                                && (
                                    detail.ProductCode.Contains(keyword)
                                    || masterProductCodes.Contains(detail.ProductCode)
                                    || (detail.Barcode != null && detail.Barcode.Contains(keyword))
                                    || (
                                        detail.ProductName != null
                                        && detail.ProductName.Contains(keyword)
                                    )
                                )
                            )
                            .Any()
                    );
                }
                else
                {
                    baseQuery = baseQuery.Where((o, d) =>
                        (o.OrderGuid != null && o.OrderGuid.Contains(keyword))
                        || (o.DeviceCode != null && o.DeviceCode.Contains(keyword))
                        || SqlFunc
                            .Subqueryable<SalesOrderDetail>()
                            .Where(detail =>
                                detail.OrderGuid == o.OrderGuid
                                && (
                                    detail.ProductCode.Contains(keyword)
                                    || (detail.Barcode != null && detail.Barcode.Contains(keyword))
                                    || (
                                        detail.ProductName != null
                                        && detail.ProductName.Contains(keyword)
                                    )
                                )
                            )
                            .Any()
                    );
                }
            }

            var q = baseQuery
                .GroupBy(
                    (o, d) =>
                        new
                        {
                            o.OrderGuid,
                            o.OrderTime,
                            o.BranchCode,
                            o.DeviceCode,
                            o.TotalAmount,
                            o.DiscountAmount,
                            o.ActualAmount,
                            o.ItemCount,
                            o.Status,
                        }
                )
                .Select(
                    (o, d) =>
                        new PosmSalesOrderDto
                        {
                            OrderGuid = o.OrderGuid,
                            OrderTime = o.OrderTime,
                            BranchCode = o.BranchCode,
                            DeviceCode = o.DeviceCode,
                            TotalAmount = o.TotalAmount,
                            DiscountAmount = o.DiscountAmount,
                            ActualAmount = o.ActualAmount,
                            ItemCount = o.ItemCount,
                            Status = o.Status,
                            SkuCount = SqlFunc.AggregateDistinctCount(d.ProductCode),
                            // ItemCount 是行数；件数需要按明细数量求和，移动端卡片以此显示。
                            QuantityTotal = SqlFunc.AggregateSum(d.Quantity),
                        }
                )
                .MergeTable();

            // SKU 是明细聚合值，必须在 GroupBy/Select 之后过滤，避免改变完整订单的聚合口径。
            if (queryParams.SkuCountMin.HasValue)
                q = q.Where(o => o.SkuCount >= queryParams.SkuCountMin.Value);
            if (queryParams.SkuCountMax.HasValue)
                q = q.Where(o => o.SkuCount <= queryParams.SkuCountMax.Value);

            var total = await q.CountAsync();

            var sortField = queryParams.SortField?.Trim().ToLowerInvariant();
            var sortDirection = queryParams.SortDirection?.Trim().ToLowerInvariant();
            if (sortDirection is not ("asc" or "desc"))
            {
                sortField = "ordertime";
                sortDirection = "asc";
            }

            var orderByType = sortDirection == "desc" ? OrderByType.Desc : OrderByType.Asc;
            // 排序字段仅允许以下白名单，非法字段统一回退到下单时间升序，避免动态 SQL 注入。
            q = sortField switch
            {
                "orderguid" => q.OrderBy(o => o.OrderGuid, orderByType),
                "branchcode" => q.OrderBy(o => o.BranchCode, orderByType),
                "devicecode" => q.OrderBy(o => o.DeviceCode, orderByType),
                "ordertime" => q.OrderBy(o => o.OrderTime, orderByType),
                "skucount" => q.OrderBy(o => o.SkuCount, orderByType),
                "itemcount" => q.OrderBy(o => o.ItemCount, orderByType),
                "totalamount" => q.OrderBy(o => o.TotalAmount, orderByType),
                "discountamount" => q.OrderBy(o => o.DiscountAmount, orderByType),
                "actualpay" => q.OrderBy(
                    o => o.TotalAmount - o.DiscountAmount,
                    orderByType
                ),
                _ => q.OrderBy(o => o.OrderTime, OrderByType.Asc),
            };
            q = q.OrderBy(o => o.OrderGuid, OrderByType.Asc);

            // 先用 long 计算再限制到 SqlSugar 的 int Skip 上限，避免极大页码乘法溢出为负数。
            var requestedSkip = ((long)pageNumber - 1L) * pageSize;
            var safeSkip = (int)Math.Min(requestedSkip, int.MaxValue);
            var items = await q.Skip(safeSkip)
                .Take(pageSize)
                .ToListAsync();

            try
            {
                var storeCodes = items
                    .Where(i => !string.IsNullOrEmpty(i.BranchCode))
                    .Select(i => i.BranchCode)
                    .Distinct()
                    .ToList();

                if (storeCodes.Any())
                {
                    var stores = await _context
                        .Db.Queryable<Store>()
                        .Where(s => storeCodes.Contains(s.StoreCode) && !s.IsDeleted)
                        .ToListAsync();
                    var storeDict = stores.ToDictionary(
                        s => s.StoreCode,
                        s => new
                        {
                            s.StoreName,
                            s.ABN,
                            s.BrandName,
                        }
                    );
                    foreach (var item in items)
                    {
                        if (
                            !string.IsNullOrEmpty(item.BranchCode)
                            && storeDict.TryGetValue(item.BranchCode, out var store)
                        )
                        {
                            item.BranchName = store.StoreName;
                            item.ABN = store.ABN;
                            item.BrandName = store.BrandName;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询分店信息失败，将只显示分店代码");
            }

            return new PagedListReactDto<PosmSalesOrderDto>
            {
                Items = items,
                Total = total,
                PageNumber = pageNumber,
                PageSize = pageSize,
            };
        }

        public async Task<Dictionary<string, List<PosmSalesOrderMatchedProductDto>>> GetMatchedProductsAsync(
            IReadOnlyCollection<string> orderGuids,
            string keyword
        )
        {
            var result = new Dictionary<string, List<PosmSalesOrderMatchedProductDto>>(
                StringComparer.Ordinal
            );
            var normalizedKeyword = keyword?.Trim();
            var guids = orderGuids
                .Where(guid => !string.IsNullOrWhiteSpace(guid))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (string.IsNullOrEmpty(normalizedKeyword) || guids.Count == 0)
            {
                return result;
            }

            // 命中条件必须与列表关键词过滤里的明细子查询保持一致，否则会出现"被搜出来却没有命中商品"的订单。
            var masterProductCodes = await ResolveMasterProductCodesAsync(normalizedKeyword);
            var detailQuery = _posmContext
                .Db.Queryable<SalesOrderDetail>()
                .Where(d => d.OrderGuid != null && guids.Contains(d.OrderGuid));
            detailQuery = masterProductCodes.Count > 0
                ? detailQuery.Where(d =>
                    d.ProductCode.Contains(normalizedKeyword)
                    || masterProductCodes.Contains(d.ProductCode)
                    || (d.Barcode != null && d.Barcode.Contains(normalizedKeyword))
                    || (d.ProductName != null && d.ProductName.Contains(normalizedKeyword))
                )
                : detailQuery.Where(d =>
                    d.ProductCode.Contains(normalizedKeyword)
                    || (d.Barcode != null && d.Barcode.Contains(normalizedKeyword))
                    || (d.ProductName != null && d.ProductName.Contains(normalizedKeyword))
                );
            var rows = await detailQuery
                .Select(d => new
                {
                    d.OrderGuid,
                    d.ProductCode,
                    d.ProductName,
                    d.Barcode,
                    d.Quantity,
                })
                .ToListAsync();
            var itemNumbers = await LookupItemNumbersAsync(
                rows.Select(row => row.ProductCode).Where(code => !string.IsNullOrWhiteSpace(code))!
            );

            foreach (var orderGroup in rows.GroupBy(row => row.OrderGuid!))
            {
                // 同一订单同一商品可能因不同折扣拆成多行，按货号聚合数量，避免命中提示重复。
                result[orderGroup.Key] = orderGroup
                    .GroupBy(row => row.ProductCode ?? string.Empty)
                    .Select(productGroup => new PosmSalesOrderMatchedProductDto
                    {
                        ProductCode = productGroup.Key,
                        ItemNumber = itemNumbers.TryGetValue(productGroup.Key, out var itemNumber)
                            ? itemNumber
                            : null,
                        ProductName = productGroup
                            .Select(row => row.ProductName)
                            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)),
                        Barcode = productGroup
                            .Select(row => row.Barcode)
                            .FirstOrDefault(barcode => !string.IsNullOrWhiteSpace(barcode)),
                        Quantity = productGroup.Sum(row => row.Quantity ?? 0),
                    })
                    .ToList();
            }

            return result;
        }

        /// <summary>主档里一次最多解析的商品数；关键词过泛时不让 IN 列表无限膨胀。</summary>
        private const int MaxMasterProductCodes = 500;

        /// <summary>
        /// 把关键词按货号 / 主档条码解析成 POSM 商品编码。
        /// 主档不可用时返回空列表，列表查询退回只按明细自身字段匹配，不能整体失败。
        /// </summary>
        private async Task<List<string>> ResolveMasterProductCodesAsync(string keyword)
        {
            try
            {
                var codes = await _context
                    .Db.Queryable<Product>()
                    .Where(p =>
                        !p.IsDeleted
                        && p.ProductCode != null
                        && (
                            (p.ItemNumber != null && p.ItemNumber.Contains(keyword))
                            || (p.Barcode != null && p.Barcode.Contains(keyword))
                        )
                    )
                    .Select(p => p.ProductCode)
                    .Take(MaxMasterProductCodes)
                    .ToListAsync();
                return codes
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "按货号解析商品编码失败，关键词将只匹配明细自身字段");
                return new List<string>();
            }
        }

        /// <summary>按商品编码回查主档货号，供命中提示与明细行展示。</summary>
        private async Task<Dictionary<string, string>> LookupItemNumbersAsync(IEnumerable<string> productCodes)
        {
            var codes = productCodes
                .Select(code => code.Trim())
                .Where(code => code.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (codes.Count == 0)
            {
                return result;
            }

            var products = await _context
                .Db.Queryable<Product>()
                .Where(p => p.ProductCode != null && codes.Contains(p.ProductCode))
                .Select(p => new { p.ProductCode, p.ItemNumber })
                .ToListAsync();
            foreach (var product in products)
            {
                if (!string.IsNullOrWhiteSpace(product.ProductCode) && !string.IsNullOrWhiteSpace(product.ItemNumber))
                {
                    result[product.ProductCode] = product.ItemNumber.Trim();
                }
            }
            return result;
        }

        public async Task<ApiResponse<PosmSalesOrderDetailResponse>> GetSalesOrderDetailAsync(
            string orderGuid
        )
        {
            try
            {
                var order = await _posmContext.SalesOrderDb.GetFirstAsync(o =>
                    o.OrderGuid == orderGuid
                );

                if (order == null)
                {
                    return new ApiResponse<PosmSalesOrderDetailResponse>
                    {
                        Success = false,
                        Message = "Order not found",
                    };
                }

                var orderDetails = await _posmContext.SalesOrderDetailDb.GetListAsync(d =>
                    d.OrderGuid == orderGuid
                );

                var paymentDetails = await _posmContext.PaymentDetailDb.GetListAsync(p =>
                    p.OrderGuid == orderGuid
                );

                var orderDto = _mapper.Map<PosmSalesOrderDto>(order);

                try
                {
                    if (!string.IsNullOrEmpty(order.BranchCode))
                    {
                        var store = await _context.StoreDb.GetFirstAsync(s =>
                            s.StoreCode == order.BranchCode && !s.IsDeleted
                        );
                        if (store != null)
                        {
                            orderDto.BranchName = store.StoreName;
                            orderDto.ABN = store.ABN;
                            orderDto.BrandName = store.BrandName;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "查询分店信息失败");
                }

                var detailDtos = _mapper.Map<List<PosmSalesOrderDetailDto>>(orderDetails);
                try
                {
                    var itemNumbers = await LookupItemNumbersAsync(
                        detailDtos.Select(d => d.ProductCode).Where(code => !string.IsNullOrWhiteSpace(code))!
                    );
                    foreach (var detail in detailDtos)
                    {
                        if (detail.ProductCode != null && itemNumbers.TryGetValue(detail.ProductCode, out var itemNumber))
                        {
                            detail.ItemNumber = itemNumber;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 货号只是展示增强，主档查询失败不能让整个详情失败。
                    _logger.LogError(ex, "查询商品货号失败，明细将只显示商品编码");
                }

                var response = new PosmSalesOrderDetailResponse
                {
                    Order = orderDto,
                    OrderDetails = detailDtos,
                    PaymentDetails = _mapper.Map<List<PosmPaymentDetailDto>>(paymentDetails),
                };

                return new ApiResponse<PosmSalesOrderDetailResponse>
                {
                    Success = true,
                    Data = response,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetSalesOrderDetailAsync failed");
                return new ApiResponse<PosmSalesOrderDetailResponse>
                {
                    Success = false,
                    Message = ex.Message,
                };
            }
        }
    }
}
