using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    public class BatchMoveCategoriesDto
    {
        [Required]
        public List<string> CategoryGuids { get; set; } = new();
        public string? NewParentGuid { get; set; }
    }

    public class BatchToggleActiveDto
    {
        [Required]
        public List<string> CategoryGuids { get; set; } = new();
        public bool IsActive { get; set; }
    }

    public class BatchSortItemDto
    {
        [Required]
        public string CategoryGuid { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }

    public class BatchSortRequestDto
    {
        [Required]
        public List<BatchSortItemDto> Items { get; set; } = new();
    }

    public class BatchAssignProductsRequestDto
    {
        [Required]
        public string CategoryGuid { get; set; } = string.Empty;
        [Required]
        public List<string> ProductCodes { get; set; } = new();
    }

    public class BatchUnassignProductsRequestDto
    {
        [Required]
        public List<string> ProductCodes { get; set; } = new();
    }
}