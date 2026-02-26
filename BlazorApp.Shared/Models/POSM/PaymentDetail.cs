using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.POSM;

 /// <summary>
    /// 支付明细表
    /// </summary>
    [SugarTable("payment_detail"), Tenant("HBPOSM")]
    public class PaymentDetail
    {
        [SugarColumn(IsPrimaryKey = true)]
        public string? PaymentGuid { get; set; } = Guid.NewGuid().ToString();  // 支付ID

        [SugarColumn(IsNullable = true)]
        public string? OrderGuid { get; set; } = string.Empty;  // 订单ID
        [SugarColumn(IsNullable = true)]
        public string? CashupGuid { get; set; } = string.Empty;  // 日结ID

        [SugarColumn(IsNullable = true)]
        public int PaymentMethod { get; set; } = 1  ;  // 支付方式1 现金 2 刷卡 3 代金券

        [SugarColumn( IsNullable = true)]
        public decimal? Amount { get; set; } = 0;  // 支付金额

        [SugarColumn(Length = 100, IsNullable = true)]
        public string? Reference { get; set; } = string.Empty;  // 支付参考号（刷卡流水号/代金券编号）

        [SugarColumn(Length = 100, IsNullable = true)]
        public string? CashierId { get; set; } = string.Empty;  // 员工ID

        [SugarColumn(Length = 100, IsNullable = true)]
        public string? CashierName { get; set; } = string.Empty;  // 员工名字

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? CreatedBy { get; set; } = string.Empty;  // 创建人

        [SugarColumn(IsNullable = true)]
        public DateTime? CreatedTime { get; set; }  // 创建时间

        [SugarColumn(Length = 50, IsNullable = true)] 
        public string? UpdatedBy { get; set; } = string.Empty;  // 修改人


        [SugarColumn(IsNullable = true)]
        public DateTime? UpdatedTime { get; set; }  // 修改时间

        [SugarColumn(IsNullable = true)]
        public DateTime? LastUploadTime { get; set; }  // 上传时间

        [Navigate(NavigateType.OneToOne, nameof(OrderGuid))]
        public SalesOrder? SalesOrder { get; set; }  // 订单主表

        [Navigate(NavigateType.OneToOne, nameof(CashupGuid))]
        public CashUp? Cashup { get; set; }  // 日结主表

        [SugarColumn(IsIgnore = true)]
        [Navigate(NavigateType.OneToMany, nameof(BankTransaction.PaymentGuid))]
        public List<BankTransaction>? BankTransactions { get; set; }  // 银行交易
    }

    /// <summary>
    /// 支付方式枚举
    /// </summary>
    public enum PaymentMethod
    {
        Cash = 1,       // 现金
        Card = 2,       // 刷卡
        Voucher = 3     // 代金券
    }
