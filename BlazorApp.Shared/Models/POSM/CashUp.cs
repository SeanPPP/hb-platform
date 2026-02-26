using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.POSM;

/// <summary>
/// 日结单实体类
/// </summary>
[SugarTable("CashUp"), Tenant("HBPOSM")]
public class CashUp 
{
    // 日结单ID
    [SugarColumn(ColumnName = "cash_up_guid", IsPrimaryKey = true, ColumnDescription = "日结单ID")]
    public string CashUpGuid { get; set; }

    // 日结单编号
    [SugarColumn(ColumnName = "cash_up_number", ColumnDescription = "日结单编号", Length = 50)]
    public string CashUpNumber { get; set; }
    
    // 分店编码
    [SugarColumn(ColumnName = "store_code", ColumnDescription = "分店编码", Length = 50)]
    public string StoreCode { get; set; } = string.Empty;

    // 设备编码
    [SugarColumn(ColumnName = "device_code", ColumnDescription = "设备编码", Length = 50)]
    public string DeviceCode { get; set; } = string.Empty;

    // 日结日期
    [SugarColumn(ColumnName = "cash_up_date", ColumnDescription = "日结日期")]
    public DateTime CashUpDate { get; set; } = DateTime.Now;

    // 开始时间
    [SugarColumn(ColumnName = "start_time", ColumnDescription = "开始时间")]
    public DateTime StartTime { get; set; } = DateTime.Now;

    // 结束时间
    [SugarColumn(ColumnName = "end_time", ColumnDescription = "结束时间")]
    public DateTime EndTime { get; set; } = DateTime.Now;

    // 收银员
    [SugarColumn(ColumnName = "cashier", ColumnDescription = "收银员", Length = 50)]
    public string Cashier { get; set; } = "Cashier";

    // 现金收入
    [SugarColumn(ColumnName = "cash_income", ColumnDescription = "现金收入", DecimalDigits = 2)]
    public decimal CashIncome { get; set; } = 0;

    // 银行卡收入
    [SugarColumn(ColumnName = "card_income", ColumnDescription = "银行卡收入", DecimalDigits = 2)]
    public decimal CardIncome { get; set; } = 0;

    // 其他支付方式收入
    [SugarColumn(ColumnName = "Voucher_income", ColumnDescription = "其他支付方式收入", DecimalDigits = 2)]
    public decimal VoucherIncome { get; set; } = 0;

    // 总收入
    [SugarColumn(ColumnName = "total_income", ColumnDescription = "总收入", DecimalDigits = 2)]
    public decimal TotalIncome { get; set; } = 0;

    // 退款金额
    [SugarColumn(ColumnName = "refund_amount", ColumnDescription = "退款金额", DecimalDigits = 2)]
    public decimal RefundAmount { get; set; } = 0;

    // 实际收入(总收入-退款)
    [SugarColumn(ColumnName = "actual_income", ColumnDescription = "实际收入", DecimalDigits = 2)]
    public decimal ActualIncome { get; set; } = 0;

    // 订单数量
    [SugarColumn(ColumnName = "order_count", ColumnDescription = "订单数量")]
    public int OrderCount { get; set; } = 0;

    // 退款订单数量
    [SugarColumn(ColumnName = "refund_order_count", ColumnDescription = "退款订单数量")]
    public int RefundOrderCount { get; set; } = 0;

    // 备注
    [SugarColumn(ColumnName = "remarks", ColumnDescription = "备注", Length = 500)]
    public string Remarks { get; set; } = "";

    // 创建时间
    [SugarColumn(ColumnName = "create_time", ColumnDescription = "创建时间")]
    public DateTime CreateTime { get; set; } = DateTime.Now;

    // 更新时间
    [SugarColumn(ColumnName = "update_time", ColumnDescription = "更新时间")]
    public DateTime UpdateTime { get; set; } = DateTime.Now;

    // 是否已上传
    [SugarColumn(ColumnName = "is_uploaded", ColumnDescription = "是否已上传")]
    public bool IsUploaded { get; set; } = false;

    // 100元数量
    [SugarColumn(ColumnName = "hundred", ColumnDescription = "100元数量")]
    public int Hundred { get; set; } = 0;

    // 50元数量
    [SugarColumn(ColumnName = "fifty", ColumnDescription = "50元数量")]
    public int Fifty { get; set; } = 0;

    // 20元数量
    [SugarColumn(ColumnName = "twenty", ColumnDescription = "20元数量")]
    public int Twenty { get; set; } = 0;

    // 10元数量
    [SugarColumn(ColumnName = "ten", ColumnDescription = "10元数量")]
    public int Ten { get; set; } = 0;

    // 5元数量
    [SugarColumn(ColumnName = "five", ColumnDescription = "5元数量")]
    public int Five { get; set; } = 0;

    // 2元硬币数量
    [SugarColumn(ColumnName = "two_coin", ColumnDescription = "2元硬币数量")]
    public int TwoCoin { get; set; } = 0;

    // 1元硬币数量
    [SugarColumn(ColumnName = "one_coin", ColumnDescription = "1元硬币数量")]
    public int OneCoin { get; set; } = 0;

    // 5角硬币数量
    [SugarColumn(ColumnName = "fifty_cent_coin", ColumnDescription = "5角硬币数量")]
    public int FiftyCentCoin { get; set; } = 0;

    // 2角硬币数量
    [SugarColumn(ColumnName = "twenty_cent_coin", ColumnDescription = "2角硬币数量")]
    public int TwentyCentCoin { get; set; } = 0;

    // 1角硬币数量
    [SugarColumn(ColumnName = "ten_cent_coin", ColumnDescription = "1角硬币数量")]
    public int TenCentCoin { get; set; } = 0;

    // 5分硬币数量
    [SugarColumn(ColumnName = "five_cent_coin", ColumnDescription = "5分硬币数量")]
    public int FiveCentCoin { get; set; } = 0;

    // 总现金金额
    [SugarColumn(ColumnName = "total_cash", ColumnDescription = "总现金金额", DecimalDigits = 2)]
    public decimal TotalCash { get; set; } = 0;

      [SugarColumn(IsNullable = true)]
        public DateTime? LastUploadTime { get; set; }  // 上传时间

        
        
    }
