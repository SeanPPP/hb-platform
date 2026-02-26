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
            var baseQuery = _posmContext
                .Db.Queryable<SalesOrder>()
                .LeftJoin<SalesOrderDetail>((o, d) => o.OrderGuid == d.OrderGuid);

            if (queryParams.StartDate.HasValue)
            {
                var start = queryParams.StartDate.Value.Date;
                baseQuery = baseQuery.Where(o => o.OrderTime >= start);
            }
            if (queryParams.EndDate.HasValue)
            {
                var end = queryParams.EndDate.Value.Date.AddDays(1).AddMilliseconds(-1);
                baseQuery = baseQuery.Where(o => o.OrderTime <= end);
            }

            if (!string.IsNullOrWhiteSpace(queryParams.BranchCode))
            {
                baseQuery = baseQuery.Where(o => o.BranchCode == queryParams.BranchCode);
            }

            if (!string.IsNullOrWhiteSpace(queryParams.DeviceCode))
            {
                baseQuery = baseQuery.Where(o => o.DeviceCode == queryParams.DeviceCode);
            }

            if (queryParams.OrderType.HasValue && queryParams.OrderType.Value != OrderType.All)
            {
                baseQuery = baseQuery.Where(o => o.Status == (int)queryParams.OrderType.Value);
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
                        }
                )
                .OrderBy(o => o.OrderTime, OrderByType.Asc);

            var total = await q.CountAsync();

            var items = await q.Skip((queryParams.PageNumber - 1) * queryParams.PageSize)
                .Take(queryParams.PageSize)
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
                PageNumber = queryParams.PageNumber,
                PageSize = queryParams.PageSize,
            };
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

                var response = new PosmSalesOrderDetailResponse
                {
                    Order = orderDto,
                    OrderDetails = _mapper.Map<List<PosmSalesOrderDetailDto>>(orderDetails),
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
