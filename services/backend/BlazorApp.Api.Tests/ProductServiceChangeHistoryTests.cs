using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ProductServiceChangeHistoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;
    private readonly SqlSugarContext _context;

    public ProductServiceChangeHistoryTests()
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
        _db.CodeFirst.InitTables(
            typeof(Product),
            typeof(WarehouseProduct),
            typeof(DomesticProduct)
        );
        _db.Ado.ExecuteCommand(
            """
            CREATE TABLE WarehouseProductChangeHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EventGuid TEXT NOT NULL,
                ProductCode TEXT NOT NULL,
                Action TEXT NOT NULL,
                Source TEXT NOT NULL,
                SourceReference TEXT NULL,
                BatchGuid TEXT NULL,
                ActorUserGuid TEXT NULL,
                ActorName TEXT NOT NULL,
                ActorType TEXT NOT NULL,
                OccurredAtUtc TEXT NOT NULL,
                ChangesJson TEXT NOT NULL
            )
            """
        );
        _context = CreateSqlSugarContext(_db);
    }

    [Fact]
    public async Task ToggleActiveStatusAsync_写入旧API来源和真实操作人()
    {
        await SeedProductAsync();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.GetCurrentUsername()).Returns("legacy-user");
        currentUser.Setup(x => x.GetCurrentUserGuid()).Returns("legacy-user-guid");
        var historyService = new WarehouseProductChangeHistoryService(
            _context,
            NullLogger<WarehouseProductChangeHistoryService>.Instance,
            currentUser.Object
        );

        var changed = await CreateService(historyService, currentUser.Object)
            .ToggleActiveStatusAsync("UUID-P01");

        Assert.True(changed);
        var product = await _db.Queryable<Product>().SingleAsync(x => x.UUID == "UUID-P01");
        var history = await _db.Queryable<WarehouseProductChangeHistory>().SingleAsync();
        Assert.False(product.IsActive);
        Assert.Equal("legacy-user", product.UpdatedBy);
        Assert.Equal("ProductLegacyApi", history.Source);
        Assert.Equal("legacy-user-guid", history.ActorUserGuid);
        Assert.Equal("legacy-user", history.ActorName);
        using var changes = JsonDocument.Parse(history.ChangesJson);
        Assert.Contains(
            changes.RootElement.EnumerateArray(),
            item => item.GetProperty("fieldKey").GetString() == "isActive"
        );
    }

    [Fact]
    public async Task ToggleActiveStatusAsync_用户名回退System但有Guid时仍记录真实用户()
    {
        await SeedProductAsync();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.GetCurrentUsername()).Returns("System");
        currentUser.Setup(x => x.GetCurrentUserGuid()).Returns("legacy-guid-without-name");
        var historyService = new WarehouseProductChangeHistoryService(
            _context,
            NullLogger<WarehouseProductChangeHistoryService>.Instance,
            currentUser.Object
        );

        Assert.True(
            await CreateService(historyService, currentUser.Object)
                .ToggleActiveStatusAsync("UUID-P01")
        );

        var history = await _db.Queryable<WarehouseProductChangeHistory>().SingleAsync();
        Assert.Equal("legacy-guid-without-name", history.ActorUserGuid);
        Assert.Equal("legacy-guid-without-name", history.ActorName);
        Assert.Equal("User", history.ActorType);
    }

    [Fact]
    public async Task ToggleActiveStatusAsync_历史写入失败回滚主档()
    {
        await SeedProductAsync();
        var history = new Mock<IWarehouseProductChangeHistoryService>(MockBehavior.Strict);
        history
            .Setup(x =>
                x.CaptureSnapshotsAsync(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new Dictionary<string, WarehouseProductChangeSnapshotDto>());
        history
            .Setup(x =>
                x.RecordChangesAsync(
                    It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                    It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                    It.IsAny<WarehouseProductChangeHistoryContextDto>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new InvalidOperationException("history failed"));
        var currentUser = Mock.Of<ICurrentUserService>(x =>
            x.GetCurrentUsername() == "legacy-user"
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(history.Object, currentUser).ToggleActiveStatusAsync("UUID-P01")
        );

        var product = await _db.Queryable<Product>().SingleAsync(x => x.UUID == "UUID-P01");
        Assert.True(product.IsActive);
    }

    private async Task SeedProductAsync()
    {
        await _db.Insertable(new Product
        {
            UUID = "UUID-P01",
            ProductCode = "P01",
            ProductName = "Legacy Product",
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private ProductService CreateService(
        IWarehouseProductChangeHistoryService historyService,
        ICurrentUserService currentUserService
    ) =>
        new(
            _context,
            Mock.Of<IMapper>(),
            NullLogger<ProductService>.Instance,
            Mock.Of<ITranslationService>(),
            historyService,
            currentUserService
        );

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

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
