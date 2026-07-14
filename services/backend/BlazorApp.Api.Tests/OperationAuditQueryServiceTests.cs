using System.Security.Claims;
using System.Text.Json;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services.OperationAudits;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.POSM;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class OperationAuditQueryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public OperationAuditQueryServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables(typeof(PosOperationAudit), typeof(PosOperationAuditItem));
    }

    [Fact]
    public async Task QueryAsync_DefaultsToRecentSevenDaysAndOccurredTimeDescending()
    {
        var now = DateTime.UtcNow;
        await InsertAuditAsync("recent-older", "BRI", now.AddDays(-2));
        await InsertAuditAsync("expired", "BRI", now.AddDays(-8));
        await InsertAuditAsync("recent-newer", "BRI", now.AddDays(-1));
        var service = CreateService("Admin");

        var result = await service.QueryAsync(new OperationAuditQueryDto(), now);

        Assert.Equal(2, result.Total);
        Assert.Equal(20, result.PageSize);
        Assert.Collection(
            result.Items,
            item => Assert.Equal("recent-newer", item.ReceiptNumber),
            item => Assert.Equal("recent-older", item.ReceiptNumber)
        );
        Assert.All(result.Items, item =>
        {
            Assert.Equal(DateTimeKind.Utc, item.OccurredAtUtc.Kind);
            Assert.Equal(DateTimeKind.Utc, item.ReceivedAtUtc.Kind);
        });
        var json = JsonSerializer.Serialize(result.Items[0], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("Z\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryAsync_ClampsPageSizeToTwoHundred()
    {
        var now = DateTime.UtcNow;
        var rows = Enumerable.Range(0, 205)
            .Select(index => CreateAudit($"receipt-{index}", "BRI", now.AddMinutes(-index)))
            .ToArray();
        await _db.Insertable(rows).ExecuteCommandAsync();
        var service = CreateService("SuperAdmin");

        var result = await service.QueryAsync(
            new OperationAuditQueryDto { PageNumber = 1, PageSize = 500 },
            now
        );

        Assert.Equal(205, result.Total);
        Assert.Equal(200, result.PageSize);
        Assert.Equal(200, result.Items.Count);
    }

    [Fact]
    public async Task QueryAsync_StoreManagerIsRestrictedToManageableStoreIntersection()
    {
        var now = DateTime.UtcNow;
        await InsertAuditAsync("allowed", "BRI", now.AddMinutes(-2));
        await InsertAuditAsync("blocked", "OTHER", now.AddMinutes(-1));
        var service = CreateService("StoreManager", ["BRI"]);

        var allRequested = await service.QueryAsync(new OperationAuditQueryDto(), now);
        var blockedRequested = await service.QueryAsync(
            new OperationAuditQueryDto { StoreCode = "OTHER" },
            now
        );

        Assert.Single(allRequested.Items);
        Assert.Equal("BRI", allRequested.Items[0].StoreCode);
        Assert.Empty(blockedRequested.Items);
        Assert.Equal(0, blockedRequested.Total);
    }

    [Theory]
    [InlineData("StoreManager")]
    [InlineData("店长")]
    [InlineData("经理")]
    public async Task QueryAsync_StoreManagerWithoutManageableStoreReturnsEmpty(string role)
    {
        var now = DateTime.UtcNow;
        await InsertAuditAsync("hidden", "BRI", now.AddMinutes(-1));
        var service = CreateService(role, [], scopeAllowed: false);

        var result = await service.QueryAsync(new OperationAuditQueryDto(), now);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task QueryAsync_ProductKeywordSearchesChildTableAndReturnsParent()
    {
        var now = DateTime.UtcNow;
        var matchingEvent = await InsertAuditAsync("matching", "BRI", now.AddMinutes(-1));
        await InsertAuditAsync("other", "BRI", now.AddMinutes(-2));
        await _db.Insertable(new PosOperationAuditItem
        {
            EventId = matchingEvent,
            LineIndex = 0,
            ProductCode = "P-100",
            ReferenceCode = "REF-APPLE",
            LookupCode = "LKP-1",
            DisplayName = "Green Apple",
        }).ExecuteCommandAsync();
        var service = CreateService("Admin");

        var result = await service.QueryAsync(
            new OperationAuditQueryDto { ProductKeyword = "apple" },
            now
        );

        var item = Assert.Single(result.Items);
        Assert.Equal(matchingEvent, item.EventId);
    }

    [Fact]
    public async Task QueryAsync_AppliesEmployeeDeviceEventOutcomeOrderAndKeywordFilters()
    {
        var now = DateTime.UtcNow;
        var matching = CreateAudit("receipt-match", "BRI", now.AddMinutes(-1));
        matching.CashierId = "cashier-match";
        matching.UserGuid = "user-match";
        matching.CashierName = "Alice Match";
        matching.DeviceCode = "POS-MATCH";
        matching.OperationType = "PAYMENT_CANCEL";
        matching.Outcome = "Denied";
        matching.OrderGuid = "order-match";
        matching.TraceId = "trace-match";
        matching.ReasonCode = "MANUAL_OVERRIDE";
        var other = CreateAudit("receipt-other", "BRI", now.AddMinutes(-2));
        other.CashierId = "cashier-other";
        other.UserGuid = "user-other";
        other.CashierName = "Bob Other";
        other.DeviceCode = "POS-OTHER";
        other.OperationType = "SALE_COMPLETE";
        other.Outcome = "Succeeded";
        other.OrderGuid = "order-other";
        other.TraceId = "trace-other";
        await _db.Insertable(new[] { matching, other }).ExecuteCommandAsync();
        var service = CreateService("Admin");

        var queries = new[]
        {
            new OperationAuditQueryDto { CashierKeyword = "Alice" },
            new OperationAuditQueryDto { DeviceCode = "POS-MATCH" },
            new OperationAuditQueryDto { OperationType = "PAYMENT_CANCEL" },
            new OperationAuditQueryDto { Outcome = "Denied" },
            new OperationAuditQueryDto { OrderGuid = "order-match" },
            new OperationAuditQueryDto { Keyword = "trace-match" },
            new OperationAuditQueryDto { Keyword = "MANUAL_OVERRIDE" },
        };
        foreach (var query in queries)
        {
            var result = await service.QueryAsync(query, now);
            Assert.Equal(matching.EventId, Assert.Single(result.Items).EventId);
        }
    }

    [Theory]
    [InlineData("storeCode", "asc", "value-a,value-b,value-c")]
    [InlineData("storeCode", "desc", "value-c,value-b,value-a")]
    [InlineData("operationType", "asc", "value-a,value-b,value-c")]
    [InlineData("operationType", "desc", "value-c,value-b,value-a")]
    [InlineData("amountDelta", "asc", "value-a,value-b,value-c")]
    [InlineData("amountDelta", "desc", "value-c,value-b,value-a")]
    [InlineData("deviceCode", "asc", "value-a,value-b,value-c")]
    [InlineData("deviceCode", "desc", "value-c,value-b,value-a")]
    [InlineData("outcome", "asc", "value-a,value-b,value-c")]
    [InlineData("outcome", "desc", "value-c,value-b,value-a")]
    public async Task QueryAsync_SortsByAllowedNonTimeFieldBeforeStableTieBreakers(
        string sortBy,
        string sortOrder,
        string expectedReceiptOrder
    )
    {
        var now = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            CreateAudit("value-a", "BRI", now.AddMinutes(-2)),
            CreateAudit("value-b", "BRI", now.AddMinutes(-1)),
            CreateAudit("value-c", "BRI", now.AddMinutes(-3)),
        };
        rows[0].EventId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        rows[1].EventId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        rows[2].EventId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var stringValues = new[] { "A-VALUE", "B-VALUE", "C-VALUE" };
        for (var index = 0; index < rows.Length; index++)
        {
            switch (sortBy)
            {
                case "storeCode":
                    rows[index].StoreCode = stringValues[index];
                    break;
                case "operationType":
                    rows[index].OperationType = stringValues[index];
                    break;
                case "amountDelta":
                    rows[index].AmountDelta = index + 1;
                    break;
                case "deviceCode":
                    rows[index].DeviceCode = stringValues[index];
                    break;
                case "outcome":
                    rows[index].Outcome = stringValues[index];
                    break;
            }
        }

        await _db.Insertable(rows).ExecuteCommandAsync();
        var service = CreateService("Admin");

        var result = await service.QueryAsync(
            new OperationAuditQueryDto { SortBy = sortBy, SortOrder = sortOrder },
            now
        );

        Assert.Equal(
            expectedReceiptOrder.Split(','),
            result.Items.Select(item => item.ReceiptNumber)
        );
    }

    [Theory]
    [InlineData("asc", "oldest,middle,newest")]
    [InlineData("desc", "newest,middle,oldest")]
    public async Task QueryAsync_SortsByOccurredTimeBeforeEventId(
        string sortOrder,
        string expectedReceiptOrder
    )
    {
        var now = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            CreateAudit("middle", "BRI", now.AddMinutes(-2)),
            CreateAudit("newest", "BRI", now.AddMinutes(-1)),
            CreateAudit("oldest", "BRI", now.AddMinutes(-3)),
        };
        await _db.Insertable(rows).ExecuteCommandAsync();
        var service = CreateService("Admin");

        var result = await service.QueryAsync(
            new OperationAuditQueryDto { SortBy = "occurredAtUtc", SortOrder = sortOrder },
            now
        );

        Assert.Equal(
            expectedReceiptOrder.Split(','),
            result.Items.Select(item => item.ReceiptNumber)
        );
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("storeCode", null)]
    [InlineData(null, "asc")]
    [InlineData("unsupported", "asc")]
    [InlineData("storeCode", "unsupported")]
    public async Task QueryAsync_InvalidOrIncompleteSortFallsBackToOccurredTimeDescending(
        string? sortBy,
        string? sortOrder
    )
    {
        var now = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
        await InsertAuditAsync("older", "Z-STORE", now.AddMinutes(-2));
        await InsertAuditAsync("newer", "A-STORE", now.AddMinutes(-1));
        var service = CreateService("Admin");

        var result = await service.QueryAsync(
            new OperationAuditQueryDto { SortBy = sortBy, SortOrder = sortOrder },
            now
        );

        Assert.Equal("newer", result.Items[0].ReceiptNumber);
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public async Task QueryAsync_AmountSortAlwaysPlacesNullLast(string sortOrder)
    {
        var now = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
        var first = CreateAudit("first", "BRI", now.AddMinutes(-3));
        first.AmountDelta = 1m;
        var second = CreateAudit("second", "BRI", now.AddMinutes(-2));
        second.AmountDelta = 2m;
        var missing = CreateAudit("missing", "BRI", now.AddMinutes(-1));
        missing.AmountDelta = null;
        await _db.Insertable(new[] { first, second, missing }).ExecuteCommandAsync();
        var service = CreateService("Admin");

        var result = await service.QueryAsync(
            new OperationAuditQueryDto { SortBy = "amountDelta", SortOrder = sortOrder },
            now
        );

        Assert.Equal("missing", result.Items[^1].ReceiptNumber);
    }

    [Theory]
    [InlineData("asc", "00000000-0000-0000-0000-000000000001")]
    [InlineData("desc", "00000000-0000-0000-0000-000000000002")]
    public async Task QueryAsync_OccurredTimeSortUsesEventIdInSameDirection(
        string sortOrder,
        string expectedFirstEventId
    )
    {
        var now = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
        var first = CreateAudit("first", "BRI", now.AddMinutes(-1));
        first.EventId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = CreateAudit("second", "BRI", first.OccurredAtUtc);
        second.EventId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await _db.Insertable(new[] { first, second }).ExecuteCommandAsync();
        var service = CreateService("Admin");

        var result = await service.QueryAsync(
            new OperationAuditQueryDto { SortBy = "occurredAtUtc", SortOrder = sortOrder },
            now
        );

        Assert.Equal(Guid.Parse(expectedFirstEventId), result.Items[0].EventId);
    }

    [Fact]
    public async Task QueryAsync_NonTimeSortUsesOccurredTimeAndEventIdDescendingForStablePages()
    {
        var now = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
        var older = CreateAudit("older", "BRI", now.AddMinutes(-2));
        older.EventId = Guid.Parse("00000000-0000-0000-0000-000000000999");
        var newerFirst = CreateAudit("newer-first", "BRI", now.AddMinutes(-1));
        newerFirst.EventId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var newerSecond = CreateAudit("newer-second", "BRI", newerFirst.OccurredAtUtc);
        newerSecond.EventId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var rows = new[] { older, newerFirst, newerSecond };
        await _db.Insertable(rows).ExecuteCommandAsync();
        var service = CreateService("Admin");

        var eventIds = new List<Guid>();
        for (var pageNumber = 1; pageNumber <= 3; pageNumber++)
        {
            var page = await service.QueryAsync(
                new OperationAuditQueryDto
                {
                    SortBy = "storeCode",
                    SortOrder = "asc",
                    PageNumber = pageNumber,
                    PageSize = 1,
                },
                now
            );
            eventIds.Add(Assert.Single(page.Items).EventId);
        }

        Assert.Equal(
            new[] { newerSecond.EventId, newerFirst.EventId, older.EventId },
            eventIds
        );
    }

    [Fact]
    public async Task GetDetailAsync_ReturnsItemsForAccessibleEvent()
    {
        var now = DateTime.UtcNow;
        var eventId = await InsertAuditAsync("detail", "BRI", now.AddMinutes(-1));
        await _db.Insertable(new PosOperationAuditItem
        {
            EventId = eventId,
            LineIndex = 1,
            ProductCode = "P-DETAIL",
            DisplayName = "Detail product",
            BeforeActualAmount = 10m,
            AfterActualAmount = 8m,
            ActualAmountDelta = -2m,
        }).ExecuteCommandAsync();
        var service = CreateService("StoreManager", ["BRI"]);

        var result = await service.GetDetailAsync(eventId);

        Assert.Equal(OperationAuditDetailAccessStatus.Found, result.Status);
        Assert.NotNull(result.Data);
        var item = Assert.Single(result.Data.Items);
        Assert.Equal("P-DETAIL", item.ProductCode);
        Assert.Equal(-2m, item.ActualAmountDelta);
    }

    [Fact]
    public async Task GetDetailAsync_RejectsEventOutsideStoreManagerScope()
    {
        var now = DateTime.UtcNow;
        var eventId = await InsertAuditAsync("blocked-detail", "OTHER", now.AddMinutes(-1));
        var service = CreateService("StoreManager", ["BRI"]);

        var result = await service.GetDetailAsync(eventId);

        Assert.Equal(OperationAuditDetailAccessStatus.Forbidden, result.Status);
        Assert.Null(result.Data);
    }

    private OperationAuditQueryService CreateService(
        string role,
        IReadOnlyList<string>? storeCodes = null,
        bool scopeAllowed = true
    )
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "test-user"), new Claim(ClaimTypes.Role, role)],
            "Test"
        );
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        var scope = new FakeStoreScopeService(
            new CurrentUserManageableStoreScope
            {
                IsAllowed = scopeAllowed,
                IsAuthenticated = true,
                StoreCodes = storeCodes ?? [],
            }
        );
        return new OperationAuditQueryService(_db, scope, accessor);
    }

    private async Task<Guid> InsertAuditAsync(string receiptNumber, string storeCode, DateTime occurredAtUtc)
    {
        var audit = CreateAudit(receiptNumber, storeCode, occurredAtUtc);
        await _db.Insertable(audit).ExecuteCommandAsync();
        return audit.EventId;
    }

    private static PosOperationAudit CreateAudit(
        string receiptNumber,
        string storeCode,
        DateTime occurredAtUtc
    ) => new()
    {
        EventId = Guid.NewGuid(),
        SchemaVersion = 1,
        OccurredAtUtc = occurredAtUtc,
        ReceivedAtUtc = occurredAtUtc.AddSeconds(1),
        OperationType = "SALE_COMPLETE",
        Outcome = "Succeeded",
        CashierId = "cashier-1",
        CashierName = "Alice",
        StoreCode = storeCode,
        DeviceCode = "POS-1",
        ReceiptNumber = receiptNumber,
        CurrencyCode = "AUD",
        AmountDelta = 10m,
        ProductCount = 1,
        PrimaryProduct = "Product",
    };

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class FakeStoreScopeService(CurrentUserManageableStoreScope scope)
        : ICurrentUserManageableStoreScopeService
    {
        public Task<CurrentUserManageableStoreScope> GetScopeAsync() => Task.FromResult(scope);

        public Task<IReadOnlyList<string>> GetAccessibleStoreCodesAsync() =>
            Task.FromResult(scope.StoreCodes);

        public Task<bool> CanAccessStoreCodeAsync(string storeCode) =>
            Task.FromResult(scope.CanAccessStoreCode(storeCode));

        public Task<bool> CanAccessOrderAsync(string orderGuid) => Task.FromResult(false);

        public Task<bool> CanManageStoreAsync(string storeGuid) => Task.FromResult(false);

        public Task<bool> CanManageUserAsync(string userGuid) => Task.FromResult(false);
    }
}

