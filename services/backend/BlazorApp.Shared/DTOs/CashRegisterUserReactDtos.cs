using System;
using System.Collections.Generic;

namespace BlazorApp.Shared.DTOs
{
    public class CashRegisterUserListDto
    {
        public int Id { get; set; }
        public string HGUID { get; set; } = string.Empty;
        public string? StoreCode { get; set; }
        public string? StoreName { get; set; }
        public string? LegacyStoreCode { get; set; }
        public string? UserGUID { get; set; }
        public string? Username { get; set; }
        public string? UserFullName { get; set; }
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
        public string? LegacyStoreCode { get; set; }
        public string? UserGUID { get; set; }
        public string? Username { get; set; }
        public string? UserFullName { get; set; }
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

    public class CashRegisterUserUserOptionDto
    {
        public string UserGUID { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string? UserFullName { get; set; }
    }

    public class CreateCashRegisterUserDto
    {
        public string? StoreCode { get; set; }
        public string? UserGUID { get; set; }
        public string? OperatorUser { get; set; }
        public string? UserBarcode { get; set; }
        public string? LoginRole { get; set; }
        public string? Remark { get; set; }
        public bool Status { get; set; } = true;
    }

    /// <summary>
    /// 移动端打印收银条码标签成功后回传；带上实际打印的条码，防止换码后旧码确认被记到新码上。
    /// </summary>
    public class ConfirmCashRegisterUserPrintDto
    {
        public string? UserBarcode { get; set; }
    }

    /// <summary>
    /// 当前账号在收银条码页的管理范围（后端判定），移动端据此决定可新建的分店与提示。
    /// </summary>
    public class CashRegisterUserScopeDto
    {
        public bool IsAdmin { get; set; }
        /// <summary>服务端实时判定的移动端管理/打印权限；角色授权变更后无需重新登录即可生效。</summary>
        public bool CanManage { get; set; }
        public bool CanPrint { get; set; }
        public List<CashRegisterUserScopeStoreDto> ManageableStores { get; set; } = new();
    }

    public class CashRegisterUserScopeStoreDto
    {
        public string? StoreCode { get; set; }
        public string? StoreName { get; set; }
    }

    public class CashRegisterUserPrintConfirmationDto
    {
        public string HGUID { get; set; } = string.Empty;
        public int PrintCount { get; set; }
    }

    public class UpdateCashRegisterUserDto
    {
        public string? StoreCode { get; set; }
        public string? UserGUID { get; set; }
        public string? OperatorUser { get; set; }
        public string? UserBarcode { get; set; }
        public string? LoginRole { get; set; }
        public string? Remark { get; set; }
        public bool Status { get; set; }
    }
}
