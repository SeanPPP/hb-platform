using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlazorApp.Shared.DTOs
{
    public class CategoryDto
    {
        public string CategoryGUID { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string? ParentCategoryGUID { get; set; }
        public List<CategoryDto> Children { get; set; } = new List<CategoryDto>();
    }
}