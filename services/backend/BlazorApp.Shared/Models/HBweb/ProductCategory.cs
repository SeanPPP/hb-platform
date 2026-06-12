using SqlSugar;
using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.Models
{
    [SugarTable("ProductCategory")]
    public class ProductCategory : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string CategoryGUID { get; set; } = Guid.NewGuid().ToString();

        [SugarColumn(IsNullable = true)]
        public string? ParentGUID { get; set; }

        [SugarColumn(IsNullable = false, Length = 100)]
        [Required(ErrorMessage = "类别名称不能为空")]
        [Display(Name = "类别名称")]
        public string CategoryName { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false)]
        public bool IsActive { get; set; } = true;

        [SugarColumn(IsNullable = true)]
        public int? SortOrder { get; set; }

        [Navigate(NavigateType.OneToMany, nameof(ParentGUID))]
        public List<ProductCategory> Children { get; set; } = new List<ProductCategory>();

        [Navigate(NavigateType.OneToOne, nameof(ParentGUID))]
        public ProductCategory? Parent { get; set; }
    }
}
