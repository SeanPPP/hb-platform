using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services.Attendance;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public sealed class AttendancePublicHolidaySyncServiceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _sqliteConnection;
        private readonly SqlSugarClient _db;

        public AttendancePublicHolidaySyncServiceTests()
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
            _db.CodeFirst.InitTables(typeof(Store), typeof(AttendanceStoreHoliday));
        }

        [Fact]
        public async Task SyncStoreAsync_ParsesStoreAddressAndIsIdempotent()
        {
            await _db.Insertable(new Store
            {
                StoreGUID = "store-bri",
                StoreCode = "BRI",
                StoreName = "Brisbane",
                Address = "123 Queen Street Brisbane QLD 4000",
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
            var service = CreateService(new[]
            {
                new PublicHolidaySourceItem("QLD", new DateTime(2026, 6, 8), "King's Birthday"),
            });

            var first = await service.SyncStoreAsync(new SyncAttendanceStoreHolidayDto
            {
                StoreCode = "BRI",
                FromDate = new DateTime(2026, 5, 25),
                ToDate = new DateTime(2026, 6, 24),
            });
            var second = await service.SyncStoreAsync(new SyncAttendanceStoreHolidayDto
            {
                StoreCode = "BRI",
                FromDate = new DateTime(2026, 5, 25),
                ToDate = new DateTime(2026, 6, 24),
            });

            Assert.True(first.Success);
            Assert.Equal(1, first.Data!.CreatedCount);
            Assert.Equal("Open", first.Data.Holidays[0].BusinessStatus);
            Assert.True(second.Success);
            Assert.Equal(1, second.Data!.UpdatedCount);
            Assert.Equal(1, await _db.Queryable<AttendanceStoreHoliday>().Where(item => !item.IsDeleted).CountAsync());
        }

        [Fact]
        public async Task SyncStoreAsync_DoesNotOverwriteManualHolidayCustomization()
        {
            await _db.Insertable(new Store
            {
                StoreGUID = "store-bri",
                StoreCode = "BRI",
                StoreName = "Brisbane",
                Address = "123 Queen Street Brisbane QLD 4000",
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
            await _db.Insertable(new AttendanceStoreHoliday
            {
                HolidayGuid = "manual-holiday",
                StoreCode = "BRI",
                HolidayDate = new DateTime(2026, 6, 8),
                HolidayName = "Manual trading plan",
                BusinessStatus = "Partial",
                OpenTime = new TimeSpan(10, 0, 0),
                CloseTime = new TimeSpan(14, 0, 0),
                IsPaidHoliday = false,
                Remark = "manager override",
                CreatedAt = DateTime.UtcNow,
            }).ExecuteCommandAsync();
            var service = CreateService(new[]
            {
                new PublicHolidaySourceItem("QLD", new DateTime(2026, 6, 8), "King's Birthday"),
            });

            var result = await service.SyncStoreAsync(new SyncAttendanceStoreHolidayDto
            {
                StoreCode = "BRI",
                FromDate = new DateTime(2026, 5, 25),
                ToDate = new DateTime(2026, 6, 24),
            });

            var holiday = await _db.Queryable<AttendanceStoreHoliday>()
                .FirstAsync(item => item.HolidayGuid == "manual-holiday");
            Assert.True(result.Success);
            Assert.Equal(0, result.Data!.CreatedCount);
            Assert.Equal(0, result.Data.UpdatedCount);
            Assert.Equal(1, result.Data.SkippedCount);
            Assert.Equal("Manual trading plan", holiday.HolidayName);
            Assert.Equal("Partial", holiday.BusinessStatus);
            Assert.Equal(new TimeSpan(10, 0, 0), holiday.OpenTime);
            Assert.Equal("manager override", holiday.Remark);
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

        private AttendancePublicHolidaySyncService CreateService(
            IReadOnlyList<PublicHolidaySourceItem> holidays
        ) =>
            new(
                CreateSqlSugarContext(_db),
                new FakeHolidayProvider(holidays),
                new FakeStoreScopeService(),
                NullLogger<AttendancePublicHolidaySyncService>.Instance
            );

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

        private sealed class FakeHolidayProvider : IAustralianPublicHolidayProvider
        {
            private readonly IReadOnlyList<PublicHolidaySourceItem> _holidays;

            public FakeHolidayProvider(IReadOnlyList<PublicHolidaySourceItem> holidays)
            {
                _holidays = holidays;
            }

            public Task<IReadOnlyList<PublicHolidaySourceItem>> GetHolidaysAsync(
                string jurisdiction,
                DateTime fromDate,
                DateTime toDate,
                CancellationToken cancellationToken = default
            ) => Task.FromResult(_holidays);
        }

        private sealed class FakeStoreScopeService : ICurrentUserManageableStoreScopeService
        {
            public Task<CurrentUserManageableStoreScope> GetScopeAsync() =>
                Task.FromResult(new CurrentUserManageableStoreScope
                {
                    IsAllowed = true,
                    IsAuthenticated = true,
                    IsAdmin = true,
                    ActorLabel = "test",
                });

            public Task<bool> CanManageStoreAsync(string storeGuid) => Task.FromResult(true);

            public Task<IReadOnlyList<string>> GetAccessibleStoreCodesAsync() =>
                Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

            public Task<bool> CanAccessStoreCodeAsync(string storeCode) => Task.FromResult(true);

            public Task<bool> CanAccessOrderAsync(string orderGuid) => Task.FromResult(true);

            public Task<bool> CanManageUserAsync(string userGuid) => Task.FromResult(true);
        }
    }
}
