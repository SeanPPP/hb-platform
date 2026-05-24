using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public sealed class AttendanceReactServiceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _sqliteConnection;
        private readonly SqlSugarClient _db;

        public AttendanceReactServiceTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
            _sqliteConnection = new SqliteConnection($"Data Source={_dbPath}");
            _sqliteConnection.Open();

            _db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = _sqliteConnection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            });

            _db.CodeFirst.InitTables(
                typeof(User),
                typeof(Store),
                typeof(UserStore),
                typeof(AttendanceSchedule),
                typeof(AttendanceAvailability),
                typeof(AttendancePunch),
                typeof(AttendanceApproval),
                typeof(AttendanceStoreHoliday),
                typeof(AttendanceLeaveRequest),
                typeof(AttendanceSettings)
            );
        }

        [Fact]
        public async Task GetSchedulesAsync_WhenStoreManagerRequestsUnmanagedStore_ReturnsForbidden()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.GetSchedulesAsync(new AttendanceScheduleQueryDto
            {
                StoreCode = "OTHER",
                WeekStartDate = new DateTime(2026, 5, 18),
            });

            Assert.False(result.Success);
            Assert.Equal("FORBIDDEN_STORE", result.ErrorCode);
        }

        [Fact]
        public async Task CreateMyAvailabilityAsync_AllowsMultipleSegmentsWithinWeek()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("staff-user", "staff", "StoreStaff");

            var result = await service.CreateMyAvailabilityAsync(
                new CreateAttendanceAvailabilityDto
                {
                    StoreCode = "BRI",
                    WeekStartDate = new DateTime(2026, 5, 18),
                    Segments = new List<AttendanceAvailabilitySegmentDto>
                    {
                        new()
                        {
                            AvailableDate = new DateTime(2026, 5, 18),
                            StartTime = new TimeSpan(9, 0, 0),
                            EndTime = new TimeSpan(12, 0, 0),
                        },
                        new()
                        {
                            AvailableDate = new DateTime(2026, 5, 20),
                            StartTime = new TimeSpan(14, 0, 0),
                            EndTime = new TimeSpan(18, 0, 0),
                        },
                    },
                }
            );

            Assert.True(result.Success);
            Assert.Equal(2, result.Data!.Count);
        }

        [Fact]
        public async Task CreateScheduleAsync_WhenSameStoreUserDateOverlaps_ReturnsConflict()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("manager-user", "manager", "StoreManager");

            var first = await service.CreateScheduleAsync(new CreateAttendanceScheduleDto
            {
                StoreCode = "BRI",
                UserGuid = "staff-user",
                WorkDate = new DateTime(2026, 5, 18),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(13, 0, 0),
            });
            var second = await service.CreateScheduleAsync(new CreateAttendanceScheduleDto
            {
                StoreCode = "BRI",
                UserGuid = "staff-user",
                WorkDate = new DateTime(2026, 5, 18),
                StartTime = new TimeSpan(12, 0, 0),
                EndTime = new TimeSpan(16, 0, 0),
            });

            Assert.True(first.Success);
            Assert.False(second.Success);
            Assert.Equal("SCHEDULE_OVERLAP", second.ErrorCode);
        }

        [Fact]
        public async Task CreateScheduleAsync_WhenStatusIsMissing_SavesDraft()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.CreateScheduleAsync(new CreateAttendanceScheduleDto
            {
                StoreCode = "BRI",
                UserGuid = "staff-user",
                WorkDate = new DateTime(2026, 5, 18),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
            });

            Assert.True(result.Success);
            Assert.Equal("Draft", result.Data!.Status);
            Assert.Equal("Draft", await _db.Queryable<AttendanceSchedule>()
                .Where(item => item.ScheduleGuid == result.Data.ScheduleGuid)
                .Select(item => item.Status)
                .FirstAsync());
        }

        [Fact]
        public async Task CreateScheduleAsync_WhenStatusIsActive_SavesDraft()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.CreateScheduleAsync(new CreateAttendanceScheduleDto
            {
                StoreCode = "BRI",
                UserGuid = "staff-user",
                WorkDate = new DateTime(2026, 5, 18),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
                Status = "Active",
            });

            Assert.True(result.Success);
            Assert.Equal("Draft", result.Data!.Status);
            Assert.Equal("Draft", await _db.Queryable<AttendanceSchedule>()
                .Where(item => item.ScheduleGuid == result.Data.ScheduleGuid)
                .Select(item => item.Status)
                .FirstAsync());
        }

        [Fact]
        public async Task PublishWeekAsync_WhenStoreManagerPublishesManagedStore_ActivatesOnlyDraftSchedulesInWeek()
        {
            await SeedStoreScopeAsync();
            await SeedScheduleAsync("draft-in-week", "BRI", "staff-user", new DateTime(2026, 5, 18), "Draft");
            await SeedScheduleAsync("active-in-week", "BRI", "staff-user", new DateTime(2026, 5, 19), "Active");
            await SeedScheduleAsync("cancelled-in-week", "BRI", "staff-user", new DateTime(2026, 5, 20), "Cancelled");
            await SeedScheduleAsync("draft-outside-week", "BRI", "staff-user", new DateTime(2026, 5, 25), "Draft");
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.PublishWeekAsync(new PublishAttendanceWeekDto
            {
                StoreCode = "BRI",
                WeekStartDate = new DateTime(2026, 5, 18),
            });

            Assert.True(result.Success);
            Assert.Equal(1, result.Data);
            Assert.Equal("Active", await GetScheduleStatusAsync("draft-in-week"));
            Assert.Equal("Active", await GetScheduleStatusAsync("active-in-week"));
            Assert.Equal("Cancelled", await GetScheduleStatusAsync("cancelled-in-week"));
            Assert.Equal("Draft", await GetScheduleStatusAsync("draft-outside-week"));
        }

        [Fact]
        public async Task PublishWeekAsync_WhenStoreManagerRequestsUnmanagedStore_ReturnsForbidden()
        {
            await SeedStoreScopeAsync();
            await SeedScheduleAsync("other-draft", "OTHER", "staff-user", new DateTime(2026, 5, 18), "Draft");
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.PublishWeekAsync(new PublishAttendanceWeekDto
            {
                StoreCode = "OTHER",
                WeekStartDate = new DateTime(2026, 5, 18),
            });

            Assert.False(result.Success);
            Assert.Equal("FORBIDDEN_STORE", result.ErrorCode);
            Assert.Equal("Draft", await GetScheduleStatusAsync("other-draft"));
        }

        [Fact]
        public async Task GetMyWeekAsync_ReturnsRelatedStoreActiveSchedules()
        {
            await SeedStoreScopeAsync();
            await SeedScheduleAsync("my-active", "BRI", "staff-user", new DateTime(2026, 5, 18), "Active");
            await SeedScheduleAsync("my-draft", "BRI", "staff-user", new DateTime(2026, 5, 19), "Draft");
            await SeedScheduleAsync("manager-active", "BRI", "manager-user", new DateTime(2026, 5, 20), "Active");
            var service = CreateService("staff-user", "staff", "StoreStaff");

            var result = await service.GetMyWeekAsync(new DateTime(2026, 5, 18), "BRI");

            Assert.True(result.Success);
            Assert.Collection(
                result.Data!,
                schedule =>
                {
                    Assert.Equal("my-active", schedule.ScheduleGuid);
                    Assert.True(schedule.IsMine);
                    Assert.Equal("Active", schedule.Status);
                },
                schedule =>
                {
                    Assert.Equal("manager-active", schedule.ScheduleGuid);
                    Assert.False(schedule.IsMine);
                    Assert.Equal("manager", schedule.EmployeeName);
                }
            );
        }

        [Fact]
        public async Task GetMyTodayAsync_WhenWorkDateProvided_ReturnsSelectedDateData()
        {
            await SeedStoreScopeAsync();
            await SeedScheduleAsync("selected-day", "BRI", "staff-user", new DateTime(2026, 5, 19), "Active");
            await SeedScheduleAsync("other-day", "BRI", "staff-user", new DateTime(2026, 5, 20), "Active");
            await _db.Insertable(new AttendancePunch
            {
                PunchGuid = "selected-punch",
                StoreCode = "BRI",
                UserGuid = "staff-user",
                WorkDate = new DateTime(2026, 5, 19),
                PunchType = "ClockIn",
                PunchTimeUtc = DateTime.Parse("2026-05-18T23:00:00Z").ToUniversalTime(),
                PunchTimeLocal = new DateTime(2026, 5, 19, 9, 0, 0),
                Status = "Normal",
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
            await _db.Insertable(new AttendanceStoreHoliday
            {
                HolidayGuid = "selected-holiday",
                StoreCode = "BRI",
                HolidayDate = new DateTime(2026, 5, 19),
                HolidayName = "Selected holiday",
                BusinessStatus = "Open",
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
            var service = CreateService("staff-user", "staff", "StoreStaff");

            var result = await service.GetMyTodayAsync(new DateTime(2026, 5, 19), "BRI");

            Assert.True(result.Success);
            Assert.Equal("2026-05-19", result.Data!.WorkDate.ToString("yyyy-MM-dd"));
            Assert.Equal("selected-day", Assert.Single(result.Data.Schedules).ScheduleGuid);
            Assert.Equal("selected-punch", Assert.Single(result.Data.Punches).PunchGuid);
            Assert.Equal("selected-holiday", Assert.Single(result.Data.Holidays).HolidayGuid);
        }

        [Fact]
        public async Task PunchAsync_WhenOnlyDraftScheduleExists_TreatsPunchAsNoSchedule()
        {
            await SeedStoreScopeAsync();
            await SeedScheduleAsync("draft-schedule", "BRI", "staff-user", new DateTime(2026, 5, 18), "Draft");
            var service = CreateService("staff-user", "staff", "StoreStaff");

            var result = await service.PunchAsync(new AttendancePunchRequestDto
            {
                StoreCode = "BRI",
                StoreTimeZone = "Australia/Brisbane",
                PunchType = "ClockIn",
                PunchTimeUtc = DateTime.Parse("2026-05-17T23:00:00Z").ToUniversalTime(),
            });

            Assert.True(result.Success);
            Assert.Equal("NoSchedule", result.Data!.Status);
            Assert.Null(result.Data.ScheduleGuid);
        }

        [Theory]
        [InlineData("Australia/Brisbane", "2026-05-20T14:30:00Z", "2026-05-21")]
        [InlineData("Australia/Melbourne", "2026-05-20T14:30:00Z", "2026-05-21")]
        [InlineData("Unexpected/Zone", "2026-05-20T14:30:00Z", "2026-05-21")]
        public async Task PunchAsync_StoresLocalTimeAndWorkDateBySupportedStoreTimeZone(
            string storeTimeZone,
            string punchTimeUtc,
            string expectedWorkDate
        )
        {
            await SeedStoreScopeAsync();
            var service = CreateService("staff-user", "staff", "StoreStaff");

            var result = await service.PunchAsync(new AttendancePunchRequestDto
            {
                StoreCode = "BRI",
                StoreTimeZone = storeTimeZone,
                PunchType = "ClockIn",
                PunchTimeUtc = DateTime.Parse(punchTimeUtc).ToUniversalTime(),
            });

            Assert.True(result.Success);
            Assert.Equal(expectedWorkDate, result.Data!.WorkDate.ToString("yyyy-MM-dd"));
            Assert.Equal(
                storeTimeZone.StartsWith("Australia/") ? storeTimeZone : "Australia/Sydney",
                result.Data.StoreTimeZone
            );
        }

        [Theory]
        [InlineData("ClockIn", "2026-05-18T00:20:00Z", "Late")]
        [InlineData("ClockOut", "2026-05-18T06:40:00Z", "EarlyLeave")]
        [InlineData("ClockIn", "2026-05-19T00:00:00Z", "NoSchedule")]
        public async Task PunchAsync_WhenExceptionalStatus_CreatesPendingApproval(
            string punchType,
            string punchTimeUtc,
            string expectedStatus
        )
        {
            await SeedStoreScopeAsync();
            var service = CreateService("staff-user", "staff", "StoreStaff");
            await SeedScheduleAsync();

            var result = await service.PunchAsync(new AttendancePunchRequestDto
            {
                StoreCode = "BRI",
                StoreTimeZone = "Australia/Brisbane",
                PunchType = punchType,
                PunchTimeUtc = DateTime.Parse(punchTimeUtc).ToUniversalTime(),
            });

            Assert.True(result.Success);
            Assert.Equal(expectedStatus, result.Data!.Status);
            Assert.Equal(1, await _db.Queryable<AttendanceApproval>().CountAsync());
        }

        [Fact]
        public async Task CreateScheduleAsync_WhenPartialHolidayExists_DoesNotBlockSchedule()
        {
            await SeedStoreScopeAsync();
            await _db.Insertable(new AttendanceStoreHoliday
            {
                HolidayGuid = "holiday-partial",
                StoreCode = "BRI",
                HolidayDate = new DateTime(2026, 5, 18),
                HolidayName = "Partial trading day",
                BusinessStatus = "Partial",
                OpenTime = new TimeSpan(10, 0, 0),
                CloseTime = new TimeSpan(16, 0, 0),
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.CreateScheduleAsync(new CreateAttendanceScheduleDto
            {
                StoreCode = "BRI",
                UserGuid = "staff-user",
                WorkDate = new DateTime(2026, 5, 18),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
            });

            Assert.True(result.Success);
        }

        [Fact]
        public async Task BatchUpsertHolidaysAsync_WhenStoresAreManaged_CreatesHolidayForEachStore()
        {
            await SeedStoreScopeAsync();
            await SeedManagerStoreAccessAsync("manager-user", "store-other");
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.BatchUpsertHolidaysAsync(new BatchUpsertAttendanceStoreHolidayDto
            {
                StoreCodes = new List<string> { "BRI", "OTHER" },
                HolidayDate = new DateTime(2026, 12, 25),
                HolidayName = "Christmas Day",
                BusinessStatus = "Closed",
                IsPaidHoliday = true,
                Remark = "Batch created",
            });

            Assert.True(result.Success);
            Assert.Equal(2, result.Data!.CreatedCount);
            Assert.Equal(0, result.Data.UpdatedCount);
            Assert.Equal(2, result.Data.Items.Count);
            Assert.Equal(2, await CountActiveHolidaysAsync(new DateTime(2026, 12, 25)));
        }

        [Fact]
        public async Task BatchUpsertHolidaysAsync_WhenHolidayAlreadyExists_UpdatesExistingRow()
        {
            await SeedStoreScopeAsync();
            await _db.Insertable(new AttendanceStoreHoliday
            {
                HolidayGuid = "existing-holiday",
                StoreCode = "BRI",
                HolidayDate = new DateTime(2026, 12, 25),
                HolidayName = "Old Christmas",
                BusinessStatus = "Open",
                IsPaidHoliday = false,
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.BatchUpsertHolidaysAsync(new BatchUpsertAttendanceStoreHolidayDto
            {
                StoreCodes = new List<string> { "BRI" },
                HolidayDate = new DateTime(2026, 12, 25),
                HolidayName = "Christmas Day",
                BusinessStatus = "Partial",
                OpenTime = new TimeSpan(10, 0, 0),
                CloseTime = new TimeSpan(16, 0, 0),
                IsPaidHoliday = true,
                Remark = "Updated",
            });

            Assert.True(result.Success);
            Assert.Equal(0, result.Data!.CreatedCount);
            Assert.Equal(1, result.Data.UpdatedCount);
            var row = await _db.Queryable<AttendanceStoreHoliday>()
                .FirstAsync(item => item.HolidayGuid == "existing-holiday");
            Assert.Equal("Christmas Day", row.HolidayName);
            Assert.Equal("Partial", row.BusinessStatus);
            Assert.True(row.IsPaidHoliday);
            Assert.Equal(1, await CountActiveHolidaysAsync(new DateTime(2026, 12, 25)));
        }

        [Fact]
        public async Task BatchUpsertHolidaysAsync_WhenAnyStoreIsForbidden_RollsBackEntireBatch()
        {
            await SeedStoreScopeAsync();
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.BatchUpsertHolidaysAsync(new BatchUpsertAttendanceStoreHolidayDto
            {
                StoreCodes = new List<string> { "BRI", "OTHER" },
                HolidayDate = new DateTime(2026, 12, 25),
                HolidayName = "Christmas Day",
                BusinessStatus = "Closed",
                IsPaidHoliday = true,
            });

            Assert.False(result.Success);
            Assert.Equal("FORBIDDEN_STORE", result.ErrorCode);
            Assert.Equal(0, await CountActiveHolidaysAsync(new DateTime(2026, 12, 25)));
        }

        [Fact]
        public async Task BatchUpsertHolidaysAsync_WhenDuplicateActiveRowsExist_UpdatesOneAndSoftDeletesExtras()
        {
            await SeedStoreScopeAsync();
            await _db.Insertable(new[]
            {
                new AttendanceStoreHoliday
                {
                    HolidayGuid = "duplicate-a",
                    StoreCode = "BRI",
                    HolidayDate = new DateTime(2026, 12, 25),
                    HolidayName = "Duplicate A",
                    BusinessStatus = "Open",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-2),
                },
                new AttendanceStoreHoliday
                {
                    HolidayGuid = "duplicate-b",
                    StoreCode = "BRI",
                    HolidayDate = new DateTime(2026, 12, 25),
                    HolidayName = "Duplicate B",
                    BusinessStatus = "Open",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                },
            }).ExecuteCommandAsync();
            var service = CreateService("manager-user", "manager", "StoreManager");

            var result = await service.BatchUpsertHolidaysAsync(new BatchUpsertAttendanceStoreHolidayDto
            {
                StoreCodes = new List<string> { "BRI" },
                HolidayDate = new DateTime(2026, 12, 25),
                HolidayName = "Christmas Day",
                BusinessStatus = "Closed",
                IsPaidHoliday = true,
            });

            Assert.True(result.Success);
            Assert.Equal(0, result.Data!.CreatedCount);
            Assert.Equal(1, result.Data.UpdatedCount);
            Assert.Equal(1, await CountActiveHolidaysAsync(new DateTime(2026, 12, 25)));
            Assert.Equal(1, await _db.Queryable<AttendanceStoreHoliday>()
                .Where(item =>
                    item.HolidayDate >= new DateTime(2026, 12, 25)
                    && item.HolidayDate < new DateTime(2026, 12, 26)
                    && item.IsDeleted
                )
                .CountAsync());
        }

        public void Dispose()
        {
            _db.Dispose();
            _sqliteConnection.Dispose();

            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }

        private AttendanceReactService CreateService(string userGuid, string username, params string[] roles)
        {
            var httpContextAccessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(CreateClaims(userGuid, username, roles), "TestAuth")
                    ),
                },
            };
            var currentUserService = new CurrentUserService(httpContextAccessor);
            var context = CreateSqlSugarContext(_db);
            var scopeService = new CurrentUserManageableStoreScopeService(
                context,
                currentUserService,
                httpContextAccessor
            );

            return new AttendanceReactService(
                context,
                currentUserService,
                scopeService,
                httpContextAccessor,
                NullLogger<AttendanceReactService>.Instance
            );
        }

        private async Task SeedStoreScopeAsync()
        {
            await _db.Insertable(
                new[]
                {
                    new Store
                    {
                        StoreGUID = "store-bri",
                        StoreCode = "BRI",
                        StoreName = "Brisbane",
                        CreatedAt = DateTime.UtcNow,
                    },
                    new Store
                    {
                        StoreGUID = "store-other",
                        StoreCode = "OTHER",
                        StoreName = "Other",
                        CreatedAt = DateTime.UtcNow,
                    },
                }
            ).ExecuteCommandAsync();

            await _db.Insertable(
                new[]
                {
                    new User
                    {
                        UserGUID = "manager-user",
                        Username = "manager",
                        Email = "manager@example.com",
                        PasswordHash = "hash",
                        CreatedAt = DateTime.UtcNow,
                    },
                    new User
                    {
                        UserGUID = "staff-user",
                        Username = "staff",
                        Email = "staff@example.com",
                        PasswordHash = "hash",
                        CreatedAt = DateTime.UtcNow,
                    },
                }
            ).ExecuteCommandAsync();

            await _db.Insertable(
                new[]
                {
                    new UserStore
                    {
                        UserStoreGUID = "manager-store-bri",
                        UserGUID = "manager-user",
                        StoreGUID = "store-bri",
                        IsPrimary = true,
                        CreatedAt = DateTime.UtcNow,
                    },
                    new UserStore
                    {
                        UserStoreGUID = "staff-store-bri",
                        UserGUID = "staff-user",
                        StoreGUID = "store-bri",
                        CreatedAt = DateTime.UtcNow,
                    },
                }
            ).ExecuteCommandAsync();
        }

        private async Task SeedManagerStoreAccessAsync(string userGuid, string storeGuid)
        {
            await _db.Insertable(new UserStore
            {
                UserStoreGUID = $"{userGuid}-{storeGuid}",
                UserGUID = userGuid,
                StoreGUID = storeGuid,
                IsPrimary = true,
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
        }

        private async Task<int> CountActiveHolidaysAsync(DateTime holidayDate)
        {
            return await _db.Queryable<AttendanceStoreHoliday>()
                .Where(item =>
                    item.HolidayDate >= holidayDate.Date
                    && item.HolidayDate < holidayDate.Date.AddDays(1)
                    && !item.IsDeleted
                )
                .CountAsync();
        }

        private async Task SeedScheduleAsync()
        {
            await SeedScheduleAsync(
                "schedule-1",
                "BRI",
                "staff-user",
                new DateTime(2026, 5, 18),
                "Active"
            );
        }

        private async Task SeedScheduleAsync(
            string scheduleGuid,
            string storeCode,
            string userGuid,
            DateTime workDate,
            string status
        )
        {
            await _db.Insertable(new AttendanceSchedule
            {
                ScheduleGuid = scheduleGuid,
                StoreCode = storeCode,
                UserGuid = userGuid,
                WorkDate = workDate,
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
                Status = status,
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
        }

        private async Task<string> GetScheduleStatusAsync(string scheduleGuid)
        {
            return await _db.Queryable<AttendanceSchedule>()
                .Where(item => item.ScheduleGuid == scheduleGuid)
                .Select(item => item.Status)
                .FirstAsync();
        }

        private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
        {
            var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
                typeof(SqlSugarContext)
            );

            var dbField = typeof(SqlSugarContext).GetField(
                "_db",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            dbField!.SetValue(context, db);

            return context;
        }

        private static IEnumerable<Claim> CreateClaims(
            string userGuid,
            string username,
            IEnumerable<string> roles
        )
        {
            yield return new Claim("userGuid", userGuid);
            yield return new Claim("userId", userGuid);
            yield return new Claim(ClaimTypes.NameIdentifier, userGuid);
            yield return new Claim(ClaimTypes.Name, username);

            foreach (var role in roles)
            {
                yield return new Claim(ClaimTypes.Role, role);
            }
        }
    }
}
