using BlazorApp.Api.Authentication;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ServiceApiTokenServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ISqlSugarClient _db;
    private readonly SqlSugarContext _context;

    public ServiceApiTokenServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"service-api-token-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"DataSource={_dbPath}",
            })
            .Build();
        _context = new SqlSugarContext(
            configuration,
            NullLogger<SqlSugarContext>.Instance,
            Mock.Of<ICurrentUserService>()
        );
        _db = _context.Db;
        _db.CodeFirst.InitTables(typeof(ServiceApiToken));
    }

    [Fact]
    public async Task CreateValidateRevokeAsync_创建后可验证且撤销后失效()
    {
        var service = CreateService();

        var createResult = await service.CreateAsync(
            new ServiceApiTokenCreateRequestDto { Name = " OTA 发布 " },
            "admin"
        );

        Assert.True(createResult.Success);
        Assert.NotNull(createResult.Data);
        Assert.StartsWith(ServiceApiTokenAuthenticationDefaults.TokenPrefix, createResult.Data.Token);
        Assert.Equal("OTA 发布", createResult.Data.Name);
        Assert.Contains(Permissions.System.ManageAppDownloads, createResult.Data.Scopes);

        var stored = await _db.Queryable<ServiceApiToken>().SingleAsync();
        Assert.NotEqual(createResult.Data.Token, stored.TokenHash);
        Assert.Equal(createResult.Data.TokenPrefix, stored.TokenPrefix);

        var validation = await service.ValidateAsync(createResult.Data.Token, "203.0.113.123");

        Assert.NotNull(validation);
        Assert.Equal(stored.Id, validation.Id);
        Assert.Contains(Permissions.System.ManageAppDownloads, validation.Scopes);

        stored = await _db.Queryable<ServiceApiToken>().SingleAsync();
        Assert.NotNull(stored.LastUsedAt);
        Assert.Equal("203.0.113.123", stored.LastUsedIp);

        var revokeResult = await service.RevokeAsync(stored.Id, "admin");

        Assert.True(revokeResult.Success);
        Assert.Equal("revoked", revokeResult.Data!.Status);
        Assert.Null(await service.ValidateAsync(createResult.Data.Token, "203.0.113.123"));

        stored = await _db.Queryable<ServiceApiToken>().SingleAsync();
        Assert.NotNull(stored.RevokedAt);
        Assert.Equal("admin", stored.RevokedBy);
        Assert.Equal("203.0.113.123", stored.LastUsedIp);
    }

    [Fact]
    public async Task ListAsync_不返回明文Token或Hash()
    {
        var service = CreateService();
        var createResult = await service.CreateAsync(
            new ServiceApiTokenCreateRequestDto { Name = "OTA 发布" },
            "admin"
        );

        var listResult = await service.ListAsync();

        Assert.True(listResult.Success);
        var item = Assert.Single(listResult.Data!);
        Assert.Equal(createResult.Data!.Id, item.Id);
        Assert.Equal(createResult.Data.TokenPrefix, item.TokenPrefix);
        Assert.Contains(Permissions.System.ManageAppDownloads, item.Scopes);
        Assert.DoesNotContain(
            typeof(ServiceApiTokenDto).GetProperties().Select(property => property.Name),
            propertyName => propertyName is "Token" or "TokenHash"
        );
    }

    [Fact]
    public async Task ValidateAsync_过期或前缀错误时返回空()
    {
        var service = CreateService();
        var expiredToken = $"{ServiceApiTokenAuthenticationDefaults.TokenPrefix}expired-test";
        await _db.Insertable(new ServiceApiToken
        {
            Id = Guid.NewGuid(),
            Name = "expired",
            TokenHash = Sha256(expiredToken),
            TokenPrefix = expiredToken[..18],
            Scopes = $"[\"{Permissions.System.ManageAppDownloads}\"]",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            IsDeleted = false,
        }).ExecuteCommandAsync();

        Assert.Null(await service.ValidateAsync(expiredToken, null));
        Assert.Null(await service.ValidateAsync("regular-jwt-token", null));
    }

    [Fact]
    public async Task ValidateAsync_最后使用时间写入异常时不放行Token()
    {
        var service = CreateService();
        var createResult = await service.CreateAsync(
            new ServiceApiTokenCreateRequestDto { Name = "OTA 发布" },
            "admin"
        );

        // 用 SQLite trigger 模拟最后使用审计更新失败；这一步同时承担并发撤销复核，失败时必须拒绝认证。
        _db.Ado.ExecuteCommand(
            "CREATE TRIGGER BlockServiceApiTokenUpdate BEFORE UPDATE ON ServiceApiToken BEGIN SELECT RAISE(ABORT, 'blocked update'); END;"
        );

        var validation = await service.ValidateAsync(createResult.Data!.Token, "203.0.113.123");

        Assert.Null(validation);
    }

    [Fact]
    public async Task CreateAsync_名称缺失或超长时返回业务错误()
    {
        var service = CreateService();

        var missingName = await service.CreateAsync(
            new ServiceApiTokenCreateRequestDto { Name = " " },
            "admin"
        );
        var longName = await service.CreateAsync(
            new ServiceApiTokenCreateRequestDto { Name = new string('x', 121) },
            "admin"
        );

        Assert.False(missingName.Success);
        Assert.Equal("SERVICE_API_TOKEN_NAME_REQUIRED", missingName.ErrorCode);
        Assert.False(longName.Success);
        Assert.Equal("SERVICE_API_TOKEN_NAME_TOO_LONG", longName.ErrorCode);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private ServiceApiTokenService CreateService() =>
        new(_context, NullLogger<ServiceApiTokenService>.Instance);

    private static string Sha256(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value)
        );
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
