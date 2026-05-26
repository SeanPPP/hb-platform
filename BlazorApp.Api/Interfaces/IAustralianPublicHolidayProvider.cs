using BlazorApp.Api.Services.Attendance;

namespace BlazorApp.Api.Interfaces
{
    public interface IAustralianPublicHolidayProvider
    {
        Task<IReadOnlyList<PublicHolidaySourceItem>> GetHolidaysAsync(
            string jurisdiction,
            DateTime fromDate,
            DateTime toDate,
            CancellationToken cancellationToken = default
        );
    }
}