public sealed class OperationAuditRetentionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public OperationAuditRetentionServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables(typeof(PosOperationAudit), typeof(PosOperationAuditItem));
    }

    [Fact]
    public async Task CleanupExpiredAsync_DeletesByReceivedTimeBeforeSevenHundredThirtyDayBoundary()
    {
        var now = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var expired = CreateAudit(now.AddDays(-730).AddTicks(-1));
        var boundary = CreateAudit(now.AddDays(-730));
        await _db.Insertable(new[] { expired, boundary }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            new PosOperationAuditItem { EventId = expired.EventId, LineIndex = 0 },
            new PosOperationAuditItem { EventId = boundary.EventId, LineIndex = 0 },
        }).ExecuteCommandAsync();
        var service = new OperationAuditRetentionService(_db);

        var deleted = await service.CleanupExpiredAsync(now);

        Assert.Equal(1, deleted);
        Assert.False(await _db.Queryable<PosOperationAudit>().AnyAsync(x => x.EventId == expired.EventId));
        Assert.False(await _db.Queryable<PosOperationAuditItem>().AnyAsync(x => x.EventId == expired.EventId));
        Assert.True(await _db.Queryable<PosOperationAudit>().AnyAsync(x => x.EventId == boundary.EventId));
        Assert.True(await _db.Queryable<PosOperationAuditItem>().AnyAsync(x => x.EventId == boundary.EventId));
    }

    [Fact]
    public async Task CleanupExpiredAsync_ProcessesMoreThanOneBatch()
    {
        var now = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 1001)
            .Select(_ => CreateAudit(now.AddDays(-731)))
            .ToArray();
        await _db.Insertable(rows).ExecuteCommandAsync();
        var service = new OperationAuditRetentionService(_db);

        var deleted = await service.CleanupExpiredAsync(now);

        Assert.Equal(1001, deleted);
        Assert.Equal(0, await _db.Queryable<PosOperationAudit>().CountAsync());
    }

    private static PosOperationAudit CreateAudit(DateTime receivedAtUtc) => new()
    {
        EventId = Guid.NewGuid(),
        SchemaVersion = 1,
        OccurredAtUtc = receivedAtUtc.AddMinutes(-1),
        ReceivedAtUtc = receivedAtUtc,
        OperationType = "SALE_COMPLETE",
        Outcome = "Succeeded",
        StoreCode = "BRI",
        DeviceCode = "POS-1",
        CurrencyCode = "AUD",
    };

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}

