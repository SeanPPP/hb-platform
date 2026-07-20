using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React;

public interface IPreorderReactService
{
    Task<PagedResult<PreorderTemplateSummaryDto>> GetTemplatesAsync(PreorderTemplateQueryDto query);
    Task<PreorderTemplateDetailDto> GetTemplateAsync(string templateGuid);
    Task<PreorderTemplateDetailDto> CreateTemplateAsync(SavePreorderTemplateDto request);
    Task<PreorderTemplateDetailDto> UpdateTemplateAsync(string templateGuid, SavePreorderTemplateDto request);
    Task<ResolvePreorderItemsResultDto> ResolveItemsAsync(ResolvePreorderItemsRequestDto request);
    Task<IReadOnlyList<PreorderActivationSummaryDto>> GetTemplateActivationsAsync(string templateGuid);
    Task<PreorderActivationSummaryDto> ActivateAsync(string templateGuid, ActivatePreorderTemplateDto request);
    Task<PreorderActivationDetailDto> GetActivationAsync(string activationGuid, string? storeCode = null);
    Task<PreorderActivationDetailDto> UpdateActivationStoresAsync(
        string activationGuid,
        UpdatePreorderActivationStoresDto request
    );
    Task<PreorderActivationSummaryDto> CloseActivationAsync(string activationGuid, ClosePreorderActivationDto request);
    Task<PreorderActivationSummaryDto> CancelActivationAsync(string activationGuid);
    Task<IReadOnlyList<PreorderOrderSummaryDto>> GetActivationOrdersAsync(string activationGuid);
    Task<PreorderActivationStatisticsDto> GetStatisticsAsync(string activationGuid);
    Task<PreorderExportFileDto> ExportAsync(string activationGuid);
    Task<PreorderOrderSummaryDto> UpdateOrderStatusAsync(string orderGuid, UpdatePreorderOrderStatusDto request);
    Task<PreorderActiveResultDto> GetActiveAsync(string storeCode);
    Task<PreorderActivationDetailDto> SaveDraftAsync(string activationGuid, SavePreorderDraftDto request);
    Task<PreorderOrderSummaryDto> SubmitAsync(string activationGuid, SubmitPreorderDto request);
}

public interface IPreorderGateService
{
    /// <summary>仅判断分店当前是否被 Preorder 阻塞，不在此处做管理员豁免。</summary>
    Task<PreorderGateResult> CheckAsync(string storeCode);
}

public sealed class PreorderBusinessException : Exception
{
    public PreorderBusinessException(
        string message,
        string errorCode,
        int statusCode = 400,
        object? details = null
    ) : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
        Details = details;
    }

    public string ErrorCode { get; }
    public int StatusCode { get; }
    public object? Details { get; }
}
