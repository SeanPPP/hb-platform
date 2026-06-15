using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SqlSugar;

using BlazorApp.Shared.DTOs;

namespace BlazorApp.Shared.DTOs
{
    public class LocationReactFilterDto
    {
        public int? LocationType { get; set; }

        public bool? IsUsed { get; set; }

        public string? LocationCode { get; set; }

        public string? LocationBarcode { get; set; }

        public int? Status { get; set; }

        public DateTime? UpdatedAtStart { get; set; }

        public DateTime? UpdatedAtEnd { get; set; }

        public string? UpdatedBy { get; set; }

        public Dictionary<string, string[]>? Filters { get; set; }

        public int PageNumber { get; set; } = 1;

        public int PageSize { get; set; } = 20;

        public string? SortBy { get; set; } = "LocationCode";

        public string? SortDirection { get; set; } = "asc";
    }

    public class LocationReactDto
    {
        public string LocationGuid { get; set; } = string.Empty;

        public string? LocationCode { get; set; }

        public string? LocationBarcode { get; set; }

        public int? Status { get; set; }

        public int? LocationType { get; set; }

        public List<LocationReactProductDto> Products { get; set; } = new();

        public DateTime? UpdatedAt { get; set; }

        public string? UpdatedBy { get; set; }
    }

    public class LocationReactProductDto
    {
        public string? ProductCode { get; set; }

        public string? ItemNumber { get; set; }

        public string? Barcode { get; set; }

        public string? ProductName { get; set; }

        public string? ProductImage { get; set; }

        public int? MiddlePackageQuantity { get; set; }
    }

    public class CreateLocationReactDto
    {
        [Required(ErrorMessage = "货位代码不能为空")]
        [StringLength(50)]
        public string LocationCode { get; set; } = string.Empty;

        [StringLength(50)]
        public string? LocationBarcode { get; set; }

        public int? LocationType { get; set; }

        public int? Status { get; set; } = 1;
    }

    public class UpdateLocationReactDto
    {
        [Required(ErrorMessage = "货位代码不能为空")]
        [StringLength(50)]
        public string LocationCode { get; set; } = string.Empty;

        [StringLength(50)]
        public string? LocationBarcode { get; set; }

        public int? LocationType { get; set; }

        public int? Status { get; set; }
    }

    public class LocationLookupItemDto
    {
        public string LocationGuid { get; set; } = string.Empty;
        public string? LocationCode { get; set; }
        public string? LocationBarcode { get; set; }
        public int? Status { get; set; }
        public int? LocationType { get; set; }
        public int ProductCount { get; set; }
    }

    public class BindLocationProductReactDto
    {
        public string? ProductIdentifier { get; set; }
        public string? ProductCode { get; set; }
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
    }

    public class LocationProductResolveDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
        public string? ProductName { get; set; }
        public string? ProductImage { get; set; }
        public string MatchedBy { get; set; } = string.Empty;
        public string MatchedValue { get; set; } = string.Empty;
    }
}
