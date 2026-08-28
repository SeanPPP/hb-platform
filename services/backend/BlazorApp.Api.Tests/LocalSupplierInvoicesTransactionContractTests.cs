using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.LocalSupplierInvoices.Details.BatchUpdate;
using BlazorApp.Api.Features.LocalSupplierInvoices.Details.BatchUpdate.Infrastructure;
using BlazorApp.Api.Features.LocalSupplierInvoices.Details.BatchUpsert;
using BlazorApp.Api.Features.LocalSupplierInvoices.Details.BatchUpsert.Infrastructure;
using BlazorApp.Api.Features.LocalSupplierInvoices.Details.Paste;
using BlazorApp.Api.Features.LocalSupplierInvoices.Details.Paste.Infrastructure;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>用数据库触发器验证明细写入失败时不会留下半完成状态。</summary>
public sealed class LocalSupplierInvoicesTransactionContractTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public LocalSupplierInvoicesTransactionContractTests()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables(
            typeof(Store),
            typeof(HBLocalSupplier),
            typeof(StoreLocalSupplierInvoice),
            typeof(StoreLocalSupplierInvoiceDetails)
        );
    }

    [Fact]
    public void BatchUpsertDetailsAsync_事务内重读单头并使用SqlServer持锁语义()
    {
        var store = ReadApiFile(
            "Features/LocalSupplierInvoices/Details/BatchUpsert/Infrastructure/LocalSupplierInvoiceBatchUpsertTransactionStore.cs"
        );
        var validator = ReadApiFile(
            "Features/LocalSupplierInvoices/Details/BatchUpsert/LocalSupplierInvoiceBatchUpsertValidator.cs"
        );
        var plan = ReadApiFile(
            "Features/LocalSupplierInvoices/Details/BatchUpsert/LocalSupplierInvoiceBatchUpsertPlan.cs"
        );
        var lockStore = ReadApiFile(
            "Features/LocalSupplierInvoices/Details/Infrastructure/LocalSupplierInvoiceDetailsLockStore.cs"
        );
        var execute = ExtractMethod(store, "ExecuteAsync");
        var beginTransaction = execute.IndexOf("BeginTranAsync", StringComparison.Ordinal);
        var lockedHeaderRead = execute.IndexOf(
            "LockHeaderAsync",
            StringComparison.Ordinal
        );
        var commitTransaction = execute.IndexOf("CommitTranAsync", StringComparison.Ordinal);

        Assert.True(beginTransaction >= 0, "BatchUpsert 事务 Store 必须开启事务");
        Assert.True(lockedHeaderRead > beginTransaction, "最终单头读取必须发生在事务开始后");
        Assert.True(commitTransaction > lockedHeaderRead, "锁定读取必须保持在事务提交前");
        Assert.Contains("freshHeader.StoreCode", validator, StringComparison.Ordinal);
        Assert.Contains("freshHeader.SupplierCode", validator, StringComparison.Ordinal);
        Assert.Contains("initialHeader.StoreCode", validator, StringComparison.Ordinal);
        Assert.Contains("initialHeader.SupplierCode", validator, StringComparison.Ordinal);
        Assert.Contains("StoreCode = freshHeader.StoreCode", plan, StringComparison.Ordinal);
        Assert.Contains("SupplierCode = freshHeader.SupplierCode", plan, StringComparison.Ordinal);
        Assert.Contains(
            "FROM [dbo].[StoreLocalSupplierInvoice] WITH (UPDLOCK, HOLDLOCK)",
            lockStore,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "new SugarParameter(\"@InvoiceGuid\", invoiceGuid)",
            lockStore,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void BatchUpsertDetailsAsync_垂直切片分离纯校验计划与事务Store()
    {
        var handlerSource = ReadDetailsHandlerSource();
        var method = ExtractMethod(handlerSource, "BatchUpsertDetailsAsync");
        Assert.True(
            method.Split('\n').Length <= 180,
            $"BatchUpsertDetailsAsync 不能超过 180 行，当前 {method.Split('\n').Length} 行"
        );
        string[] forbiddenHandlerTokens =
        [
            ".Queryable<",
            ".Insertable(",
            ".Updateable(",
            ".Ado.",
            "BeginTran",
            "CommitTran",
            "RollbackTran",
            "SugarParameter",
            "new StoreLocalSupplierInvoiceDetails",
        ];
        Assert.DoesNotContain(
            forbiddenHandlerTokens,
            token => method.Contains(token, StringComparison.Ordinal)
        );

        const string validatorPath =
            "Features/LocalSupplierInvoices/Details/BatchUpsert/LocalSupplierInvoiceBatchUpsertValidator.cs";
        const string planPath =
            "Features/LocalSupplierInvoices/Details/BatchUpsert/LocalSupplierInvoiceBatchUpsertPlan.cs";
        const string storePath =
            "Features/LocalSupplierInvoices/Details/BatchUpsert/Infrastructure/LocalSupplierInvoiceBatchUpsertTransactionStore.cs";
        var missing = new[] { validatorPath, planPath, storePath }
            .Where(path => !File.Exists(ResolveApiFilePath(path)))
            .ToArray();
        Assert.True(
            missing.Length == 0,
            "BatchUpsert 缺少独立校验、计划或事务 Store：\n" + string.Join("\n", missing)
        );

        var validator = ReadApiFile(validatorPath);
        var plan = ReadApiFile(planPath);
        var store = ReadApiFile(storePath);
        string[] pureLayerForbiddenTokens =
        [
            "using SqlSugar",
            ".Queryable<",
            ".Insertable(",
            ".Updateable(",
            ".Ado.",
            "BeginTran",
            "CommitTran",
            "RollbackTran",
            "SugarParameter",
        ];
        Assert.DoesNotContain(
            pureLayerForbiddenTokens,
            token => validator.Contains(token, StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            pureLayerForbiddenTokens,
            token => plan.Contains(token, StringComparison.Ordinal)
        );
        Assert.Contains("ValidateFreshHeader", validator, StringComparison.Ordinal);
        Assert.Contains("ValidateDetailOwnership", validator, StringComparison.Ordinal);
        Assert.Contains("BuildWriteSet", plan, StringComparison.Ordinal);
        Assert.Contains("BeginTranAsync", store, StringComparison.Ordinal);
        Assert.Contains("CommitTranAsync", store, StringComparison.Ordinal);
        Assert.Contains("RollbackTranAsync", store, StringComparison.Ordinal);
        Assert.Contains("BuildWriteSet", store, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "new StoreLocalSupplierInvoiceDetails",
            store,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void BatchUpdateDetailsAsync_事务重读持锁并分离受限字段计划与Store()
    {
        const string validatorPath =
            "Features/LocalSupplierInvoices/Details/BatchUpdate/LocalSupplierInvoiceBatchUpdateValidator.cs";
        const string planPath =
            "Features/LocalSupplierInvoices/Details/BatchUpdate/LocalSupplierInvoiceBatchUpdatePlan.cs";
        const string storePath =
            "Features/LocalSupplierInvoices/Details/BatchUpdate/Infrastructure/LocalSupplierInvoiceBatchUpdateTransactionStore.cs";
        var missing = new[] { validatorPath, planPath, storePath }
            .Where(path => !File.Exists(ResolveApiFilePath(path)))
            .ToArray();
        Assert.True(
            missing.Length == 0,
            "BatchUpdate 缺少独立校验、计划或事务 Store：\n" + string.Join("\n", missing)
        );

        var handler = ExtractMethod(ReadDetailsHandlerSource(), "BatchUpdateDetailsAsync");
        Assert.True(
            handler.Split('\n').Length <= 100,
            $"BatchUpdateDetailsAsync 只能预检和委派，当前 {handler.Split('\n').Length} 行"
        );
        string[] forbiddenHandlerTokens =
        [
            ".Queryable<",
            ".Updateable(",
            ".Ado.",
            "BeginTran",
            "CommitTran",
            "RollbackTran",
            "new StoreLocalSupplierInvoiceDetails",
        ];
        Assert.DoesNotContain(
            forbiddenHandlerTokens,
            token => handler.Contains(token, StringComparison.Ordinal)
        );

        var validator = ReadApiFile(validatorPath);
        var plan = ReadApiFile(planPath);
        var store = ReadApiFile(storePath);
        var lockStore = ReadApiFile(
            "Features/LocalSupplierInvoices/Details/Infrastructure/LocalSupplierInvoiceDetailsLockStore.cs"
        );
        Assert.DoesNotContain("using SqlSugar", validator, StringComparison.Ordinal);
        Assert.DoesNotContain("using SqlSugar", plan, StringComparison.Ordinal);
        Assert.Contains("ValidateRequest", validator, StringComparison.Ordinal);
        Assert.Contains("ValidateFreshScope", validator, StringComparison.Ordinal);
        Assert.Contains("ApplyAllowedFieldsAsync", plan, StringComparison.Ordinal);
        Assert.Contains("PersistenceColumns", plan, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "nameof(StoreLocalSupplierInvoiceDetails.InvoiceGUID)",
            plan,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "nameof(StoreLocalSupplierInvoiceDetails.StoreCode)",
            plan,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "nameof(StoreLocalSupplierInvoiceDetails.SupplierCode)",
            plan,
            StringComparison.Ordinal
        );

        var execute = ExtractMethod(store, "ExecuteAsync");
        var begin = execute.IndexOf("BeginTranAsync", StringComparison.Ordinal);
        var freshHeader = execute.IndexOf("LockHeaderAsync", StringComparison.Ordinal);
        var freshDetails = execute.IndexOf("LockDetailsByGuidsAsync", StringComparison.Ordinal);
        var applyPlan = execute.IndexOf("ApplyAllowedFieldsAsync", StringComparison.Ordinal);
        var commit = execute.IndexOf("CommitTranAsync", StringComparison.Ordinal);
        Assert.True(begin >= 0, "BatchUpdate Store 必须开启事务");
        Assert.True(freshHeader > begin, "BatchUpdate fresh header 必须在事务开始后读取");
        Assert.True(freshDetails > freshHeader, "BatchUpdate fresh details 必须在 header 持锁后读取");
        Assert.True(applyPlan > freshDetails, "BatchUpdate 只能对锁内 fresh details 应用计划");
        Assert.True(commit > applyPlan, "BatchUpdate 锁必须保持至提交");
        Assert.Contains(
            "FROM [dbo].[StoreLocalSupplierInvoice] WITH (UPDLOCK, HOLDLOCK)",
            lockStore,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "FROM [dbo].[StoreLocalSupplierInvoiceDetails] WITH (UPDLOCK, HOLDLOCK)",
            lockStore,
            StringComparison.Ordinal
        );
        Assert.Contains("new SugarParameter", lockStore, StringComparison.Ordinal);
        Assert.Contains(".UpdateColumns(plan.PersistenceColumns)", store, StringComparison.Ordinal);
        Assert.Contains("if (headerUpdated != 1)", lockStore, StringComparison.Ordinal);
    }

    [Fact]
    public void PasteDetailsAsync_事务重读持锁并以FreshHeader构造明细()
    {
        const string validatorPath =
            "Features/LocalSupplierInvoices/Details/Paste/LocalSupplierInvoicePasteValidator.cs";
        const string planPath =
            "Features/LocalSupplierInvoices/Details/Paste/LocalSupplierInvoicePastePlan.cs";
        const string storePath =
            "Features/LocalSupplierInvoices/Details/Paste/Infrastructure/LocalSupplierInvoicePasteTransactionStore.cs";
        var missing = new[] { validatorPath, planPath, storePath }
            .Where(path => !File.Exists(ResolveApiFilePath(path)))
            .ToArray();
        Assert.True(
            missing.Length == 0,
            "Paste 缺少独立校验、计划或事务 Store：\n" + string.Join("\n", missing)
        );

        var handler = ExtractMethod(ReadDetailsHandlerSource(), "PasteDetailsAsync");
        Assert.True(
            handler.Split('\n').Length <= 100,
            $"PasteDetailsAsync 只能预检和委派，当前 {handler.Split('\n').Length} 行"
        );
        string[] forbiddenHandlerTokens =
        [
            ".Queryable<",
            ".Deleteable<",
            ".Insertable(",
            ".Updateable<",
            ".Ado.",
            "BeginTran",
            "CommitTran",
            "RollbackTran",
            "new StoreLocalSupplierInvoiceDetails",
        ];
        Assert.DoesNotContain(
            forbiddenHandlerTokens,
            token => handler.Contains(token, StringComparison.Ordinal)
        );

        var validator = ReadApiFile(validatorPath);
        var plan = ReadApiFile(planPath);
        var store = ReadApiFile(storePath);
        var lockStore = ReadApiFile(
            "Features/LocalSupplierInvoices/Details/Infrastructure/LocalSupplierInvoiceDetailsLockStore.cs"
        );
        Assert.DoesNotContain("using SqlSugar", validator, StringComparison.Ordinal);
        Assert.DoesNotContain("using SqlSugar", plan, StringComparison.Ordinal);
        Assert.Contains("ValidateFreshHeader", validator, StringComparison.Ordinal);
        Assert.Contains("BuildRows", plan, StringComparison.Ordinal);
        Assert.Contains("StoreCode = freshHeader.StoreCode", plan, StringComparison.Ordinal);
        Assert.Contains("SupplierCode = freshHeader.SupplierCode", plan, StringComparison.Ordinal);

        var execute = ExtractMethod(store, "ExecuteAsync");
        var begin = execute.IndexOf("BeginTranAsync", StringComparison.Ordinal);
        var freshHeader = execute.IndexOf("LockHeaderAsync", StringComparison.Ordinal);
        var freshDetails = execute.IndexOf("LockAllDetailsAsync", StringComparison.Ordinal);
        var buildRows = execute.IndexOf("BuildRows", StringComparison.Ordinal);
        var deleteRows = execute.IndexOf("Deleteable<StoreLocalSupplierInvoiceDetails>", StringComparison.Ordinal);
        var insertRows = execute.IndexOf("Insertable", StringComparison.Ordinal);
        var commit = execute.IndexOf("CommitTranAsync", StringComparison.Ordinal);
        Assert.True(begin >= 0, "Paste Store 必须开启事务");
        Assert.True(freshHeader > begin, "Paste fresh header 必须在事务开始后读取");
        Assert.True(freshDetails > freshHeader, "Paste 必须在 header 后锁定当前 details");
        Assert.True(buildRows > freshDetails, "Paste rows 必须在锁内用 fresh header 构造");
        Assert.True(deleteRows > buildRows, "Paste replace 删除必须位于同一事务");
        Assert.True(insertRows > buildRows, "Paste 插入必须位于同一事务");
        Assert.True(commit > insertRows, "Paste 锁必须保持至提交");
        Assert.Contains(
            "FROM [dbo].[StoreLocalSupplierInvoice] WITH (UPDLOCK, HOLDLOCK)",
            lockStore,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "new SugarParameter(\"@InvoiceGuid\", invoiceGuid)",
            lockStore,
            StringComparison.Ordinal
        );
        Assert.Contains("if (headerUpdated != 1)", lockStore, StringComparison.Ordinal);
    }

    [Fact]
    public void 所有明细写入口_统一为Invoice进程锁后事务内先锁Header再锁Details()
    {
        const string processLockPath =
            "Features/LocalSupplierInvoices/Details/Infrastructure/LocalSupplierInvoiceDetailsMutationLock.cs";
        const string lockStorePath =
            "Features/LocalSupplierInvoices/Details/Infrastructure/LocalSupplierInvoiceDetailsLockStore.cs";
        const string mutationStorePath =
            "Features/LocalSupplierInvoices/Details/Mutations/Infrastructure/LocalSupplierInvoiceDetailsMutationTransactionStore.cs";
        var missing = new[] { processLockPath, lockStorePath, mutationStorePath }
            .Where(path => !File.Exists(ResolveApiFilePath(path)))
            .ToArray();
        Assert.True(
            missing.Length == 0,
            "缺少统一 invoice 锁或明细事务 Store：\n" + string.Join("\n", missing)
        );

        var processLock = ReadApiFile(processLockPath);
        var lockStore = ReadApiFile(lockStorePath);
        var mutationStore = ReadApiFile(mutationStorePath);
        Assert.Contains("AcquireProcessAsync", processLock, StringComparison.Ordinal);
        Assert.Contains("SemaphoreSlim", processLock, StringComparison.Ordinal);
        Assert.Contains(
            "FROM [dbo].[StoreLocalSupplierInvoice] WITH (UPDLOCK, HOLDLOCK)",
            lockStore,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "FROM [dbo].[StoreLocalSupplierInvoiceDetails] WITH (UPDLOCK, HOLDLOCK)",
            lockStore,
            StringComparison.Ordinal
        );
        Assert.Contains("new SugarParameter(\"@InvoiceGuid\"", lockStore, StringComparison.Ordinal);
        Assert.Contains("if (headerUpdated != 1)", lockStore, StringComparison.Ordinal);

        AssertUnifiedActionLockOrder(
            mutationStore,
            "ExecuteUpdateActionAsync",
            "LockDetailsByGuidsAsync"
        );
        AssertUnifiedActionLockOrder(
            mutationStore,
            "ExecuteBatchUpdateActionAsync",
            "LockDetailsByGuidsAsync"
        );
        AssertUnifiedLockOrder(
            mutationStore,
            "ExecuteDeleteAsync",
            "LockDetailsByGuidsAsync"
        );

        var batchUpsertStore = ReadApiFile(
            "Features/LocalSupplierInvoices/Details/BatchUpsert/Infrastructure/LocalSupplierInvoiceBatchUpsertTransactionStore.cs"
        );
        var batchUpdateStore = ReadApiFile(
            "Features/LocalSupplierInvoices/Details/BatchUpdate/Infrastructure/LocalSupplierInvoiceBatchUpdateTransactionStore.cs"
        );
        var pasteStore = ReadApiFile(
            "Features/LocalSupplierInvoices/Details/Paste/Infrastructure/LocalSupplierInvoicePasteTransactionStore.cs"
        );
        AssertUnifiedLockOrder(batchUpsertStore, "ExecuteAsync", "LockDetailsByGuidsAsync");
        AssertUnifiedLockOrder(batchUpdateStore, "ExecuteAsync", "LockDetailsByGuidsAsync");
        AssertUnifiedLockOrder(pasteStore, "ExecuteAsync", "LockAllDetailsAsync");

        var handler = ReadDetailsHandlerSource();
        foreach (
            var methodName in new[]
            {
                "UpdateDetailActionAsync",
                "BatchUpdateDetailActionAsync",
                "DeleteDetailsAsync",
            }
        )
        {
            var method = ExtractMethod(handler, methodName);
            Assert.DoesNotContain(".Queryable<", method, StringComparison.Ordinal);
            Assert.DoesNotContain(".Updateable<", method, StringComparison.Ordinal);
            Assert.DoesNotContain(".Ado.", method, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BatchUpsertDetailsAsync_更新仅包含请求字段且不回写归属或ActivityType()
    {
        var plan = ReadApiFile(
            "Features/LocalSupplierInvoices/Details/BatchUpsert/LocalSupplierInvoiceBatchUpsertPlan.cs"
        );
        var store = ReadApiFile(
            "Features/LocalSupplierInvoices/Details/BatchUpsert/Infrastructure/LocalSupplierInvoiceBatchUpsertTransactionStore.cs"
        );

        Assert.Contains("BuildUpdateColumns", plan, StringComparison.Ordinal);
        Assert.Contains("UpdateColumns(updateColumns)", store, StringComparison.Ordinal);
        Assert.DoesNotContain("Updateable(writeSet.Updates)", store, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "nameof(StoreLocalSupplierInvoiceDetails.InvoiceGUID)",
            plan,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "nameof(StoreLocalSupplierInvoiceDetails.StoreCode)",
            plan,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "nameof(StoreLocalSupplierInvoiceDetails.SupplierCode)",
            plan,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_SQLite初读后在事务内重读Header和Details()
    {
        await SeedInvoiceAsync("batch-update-fresh-read", totalAmount: 2m);
        await SeedDetailAsync(
            "batch-update-fresh-detail",
            "batch-update-fresh-read",
            "FRESH",
            2m
        );
        var headerReadStates = new List<bool>();
        var detailReadStates = new List<bool>();
        _db.Aop.OnLogExecuting = (sql, _) =>
        {
            if (!sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                return;

            if (
                sql.Contains("StoreLocalSupplierInvoiceDetails", StringComparison.OrdinalIgnoreCase)
            )
            {
                detailReadStates.Add(_db.Ado.Transaction != null);
            }
            else if (
                sql.Contains("StoreLocalSupplierInvoice", StringComparison.OrdinalIgnoreCase)
            )
            {
                headerReadStates.Add(_db.Ado.Transaction != null);
            }
        };

        ApiResponse<BatchResultDto> result;
        try
        {
            result = await CreateService().BatchUpdateDetailsAsync(
                "batch-update-fresh-read",
                new BatchUpdateInvoiceDetailsRequest
                {
                    Items = new List<InvoiceDetailUpsertItemDto>
                    {
                        new() { DetailGUID = "batch-update-fresh-detail" },
                    },
                    EditFields = new UpdateToStorePricesFields
                    {
                        UpdateRetailPrice = true,
                        RetailPrice = 5m,
                    },
                },
                "tester"
            );
        }
        finally
        {
            _db.Aop.OnLogExecuting = null;
        }

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { false, true }, headerReadStates);
        Assert.True(detailReadStates.Count >= 2, "必须同时观察到初读与事务内 fresh details 读取");
        Assert.Equal(new[] { false, true }, detailReadStates.Take(2));
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("store")]
    [InlineData("supplier")]
    public async Task BatchUpdateTransactionStore_旧Scope失效时拒绝且不回写(string change)
    {
        const string invoiceGuid = "batch-update-stale-scope";
        const string detailGuid = "batch-update-stale-detail";
        await SeedInvoiceAsync(invoiceGuid, totalAmount: 3m);
        await SeedDetailAsync(detailGuid, invoiceGuid, "STALE", 3m);
        var store = new LocalSupplierInvoiceBatchUpdateTransactionStore(_db);
        var initialState = await store.LoadInitialStateAsync(
            invoiceGuid,
            new[] { detailGuid }
        );

        if (change == "deleted")
        {
            await _db.Updateable<StoreLocalSupplierInvoice>()
                .SetColumns(header => header.IsDeleted == true)
                .Where(header => header.InvoiceGUID == invoiceGuid)
                .ExecuteCommandAsync();
            await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(detail => detail.IsDeleted == true)
                .Where(detail => detail.DetailGUID == detailGuid)
                .ExecuteCommandAsync();
        }
        else if (change == "store")
        {
            await _db.Updateable<StoreLocalSupplierInvoice>()
                .SetColumns(header => header.StoreCode == "S02")
                .Where(header => header.InvoiceGUID == invoiceGuid)
                .ExecuteCommandAsync();
            await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(detail => detail.StoreCode == "S02")
                .Where(detail => detail.DetailGUID == detailGuid)
                .ExecuteCommandAsync();
        }
        else
        {
            await _db.Updateable<StoreLocalSupplierInvoice>()
                .SetColumns(header => header.SupplierCode == "SUP02")
                .Where(header => header.InvoiceGUID == invoiceGuid)
                .ExecuteCommandAsync();
            await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(detail => detail.SupplierCode == "SUP02")
                .Where(detail => detail.DetailGUID == detailGuid)
                .ExecuteCommandAsync();
        }

        var plan = LocalSupplierInvoiceBatchUpdatePlan.Create(
            invoiceGuid,
            new[] { detailGuid },
            new UpdateToStorePricesFields
            {
                UpdateRetailPrice = true,
                RetailPrice = 99m,
            },
            "tester",
            DateTime.UtcNow
        );
        var execution = await store.ExecuteAsync(
            initialState,
            plan,
            static (_, _, _) => Task.CompletedTask
        );

        Assert.NotNull(execution.Failure);
        Assert.Equal("BATCH_UPDATE_ERROR", execution.Failure.ErrorCode);
        Assert.Equal(0, execution.Updated);
        var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .SingleAsync(item => item.DetailGUID == detailGuid);
        Assert.Null(detail.RetailPrice);
        Assert.Equal(3m, detail.Amount);
        Assert.Equal(change == "deleted", detail.IsDeleted);
        Assert.Equal(change == "store" ? "S02" : "S01", detail.StoreCode);
        Assert.Equal(change == "supplier" ? "SUP02" : "SUP01", detail.SupplierCode);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_单头金额更新失败时回滚允许字段()
    {
        const string invoiceGuid = "batch-update-rollback";
        const string detailGuid = "batch-update-rollback-detail";
        await SeedInvoiceAsync(invoiceGuid, totalAmount: 2m);
        await SeedDetailAsync(detailGuid, invoiceGuid, "ROLLBACK", 2m);
        await RejectHeaderUpdateAsync(invoiceGuid);

        var result = await CreateService().BatchUpdateDetailsAsync(
            invoiceGuid,
            new BatchUpdateInvoiceDetailsRequest
            {
                Items = new List<InvoiceDetailUpsertItemDto>
                {
                    new() { DetailGUID = detailGuid },
                },
                EditFields = new UpdateToStorePricesFields
                {
                    UpdatePurchasePrice = true,
                    PurchasePrice = 9m,
                },
            },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("BATCH_UPDATE_ERROR", result.ErrorCode);
        var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .SingleAsync(item => item.DetailGUID == detailGuid);
        Assert.Equal(2m, detail.PurchasePrice);
        Assert.Equal(2m, detail.Amount);
        Assert.Equal(
            2m,
            await _db.Queryable<StoreLocalSupplierInvoice>()
                .Where(header => header.InvoiceGUID == invoiceGuid)
                .Select(header => header.TotalAmount)
                .SingleAsync()
        );
    }

    [Fact]
    public async Task PasteDetailsAsync_SQLite初读后在事务内重读Header并写入FreshScope()
    {
        const string invoiceGuid = "paste-fresh-read";
        await SeedInvoiceAsync(invoiceGuid, storeCode: "S01", supplierCode: "SUP01");
        var headerReadStates = new List<bool>();
        _db.Aop.OnLogExecuting = (sql, _) =>
        {
            if (
                sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("StoreLocalSupplierInvoice", StringComparison.OrdinalIgnoreCase)
                && !sql.Contains(
                    "StoreLocalSupplierInvoiceDetails",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                headerReadStates.Add(_db.Ado.Transaction != null);
            }
        };

        ApiResponse<BatchResultDto> result;
        try
        {
            result = await CreateService().PasteDetailsAsync(
                new PasteDetailsRequest
                {
                    InvoiceGuid = invoiceGuid,
                    Mode = "append",
                    Items = new List<PastedDetailItemDto>
                    {
                        new()
                        {
                            ItemNumber = "FRESH-PASTE",
                            Barcode = "9300000000002",
                            Quantity = 2,
                            PurchasePrice = 3m,
                        },
                    },
                },
                "tester"
            );
        }
        finally
        {
            _db.Aop.OnLogExecuting = null;
        }

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { false, true }, headerReadStates);
        var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .SingleAsync(item => item.InvoiceGUID == invoiceGuid);
        Assert.Equal("S01", detail.StoreCode);
        Assert.Equal("SUP01", detail.SupplierCode);
        Assert.Equal(6m, detail.Amount);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("store")]
    [InlineData("supplier")]
    public async Task PasteTransactionStore_旧HeaderScope失效时拒绝且不删除插入(string change)
    {
        const string invoiceGuid = "paste-stale-scope";
        const string oldDetailGuid = "paste-stale-old";
        await SeedInvoiceAsync(invoiceGuid, totalAmount: 3m);
        await SeedDetailAsync(oldDetailGuid, invoiceGuid, "OLD", 3m);
        var store = new LocalSupplierInvoicePasteTransactionStore(_db);
        var initialHeader = await store.LoadInitialHeaderAsync(invoiceGuid);
        Assert.NotNull(initialHeader);

        if (change == "deleted")
        {
            await _db.Updateable<StoreLocalSupplierInvoice>()
                .SetColumns(header => header.IsDeleted == true)
                .Where(header => header.InvoiceGUID == invoiceGuid)
                .ExecuteCommandAsync();
        }
        else if (change == "store")
        {
            await _db.Updateable<StoreLocalSupplierInvoice>()
                .SetColumns(header => header.StoreCode == "S02")
                .Where(header => header.InvoiceGUID == invoiceGuid)
                .ExecuteCommandAsync();
        }
        else
        {
            await _db.Updateable<StoreLocalSupplierInvoice>()
                .SetColumns(header => header.SupplierCode == "SUP02")
                .Where(header => header.InvoiceGUID == invoiceGuid)
                .ExecuteCommandAsync();
        }

        var plan = LocalSupplierInvoicePastePlan.Create(
            new PasteDetailsRequest
            {
                InvoiceGuid = invoiceGuid,
                Mode = "replace",
                Items = new List<PastedDetailItemDto>
                {
                    new()
                    {
                        ItemNumber = "MUST-NOT-WRITE",
                        Barcode = "9300000000003",
                        Quantity = 1,
                        PurchasePrice = 9m,
                    },
                },
            },
            "tester",
            DateTime.UtcNow,
            static _ => false,
            static item => item,
            static (_, _) => null
        );
        var execution = await store.ExecuteAsync(initialHeader!, plan);

        Assert.NotNull(execution.Failure);
        Assert.Equal("PASTE_ERROR", execution.Failure.ErrorCode);
        Assert.Equal(0, execution.Inserted);
        Assert.True(
            await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .AnyAsync(detail => detail.DetailGUID == oldDetailGuid)
        );
        Assert.False(
            await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .AnyAsync(detail => detail.ItemNumber == "MUST-NOT-WRITE")
        );
        Assert.Equal(
            3m,
            await _db.Queryable<StoreLocalSupplierInvoice>()
                .Where(header => header.InvoiceGUID == invoiceGuid)
                .Select(header => header.TotalAmount)
                .SingleAsync()
        );
    }

    [Fact]
    public async Task BatchUpsertDetailsAsync_SQLite外层预检后在事务内重读单头()
    {
        await SeedInvoiceAsync("fresh-header");
        var headerReadTransactionStates = new List<bool>();
        _db.Aop.OnLogExecuting = (sql, _) =>
        {
            if (
                sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("StoreLocalSupplierInvoice", StringComparison.OrdinalIgnoreCase)
                && !sql.Contains("StoreLocalSupplierInvoiceDetails", StringComparison.OrdinalIgnoreCase)
            )
            {
                headerReadTransactionStates.Add(_db.Ado.Transaction != null);
            }
        };

        ApiResponse<BatchResultDto> result;
        try
        {
            result = await CreateService().BatchUpsertDetailsAsync(
                "fresh-header",
                new List<InvoiceDetailUpsertItemDto>
                {
                    new()
                    {
                        ItemNumber = "FRESH",
                        Amount = 5m,
                    },
                },
                "tester"
            );
        }
        finally
        {
            _db.Aop.OnLogExecuting = null;
        }

        Assert.True(result.Success);
        Assert.Equal(new[] { false, true }, headerReadTransactionStates);
        var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .SingleAsync(x => x.InvoiceGUID == "fresh-header");
        Assert.Equal("S01", detail.StoreCode);
        Assert.Equal("SUP01", detail.SupplierCode);
    }

    [Theory]
    [InlineData(false, "VALIDATION_ERROR")]
    [InlineData(true, "NOT_FOUND")]
    public async Task BatchUpsertTransactionStore_旧单头快照失效时事务内拒绝且不写明细(
        bool markDeleted,
        string expectedErrorCode
    )
    {
        const string invoiceGuid = "stale-header";
        await SeedInvoiceAsync(
            invoiceGuid,
            storeCode: markDeleted ? "S01" : "S02",
            supplierCode: markDeleted ? "SUP01" : "SUP02",
            totalAmount: 9m
        );
        if (markDeleted)
        {
            await _db.Updateable<StoreLocalSupplierInvoice>()
                .SetColumns(header => header.IsDeleted == true)
                .Where(header => header.InvoiceGUID == invoiceGuid)
                .ExecuteCommandAsync();
        }

        // 显式旧快照等价于外层预检后并发发生归属变更或软删。
        var staleHeader = new StoreLocalSupplierInvoice
        {
            InvoiceGUID = invoiceGuid,
            StoreCode = "S01",
            SupplierCode = "SUP01",
            TotalAmount = 9m,
            IsDeleted = false,
        };
        var plan = LocalSupplierInvoiceBatchUpsertPlan.Create(
            invoiceGuid,
            new List<InvoiceDetailUpsertItemDto>
            {
                new()
                {
                    ItemNumber = "MUST-NOT-WRITE",
                    Amount = 5m,
                },
            },
            "tester",
            DateTime.UtcNow,
            static (_, _) => null
        );
        var transactionStore = new LocalSupplierInvoiceBatchUpsertTransactionStore(_db);

        var result = await transactionStore.ExecuteAsync(staleHeader, plan);

        Assert.NotNull(result.Failure);
        Assert.Equal(expectedErrorCode, result.Failure.ErrorCode);
        Assert.Equal(0, result.Inserted);
        Assert.Equal(0, result.Updated);
        Assert.False(
            await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .AnyAsync(detail => detail.InvoiceGUID == invoiceGuid)
        );
        Assert.Equal(
            9m,
            await _db.Queryable<StoreLocalSupplierInvoice>()
                .Where(header => header.InvoiceGUID == invoiceGuid)
                .Select(header => header.TotalAmount)
                .SingleAsync()
        );
    }

    [Fact]
    public async Task BatchUpsertDetailsAsync_单头不存在时保持NotFound且不写明细()
    {
        var result = await CreateService().BatchUpsertDetailsAsync(
            "missing-invoice",
            new List<InvoiceDetailUpsertItemDto>
            {
                new()
                {
                    ItemNumber = "MUST-NOT-WRITE",
                    Amount = 5m,
                },
            },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
        Assert.False(await _db.Queryable<StoreLocalSupplierInvoiceDetails>().AnyAsync());
    }

    [Fact]
    public async Task BatchUpsertDetailsAsync_跨单据DetailGuid时拒绝且不修改原明细()
    {
        await SeedInvoiceAsync("target-invoice", totalAmount: 10m);
        await SeedInvoiceAsync("other-invoice");
        await SeedDetailAsync("foreign-detail", "other-invoice", "FOREIGN", 2m);

        var result = await CreateService().BatchUpsertDetailsAsync(
            "target-invoice",
            new List<InvoiceDetailUpsertItemDto>
            {
                new()
                {
                    DetailGUID = "foreign-detail",
                    ItemNumber = "CHANGED",
                    Amount = 99m,
                },
            },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        var foreignDetail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .SingleAsync(x => x.DetailGUID == "foreign-detail");
        Assert.Equal("FOREIGN", foreignDetail.ItemNumber);
        Assert.Equal(2m, foreignDetail.Amount);
        Assert.Equal(
            10m,
            await _db.Queryable<StoreLocalSupplierInvoice>()
                .Where(x => x.InvoiceGUID == "target-invoice")
                .Select(x => x.TotalAmount)
                .SingleAsync()
        );
    }

    [Fact]
    public async Task BatchUpsertDetailsAsync_跨门店DetailGuid时拒绝且不修改原明细()
    {
        await SeedInvoiceAsync("store-invoice", storeCode: "S01", totalAmount: 10m);
        await SeedDetailAsync(
            "other-store-detail",
            "store-invoice",
            "FOREIGN-STORE",
            3m,
            storeCode: "S02"
        );

        var result = await CreateService().BatchUpsertDetailsAsync(
            "store-invoice",
            new List<InvoiceDetailUpsertItemDto>
            {
                new()
                {
                    DetailGUID = "other-store-detail",
                    ItemNumber = "CHANGED",
                    Amount = 88m,
                },
            },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        var foreignDetail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .SingleAsync(x => x.DetailGUID == "other-store-detail");
        Assert.Equal("FOREIGN-STORE", foreignDetail.ItemNumber);
        Assert.Equal(3m, foreignDetail.Amount);
        Assert.Equal(
            10m,
            await _db.Queryable<StoreLocalSupplierInvoice>()
                .Where(x => x.InvoiceGUID == "store-invoice")
                .Select(x => x.TotalAmount)
                .SingleAsync()
        );
    }

    [Fact]
    public async Task BatchUpsertDetailsAsync_跨供应商DetailGuid时拒绝且不修改原明细()
    {
        await SeedInvoiceAsync("supplier-invoice", supplierCode: "SUP01", totalAmount: 10m);
        await SeedDetailAsync(
            "other-supplier-detail",
            "supplier-invoice",
            "FOREIGN-SUPPLIER",
            3m,
            supplierCode: "SUP02"
        );

        var result = await CreateService().BatchUpsertDetailsAsync(
            "supplier-invoice",
            new List<InvoiceDetailUpsertItemDto>
            {
                new()
                {
                    DetailGUID = "other-supplier-detail",
                    ItemNumber = "CHANGED",
                    Amount = 88m,
                },
            },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        var foreignDetail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .SingleAsync(x => x.DetailGUID == "other-supplier-detail");
        Assert.Equal("FOREIGN-SUPPLIER", foreignDetail.ItemNumber);
        Assert.Equal(3m, foreignDetail.Amount);
    }

    [Fact]
    public async Task BatchUpsertDetailsAsync_单头金额更新失败时回滚全部明细写入()
    {
        await SeedInvoiceAsync("batch-rollback", totalAmount: 2m);
        await SeedDetailAsync("existing-detail", "batch-rollback", "OLD", 2m);
        await RejectHeaderUpdateAsync("batch-rollback");

        var result = await CreateService().BatchUpsertDetailsAsync(
            "batch-rollback",
            new List<InvoiceDetailUpsertItemDto>
            {
                new()
                {
                    DetailGUID = "existing-detail",
                    ItemNumber = "UPDATED",
                    Amount = 9m,
                },
                new()
                {
                    ItemNumber = "NEW",
                    Amount = 4m,
                },
            },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("BATCH_UPSERT_ERROR", result.ErrorCode);
        var existingDetail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .SingleAsync(x => x.DetailGUID == "existing-detail");
        Assert.Equal("OLD", existingDetail.ItemNumber);
        Assert.Equal(2m, existingDetail.Amount);
        Assert.False(await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .AnyAsync(x => x.InvoiceGUID == "batch-rollback" && x.ItemNumber == "NEW"));
        Assert.Equal(
            2m,
            await _db.Queryable<StoreLocalSupplierInvoice>()
                .Where(x => x.InvoiceGUID == "batch-rollback")
                .Select(x => x.TotalAmount)
                .SingleAsync()
        );
    }

    [Fact]
    public async Task BatchUpsertDetailsAsync_成功时原子提交明细与单头金额()
    {
        await SeedInvoiceAsync("batch-success", totalAmount: 2m);
        await SeedDetailAsync("success-existing", "batch-success", "OLD", 2m);

        var result = await CreateService().BatchUpsertDetailsAsync(
            "batch-success",
            new List<InvoiceDetailUpsertItemDto>
            {
                new()
                {
                    DetailGUID = "success-existing",
                    ItemNumber = "UPDATED",
                    Amount = 5m,
                },
                new()
                {
                    ItemNumber = "NEW",
                    Amount = 7m,
                },
            },
            "tester"
        );

        Assert.True(result.Success);
        Assert.Equal(1, result.Data?.Inserted);
        Assert.Equal(1, result.Data?.Updated);
        var details = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .Where(x => x.InvoiceGUID == "batch-success" && x.IsDeleted == false)
            .OrderBy(x => x.ItemNumber)
            .ToListAsync();
        Assert.Equal(2, details.Count);
        Assert.Contains(details, x => x.ItemNumber == "UPDATED" && x.Amount == 5m);
        Assert.Contains(details, x => x.ItemNumber == "NEW" && x.Amount == 7m);
        Assert.Equal(
            12m,
            await _db.Queryable<StoreLocalSupplierInvoice>()
                .Where(x => x.InvoiceGUID == "batch-success")
                .Select(x => x.TotalAmount)
                .SingleAsync()
        );
    }

    [Fact]
    public async Task PasteDetailsAsync_单头金额更新失败时回滚覆盖删除和插入()
    {
        await SeedInvoiceAsync("paste-rollback");
        await SeedDetailAsync("old-detail", "paste-rollback", "OLD", 2m);
        await RejectHeaderUpdateAsync("paste-rollback");

        var result = await CreateService().PasteDetailsAsync(
            new PasteDetailsRequest
            {
                InvoiceGuid = "paste-rollback",
                Mode = "replace",
                Items = { new PastedDetailItemDto { ItemNumber = "NEW", Barcode = "9300000000001", Quantity = 3, PurchasePrice = 4m } },
            },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("PASTE_ERROR", result.ErrorCode);
        Assert.True(await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .AnyAsync(x => x.DetailGUID == "old-detail" && x.IsDeleted == false));
        Assert.False(await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .AnyAsync(x => x.ItemNumber == "NEW"));
    }

    [Fact]
    public async Task DeleteDetailsAsync_单头金额更新失败时回滚软删()
    {
        await SeedInvoiceAsync("delete-rollback");
        await SeedDetailAsync("delete-detail", "delete-rollback", "DELETE", 5m);
        await RejectHeaderUpdateAsync("delete-rollback");

        var result = await CreateService().DeleteDetailsAsync(
            "delete-rollback",
            new List<string> { "delete-detail" },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("DELETE_ERROR", result.ErrorCode);
        var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .SingleAsync(x => x.DetailGUID == "delete-detail");
        Assert.False(detail.IsDeleted);
    }

    [Theory]
    [InlineData("single")]
    [InlineData("batch")]
    [InlineData("delete")]
    public async Task Action与Delete_SQLite事务内按Header再Details顺序读取(string operation)
    {
        var invoiceGuid = $"lock-order-{operation}";
        var detailGuid = $"lock-order-detail-{operation}";
        await SeedInvoiceAsync(invoiceGuid, totalAmount: 5m);
        await SeedDetailAsync(detailGuid, invoiceGuid, "LOCK", 5m);
        var reads = new List<string>();
        _db.Aop.OnLogExecuting = (sql, _) =>
        {
            if (!sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                return;

            if (sql.Contains("StoreLocalSupplierInvoiceDetails", StringComparison.OrdinalIgnoreCase))
            {
                reads.Add($"details:{_db.Ado.Transaction != null}");
            }
            else if (sql.Contains("StoreLocalSupplierInvoice", StringComparison.OrdinalIgnoreCase))
            {
                reads.Add($"header:{_db.Ado.Transaction != null}");
            }
        };

        try
        {
            if (operation == "single")
            {
                var result = await CreateService().UpdateDetailActionAsync(
                    invoiceGuid,
                    detailGuid,
                    1
                );
                Assert.True(result.Success, result.Message);
            }
            else if (operation == "batch")
            {
                var result = await CreateService().BatchUpdateDetailActionAsync(
                    invoiceGuid,
                    new BatchUpdateDetailActionRequest
                    {
                        DetailGuids = new List<string> { detailGuid },
                        Action = 1,
                    }
                );
                Assert.True(result.Success, result.Message);
            }
            else
            {
                var result = await CreateService().DeleteDetailsAsync(
                    invoiceGuid,
                    new List<string> { detailGuid },
                    "tester"
                );
                Assert.True(result.Success, result.Message);
            }
        }
        finally
        {
            _db.Aop.OnLogExecuting = null;
        }

        var headerIndex = reads.FindIndex(item => item == "header:True");
        var detailsIndex = reads.FindIndex(item => item == "details:True");
        Assert.True(headerIndex >= 0, $"{operation} 必须在事务内重读 header：{string.Join(",", reads)}");
        Assert.True(
            detailsIndex > headerIndex,
            $"{operation} 必须在 header 后读取/锁定 details：{string.Join(",", reads)}"
        );
    }

    [Theory]
    [InlineData("single")]
    [InlineData("batch")]
    public async Task Action更新不修改Header金额或更新时间(string operation)
    {
        var invoiceGuid = $"action-header-{operation}";
        var detailGuid = $"action-header-detail-{operation}";
        var originalUpdatedAt = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await SeedInvoiceAsync(invoiceGuid, totalAmount: 5m);
        await SeedDetailAsync(detailGuid, invoiceGuid, "ACTION", 5m);
        await _db.Updateable<StoreLocalSupplierInvoice>()
            .SetColumns(header => header.UpdatedAt == originalUpdatedAt)
            .Where(header => header.InvoiceGUID == invoiceGuid)
            .ExecuteCommandAsync();
        await RejectHeaderUpdateAsync(invoiceGuid);

        if (operation == "single")
        {
            var result = await CreateService().UpdateDetailActionAsync(
                invoiceGuid,
                detailGuid,
                1
            );
            Assert.True(result.Success, result.Message);
        }
        else
        {
            var result = await CreateService().BatchUpdateDetailActionAsync(
                invoiceGuid,
                new BatchUpdateDetailActionRequest
                {
                    DetailGuids = new List<string> { detailGuid },
                    Action = 1,
                }
            );
            Assert.True(result.Success, result.Message);
        }

        var header = await _db.Queryable<StoreLocalSupplierInvoice>()
            .SingleAsync(item => item.InvoiceGUID == invoiceGuid);
        var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .SingleAsync(item => item.DetailGUID == detailGuid);
        Assert.Equal(5m, header.TotalAmount);
        Assert.Equal(originalUpdatedAt, header.UpdatedAt);
        Assert.Equal(1, detail.ActivityType);
    }

    [Fact]
    public async Task BatchUpsertDetailsAsync_未请求ActivityType时UpdateSql不包含该列()
    {
        const string invoiceGuid = "batch-upsert-narrow-update";
        const string detailGuid = "batch-upsert-narrow-detail";
        await SeedInvoiceAsync(invoiceGuid, totalAmount: 5m);
        await SeedDetailAsync(detailGuid, invoiceGuid, "OLD", 5m);
        await _db.Updateable<StoreLocalSupplierInvoiceDetails>()
            .SetColumns(detail => detail.ActivityType == 4)
            .Where(detail => detail.DetailGUID == detailGuid)
            .ExecuteCommandAsync();
        var detailUpdateSql = new List<string>();
        _db.Aop.OnLogExecuting = (sql, _) =>
        {
            if (
                sql.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("StoreLocalSupplierInvoiceDetails", StringComparison.OrdinalIgnoreCase)
            )
            {
                detailUpdateSql.Add(sql);
            }
        };

        ApiResponse<BatchResultDto> result;
        try
        {
            result = await CreateService().BatchUpsertDetailsAsync(
                invoiceGuid,
                new List<InvoiceDetailUpsertItemDto>
                {
                    new() { DetailGUID = detailGuid, ItemNumber = "NEW" },
                },
                "tester"
            );
        }
        finally
        {
            _db.Aop.OnLogExecuting = null;
        }

        Assert.True(result.Success, result.Message);
        var update = Assert.Single(detailUpdateSql);
        Assert.DoesNotContain("ActivityType", update, StringComparison.OrdinalIgnoreCase);
        var detail = await _db.Queryable<StoreLocalSupplierInvoiceDetails>()
            .SingleAsync(item => item.DetailGUID == detailGuid);
        Assert.Equal(4, detail.ActivityType);
        Assert.Equal("NEW", detail.ItemNumber);
    }

    private async Task SeedInvoiceAsync(
        string invoiceGuid,
        string storeCode = "S01",
        string supplierCode = "SUP01",
        decimal totalAmount = 0m
    )
    {
        await _db.Insertable(new StoreLocalSupplierInvoice
        {
            InvoiceGUID = invoiceGuid,
            StoreCode = storeCode,
            SupplierCode = supplierCode,
            InvoiceNo = invoiceGuid,
            TotalAmount = totalAmount,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedDetailAsync(
        string detailGuid,
        string invoiceGuid,
        string itemNumber,
        decimal amount,
        string storeCode = "S01",
        string supplierCode = "SUP01"
    )
    {
        await _db.Insertable(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = detailGuid,
            InvoiceGUID = invoiceGuid,
            StoreCode = storeCode,
            SupplierCode = supplierCode,
            ItemNumber = itemNumber,
            Quantity = 1,
            PurchasePrice = amount,
            Amount = amount,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private Task RejectHeaderUpdateAsync(string invoiceGuid) => _db.Ado.ExecuteCommandAsync($"""
        CREATE TRIGGER reject_{invoiceGuid.Replace("-", "_")}
        BEFORE UPDATE ON StoreLocalSupplierInvoice
        WHEN NEW.InvoiceGUID = '{invoiceGuid}'
        BEGIN SELECT RAISE(ABORT, 'forced invoice total failure'); END;
        """);

    private static string ReadDetailsHandlerSource() =>
        ReadApiFile(
            "Features/LocalSupplierInvoices/Details/LocalSupplierInvoicesDetailsHandler.cs"
        );

    private static string ReadApiFile(string relativePath) =>
        File.ReadAllText(ResolveApiFilePath(relativePath));

    private static string ResolveApiFilePath(
        string relativePath,
        [CallerFilePath] string sourcePath = ""
    ) =>
        Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(sourcePath)!,
                "..",
                "BlazorApp.Api",
                relativePath
            )
        );

    private static string ExtractMethod(string source, string methodName)
    {
        var methodStart = source.IndexOf(methodName, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"未找到方法 {methodName}");
        var openBrace = source.IndexOf('{', methodStart);
        Assert.True(openBrace >= 0, $"未找到方法 {methodName} 的起始大括号");

        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            if (source[index] != '}') continue;
            depth--;
            if (depth == 0) return source[methodStart..(index + 1)];
        }

        throw new InvalidOperationException($"未找到方法 {methodName} 的结束大括号");
    }

    private static void AssertUnifiedLockOrder(
        string source,
        string methodName,
        string detailLockMethod
    )
    {
        var method = ExtractMethod(source, methodName);
        var processLock = method.IndexOf("AcquireProcessAsync", StringComparison.Ordinal);
        var begin = method.IndexOf("BeginTranAsync", StringComparison.Ordinal);
        var headerLock = method.IndexOf("LockHeaderAsync", StringComparison.Ordinal);
        var detailLock = method.IndexOf(detailLockMethod, StringComparison.Ordinal);
        var headerTotal = method.IndexOf("UpdateHeaderTotalAsync", StringComparison.Ordinal);
        var commit = method.IndexOf("CommitTranAsync", StringComparison.Ordinal);

        Assert.True(processLock >= 0, $"{methodName} 缺少 invoice 进程锁");
        Assert.True(begin > processLock, $"{methodName} 必须先取进程锁再开启事务");
        Assert.True(headerLock > begin, $"{methodName} 必须在事务开始后先锁 header");
        Assert.True(detailLock > headerLock, $"{methodName} 必须在 header 后锁 details");
        Assert.True(headerTotal > detailLock, $"{methodName} 必须在明细写入后更新 header total");
        Assert.True(commit > headerTotal, $"{methodName} 必须保持锁至提交");
    }

    private static void AssertUnifiedActionLockOrder(
        string source,
        string methodName,
        string detailLockMethod
    )
    {
        var method = ExtractMethod(source, methodName);
        var processLock = method.IndexOf("AcquireProcessAsync", StringComparison.Ordinal);
        var begin = method.IndexOf("BeginTranAsync", StringComparison.Ordinal);
        var headerLock = method.IndexOf("LockHeaderAsync", StringComparison.Ordinal);
        var detailLock = method.IndexOf(detailLockMethod, StringComparison.Ordinal);
        var detailWrite = method.IndexOf(
            "Updateable<StoreLocalSupplierInvoiceDetails>",
            StringComparison.Ordinal
        );
        var commit = method.IndexOf("CommitTranAsync", StringComparison.Ordinal);

        Assert.True(processLock >= 0, $"{methodName} 缺少 invoice 进程锁");
        Assert.True(begin > processLock, $"{methodName} 必须先取进程锁再开启事务");
        Assert.True(headerLock > begin, $"{methodName} 必须在事务开始后先锁 header");
        Assert.True(detailLock > headerLock, $"{methodName} 必须在 header 后锁 details");
        Assert.True(detailWrite > detailLock, $"{methodName} 必须仅在锁内写 detail");
        Assert.True(commit > detailWrite, $"{methodName} 必须保持锁至提交");
        Assert.DoesNotContain("UpdateHeaderTotalAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Updateable<StoreLocalSupplierInvoice>",
            method,
            StringComparison.Ordinal
        );
    }

    private LocalSupplierInvoicesReactService CreateService()
    {
        var autoPricing = new Mock<IAutoPricingService>();
        autoPricing.Setup(x => x.GetAllActiveStrategiesAsync())
            .ReturnsAsync(new List<BlazorApp.Shared.Models.HBweb.PricingStrategy>());
        return new LocalSupplierInvoicesReactService(
            CreateSqlSugarContext(_db),
            CreateHqSqlSugarContext(),
            Mock.Of<IMapper>(),
            NullLogger<LocalSupplierInvoicesReactService>.Instance,
            autoPricing.Object,
            WarehouseProductChangeHistoryTestDouble.CreateNoop()
        );
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static HqSqlSugarContext CreateHqSqlSugarContext()
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HqSqlSugarContext));
        typeof(HqSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, new Mock<ISqlSugarClient>().Object);
        return context;
    }

    public void Dispose()
    {
        _connection.Dispose();
        _db.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }
}
