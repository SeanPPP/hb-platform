using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

public class StoreVoucherReactService : IStoreVoucherReactService
{
    private readonly POSMSqlSugarContext _posmContext;
    private readonly SqlSugarContext _context;

    public StoreVoucherReactService(POSMSqlSugarContext posmContext, SqlSugarContext context)
    {
        _posmContext = posmContext;
        _context = context;
    }

    public async Task<PagedListReactDto<StoreVoucherDto>> GetVoucherListAsync(
        StoreVoucherQueryParams queryParams
    )
    {
        var pageNumber = Math.Max(1, queryParams.PageNumber);
        var pageSize = new[] { 20, 50, 100 }.Contains(queryParams.PageSize)
            ? queryParams.PageSize
            : 20;
        var query = _posmContext.Db.Queryable<StoreVoucher>().Where(v => v.IsDelete != true);

        if (!string.IsNullOrWhiteSpace(queryParams.StoreCode))
            query = query.Where(v => v.StoreCode == queryParams.StoreCode.Trim());
        if (queryParams.StoreCodes?.Count > 0)
            query = query.Where(v => queryParams.StoreCodes.Contains(v.StoreCode!));
        if (!string.IsNullOrWhiteSpace(queryParams.Status))
            query = query.Where(v => v.Status == queryParams.Status.Trim());
        if (queryParams.StartDate.HasValue)
            query = query.Where(v => v.CreateTime >= queryParams.StartDate.Value.Date);
        if (queryParams.EndDate.HasValue)
        {
            var endExclusive = queryParams.EndDate.Value.Date.AddDays(1);
            query = query.Where(v => v.CreateTime < endExclusive);
        }

        var total = await query.CountAsync();
        var vouchers = await query
            .OrderBy(v => v.CreateTime, OrderByType.Desc)
            .ToPageListAsync(pageNumber, pageSize);
        var items = vouchers.Select(MapVoucher).ToList();
        await AttachStoreNamesAsync(items);

        return new PagedListReactDto<StoreVoucherDto>
        {
            Items = items,
            Total = total,
            PageNumber = pageNumber,
            PageSize = pageSize,
        };
    }

    public async Task<ApiResponse<StoreVoucherDetailResponse>> GetVoucherDetailAsync(string idOrCode)
    {
        var target = idOrCode.Trim();
        var query = _posmContext.Db.Queryable<StoreVoucher>().Where(v => v.IsDelete != true);
        StoreVoucher? voucher;

        if (int.TryParse(target, out var id))
            voucher = await query.Where(v => v.ID == id || v.VoucherCode == target).FirstAsync();
        else
            voucher = await query.Where(v => v.VoucherCode == target).FirstAsync();

        if (voucher == null)
            return new ApiResponse<StoreVoucherDetailResponse>
            {
                Success = false,
                Message = "Voucher not found",
            };

        var voucherDto = MapVoucher(voucher);
        await AttachStoreNamesAsync(new List<StoreVoucherDto> { voucherDto });

        var payments = string.IsNullOrWhiteSpace(voucher.VoucherCode)
            ? new List<PaymentDetail>()
            : await _posmContext.Db.Queryable<PaymentDetail>()
                .Where(p =>
                    p.PaymentMethod == (int)PaymentMethod.Voucher
                    && p.Reference == voucher.VoucherCode
                )
                .OrderBy(p => p.CreatedTime, OrderByType.Asc)
                .ToListAsync();
        var orderGuids = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.OrderGuid))
            .Select(p => p.OrderGuid!)
            .Distinct()
            .ToList();
        var orders = orderGuids.Count == 0
            ? new List<SalesOrder>()
            : await _posmContext.Db.Queryable<SalesOrder>()
                .Where(o => orderGuids.Contains(o.OrderGuid!))
                .ToListAsync();
        var orderByGuid = orders
            .Where(o => !string.IsNullOrWhiteSpace(o.OrderGuid))
            .ToDictionary(o => o.OrderGuid!, StringComparer.OrdinalIgnoreCase);

        var response = new StoreVoucherDetailResponse
        {
            Voucher = voucherDto,
            Ledger = new List<StoreVoucherLedgerDto>
            {
                new()
                {
                    ID = $"issued-{voucher.ID}",
                    VoucherCode = voucher.VoucherCode,
                    Action = "issued",
                    Amount = voucher.Amount,
                    RemainingAmount = voucher.Amount,
                    ActionTime = voucher.CreateTime,
                    OperatorName = voucher.CreateUser,
                    Remark = voucher.Remark,
                },
            },
        };

        foreach (var payment in payments)
        {
            response.Ledger.Add(new StoreVoucherLedgerDto
            {
                ID = payment.PaymentGuid,
                VoucherCode = voucher.VoucherCode,
                Action = "used",
                Amount = payment.Amount,
                ActionTime = payment.CreatedTime,
                PaymentMethod = payment.PaymentMethod,
                Reference = payment.Reference,
                OrderGuid = payment.OrderGuid,
                OperatorId = payment.CashierId,
                OperatorName = payment.CashierName,
            });

            if (
                payment.OrderGuid != null
                && orderByGuid.TryGetValue(payment.OrderGuid, out var order)
            )
            {
                response.RelatedOrders.Add(new StoreVoucherRelatedOrderDto
                {
                    OrderGuid = order.OrderGuid,
                    StoreCode = order.BranchCode,
                    Amount = payment.Amount,
                    OrderTime = order.OrderTime,
                });
            }
        }

        return ApiResponse<StoreVoucherDetailResponse>.OK(response);
    }

    private async Task AttachStoreNamesAsync(List<StoreVoucherDto> vouchers)
    {
        var storeCodes = vouchers
            .Where(v => !string.IsNullOrWhiteSpace(v.StoreCode))
            .Select(v => v.StoreCode!)
            .Distinct()
            .ToList();
        if (storeCodes.Count == 0)
            return;

        var stores = await _context.Db.Queryable<Store>()
            .Where(s => storeCodes.Contains(s.StoreCode) && !s.IsDeleted)
            .Select(s => new { s.StoreCode, s.StoreName })
            .ToListAsync();
        var names = stores.ToDictionary(s => s.StoreCode, s => s.StoreName);
        foreach (var voucher in vouchers)
        {
            if (voucher.StoreCode != null && names.TryGetValue(voucher.StoreCode, out var name))
                voucher.StoreName = name;
        }
    }

    private static StoreVoucherDto MapVoucher(StoreVoucher voucher) =>
        new()
        {
            ID = voucher.ID,
            StoreCode = voucher.StoreCode,
            VoucherCode = voucher.VoucherCode,
            VoucherType = voucher.VoucherType,
            CustomerCode = voucher.CustomerCode,
            DiscountRate = voucher.DiscountRate,
            Amount = voucher.Amount,
            RemainingAmount = voucher.RemainingAmount,
            Status = voucher.Status,
            CreateTime = voucher.CreateTime,
            UpdateTime = voucher.UpdateTime,
            CreateUser = voucher.CreateUser,
            UpdateUser = voucher.UpdateUser,
            Remark = voucher.Remark,
            ExpiredDate = voucher.ExpiredDate,
        };
}
