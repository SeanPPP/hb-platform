using SqlSugar;

namespace BlazorApp.Shared.Models.POSM;

/// <summary>
/// WPF POS 写入的分期付款记录表。
/// </summary>
[SugarTable("InstallmentPayment")]
public sealed class InstallmentPayment
{
    [SugarColumn(IsPrimaryKey = true, Length = 36)]
    public string PaymentGuid { get; set; } = string.Empty;

    [SugarColumn(Length = 36)]
    public string InstallmentGuid { get; set; } = string.Empty;

    public int Method { get; set; }
    public decimal Amount { get; set; }

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? Reference { get; set; }

    public int Status { get; set; }
    public DateTime RecordedAt { get; set; }

    [SugarColumn(Length = 50)]
    public string CashierId { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string DeviceCode { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public string? CardTransactionsJson { get; set; }

    [SugarColumn(Length = 100, IsNullable = true)]
    public string? IdempotencyKey { get; set; }
}
