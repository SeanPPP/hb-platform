using BlazorApp.Api.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace BlazorApp.Api.Services.Attendance
{
    public class AustralianPublicHolidayProvider : IAustralianPublicHolidayProvider
    {
        private const string FairWorkUrlFormat =
            "https://www.fairwork.gov.au/employment-conditions/public-holidays/{0}-public-holidays";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<AustralianPublicHolidayProvider> _logger;

        public AustralianPublicHolidayProvider(
            IHttpClientFactory httpClientFactory,
            IMemoryCache cache,
            ILogger<AustralianPublicHolidayProvider> logger
        )
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _logger = logger;
        }

        public async Task<IReadOnlyList<PublicHolidaySourceItem>> GetHolidaysAsync(
            string jurisdiction,
            DateTime fromDate,
            DateTime toDate,
            CancellationToken cancellationToken = default
        )
        {
            jurisdiction = PublicHolidaySyncHelper.NormalizeJurisdiction(jurisdiction) ?? string.Empty;
            if (jurisdiction is not ("NSW" or "QLD"))
            {
                return Array.Empty<PublicHolidaySourceItem>();
            }

            var years = Enumerable.Range(fromDate.Year, toDate.Year - fromDate.Year + 1);
            var result = new List<PublicHolidaySourceItem>();
            foreach (var year in years)
            {
                result.AddRange(await GetYearHolidaysAsync(jurisdiction, year, cancellationToken));
            }

            var window = new PublicHolidaySyncWindow(fromDate.Date, toDate.Date);
            return PublicHolidaySyncHelper.FilterStatewideHolidays(result)
                .Where(item => PublicHolidaySyncHelper.IsInWindow(item.HolidayDate, window))
                .OrderBy(item => item.HolidayDate)
                .ThenBy(item => item.HolidayName)
                .ToList();
        }

        private Task<List<PublicHolidaySourceItem>> GetYearHolidaysAsync(
            string jurisdiction,
            int year,
            CancellationToken cancellationToken
        )
        {
            return _cache.GetOrCreateAsync(
                $"public-holidays:{jurisdiction}:{year}",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
                    var fetched = await TryFetchFairWorkHolidaysAsync(jurisdiction, year, cancellationToken);
                    return fetched.Count > 0 ? fetched : CalculateStatewideHolidays(jurisdiction, year).ToList();
                }
            )!;
        }

        private async Task<List<PublicHolidaySourceItem>> TryFetchFairWorkHolidaysAsync(
            string jurisdiction,
            int year,
            CancellationToken cancellationToken
        )
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                var html = await client.GetStringAsync(
                    string.Format(FairWorkUrlFormat, year),
                    cancellationToken
                );
                return PublicHolidaySyncHelper.ParseFairWorkSection(html, jurisdiction, year).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "获取 {Year} {Jurisdiction} 公共假期页面失败，使用本地规则回退",
                    year,
                    jurisdiction
                );
                return new List<PublicHolidaySourceItem>();
            }
        }

        private static IEnumerable<PublicHolidaySourceItem> CalculateStatewideHolidays(
            string jurisdiction,
            int year
        )
        {
            foreach (var holiday in CommonAustralianHolidays(jurisdiction, year))
            {
                yield return holiday;
            }

            if (jurisdiction == "NSW")
            {
                yield return new PublicHolidaySourceItem(jurisdiction, NthWeekday(year, 6, DayOfWeek.Monday, 2), "King's Birthday");
                yield return new PublicHolidaySourceItem(jurisdiction, NthWeekday(year, 10, DayOfWeek.Monday, 1), "Labour Day");
            }
            else if (jurisdiction == "QLD")
            {
                yield return new PublicHolidaySourceItem(jurisdiction, NthWeekday(year, 5, DayOfWeek.Monday, 1), "Labour Day");
                yield return new PublicHolidaySourceItem(jurisdiction, NthWeekday(year, 10, DayOfWeek.Monday, 1), "King's Birthday");
            }
        }

        private static IEnumerable<PublicHolidaySourceItem> CommonAustralianHolidays(
            string jurisdiction,
            int year
        )
        {
            var newYearsDay = new DateTime(year, 1, 1);
            yield return new PublicHolidaySourceItem(jurisdiction, newYearsDay, "New Year's Day");
            if (newYearsDay.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                yield return new PublicHolidaySourceItem(jurisdiction, NextMonday(newYearsDay), "Additional public holiday for New Year's Day");
            }

            var australiaDay = new DateTime(year, 1, 26);
            yield return new PublicHolidaySourceItem(
                jurisdiction,
                australiaDay.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                    ? NextMonday(australiaDay)
                    : australiaDay,
                "Australia Day"
            );

            var easter = EasterSunday(year);
            yield return new PublicHolidaySourceItem(jurisdiction, easter.AddDays(-2), "Good Friday");
            yield return new PublicHolidaySourceItem(jurisdiction, easter.AddDays(-1), jurisdiction == "QLD" ? "The day after Good Friday" : "Easter Saturday");
            yield return new PublicHolidaySourceItem(jurisdiction, easter, "Easter Sunday");
            yield return new PublicHolidaySourceItem(jurisdiction, easter.AddDays(1), "Easter Monday");

            var anzacDay = new DateTime(year, 4, 25);
            yield return new PublicHolidaySourceItem(jurisdiction, anzacDay, "Anzac Day");
            if (jurisdiction == "NSW" && anzacDay.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                yield return new PublicHolidaySourceItem(jurisdiction, NextMonday(anzacDay), "Additional public holiday for Anzac Day");
            }
            else if (jurisdiction == "QLD" && anzacDay.DayOfWeek == DayOfWeek.Sunday)
            {
                yield return new PublicHolidaySourceItem(jurisdiction, anzacDay.AddDays(1), "Additional public holiday for Anzac Day");
            }

            AddChristmasBoxingDay(jurisdiction, year, out var holidays);
            foreach (var holiday in holidays)
            {
                yield return holiday;
            }
        }

        private static void AddChristmasBoxingDay(
            string jurisdiction,
            int year,
            out List<PublicHolidaySourceItem> holidays
        )
        {
            holidays = new List<PublicHolidaySourceItem>();
            var christmas = new DateTime(year, 12, 25);
            var boxing = new DateTime(year, 12, 26);
            holidays.Add(new PublicHolidaySourceItem(jurisdiction, christmas, "Christmas Day"));
            holidays.Add(new PublicHolidaySourceItem(jurisdiction, boxing, "Boxing Day"));

            var next = new DateTime(year, 12, 27);
            foreach (var fixedDate in new[] { christmas, boxing })
            {
                if (fixedDate.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                {
                    continue;
                }

                while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    next = next.AddDays(1);
                }

                holidays.Add(new PublicHolidaySourceItem(
                    jurisdiction,
                    next,
                    fixedDate == christmas
                        ? "Additional public holiday for Christmas Day"
                        : "Additional public holiday for Boxing Day"
                ));
                next = next.AddDays(1);
            }
        }

        private static DateTime NextMonday(DateTime date)
        {
            while (date.DayOfWeek != DayOfWeek.Monday)
            {
                date = date.AddDays(1);
            }

            return date;
        }

        private static DateTime NthWeekday(int year, int month, DayOfWeek dayOfWeek, int occurrence)
        {
            var date = new DateTime(year, month, 1);
            while (date.DayOfWeek != dayOfWeek)
            {
                date = date.AddDays(1);
            }

            return date.AddDays((occurrence - 1) * 7);
        }

        private static DateTime EasterSunday(int year)
        {
            var a = year % 19;
            var b = year / 100;
            var c = year % 100;
            var d = b / 4;
            var e = b % 4;
            var f = (b + 8) / 25;
            var g = (b - f + 1) / 3;
            var h = (19 * a + b - d - g + 15) % 30;
            var i = c / 4;
            var k = c % 4;
            var l = (32 + 2 * e + 2 * i - h - k) % 7;
            var m = (a + 11 * h + 22 * l) / 451;
            var month = (h + l - 7 * m + 114) / 31;
            var day = ((h + l - 7 * m + 114) % 31) + 1;
            return new DateTime(year, month, day);
        }
    }
}
