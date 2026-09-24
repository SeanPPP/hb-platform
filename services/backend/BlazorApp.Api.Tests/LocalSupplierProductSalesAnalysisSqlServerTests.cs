using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class LocalProductSalesAnalysisSqlServerFactAttribute : FactAttribute
{
    internal const string ConnectionEnvironmentVariable = "LSPA_SQLSERVER_TEST_CONNECTION";

    public LocalProductSalesAnalysisSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
        {
            Skip = $"未配置 {ConnectionEnvironmentVariable}，跳过本地商品分析 SQL Server 验证。";
        }
    }
}

/// <summary>
/// SQLite 测试走不到 SQL Server 专用的关键词谓词（UPPER + BIN2）与过滤索引直连路径，
/// 这里在一次性临时库里按生产形态建索引后验证语法、参数复用与匹配语义。
/// </summary>
[Trait("Category", "SQL")]
public sealed class LocalSupplierProductSalesAnalysisSqlServerTests : IDisposable
{
    private static readonly DateTime EndDate = DateTime.UtcNow.AddHours(10).Date.AddDays(-1);
    private static readonly DateTime StartDate = EndDate.AddDays(-30);

    private readonly string? _baseConnection;
    private readonly string _databaseName = $"lspa_it_{Guid.NewGuid():N}";
    private readonly SqlSugarClient? _db;

