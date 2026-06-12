using System;

namespace BlazorApp.Shared.DTOs
{
    public class CashRegisterUserListDto
    {
        public int Id { get; set; }
        public string HGUID { get; set; } = string.Empty;
        public string? StoreCode { get; set; }
        public string? StoreName { get; set; }
        public string? OperatorUser { get; set; }
        public string? UserBarcode { get; set; }
        public string? LoginRole { get; set; }
        public string? Remark { get; set; }
        public int PrintCount { get; set; }
        public bool Status { get; set; }
        public DateTime CreateDate { get; set; }
        public DateTime LastModifyDate { get; set; }
        public string? LastModifier { get; set; }
    }

    public class CashRegisterUserDetailDto
    {
        public int Id { get; set; }
        public string HGUID { get; set; } = string.Empty;
        public string? StoreCode { get; set; }
        public string? StoreName { get; set; }
        public string? OperatorUser { get; set; }
        public string? UserBarcode { get; set; }
        public string? LoginRole { get; set; }
        public string? Remark { get; set; }
        public int PrintCount { get; set; }
        public bool Status { get; set; }
        public string? Creator { get; set; }
        public DateTime CreateDate { get; set; }
        public string? LastModifier { get; set; }
        public DateTime LastModifyDate { get; set; }
    }

    public class CreateCashRegisterUserDto
    {
        public string? StoreCode { get; set; }
        public string? OperatorUser { get; set; }
        public string? UserBarcode { get; set; }
        public string? LoginRole { get; set; }
        public string? Remark { get; set; }
        public bool Status { get; set; } = true;
    }

    public class UpdateCashRegisterUserDto
    {
        public string? StoreCode { get; set; }
        public string? OperatorUser { get; set; }
        public string? UserBarcode { get; set; }
        public string? LoginRole { get; set; }
        public string? Remark { get; set; }
        public bool Status { get; set; }
    }
}
