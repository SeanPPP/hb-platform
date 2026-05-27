using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/seasonal-card-remaining")]
    [Authorize]
    public class SeasonalCardRemainingController : ControllerBase
    {
        private readonly ISeasonalCardRemainingReactService _service;

        public SeasonalCardRemainingController(ISeasonalCardRemainingReactService service)
        {
            _service = service;
        }

        [HttpGet("catalog")]
        [Authorize(Policy = Permissions.SeasonalCards.Remaining.SubmitManagedStore)]
        public async Task<IActionResult> GetCatalog() => Ok(await _service.GetCatalogAsync());

        [HttpPost("submissions")]
        [Authorize(Policy = Permissions.SeasonalCards.Remaining.SubmitManagedStore)]
        public async Task<IActionResult> CreateSubmission(
            [FromBody] CreateSeasonalCardRemainingSubmissionDto request
        ) => Ok(await _service.CreateSubmissionAsync(request));

        [HttpGet("submissions")]
        [Authorize(Policy = Permissions.SeasonalCards.Remaining.ViewManagedStore)]
        public async Task<IActionResult> GetSubmissions(
            [FromQuery] SeasonalCardRemainingSubmissionQueryDto query
        ) => Ok(await _service.GetSubmissionsAsync(query));

        [HttpGet("submissions/{submissionGuid}")]
        [Authorize(Policy = Permissions.SeasonalCards.Remaining.ViewManagedStore)]
        public async Task<IActionResult> GetSubmission(string submissionGuid) =>
            Ok(await _service.GetSubmissionByGuidAsync(submissionGuid));
    }
}