    public LocalSupplierProductSalesAnalysisSqlServerTests()
    {
        _baseConnection = Environment.GetEnvironmentVariable(
            LocalProductSalesAnalysisSqlServerFactAttribute.ConnectionEnvironmentVariable
        );
        if (string.IsNullOrWhiteSpace(_baseConnection))
        {
            return;
        }

        // 只在一次性临时库内读写；排序规则取不区分大小写的中文规则，贴近生产的 CI 语义。
        ExecuteOnMaster($"CREATE DATABASE [{_databaseName}] COLLATE Chinese_PRC_CI_AS");
        try
        {
            var builder = new SqlConnectionStringBuilder(_baseConnection) { InitialCatalog = _databaseName };
            _db = new SqlSugarClient(
                new ConnectionConfig
                {
                    ConnectionString = builder.ConnectionString,
                    DbType = DbType.SqlServer,
                    IsAutoCloseConnection = true,
                    InitKeyType = InitKeyType.Attribute,
                }
            );
            _db.CodeFirst.InitTables(
                typeof(Store),
                typeof(UserStore),
                typeof(HBLocalSupplier),
                typeof(Product),
                typeof(WarehouseCategory),
                typeof(StoreLocalSupplierInvoice),
                typeof(StoreLocalSupplierInvoiceDetails),
                typeof(ProductStoreDailySalesStatistic)
            );
            // 与生产脚本同名的过滤索引：健康探测据此启用“无重复编码直连”路径。
            // CodeFirst 建出的编码列是 varchar，过滤条件里的空串常量不能写成 N''（类型优先级更高会被拒绝）。
            _db.Ado.ExecuteCommand(
                @"SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
    CREATE NONCLUSTERED INDEX [IX_LSPSA_Product_ProductCode_UUID]
        ON [dbo].[Product] ([ProductCode], [UUID])
        INCLUDE ([LocalSupplierCode], [ItemNumber], [Barcode], [ProductName], [EnglishName], [ProductImage], [WarehouseCategoryGUID])
        WHERE [IsDeleted] = 0 AND [IsActive] = 1
          AND [LocalSupplierCode] IS NOT NULL AND [LocalSupplierCode] <> ''
          AND [ProductCode] IS NOT NULL AND [ProductCode] <> '';"
            );
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    [LocalProductSalesAnalysisSqlServerFact]
    public async Task 关键词查询在SqlServer上使用BIN2谓词且匹配语义不变()
    {
        var db = _db!;
        await SeedAsync(db);
        var sql = new List<string>();
        db.Aop.OnLogExecuting = (statement, _) => sql.Add(statement);
        var service = CreateService(db);

        // 小写关键词命中货号（大小写不敏感）
        var byItemNumber = await service.GetCandidatesAsync(CreateRequest("ki116996"), new List<string> { "B1" });
        Assert.True(byItemNumber.Success, byItemNumber.Message);
        Assert.Equal(new[] { "P001" }, byItemNumber.Data!.Items.Select(item => item.ProductCode));
        Assert.Equal(1, byItemNumber.Data.Total);

        // 中文名称与英文名称
        var byChineseName = await service.GetCandidatesAsync(CreateRequest("餐巾"), new List<string> { "B1" });
        Assert.Equal(new[] { "P001" }, byChineseName.Data!.Items.Select(item => item.ProductCode));
        var byEnglishName = await service.GetCandidatesAsync(CreateRequest("straws"), new List<string> { "B1" });
        Assert.Equal(new[] { "P002" }, byEnglishName.Data!.Items.Select(item => item.ProductCode));

        // 下划线必须按字面匹配，不能当作单字符通配
        var byUnderscore = await service.GetCandidatesAsync(CreateRequest("A_B"), new List<string> { "B1" });
        Assert.Equal(new[] { "P003" }, byUnderscore.Data!.Items.Select(item => item.ProductCode));

        // 已删除与非本地供应商商品不得出现
        var excluded = await service.GetCandidatesAsync(CreateRequest("GHOST"), new List<string> { "B1" });
        Assert.Empty(excluded.Data!.Items);

        var productStatements = sql
            .Where(statement => statement.Contains("[Product]", StringComparison.OrdinalIgnoreCase)
                && statement.Contains("@lspaKeywordPattern", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.NotEmpty(productStatements);
        Assert.All(
            productStatements,
            statement =>
            {
                Assert.Contains("AS nvarchar(4000))) COLLATE Latin1_General_100_BIN2 LIKE", statement);
                Assert.Contains("[IsDeleted] = 0 AND [IsActive] = 1", statement);
                Assert.DoesNotContain("@IsDeleted", statement, StringComparison.OrdinalIgnoreCase);
            }
        );
    }

    [LocalProductSalesAnalysisSqlServerFact]
    public async Task 关键词Bootstrap与全选汇总在SqlServer上返回完整分段()
    {
        var db = _db!;
        await SeedAsync(db);
        var service = CreateService(db);

        var request = CreateRequest("napkin");
        request.AutoSelectFirst = true;
        request.CandidatePageNumber = 1;
        request.CandidatePageSize = 20;
        request.SummaryPageNumber = 1;
        request.SummaryPageSize = 50;
        var bootstrap = await service.BootstrapAsync(request, new List<string> { "B1" });

        Assert.True(bootstrap.Success, bootstrap.Message);
        Assert.False(bootstrap.Data!.Partial, string.Join("; ", bootstrap.Data.SectionErrors.Select(item => $"{item.Key}={item.Value}")));
        Assert.Equal(new[] { "P001", "P004" }, bootstrap.Data.Candidates.Items.Select(item => item.ProductCode));
        Assert.Equal("P001", bootstrap.Data.CurrentProduct?.ProductCode);
        // 首屏自动选中首项时，选择范围是“全部筛选结果”，汇总覆盖两个命中商品。
        Assert.Equal(2, bootstrap.Data.Summary.Total);
        Assert.Equal(24m, bootstrap.Data.Summary.Totals.PurchaseQuantity);
        Assert.Equal(10m, bootstrap.Data.Summary.Totals.NetSalesQuantity);
        var currentRow = Assert.Single(bootstrap.Data.Summary.Items, item => item.ProductCode == "P001");
        Assert.Equal(7m, currentRow.NetSalesQuantity);

        // 关键词 + 全选筛选结果：同一参数化谓词会在进货、销量与主档三处派生表内复用
        var summary = await service.GetSummaryAsync(CreateRequest("napkin"), new List<string> { "B1" });
        Assert.True(summary.Success, summary.Message);
        Assert.Equal(2, summary.Data!.Total);
        Assert.Equal(24m, summary.Data.Totals.PurchaseQuantity);
        Assert.Equal(10m, summary.Data.Totals.NetSalesQuantity);
    }

    public void Dispose()
    {
        _db?.Dispose();
        if (string.IsNullOrWhiteSpace(_baseConnection))
        {
            return;
        }

        try
        {
            SqlConnection.ClearAllPools();
            ExecuteOnMaster(
                $"IF DB_ID(N'{_databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]; END"
            );
        }
        catch
        {
            // 临时库清理失败不影响断言结果；库名带随机后缀，可人工清理。
        }
    }

    private void ExecuteOnMaster(string sql)
    {
        var builder = new SqlConnectionStringBuilder(_baseConnection) { InitialCatalog = "master" };
        using var connection = new SqlConnection(builder.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static async Task SeedAsync(SqlSugarClient db)
    {
        await db.Insertable(new Store { StoreGUID = "guid-B1", StoreCode = "B1", StoreName = "Branch One", IsDeleted = false, IsActive = true }).ExecuteCommandAsync();
        await db.Insertable(new HBLocalSupplier { Guid = "guid-SUP", LocalSupplierCode = "SUP", Name = "Supplier One", Status = 1, IsDeleted = false }).ExecuteCommandAsync();
        await db.Insertable(
            new List<Product>
            {
                NewProduct("P001", "KI116996", "9328644116996", "环保餐巾纸 40x40", "Eco Napkin 40x40cm Pk50"),
                NewProduct("P002", "KI200000", "9328644200000", "纸吸管", "Paper Straws 6mm"),
                NewProduct("P003", "ZZ-A_B-01", "9328644300000", "下划线货号", "Underscore Item"),
                NewProduct("P004", "KI116990", "9328644116990", "环保纸巾 33x33", "Eco Napkin 33x33cm Pk100"),
                NewProduct("P005", "ZZ-AXB-01", "9328644500000", "干扰项", "Wildcard Decoy"),
                NewProduct("P006", "GHOST-1", "9328644600000", "已删除", "Ghost Deleted", isDeleted: true),
                NewProduct("P007", "GHOST-2", "9328644700000", "非本地供应商", "Ghost No Supplier", localSupplierCode: null),
            }
        ).ExecuteCommandAsync();
        await db.Insertable(new StoreLocalSupplierInvoice { InvoiceGUID = "h1", StoreCode = "B1", SupplierCode = "SUP", InboundDate = EndDate.AddDays(-5), IsDeleted = false }).ExecuteCommandAsync();
        await db.Insertable(new StoreLocalSupplierInvoiceDetails { DetailGUID = "d1", InvoiceGUID = "h1", StoreCode = "B1", ProductCode = "P001", Quantity = 24m, Amount = 26.64m, IsDeleted = false }).ExecuteCommandAsync();
        await db.Insertable(
            new List<ProductStoreDailySalesStatistic>
            {
                new() { Date = EndDate.AddDays(-3), BranchCode = "B1", SupplierCode = "SUP", ProductCode = "P001", TotalQuantity = 7, TotalAmount = 20.93m },
                new() { Date = EndDate.AddDays(-2), BranchCode = "B1", SupplierCode = "SUP", ProductCode = "P004", TotalQuantity = 3, TotalAmount = 11.97m },
                new() { Date = EndDate.AddDays(-2), BranchCode = "B1", SupplierCode = "SUP", ProductCode = "P002", TotalQuantity = 9, TotalAmount = 17.91m },
            }
        ).ExecuteCommandAsync();
    }

    private static Product NewProduct(
        string productCode,
        string itemNumber,
        string barcode,
        string name,
        string englishName,
        bool isDeleted = false,
        string? localSupplierCode = "SUP"
    ) => new()
    {
        UUID = $"uuid-{productCode}",
        ProductCode = productCode,
        ItemNumber = itemNumber,
        Barcode = barcode,
        ProductName = name,
        EnglishName = englishName,
        ProductImage = $"{productCode}.jpg",
        LocalSupplierCode = localSupplierCode,
        IsDeleted = isDeleted,
        IsActive = true,
    };

    private static LocalSupplierProductSalesAnalysisRequest CreateRequest(string keyword) => new()
    {
        Filter = new LocalSupplierProductSalesAnalysisFilterDto
        {
            StartDate = StartDate,
            EndDate = EndDate,
            Keyword = keyword,
        },
        Selection = new LocalSupplierProductSalesSelectionDto { Mode = "allFiltered" },
    };

    private static LocalSupplierProductSalesAnalysisService CreateService(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return new LocalSupplierProductSalesAnalysisService(
            context,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<LocalSupplierProductSalesAnalysisService>.Instance
        );
    }
}
