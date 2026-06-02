using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities
{
    [SugarTable("CPT_DIC_外购客户信息表")]
    public class CPT_DIC_外购客户信息表
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int ID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? HGUID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 客户名称 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 联系人 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? EMAIL地址 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 电话 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 备用电话 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 备注 { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? 状态 { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? FGC_CreateDate { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? FGC_LastModifyDate { get; set; }
    }
}
