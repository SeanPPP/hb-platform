namespace BlazorApp.Shared.DTOs;

public class StoreVoucherQueryParams
{
    public string? StoreCode { get; set; }
    public List<string>? StoreCodes { get; set; }
    public string? Status { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class StoreVoucherDto
{
    public int ID { get; set; }
    public string? StoreCode { get; set; }
    public string? StoreName { get; set; }
    public string? VoucherCode { get; set; }
    public int? VoucherType { get; set; }
    public string? CustomerCode { get; set; }
    public decimal? DiscountRate { get; set; }
    public decimal? Amount { get; set; }
    public decimal? RemainingAmount { get; set; }
    public string? Status { get; set; }
    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }
    public string? CreateUser { get; set; }
    public string? UpdateUser { get; set; }
    public string? Remark { get; set; }
    public DateTime? ExpiredDate { get; set; }
}

public class StoreVoucherLedgerDto
{
    public string? ID { get; set; }
    public string? VoucherCode { get; set; }
    public string? Action { get; set; }
    public decimal? Amount { get; set; }
    public decimal? RemainingAmount { get; set; }
    public DateTime? ActionTime { get; set; }
    public int? PaymentMethod { get; set; }
    public string? Reference { get; set; }
    public string? OrderGuid { get; set; }
    public string? OperatorId { get; set; }
    public string? OperatorName { get; set; }
    public string? Remark { get; set; }
}

public class StoreVoucherRelatedOrderDto
{
    public string? OrderGuid { get; set; }
    public string? StoreCode { get; set; }
    public decimal? Amount { get; set; }
    public DateTime? OrderTime { get; set; }
}

public class StoreVoucherDetailResponse
{
    public StoreVoucherDto? Voucher { get; set; }
    public List<StoreVoucherLedgerDto> Ledger { get; set; } = new();
    public List<StoreVoucherRelatedOrderDto> RelatedOrders { get; set; } = new();
}
