using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BlazorApp.Shared.DTOs;

/// <summary>
/// 创建或修改国内套装模板的请求。
/// </summary>
public sealed class SaveDomesticSetTemplateRequest
{
    [Required(ErrorMessage = "供应商不能为空")]
    [JsonPropertyName("supplierCode")]
    public string SupplierCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "模板名不能为空")]
    [JsonPropertyName("templateName")]
    public string TemplateName { get; set; } = string.Empty;

    [Required(ErrorMessage = "套装商品名不能为空")]
    [JsonPropertyName("setProductName")]
    public string SetProductName { get; set; } = string.Empty;

    /// <summary>
    /// 创建时缺省启用；修改时缺省保留当前状态，显式 true 才恢复已停用模板。
    /// </summary>
    [JsonPropertyName("isEnabled")]
    public bool? IsEnabled { get; set; }

    [JsonPropertyName("subItems")]
    public List<SaveDomesticSetTemplateItemRequest> SubItems { get; set; } = new();
}

/// <summary>
/// 国内套装模板子项保存请求。使用可空类型区分缺失价格与有效的零价格。
/// </summary>
public sealed class SaveDomesticSetTemplateItemRequest
{
    [Required(ErrorMessage = "子项名称不能为空")]
    [JsonPropertyName("productName")]
    public string ProductName { get; set; } = string.Empty;

    [Required(ErrorMessage = "OEM/PrivateLabel 价格不能为空")]
    [Range(0, double.MaxValue, ErrorMessage = "OEM/PrivateLabel 价格不能为负数")]
    [JsonPropertyName("privateLabelPrice")]
    public decimal? PrivateLabelPrice { get; set; }
}

/// <summary>
/// 国内套装模板列表项。
/// </summary>
public class DomesticSetTemplateListItemDto
{
    [JsonPropertyName("templateId")]
    public string TemplateId { get; set; } = string.Empty;

    [JsonPropertyName("supplierCode")]
    public string SupplierCode { get; set; } = string.Empty;

    [JsonPropertyName("templateName")]
    public string TemplateName { get; set; } = string.Empty;

    [JsonPropertyName("setProductName")]
    public string SetProductName { get; set; } = string.Empty;

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }

    /// <summary>
    /// 套装数量由有效子项数量计算，模板不单独持久化该值。
    /// </summary>
    [JsonPropertyName("setQuantity")]
    public int SetQuantity { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// 国内套装模板详情。
/// </summary>
public sealed class DomesticSetTemplateDetailDto : DomesticSetTemplateListItemDto
{
    [JsonPropertyName("subItems")]
    public List<DomesticSetTemplateItemDto> SubItems { get; set; } = new();
}

/// <summary>
/// 国内套装模板子项详情。
/// </summary>
public sealed class DomesticSetTemplateItemDto
{
    [JsonPropertyName("productName")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("privateLabelPrice")]
    public decimal PrivateLabelPrice { get; set; }

    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; set; }
}
