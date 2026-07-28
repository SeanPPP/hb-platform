using SqlSugar;

namespace BlazorApp.Shared.Models;

/// <summary>
/// 国内套装的可复用模板；只保存创建前的商品与价格快照，不保存货号、条码或父级价格。
/// </summary>
[SugarTable("DomesticSetTemplate")]
public class DomesticSetTemplate : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, Length = 50)]
    public string TemplateId { get; set; } = Guid.NewGuid().ToString("N");

    [SugarColumn(Length = 50)]
    public string SupplierCode { get; set; } = string.Empty;

    [SugarColumn(Length = 150)]
    public string TemplateName { get; set; } = string.Empty;

    [SugarColumn(Length = 200)]
    public string SetProductName { get; set; } = string.Empty;

    /// <summary>
    /// 停用仅影响模板选择器可见性，不删除模板审计记录。
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// 国内套装模板的有序子项快照。
/// </summary>
[SugarTable("DomesticSetTemplateItem")]
public class DomesticSetTemplateItem : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, Length = 50)]
    public string TemplateItemId { get; set; } = Guid.NewGuid().ToString("N");

    [SugarColumn(Length = 50)]
    public string TemplateId { get; set; } = string.Empty;

    [SugarColumn(Length = 200)]
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// 现有国内商品创建 API 中 PrivateLabelPrice 对应 OEMPrice；模板统一以该字段保存。
    /// </summary>
    [SugarColumn(DecimalDigits = 4)]
    public decimal PrivateLabelPrice { get; set; }

    public int SortOrder { get; set; }
}
