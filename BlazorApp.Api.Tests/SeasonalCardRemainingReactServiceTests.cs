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

namespace BlazorApp.Api.Tests;

public sealed class SeasonalCardRemainingReactServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;

    public SeasonalCardRemainingReactServiceTests()
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
            typeof(SeasonalCardCatalog),
            typeof(SeasonalCardRemainingSubmission)
        );
    }

    [Fact]
    public async Task CreateSubmissionAsync_WhenCatalogUsesFixedPriceAndRequestCarriesCustomUnitPrice_ReturnsErrorAndDoesNotInsert()
    {
        await SeedStoreScopeAsync();
        await SeedCatalogAsync(
            "catalog-fixed-2",
            SeasonalCardType.Christmas,
            "$2",
            false,
            2m,
            2
        );
        var service = CreateService("manager-user", "manager", "StoreManager");

        var result = await service.CreateSubmissionAsync(new CreateSeasonalCardRemainingSubmissionDto
        {
            StoreCode = "BRI",
            CatalogGuid = "catalog-fixed-2",
            SeasonYear = 2026,
            RemainingQuantity = 7,
            CustomUnitPrice = 9.99m,
            Remark = "fixed",
        });

        Assert.False(result.Success);
        Assert.Equal("FIXED_PRICE_OVERRIDE_NOT_ALLOWED", result.ErrorCode);

        Assert.Equal(0, await _db.Queryable<SeasonalCardRemainingSubmission>().CountAsync());
    }

    [Fact]
    public async Task CreateSubmissionAsync_WhenCatalogUsesOtherPrice_RequiresPositiveCustomUnitPrice()
    {
        await SeedStoreScopeAsync();
        await SeedCatalogAsync(
            "catalog-other",
            SeasonalCardType.Easter,
            "其他",
            true,
            null,
            4
        );
        var service = CreateService("manager-user", "manager", "StoreManager");

        var invalidResult = await service.CreateSubmissionAsync(
            new CreateSeasonalCardRemainingSubmissionDto
            {
                StoreCode = "BRI",
                CatalogGuid = "catalog-other",
                SeasonYear = 2026,
                RemainingQuantity = 3,
            }
        );

        Assert.False(invalidResult.Success);
        Assert.Equal("CUSTOM_PRICE_REQUIRED", invalidResult.ErrorCode);

        var validResult = await service.CreateSubmissionAsync(
            new CreateSeasonalCardRemainingSubmissionDto
            {
                StoreCode = "BRI",
                CatalogGuid = "catalog-other",
                SeasonYear = 2026,
                RemainingQuantity = 3,
                CustomUnitPrice = 4.5m,
            }
        );

        Assert.True(validResult.Success);
        Assert.Equal(4.5m, validResult.Data!.UnitPrice);
    }

    [Fact]
    public async Task CreateSubmissionAsync_WhenOtherPriceRoundsToZero_ReturnsErrorAndDoesNotInsert()
    {
        await SeedStoreScopeAsync();
        await SeedCatalogAsync(
            "catalog-other",
            SeasonalCardType.ValentinesDay,
            "其他",
            true,
            null,
            4
        );
        var service = CreateService("manager-user", "manager", "StoreManager");

        var result = await service.CreateSubmissionAsync(
            new CreateSeasonalCardRemainingSubmissionDto
            {
                StoreCode = "BRI",
                CatalogGuid = "catalog-other",
                SeasonYear = 2026,
                RemainingQuantity = 3,
                CustomUnitPrice = 0.001m,
            }
        );

        Assert.False(result.Success);
        Assert.Equal("CUSTOM_PRICE_REQUIRED", result.ErrorCode);
        Assert.Equal(0, await _db.Queryable<SeasonalCardRemainingSubmission>().CountAsync());
    }

    [Fact]
    public async Task GetSubmissionsAsync_WhenStoreManagerRequestsUnmanagedStore_ReturnsForbidden()
    {
        await SeedStoreScopeAsync();
        await SeedCatalogAsync(
            "catalog-fixed-1",
            SeasonalCardType.FathersDay,
            "$1",
            false,
            1m,
            1
        );
        await SeedSubmissionAsync(
            "submission-other",
            "OTHER",
            "catalog-fixed-1",
            SeasonalCardType.FathersDay,
            "$1",
            1m
        );
        var service = CreateService("manager-user", "manager", "StoreManager");

        var result = await service.GetSubmissionsAsync(new SeasonalCardRemainingSubmissionQueryDto
        {
            StoreCode = "OTHER",
            PageNumber = 1,
            PageSize = 20,
        });

        Assert.False(result.Success);
        Assert.Equal("FORBIDDEN_STORE", result.ErrorCode);
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

    private SeasonalCardRemainingReactService CreateService(
        string userGuid,
        string username,
        params string[] roles
    )
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

        return new SeasonalCardRemainingReactService(
            context,
            currentUserService,
            scopeService,
            NullLogger<SeasonalCardRemainingReactService>.Instance
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
                    UserGUID = "admin-user",
                    Username = "admin",
                    Email = "admin@example.com",
                    PasswordHash = "hash",
                    CreatedAt = DateTime.UtcNow,
                },
            }
        ).ExecuteCommandAsync();

        await _db.Insertable(new UserStore
        {
            UserStoreGUID = "manager-store-bri",
            UserGUID = "manager-user",
            StoreGUID = "store-bri",
            IsPrimary = true,
            CreatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedCatalogAsync(
        string catalogGuid,
        SeasonalCardType cardType,
        string priceLabel,
        bool allowsCustomUnitPrice,
        decimal? fixedUnitPrice,
        int sortOrder
    )
    {
        await _db.Insertable(new SeasonalCardCatalog
        {
            CatalogGuid = catalogGuid,
            CatalogCode = $"{cardType}-{priceLabel}",
            CardType = cardType,
            PriceLabel = priceLabel,
            AllowsCustomUnitPrice = allowsCustomUnitPrice,
            FixedUnitPrice = fixedUnitPrice,
            SortOrder = sortOrder,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedSubmissionAsync(
        string submissionGuid,
        string storeCode,
        string catalogGuid,
        SeasonalCardType cardType,
        string priceLabel,
        decimal unitPrice
    )
    {
        await _db.Insertable(new SeasonalCardRemainingSubmission
        {
            SubmissionGuid = submissionGuid,
            StoreCode = storeCode,
            CatalogGuid = catalogGuid,
            CardType = cardType,
            PriceLabel = priceLabel,
            UnitPrice = unitPrice,
            SeasonYear = 2026,
            RemainingQuantity = 1,
            SubmittedAt = DateTime.UtcNow,
            SubmittedByUserGuid = "manager-user",
            SubmittedByName = "manager",
            CreatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();
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
