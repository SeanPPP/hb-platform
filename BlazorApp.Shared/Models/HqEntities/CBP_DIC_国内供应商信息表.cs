using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities
{
    [SugarTable("CPT_DIC_供应商信息表")]
    public class CBP_DIC_国内供应商信息表
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int ID { get; set; }

        [SugarColumn(IsNullable = true, ColumnName = "HGUID")]
        public string? HGUID { get; set; }

        [SugarColumn(IsNullable = true, ColumnName = "供应商编码")]
        public string? H供应商编码 { get; set; }

        [SugarColumn(IsNullable = true, ColumnName = "供应商名称")]
        public string? H供应商名称 { get; set; }

        [SugarColumn(IsNullable = true, ColumnName = "商铺编号")]
        public string? H商铺编号 { get; set; }

        [SugarColumn(IsNullable = true, ColumnName = "联系人")]
        public string? H联系人 { get; set; }

        [SugarColumn(IsNullable = true, ColumnName = "电话")]
        public string? H电话 { get; set; }

        [SugarColumn(IsNullable = true, ColumnName = "EMAIL地址")]
        public string? HEMAIL地址 { get; set; }

        [SugarColumn(IsNullable = true, ColumnName = "商户门头照片")]
        public string? H商户门头照片 { get; set; }

        [SugarColumn(IsNullable = true, ColumnName = "备注")]
        public string? 备注 { get; set; }

        [SugarColumn(IsNullable = true, ColumnName = "状态")]
        public int? 状态 { get; set; }

        [SugarColumn(IsNullable = true, ColumnName = "供应商类型")]
        public int? H供应商类型 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? FGC_Creator { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? FGC_CreateDate { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? FGC_LastModifier { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? FGC_LastModifyDate { get; set; }

        [SugarColumn(IsNullable = true, IsOnlyIgnoreInsert = true)]
        public string? FGC_Rowversion { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? FGC_UpdateHelp { get; set; }
    }
}
