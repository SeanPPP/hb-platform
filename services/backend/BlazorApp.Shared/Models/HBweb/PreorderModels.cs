using SqlSugar;

namespace BlazorApp.Shared.Models;

public static class PreorderActivationStatuses
{
    public const string Scheduled = "Scheduled";
    public const string Active = "Active";
    public const string Closed = "Closed";
    public const string Cancelled = "Cancelled";
}

public static class PreorderWarehouseOrderStatuses
{
    public const string Draft = "Draft";
    public const string ReturnedForRevision = "ReturnedForRevision";
    public const string Submitted = "Submitted";
    public const string NoDemand = "NoDemand";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}

[SugarTable("PreorderTemplate")]
public class PreorderTemplate : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, Length = 50)]
    public string TemplateGuid { get; set; } = Guid.NewGuid().ToString("N");

    [SugarColumn(Length = 150)]
    public string Name { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;
    public int Revision { get; set; } = 1;

    [SugarColumn(Length = 1000, IsNullable = true)]
    public string? Notes { get; set; }
}

[SugarTable("PreorderTemplateItem")]
public class PreorderTemplateItem : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, Length = 50)]
    public string TemplateItemGuid { get; set; } = Guid.NewGuid().ToString("N");

    [SugarColumn(Length = 50)]
    public string TemplateGuid { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string ProductCode { get; set; } = string.Empty;

    public int MinimumOrderQuantity { get; set; }
    public int SortOrder { get; set; }
}

[SugarTable("PreorderTemplateStore")]
public class PreorderTemplateStore : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, Length = 50)]
    public string TemplateStoreGuid { get; set; } = Guid.NewGuid().ToString("N");

    [SugarColumn(Length = 50)]
    public string TemplateGuid { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string StoreGuid { get; set; } = string.Empty;
}

[SugarTable("PreorderActivation")]
public class PreorderActivation : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, Length = 50)]
    public string ActivationGuid { get; set; } = Guid.NewGuid().ToString("N");

    [SugarColumn(Length = 50)]
    public string TemplateGuid { get; set; } = string.Empty;

    [SugarColumn(Length = 150)]
    public string TemplateNameSnapshot { get; set; } = string.Empty;

    public int PeriodNumber { get; set; }

    [SugarColumn(Length = 80)]
    public string ActivationCode { get; set; } = string.Empty;

    public int SourceTemplateRevision { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }

    [SugarColumn(ColumnDataType = "date", IsNullable = true)]
    public DateTime? EstimatedArrivalDate { get; set; }

    [SugarColumn(Length = 30)]
    public string Status { get; set; } = PreorderActivationStatuses.Scheduled;

    [SugarColumn(IsNullable = true)]
    public DateTime? ClosedAtUtc { get; set; }
}

[SugarTable("PreorderActivationItem")]
public class PreorderActivationItem : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, Length = 50)]
    public string ActivationItemGuid { get; set; } = Guid.NewGuid().ToString("N");

    [SugarColumn(Length = 50)]
    public string ActivationGuid { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string ProductCode { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string ItemNumber { get; set; } = string.Empty;

    [SugarColumn(Length = 200)]
    public string ProductName { get; set; } = string.Empty;

    [SugarColumn(Length = 500, IsNullable = true)]
    public string? ProductImage { get; set; }

    [SugarColumn(DecimalDigits = 4)]
    public decimal ImportPrice { get; set; }

    [SugarColumn(DecimalDigits = 4)]
    public decimal RetailPrice { get; set; }

    public int MinimumOrderQuantity { get; set; }
    public int SortOrder { get; set; }
}

[SugarTable("PreorderActivationStore")]
public class PreorderActivationStore : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, Length = 50)]
    public string ActivationStoreGuid { get; set; } = Guid.NewGuid().ToString("N");

    [SugarColumn(Length = 50)]
    public string ActivationGuid { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string StoreGuid { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string StoreCode { get; set; } = string.Empty;

    [SugarColumn(Length = 100)]
    public string StoreName { get; set; } = string.Empty;
}

[SugarTable("PreorderWarehouseOrder")]
public class PreorderWarehouseOrder : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, Length = 50)]
    public string OrderGuid { get; set; } = Guid.NewGuid().ToString("N");

    [SugarColumn(Length = 50)]
    public string ActivationGuid { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string StoreGuid { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string StoreCode { get; set; } = string.Empty;

    [SugarColumn(Length = 100)]
    public string StoreName { get; set; } = string.Empty;

    [SugarColumn(Length = 80)]
    public string OrderNo { get; set; } = string.Empty;

    [SugarColumn(Length = 30)]
    public string Status { get; set; } = PreorderWarehouseOrderStatuses.Draft;

    public int DraftRevision { get; set; } = 1;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? SubmittedByUserGuid { get; set; }

    [SugarColumn(Length = 150, IsNullable = true)]
    public string? SubmittedByName { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? SubmittedAtUtc { get; set; }

    [SugarColumn(Length = 1000, IsNullable = true)]
    public string? WarehouseNotes { get; set; }
}

[SugarTable("PreorderWarehouseOrderItem")]
public class PreorderWarehouseOrderItem : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, Length = 50)]
    public string OrderItemGuid { get; set; } = Guid.NewGuid().ToString("N");

    [SugarColumn(Length = 50)]
    public string OrderGuid { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string ActivationItemGuid { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string ProductCode { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string ItemNumber { get; set; } = string.Empty;

    [SugarColumn(Length = 200)]
    public string ProductName { get; set; } = string.Empty;

    [SugarColumn(Length = 500, IsNullable = true)]
    public string? ProductImage { get; set; }

    public int PackCount { get; set; }
    public int MinimumOrderQuantity { get; set; }
    public int OrderedQuantity { get; set; }

    [SugarColumn(DecimalDigits = 4)]
    public decimal ImportPrice { get; set; }

    [SugarColumn(DecimalDigits = 4)]
    public decimal RetailPrice { get; set; }

    [SugarColumn(DecimalDigits = 4)]
    public decimal ImportAmount { get; set; }

    [SugarColumn(DecimalDigits = 4)]
    public decimal RetailAmount { get; set; }
}