public sealed class OperationAuditControllerContractTests
{
    [Fact]
    public void Controller_UsesExpectedRouteAndAuditPermissionPolicy()
    {
        var type = typeof(PosOperationAuditController);

        Assert.Equal(
            "api/react/pos-operation-audits",
            type.GetCustomAttributes(typeof(RouteAttribute), true)
                .Cast<RouteAttribute>()
                .Single()
                .Template
        );
        Assert.Equal(
            Permissions.PosTerminal.Audit.View,
            type.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>()
                .Single()
                .Policy
        );
        Assert.NotNull(type.GetMethod(nameof(PosOperationAuditController.GetList)));
        Assert.NotNull(type.GetMethod(nameof(PosOperationAuditController.GetDetail)));
    }

    [Fact]
    public async Task Program_RegistersQueryRetentionAndDailyCleanupAgainstPosmDatabase()
    {
        var programPath = Path.Combine(FindRepoRoot(), "services/backend/BlazorApp.Api/Program.cs");
        var program = await File.ReadAllTextAsync(programPath);

        Assert.Contains("AddScoped<OperationAuditQueryService>(sp =>", program);
        Assert.Contains("AddScoped<OperationAuditRetentionService>(sp =>", program);
        Assert.Contains("sp.GetRequiredService<POSMSqlSugarContext>().Db", program);
        Assert.Contains("AddHostedService<OperationAuditCleanupBackgroundService>()", program);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var programPath = Path.Combine(
                directory.FullName,
                "services/backend/BlazorApp.Api/Program.cs"
            );
            if (File.Exists(programPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 hb-platform 仓库根目录");
    }
}
