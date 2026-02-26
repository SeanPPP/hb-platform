using System.ComponentModel.DataAnnotations;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
  [SugarTable("HolidayProduct")]
  public class HolidayProduct : BaseEntity
  {
    [SugarColumn(IsPrimaryKey = true, IsNullable = false, Length = 50)]
    public string GUID { get; set; } = Guid.NewGuid().ToString();

    [SugarColumn(IsNullable = true, Length = 50)]
    public string? Sequence { get; set; }
       [SugarColumn(IsNullable = true)]
    public int? row { get; set; }

    [SugarColumn(IsNullable = false, Length = 50)]
    public string ProductCode { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 50)]
    public string ItemNumber { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 50)]
    public string SupplierCode { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true, Length = 500)]
    public string? ProductImage { get; set; }

    [SugarColumn(IsNullable = false)]
    public int HolidayType { get; set; } = 0;

    [SugarColumn(IsNullable = false)]
    public int Year { get; set; } = DateTime.Now.Year;

    [SugarColumn(IsNullable = false)]
    public DateTime ImportDate { get; set; } = DateTime.Now;

    [Navigate(NavigateType.OneToOne, nameof(ProductCode), nameof(Models.Product.ProductCode))]
    public Models.Product? Product { get; set; }
  }
}
