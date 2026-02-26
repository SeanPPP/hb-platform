using SqlSugar;
using System;

namespace BlazorApp.Shared.Models.POSM
{
    [SugarTable("posm_product_supplier_mapping")]
    public class PosmProductSupplierMapping : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsNullable = false, Length = 50)]
        public string ProductCode { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false, Length = 50)]
        public string LocalSupplierCode { get; set; } = string.Empty;

        [SugarColumn(IsNullable = true, Length = 50)]
        public string? ChinaSupplierCode { get; set; }

        [SugarColumn(IsNullable = false)]
        public DateTime LastUpdateTime { get; set; } = DateTime.Now;

        [SugarColumn(IsNullable = false)]
        public bool IsDeleted { get; set; } = false;
    }
}
