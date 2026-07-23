using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class LocalSupplierHqSyncTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hqDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hqConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarScope _hqDb;

    public LocalSupplierHqSyncTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hqDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _hqConnection = new SqliteConnection($"Data Source={_hqDbPath}");
        _localConnection.Open();
        _hqConnection.Open();

        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _hqDb = new SqlSugarScope(CreateConnectionConfig(_hqConnection.ConnectionString));
        _localDb.CodeFirst.InitTables<HBLocalSupplier>();
        _hqDb.CodeFirst.InitTables<DIC_供应商信息表>();
    }

    [Fact]
    public async Task SyncToHqAsync_只新增和更新指定澳洲供应商()
    {
        await SeedLocalSupplierAsync("AUS-NEW", "新供应商", "New Contact", "new@example.com");
        await SeedLocalSupplierAsync("AUS-EXISTING", "更新后名称", "Updated Contact", "updated@example.com");
        await SeedLocalSupplierAsync("AUS-NOT-SELECTED", "未选择供应商", null, null);

        var originalCreateDate = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await _hqDb.Insertable(new DIC_供应商信息表
        {
            HGUID = "hq-existing",
            H供应商编码 = "AUS-EXISTING",
            H供应商名称 = "旧名称",
            H供应商全称 = "保留的公司全称",
            H公司地址 = "保留的公司地址",
            H联系人 = "Old Contact",
            HEMAIL地址 = "old@example.com",
            FGC_CreateDate = originalCreateDate,
            FGC_LastModifyDate = originalCreateDate,
        }).ExecuteCommandAsync();

        var response = await CreateService().SyncToHqAsync(
            new[] { "AUS-NEW", "AUS-EXISTING" }
        );

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data?.CreatedCount);
        Assert.Equal(1, response.Data?.UpdatedCount);
        Assert.Equal(0, response.Data?.SkippedCount);

        var inserted = await _hqDb.Queryable<DIC_供应商信息表>()
            .SingleAsync(x => x.H供应商编码 == "AUS-NEW");
        Assert.Equal("新供应商", inserted.H供应商名称);
        Assert.Equal("新供应商", inserted.H供应商全称);
        Assert.Equal("New Contact", inserted.H联系人);
        Assert.Equal("new@example.com", inserted.HEMAIL地址);

        var updated = await _hqDb.Queryable<DIC_供应商信息表>()
            .SingleAsync(x => x.H供应商编码 == "AUS-EXISTING");
        Assert.Equal("更新后名称", updated.H供应商名称);
        Assert.Equal("Updated Contact", updated.H联系人);
        Assert.Equal("updated@example.com", updated.HEMAIL地址);
        Assert.Equal("保留的公司全称", updated.H供应商全称);
        Assert.Equal("保留的公司地址", updated.H公司地址);
        Assert.Equal(originalCreateDate, updated.FGC_CreateDate);

        Assert.False(
            await _hqDb.Queryable<DIC_供应商信息表>()
                .AnyAsync(x => x.H供应商编码 == "AUS-NOT-SELECTED")
        );
    }

    [Fact]
    public async Task SyncToHqAsync_Hq代码重复时跳过且不覆盖历史记录()
    {
        await SeedLocalSupplierAsync("AUS-DUP", "不应写入", "New Contact", "new@example.com");
        await _hqDb.Insertable(new[]
        {
            CreateHqSupplier("hq-1", "AUS-DUP", "旧记录 1"),
            CreateHqSupplier("hq-2", "AUS-DUP", "旧记录 2"),
        }).ExecuteCommandAsync();

        var response = await CreateService().SyncToHqAsync(new[] { "AUS-DUP" });

        Assert.True(response.Success, response.Message);
        Assert.Equal(0, response.Data?.CreatedCount);
        Assert.Equal(0, response.Data?.UpdatedCount);
        Assert.Equal(1, response.Data?.SkippedCount);
        Assert.Contains(response.Data?.Errors ?? new List<string>(), error => error.Contains("存在 2 条记录"));

        var names = await _hqDb.Queryable<DIC_供应商信息表>()
            .Where(x => x.H供应商编码 == "AUS-DUP")
            .OrderBy(x => x.ID)
            .Select(x => x.H供应商名称)
            .ToListAsync();
        Assert.Equal(new[] { "旧记录 1", "旧记录 2" }, names);
    }

    [Fact]
    public async Task SyncToHqAsync_未选择供应商时拒绝写入()
    {
        var response = await CreateService().SyncToHqAsync(Array.Empty<string>());

        Assert.False(response.Success);
        Assert.Equal("SUPPLIER_REQUIRED", response.Code);
        Assert.Equal(0, await _hqDb.Queryable<DIC_供应商信息表>().CountAsync());
    }

    [Fact]
    public async Task SyncToHq_控制器只转发所选代码并要求管理角色()
    {
        var method = typeof(LocalSuppliersController).GetMethod(
            nameof(LocalSuppliersController.SyncToHq)
        )!;
        Assert.Equal(
            "sync-to-hq",
            method.GetCustomAttribute<HttpPostAttribute>()?.Template
        );
        Assert.Equal(
            "Admin,WarehouseManager",
            method.GetCustomAttribute<AuthorizeAttribute>()?.Roles
        );

        var service = new Mock<ILocalSuppliersReactService>(MockBehavior.Strict);
        service
            .Setup(x => x.SyncToHqAsync(
                It.Is<IReadOnlyCollection<string>>(codes =>
                    codes.SequenceEqual(new[] { "AUS-001", "AUS-002" })
                )
            ))
            .ReturnsAsync(
                ApiResponse<LocalSupplierSyncResultDto>.OK(
                    new LocalSupplierSyncResultDto
                    {
                        CreatedCount = 1,
                        UpdatedCount = 1,
                    }
                )
            );
        var controller = new LocalSuppliersController(
            service.Object,
            NullLogger<LocalSuppliersController>.Instance
        );

        var response = await controller.SyncToHq(
            new LocalSuppliersController.SyncToHqRequest
            {
                SupplierCodes = new List<string> { "AUS-001", "AUS-002" },
            }
        );

        Assert.IsType<OkObjectResult>(response);
        service.VerifyAll();
    }

    public void Dispose()
    {
        _localDb.Dispose();
        _hqDb.Dispose();
        _localConnection.Dispose();
        _hqConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        SqliteTempFileCleanup.DeleteIfExists(_hqDbPath);
    }

    private LocalSupplierReactService CreateService()
    {
        return new LocalSupplierReactService(
            CreateSqlSugarContext(_localDb),
            CreateHqSqlSugarContext(_hqDb),
            NullLogger<LocalSupplierReactService>.Instance
        );
    }

    private async Task SeedLocalSupplierAsync(
        string code,
        string name,
        string? contactPerson,
        string? email
    )
    {
        await _localDb.Insertable(new HBLocalSupplier
        {
            Guid = $"guid-{code}",
            LocalSupplierCode = code,
            Name = name,
            Status = 1,
            ContactPerson = contactPerson,
            Email = email,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private static DIC_供应商信息表 CreateHqSupplier(
        string guid,
        string code,
        string name
    )
    {
        return new DIC_供应商信息表
        {
            HGUID = guid,
            H供应商编码 = code,
            H供应商名称 = name,
            FGC_CreateDate = DateTime.UtcNow.AddDays(-1),
            FGC_LastModifyDate = DateTime.UtcNow.AddDays(-1),
        };
    }

    private static ConnectionConfig CreateConnectionConfig(string connectionString)
    {
        return new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        };
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(SqlSugarContext)
        );
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static HqSqlSugarContext CreateHqSqlSugarContext(ISqlSugarClient db)
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(HqSqlSugarContext)
        );
        typeof(HqSqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        typeof(HqSqlSugarContext)
            .GetField(
                "<Configuration>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!
            .SetValue(context, new ConfigurationBuilder().Build());
        return context;
    }
}
