using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces;

/// <summary>
/// 国内套装模板的保存、查询和停用服务。
/// </summary>
public interface IDomesticSetTemplateService
{
    Task<ApiResponse<List<DomesticSetTemplateListItemDto>>> GetTemplatesAsync(
        string supplierCode,
        bool includeInactive = false
    );

    Task<ApiResponse<DomesticSetTemplateDetailDto>> GetTemplateAsync(
        string templateId,
        string supplierCode
    );

    Task<ApiResponse<DomesticSetTemplateDetailDto>> CreateAsync(SaveDomesticSetTemplateRequest request);

    Task<ApiResponse<DomesticSetTemplateDetailDto>> UpdateAsync(
        string templateId,
        string supplierCode,
        SaveDomesticSetTemplateRequest request
    );

    Task<ApiResponse<object>> DeactivateAsync(string templateId, string supplierCode);
}
