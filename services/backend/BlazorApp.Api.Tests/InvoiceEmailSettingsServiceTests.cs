using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class InvoiceEmailSettingsServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;

    public InvoiceEmailSettingsServiceTests()
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

        _db.CodeFirst.InitTables(typeof(InvoiceEmailConfiguration));
    }

    [Fact]
    public async Task GetSettingsAsync_WhenDatabaseEmpty_ReturnsAppsettingsFallbackWithoutPassword()
    {
        var service = CreateService(new InvoiceEmailOptions
        {
            Host = "smtp.example.com",
            Port = 465,
            UseSsl = true,
            CheckCertificateRevocation = false,
            Username = "smtp-user",
            Password = "secret",
            FromEmail = "warehouse@example.com",
            FromName = "HOT BARGAIN",
            MaxAttachmentBytes = 12345,
        });

        var result = await service.GetSettingsAsync();

        Assert.True(result.Success);
        Assert.Equal("smtp.example.com", result.Data!.Host);
        Assert.Equal(465, result.Data.Port);
        Assert.False(result.Data.CheckCertificateRevocation);
        Assert.Equal("smtp-user", result.Data.Username);
        Assert.True(result.Data.HasPassword);
        Assert.Equal("warehouse@example.com", result.Data.FromEmail);
        Assert.Equal("HOT BARGAIN", result.Data.FromName);
        Assert.Equal(12345, result.Data.MaxAttachmentBytes);
        Assert.Null(result.Data.UpdatedBy);
    }

    [Fact]
    public async Task UpdateSettingsAsync_SavesEncryptedPasswordAndDoesNotReturnPlainText()
    {
        var service = CreateService();

        var result = await service.UpdateSettingsAsync(
            new UpdateInvoiceEmailSettingsDto
            {
                Host = "mail.hotbargain.com.au",
                Port = 465,
                UseSsl = true,
                CheckCertificateRevocation = false,
                Username = "sender@hotbargain.com.au",
                Password = "smtp-secret",
                FromEmail = "sender@hotbargain.com.au",
                FromName = "HOT BARGAIN INTERNATIONAL",
                MaxAttachmentBytes = 5_242_880,
            },
            "admin"
        );

        var row = await _db.Queryable<InvoiceEmailConfiguration>().FirstAsync();
        var runtime = await service.GetEffectiveOptionsAsync();

        Assert.True(result.Success);
        Assert.True(result.Data!.HasPassword);
        Assert.Equal("admin", result.Data.UpdatedBy);
        Assert.NotEqual("smtp-secret", row!.EncryptedPassword);
        Assert.False(string.IsNullOrWhiteSpace(row.EncryptedPassword));
        Assert.Equal("smtp-secret", runtime.Password);
        Assert.Equal("sender@hotbargain.com.au", runtime.FromEmail);
        Assert.False(runtime.CheckCertificateRevocation);
    }

    [Fact]
    public async Task UpdateSettingsAsync_WhenPasswordEmpty_PreservesExistingPassword()
    {
        var service = CreateService();
        await service.UpdateSettingsAsync(CreateRequest(password: "initial-secret"), "admin");

        var result = await service.UpdateSettingsAsync(
            CreateRequest(
                host: "smtp-updated.example.com",
                password: "",
                clearPassword: false
            ),
            "admin"
        );
        var runtime = await service.GetEffectiveOptionsAsync();

        Assert.True(result.Success);
        Assert.True(result.Data!.HasPassword);
        Assert.Equal("smtp-updated.example.com", runtime.Host);
        Assert.Equal("initial-secret", runtime.Password);
    }

    [Fact]
    public async Task UpdateSettingsAsync_WhenClearPassword_RemovesStoredPassword()
    {
        var service = CreateService();
        await service.UpdateSettingsAsync(CreateRequest(password: "initial-secret"), "admin");

        var result = await service.UpdateSettingsAsync(
            CreateRequest(password: "", clearPassword: true),
            "admin"
        );
        var runtime = await service.GetEffectiveOptionsAsync();

        Assert.True(result.Success);
        Assert.False(result.Data!.HasPassword);
        Assert.True(string.IsNullOrWhiteSpace(runtime.Password));
    }

    [Fact]
    public async Task BuildTransientOptions_UsesRequestPasswordWithoutSaving()
    {
        var service = CreateService();
        var request = CreateRequest(
            host: "smtp-test.example.com",
            password: "temporary-secret"
        );

        var options = await service.BuildTransientOptionsAsync(request);
        var rowCount = await _db.Queryable<InvoiceEmailConfiguration>().CountAsync();

        Assert.Equal("smtp-test.example.com", options.Host);
        Assert.Equal("temporary-secret", options.Password);
        Assert.Equal(0, rowCount);
    }

    [Fact]
    public async Task BuildTransientOptions_WhenPasswordEmpty_ReusesSavedPassword()
    {
        var service = CreateService();
        await service.UpdateSettingsAsync(CreateRequest(password: "saved-secret"), "admin");

        var options = await service.BuildTransientOptionsAsync(
            CreateRequest(
                host: "smtp-test.example.com",
                password: "",
                clearPassword: false
            )
        );

        Assert.Equal("smtp-test.example.com", options.Host);
        Assert.Equal("saved-secret", options.Password);
    }

    [Fact]
    public async Task BuildTransientOptions_WhenRequestPasswordProvided_DoesNotDecryptStoredPassword()
    {
        var service = CreateService();
        await SeedInvalidEncryptedPasswordAsync();

        var options = await service.BuildTransientOptionsAsync(
            CreateRequest(
                host: "smtp-test.example.com",
                password: "temporary-secret",
                clearPassword: false
            )
        );

        Assert.Equal("smtp-test.example.com", options.Host);
        Assert.Equal("temporary-secret", options.Password);
    }

    [Fact]
    public async Task BuildTransientOptions_WhenClearPassword_DoesNotDecryptStoredPassword()
    {
        var service = CreateService();
        await SeedInvalidEncryptedPasswordAsync();

        var options = await service.BuildTransientOptionsAsync(
            CreateRequest(
                host: "smtp-test.example.com",
                password: "",
                clearPassword: true
            )
        );

        Assert.Equal("smtp-test.example.com", options.Host);
        Assert.Null(options.Password);
    }

    [Fact]
    public async Task GetEffectiveOptionsAsync_WhenEncryptedPasswordInvalid_ThrowsDecryptException()
    {
        var service = CreateService();
        await SeedInvalidEncryptedPasswordAsync();

        await Assert.ThrowsAsync<InvoiceEmailPasswordDecryptException>(
            () => service.GetEffectiveOptionsAsync()
        );
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private InvoiceEmailSettingsService CreateService(InvoiceEmailOptions? fallback = null)
    {
        return new InvoiceEmailSettingsService(
            CreateSqlSugarContext(_db),
            Options.Create(fallback ?? new InvoiceEmailOptions()),
            DataProtectionProvider.Create("InvoiceEmailSettingsServiceTests"),
            NullLogger<InvoiceEmailSettingsService>.Instance
        );
    }

    private static UpdateInvoiceEmailSettingsDto CreateRequest(
        string host = "smtp.example.com",
        string password = "secret",
        bool clearPassword = false
    ) =>
        new()
        {
            Host = host,
            Port = 465,
            UseSsl = true,
            CheckCertificateRevocation = true,
            Username = "smtp-user",
            Password = password,
            ClearPassword = clearPassword,
            FromEmail = "warehouse@example.com",
            FromName = "HOT BARGAIN",
            MaxAttachmentBytes = 5_242_880,
        };

    private async Task SeedInvalidEncryptedPasswordAsync()
    {
        await _db.Insertable(
            new InvoiceEmailConfiguration
            {
                Id = InvoiceEmailConfiguration.DefaultId,
                Host = "smtp.example.com",
                Port = 465,
                UseSsl = true,
                CheckCertificateRevocation = true,
                Username = "smtp-user",
                EncryptedPassword = "invalid-protected-payload",
                FromEmail = "warehouse@example.com",
                MaxAttachmentBytes = 5_242_880,
            }
        ).ExecuteCommandAsync();
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, db);
        return context;
    }
}
