using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.POSM;

[SugarTable("BankTransaction"), Tenant("HBPOSM")]
public class BankTransaction
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }
    
    [SugarColumn(IsNullable = true)]
    public string? TxnRef { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? PaymentGuid { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? OrderGuid { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? Caid { get; set; }//MerchantID
    [SugarColumn(IsNullable = true)]
    public string? AuthCode { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? CardType { get; set; }
    [SugarColumn(IsNullable = true)]
    public int? CardBIN { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? CardNumber { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? CardName { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? CardDateExpiry { get; set; }
    [SugarColumn(IsNullable = true)]
    public DateTime? BankDateTime { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? ResponseCode { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? ResponseText { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? Stan { get; set; }
    [SugarColumn(IsNullable = true)]
    public decimal? Amount { get; set; }
    [SugarColumn(IsNullable = true, Length = 1000)]
    public string? ReceiptText { get; set; }
 
}

public enum CardBIN
{
    Unknown = 0,
    DebitCard = 1,
    ChinaUnionPay = 2,
    MasterCard = 3,
    Visa = 4,
    AmericanExpress = 5,
    DinersClub = 6,
    JCB = 7,
    PrivateLabelCard = 8,
    JCB_9 = 9,       // 重复的JCB
    Maestro = 10,
    JCB_11 = 11,     // 重复的JCB
    Other = 12,
    Cabcharge = 13,
    Bartercard = 14,
    FuelCard = 15,
    Loyalty = 16,
    GiftCard = 17,
    ReturnCard = 18,
    ShopCard = 19,
    GECard = 20,
    NonFICard = 21,
    MyerBlackCard = 22,
    FleetCard = 23,
    Motopass = 24,
    Motorcharge = 25,
    Logo1 = 26,
    Logo2 = 27,
    VisaDebit = 28,
    MastercardDebit = 29,
    UnionpayCredit = 30,
    UnionpayDebit = 31,
    Wishlist = 51,
    GiveX = 52,
    Blackhawk = 53,
    PayPal = 54,
    Reserved55 = 55,
    Reserved56 = 56,
    FDI = 57,
    Reserved58 = 58,
    WrightExpress = 59,
    Reserved60 = 60,
    Reserved61 = 61,
    ePayUniversal = 63,
    Incomm = 64,
    AfterPay = 65,
    AliPay = 66,
    Humm = 67,
    FirstDataGiftCard = 68,
    WeChat = 69,
    Klarna = 70,
    ZipMoney = 89,
    TruRating = 90,
    Reserved98 = 98,
    Reserved99 = 99
}





