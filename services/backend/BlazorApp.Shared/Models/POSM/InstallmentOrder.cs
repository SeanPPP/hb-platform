using SqlSugar;

namespace BlazorApp.Shared.Models.POSM;

/// <summary>
/// WPF POS 写入的分期订单主表。
/// </summary>
[SugarTable("InstallmentOrder")]
public sealed class InstallmentOrder
{
    [SugarColumn(IsPrimaryKey = true, Length = 36)]
    public string InstallmentGuid { get; set; } = string.Empty;

    [SugarColumn(Length = 40)]
    public string InstallmentNumber { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string StoreCode { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string DeviceCode { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string CashierId { get; set; } = string.Empty;

    [SugarColumn(Length = 100)]
    public string CashierName { get; set; } = string.Empty;

    [SugarColumn(Length = 100)]
    public string CustomerName { get; set; } = string.Empty;

    [SugarColumn(Length = 40)]
    public string CustomerPhone { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }
    public decimal MinimumDownPayment { get; set; }
    public decimal DownPaymentAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceAmount { get; set; }
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? PickedUpAt { get; set; }

    [SugarColumn(Length = 100, IsNullable = true)]
    public string? PickedUpBy { get; set; }

    [SugarColumn(Length = 500, IsNullable = true)]
    public string? Note { get; set; }

    [SugarColumn(Length = 500, IsNullable = true)]
    public string? PickupNote { get; set; }

    [SugarColumn(IsNullable = true)]
    public int? CancellationKind { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CancelledAt { get; set; }

    [SugarColumn(Length = 100, IsNullable = true)]
    public string? CancelledBy { get; set; }

    [SugarColumn(Length = 500, IsNullable = true)]
    public string? CancellationReason { get; set; }

    [SugarColumn(Length = 100, IsNullable = true)]
    public string? CancellationIdempotencyKey { get; set; }
}
