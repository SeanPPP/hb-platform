using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface ISeasonalCardRemainingReactService
    {
        Task<ApiResponse<List<SeasonalCardCatalogDto>>> GetCatalogAsync();
        Task<ApiResponse<SeasonalCardRemainingSubmissionDto>> CreateSubmissionAsync(
            CreateSeasonalCardRemainingSubmissionDto request
        );
        Task<ApiResponse<PagedResult<SeasonalCardRemainingSubmissionDto>>> GetSubmissionsAsync(
            SeasonalCardRemainingSubmissionQueryDto query
        );
        Task<ApiResponse<SeasonalCardRemainingSubmissionDto>> GetSubmissionByGuidAsync(
            string submissionGuid
        );
    }
}
