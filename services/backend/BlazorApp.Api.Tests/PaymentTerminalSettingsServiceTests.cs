using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PaymentTerminalSettingsServiceTests : IDisposable
{
    private readonly string _mainDbPath;
    private readonly string _posmDbPath;
    private readonly SqliteConnection _mainConnection;
    private readonly SqliteConnection _posmConnection;
    private readonly SqlSugarClient _mainDb;
    private readonly SqlSugarClient _posmDb;

    public PaymentTerminalSettingsServiceTests()
    {
        _mainDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.main.db");
        _posmDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.posm.db");
        _mainConnection = new SqliteConnection($"Data Source={_mainDbPath}");
        _posmConnection = new SqliteConnection($"Data Source={_posmDbPath}");
        _mainConnection.Open();
        _posmConnection.Open();

        _mainDb = CreateDb(_mainConnection.ConnectionString);
        _posmDb = CreateDb(_posmConnection.ConnectionString);

        _mainDb.CodeFirst.InitTables(typeof(Store));
        CreatePaymentTables();
    }

    [Fact]
    public async Task UpdateSquareTokenAsync_WritesEnabledTokenAndReturnsSanitizedStatus()
    {
        var service = CreateService();

        var result = await service.UpdateSquareTokenAsync(
            new UpdateSquareTokenDto
            {
                Environment = "sandbox",
                AccessToken = "  sandbox-secret  ",
            },
            "admin"
        );

        var rows = await QuerySquareRowsAsync("Sandbox");

        Assert.True(result.Success);
        Assert.True(result.Data!.Square.Single(item => item.Environment == "Sandbox").Configured);
        Assert.Single(rows);
        Assert.True(rows[0].IsEnabled);
        Assert.Equal("sandbox-secret", rows[0].AccessToken);
        Assert.DoesNotContain(
            typeof(PaymentTerminalEnvironmentStatusDto).GetProperties(),
            property => property.Name == "AccessToken"
        );
    }

    [Fact]
    public async Task UpdateSquareTokenAsync_ReplacesEnabledTokenForEnvironment()
    {
        var service = CreateService();
        await service.UpdateSquareTokenAsync(
            new UpdateSquareTokenDto { Environment = "Production", AccessToken = "first-token" },
            "admin"
        );

        await service.UpdateSquareTokenAsync(
            new UpdateSquareTokenDto { Environment = "Production", AccessToken = "second-token" },
            "admin"
        );

        var rows = await QuerySquareRowsAsync("Production");

        Assert.Equal(2, rows.Count);
        Assert.Single(rows, row => row.IsEnabled);
        Assert.Equal("second-token", rows.Single(row => row.IsEnabled).AccessToken);
        Assert.False(rows.Single(row => row.AccessToken == "first-token").IsEnabled);
    }

    [Fact]
    public async Task UpdateSquareTokenAsync_ClearDisablesTokenAndBlanksSecret()
    {
        var service = CreateService();
        await service.UpdateSquareTokenAsync(
            new UpdateSquareTokenDto { Environment = "Production", AccessToken = "secret-token" },
            "admin"
        );

        var result = await service.UpdateSquareTokenAsync(
            new UpdateSquareTokenDto { Environment = "Production", ClearToken = true },
            "admin"
        );
        var row = (await QuerySquareRowsAsync("Production")).Single();

        Assert.True(result.Success);
        Assert.False(result.Data!.Square.Single(item => item.Environment == "Production").Configured);
        Assert.False(row.IsEnabled);
        Assert.Equal(string.Empty, row.AccessToken);
    }

    [Fact]
    public async Task UpdateSquareTokenAsync_ReturnsCurrentStoreSelectionWhenProvided()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        SeedStore("002", "Beach Store");

        var result = await service.UpdateSquareTokenAsync(
            new UpdateSquareTokenDto { Environment = "Sandbox", AccessToken = "sandbox-token" },
            "admin",
            "002"
        );

        Assert.True(result.Success);
        Assert.Equal("002", result.Data!.SelectedStoreCode);
    }

    [Fact]
    public async Task UpdateLinklyCredentialAsync_KeepsExistingPasswordWhenPasswordIsBlank()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        await service.UpdateLinklyCredentialAsync(
            new UpdateLinklyCredentialDto
            {
                StoreCode = "001",
                Environment = "Production",
                Username = "old-user",
                Password = "old-password",
            },
            "admin"
        );

        var result = await service.UpdateLinklyCredentialAsync(
            new UpdateLinklyCredentialDto
            {
                StoreCode = "001",
                Environment = "Production",
                Username = "new-user",
                Password = "   ",
            },
            "admin"
        );
        var row = (await QueryLinklyRowsAsync("001", "Production")).Single();

        Assert.True(result.Success);
        Assert.True(result.Data!.Linkly.Single(item => item.Environment == "Production").HasPassword);
        Assert.Equal("new-user", row.Username);
        Assert.Equal("old-password", row.Password);
        Assert.DoesNotContain(
            typeof(LinklyCloudCredentialAdminDto).GetProperties(),
            property => property.Name == "Password"
        );
    }

    [Fact]
    public async Task UpdateLinklyCredentialAsync_WhenPasswordBlankWithoutExistingCredential_ReturnsValidationError()
    {
        var service = CreateService();
        SeedStore("001", "City Store");

        var result = await service.UpdateLinklyCredentialAsync(
            new UpdateLinklyCredentialDto
            {
                StoreCode = "001",
                Environment = "Sandbox",
                Username = "sandbox-user",
                Password = " ",
            },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("LINKLY_PASSWORD_REQUIRED", result.ErrorCode);
        Assert.Empty(await QueryLinklyRowsAsync("001", "Sandbox"));
    }

    [Fact]
    public async Task UpdateLinklyCredentialAsync_ClearDeletesCredential()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        await service.UpdateLinklyCredentialAsync(
            new UpdateLinklyCredentialDto
            {
                StoreCode = "001",
                Environment = "Sandbox",
                Username = "sandbox-user",
                Password = "sandbox-password",
            },
            "admin"
        );

        var result = await service.UpdateLinklyCredentialAsync(
            new UpdateLinklyCredentialDto
            {
                StoreCode = "001",
                Environment = "Sandbox",
                ClearCredential = true,
            },
            "admin"
        );

        Assert.True(result.Success);
        Assert.False(result.Data!.Linkly.Single(item => item.Environment == "Sandbox").HasPassword);
        Assert.Empty(await QueryLinklyRowsAsync("001", "Sandbox"));
    }

    [Fact]
    public async Task GetSettingsAsync_ReturnsStoresAndSelectedStoreStatuses()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        SeedStore("002", "Beach Store");
        await service.UpdateLinklyCredentialAsync(
            new UpdateLinklyCredentialDto
            {
                StoreCode = "002",
                Environment = "Production",
                Username = "linkly-user",
                Password = "linkly-password",
            },
            "admin"
        );

        var result = await service.GetSettingsAsync("002");

        Assert.True(result.Success);
        Assert.Equal("002", result.Data!.SelectedStoreCode);
        Assert.Equal(new[] { "001", "002" }, result.Data.Stores.Select(store => store.StoreCode).ToArray());
        Assert.True(result.Data.Linkly.Single(item => item.Environment == "Production").HasPassword);
        Assert.Equal("linkly-user", result.Data.Linkly.Single(item => item.Environment == "Production").Username);
    }

    [Fact]
    public void Controller_UsesExpectedRouteAndSystemSettingsPolicy()
    {
        var controllerType = typeof(ReactPaymentTerminalSettingsController);
        var route = controllerType.GetCustomAttribute<RouteAttribute>();
        var policies = controllerType.GetMethods()
            .Where(method => method.DeclaringType == controllerType)
            .SelectMany(method => method.GetCustomAttributes<AuthorizeAttribute>())
            .Select(attribute => attribute.Policy)
            .ToArray();

        Assert.Equal("api/react/v1/payment-terminal-settings", route?.Template);
        Assert.Contains(Permissions.System.ManageSettings, policies);
        Assert.All(policies, policy => Assert.Equal(Permissions.System.ManageSettings, policy));
    }

    public void Dispose()
    {
        _mainDb.Dispose();
        _posmDb.Dispose();
        _mainConnection.Dispose();
        _posmConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_mainDbPath);
        SqliteTempFileCleanup.DeleteIfExists(_posmDbPath);
    }

    private PaymentTerminalSettingsService CreateService()
    {
        return new PaymentTerminalSettingsService(
            CreatePOSMSqlSugarContext(_posmDb),
            CreateSqlSugarContext(_mainDb),
            NullLogger<PaymentTerminalSettingsService>.Instance
        );
    }

    private void CreatePaymentTables()
    {
        _posmDb.Ado.ExecuteCommand("""
            CREATE TABLE POSM_SquareToken (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Environment TEXT NOT NULL,
                AccessToken TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL,
                UpdatedAt TEXT NOT NULL,
                UpdatedBy TEXT NULL
            );
            """);
        _posmDb.Ado.ExecuteCommand("""
            CREATE TABLE POSM_LinklyCloudCredential (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StoreCode TEXT NOT NULL,
                Environment TEXT NOT NULL,
                Username TEXT NOT NULL,
                Password TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                UpdatedBy TEXT NULL
            );
            """);
    }

    private void SeedStore(string storeCode, string storeName)
    {
        _mainDb.Insertable(new Store
        {
            StoreGUID = Guid.NewGuid().ToString("N"),
            StoreCode = storeCode,
            StoreName = storeName,
            IsActive = true,
        }).ExecuteCommand();
    }

    private Task<List<SquareRow>> QuerySquareRowsAsync(string environment)
    {
        return _posmDb.Ado.SqlQueryAsync<SquareRow>(
            """
            SELECT Id, Environment, AccessToken, IsEnabled, UpdatedAt, UpdatedBy
            FROM POSM_SquareToken
            WHERE Environment = @Environment
            ORDER BY Id
            """,
            new SugarParameter("@Environment", environment)
        );
    }

    private Task<List<LinklyRow>> QueryLinklyRowsAsync(string storeCode, string environment)
    {
        return _posmDb.Ado.SqlQueryAsync<LinklyRow>(
            """
            SELECT Id, StoreCode, Environment, Username, Password, UpdatedAt, UpdatedBy
            FROM POSM_LinklyCloudCredential
            WHERE StoreCode = @StoreCode AND Environment = @Environment
            ORDER BY Id
            """,
            new SugarParameter("@StoreCode", storeCode),
            new SugarParameter("@Environment", environment)
        );
    }

    private static SqlSugarClient CreateDb(string connectionString) =>
        new(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
        return context;
    }

    private static POSMSqlSugarContext CreatePOSMSqlSugarContext(ISqlSugarClient db)
    {
        var context = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(POSMSqlSugarContext));
        typeof(POSMSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
        return context;
    }

    private sealed class SquareRow
    {
        public long Id { get; set; }
        public string Environment { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }

    private sealed class LinklyRow
    {
        public long Id { get; set; }
        public string StoreCode { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }
}
