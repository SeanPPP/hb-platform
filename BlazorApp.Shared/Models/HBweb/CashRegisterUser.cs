using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 收银用户信息实体类，表示收银系统的用户信息
    /// </summary>
    [SugarTable("CashRegisterUsers")]
    public class CashRegisterUser
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 全局唯一标识符
        /// </summary>
        [SugarColumn(ColumnName = "HGUID", Length = 50)]
        public string HGUID { get; set; } = string.Empty;

        /// <summary>
        /// 分店代码
        /// </summary>
        [SugarColumn(ColumnName = "StoreCode", Length = 50)]
        public string StoreCode { get; set; } = string.Empty;

        /// <summary>
        /// 操作用户
        /// </summary>
        [SugarColumn(ColumnName = "OperatorUser", Length = 100)]
        public string OperatorUser { get; set; } = string.Empty;

        /// <summary>
        /// 用户条码
        /// </summary>
        [SugarColumn(ColumnName = "UserBarcode", Length = 50)]
        public string UserBarcode { get; set; } = string.Empty;

        /// <summary>
        /// 登陆角色
        /// </summary>
        [SugarColumn(ColumnName = "LoginRole", Length = 50)]
        public string LoginRole { get; set; } = string.Empty;

        /// <summary>
        /// 备注
        /// </summary>
        [SugarColumn(ColumnName = "Remark", Length = 500)]
        public string? Remark { get; set; }

        /// <summary>
        /// 条码打印次数
        /// </summary>
        [SugarColumn(ColumnName = "PrintCount")]
        public int PrintCount { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        [SugarColumn(ColumnName = "Status")]
        public bool Status { get; set; }

        /// <summary>
        /// 创建者
        /// </summary>
        [SugarColumn(ColumnName = "Creator", Length = 100)]
        public string? Creator { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [SugarColumn(ColumnName = "CreateDate")]
        public DateTime CreateDate { get; set; }

        /// <summary>
        /// 最后修改者
        /// </summary>
        [SugarColumn(ColumnName = "LastModifier", Length = 100)]
        public string? LastModifier { get; set; }

        /// <summary>
        /// 最后修改时间
        /// </summary>
        [SugarColumn(ColumnName = "LastModifyDate")]
        public DateTime LastModifyDate { get; set; }
    }
}
