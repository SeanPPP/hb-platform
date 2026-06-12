using System;
using SqlSugar;

namespace BlazorApp.Shared.Models
{
    [SugarTable("LocalSupplier")]
    public class HBLocalSupplier : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true)]
        public string? Guid { get; set; }

        [SugarColumn(
            IsNullable = false,
            Length = 64,
            UniqueGroupNameList = new[] { "uk_local_supplier_code" }
        )]
        public string LocalSupplierCode { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false, Length = 128)]
        public string Name { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false)]
        public int Status { get; set; } = 1;

        [SugarColumn(IsNullable = true, Length = 64)]
        public string? ContactPerson { get; set; }

        [SugarColumn(IsNullable = true, Length = 32)]
        public string? Phone { get; set; }

        [SugarColumn(IsNullable = true, Length = 128)]
        public string? Email { get; set; }

        [SugarColumn(IsNullable = true, Length = 256)]
        public string? Remark { get; set; }

        [SugarColumn(IsNullable = true, Length = 512)]
        public string? ImageBaseUrl { get; set; }
    }
}
