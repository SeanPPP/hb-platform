using BlazorApp.Shared.Helper;
using SqlSugar;

namespace BlazorApp.Shared.Models
{
    [SugarTable("ProductGrade")]
    public class ProductGrade : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string Id { get; set; } = UuidHelper.GenerateUuid7();

        [SugarColumn(
            IsNullable = false,
            Length = 50,
            UniqueGroupNameList = new[] { "idx_product_grade_code" }
        )]
        public string ProductCode { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false, Length = 1)]
        public string Grade { get; set; } = string.Empty;

        [Navigate(NavigateType.OneToOne, nameof(ProductCode), nameof(DomesticProduct.ProductCode))]
        public DomesticProduct? DomesticProduct { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(ProductCode), nameof(Models.Product.UUID))]
        public Product? Product { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(ProductCode), nameof(WarehouseProduct.ProductCode))]
        public WarehouseProduct? WarehouseProduct { get; set; }
    }
}
