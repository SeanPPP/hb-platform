using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BlazorApp.Shared.DTOs
{
    public class ProductCategoryDto
    {
        public string CategoryGUID { get; set; } = string.Empty;

        public string? ParentGUID { get; set; }

        [Required(ErrorMessage = "类别名称不能为空")]
        [StringLength(100, ErrorMessage = "类别名称不能超过100个字符")]
        public string CategoryName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public int? SortOrder { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public List<ProductCategoryDto> Children { get; set; } = new List<ProductCategoryDto>();

        [JsonIgnore]
        public ProductCategoryDto? Parent { get; set; }
    }

    public class CreateProductCategoryDto
    {
        public string? ParentGUID { get; set; }

        [Required(ErrorMessage = "类别名称不能为空")]
        [StringLength(100, ErrorMessage = "类别名称不能超过100个字符")]
        public string CategoryName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public int? SortOrder { get; set; }
    }

    public class UpdateProductCategoryDto
    {
        [Required(ErrorMessage = "CategoryGUID不能为空")]
        public string CategoryGUID { get; set; } = string.Empty;

        public string? ParentGUID { get; set; }

        [Required(ErrorMessage = "类别名称不能为空")]
        [StringLength(100, ErrorMessage = "类别名称不能超过100个字符")]
        public string CategoryName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public int? SortOrder { get; set; }
    }

    public class ProductCategoryFilterDto
    {
        public string? CategoryName { get; set; }

        public bool? IsActive { get; set; }

        public string? ParentGUID { get; set; }

        public int PageNumber { get; set; } = 1;

        public int PageSize { get; set; } = 10;

        public string? SortBy { get; set; } = "CategoryName";

        public bool SortDescending { get; set; } = false;
    }
}
