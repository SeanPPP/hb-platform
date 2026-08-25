using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class DataSyncIncrementalSetProtectionContractTests
{
    [Fact]
    public void 分店多码增量同步_每页必须事务内加锁并锁内重读保护键()
    {
        var source = ReadServiceSource();
        var method = ExtractMethod(
            source,
            "public async Task<SyncResult> SyncStoreMultiCodeProductsFromHqIncrementalAsync",
            "public async Task<SyncResult> SyncProductSetCodesFromHqIncrementalAsync"
        );

        AssertOrdered(
            method,
            "for (var page = 1; page <= pages; page++)",
            "await _localContext.Db.Ado.BeginTranAsync();",
            "SetChildPurchasePriceMutationLock.AcquireProductsAsync(",
            "GetProtectedStoreMultiCodeKeysAsync(",
            "Queryable<StoreMultiCodeProduct>()",
            ".UpdateColumns(row => new",
            "CommitTranAsync()"
        );
        Assert.Contains("GetStoreMultiCodeIdentityKey", method);
        Assert.Contains("RollbackTranAsync()", method);
    }

    [Fact]
    public void 全局多码增量同步_每页必须事务内加锁并按复合业务键匹配()
    {
        var source = ReadServiceSource();
        var method = ExtractMethod(
            source,
            "public async Task<SyncResult> SyncProductSetCodesFromHqIncrementalAsync",
            "public async Task<SyncResult> SyncStoreClearancePricesFromHqIncrementalAsync"
        );

        AssertOrdered(
            method,
            "for (var page = 1; page <= pages; page++)",
            "await _localContext.Db.Ado.BeginTranAsync();",
            "SetChildPurchasePriceMutationLock.AcquireProductsAsync(",
            "GetProtectedSetCodeKeysAsync(",
            "Queryable<ProductSetCode>()",
            "GetSetCodeBusinessKey(item)",
            ".UpdateColumns(row => new",
            "CommitTranAsync()"
        );
        Assert.Contains("RollbackTranAsync()", method);
    }

    [Fact]
    public void 分店多码增量同步_软删除业务键必须作为墓碑跳过且更新不得恢复删除或创建字段()
    {
        var source = ReadServiceSource();
        var method = ExtractMethod(
            source,
            "public async Task<SyncResult> SyncStoreMultiCodeProductsFromHqIncrementalAsync",
            "public async Task<SyncResult> SyncProductSetCodesFromHqIncrementalAsync"
        );

        AssertTombstoneProtectedUpdate(method);
    }

    [Fact]
    public void 全局多码增量同步_软删除Type2业务键必须作为墓碑跳过且更新不得恢复删除或创建字段()
    {
        var source = ReadServiceSource();
        var method = ExtractMethod(
            source,
            "public async Task<SyncResult> SyncProductSetCodesFromHqIncrementalAsync",
            "public async Task<SyncResult> SyncStoreClearancePricesFromHqIncrementalAsync"
        );

        AssertTombstoneProtectedUpdate(method);
    }

    [Fact]
    public void 分店多码身份键_必须包含门店主商品和子项编码()
    {
        var source = ReadServiceSource();
        var helper = ExtractMethod(
            source,
            "private static string GetStoreMultiCodeIdentityKey",
            "private static async Task<HashSet<string>> GetProtectedSetCodeKeysAsync"
        );

        Assert.Contains("item.StoreCode?.Trim()", helper);
        Assert.Contains("item.ProductCode?.Trim()", helper);
        Assert.Contains("item.MultiCodeProductCode?.Trim()", helper);
    }

    [Fact]
    public void 全量套装同步_必须在读取保护快照前获取独占总闸()
    {
        var source = ReadSource("DataSyncFullService.cs");
        var method = ExtractMethod(
            source,
            "public async Task<SyncResult> SyncProductSetCodesFromHqAsync",
            "private static string GetSetCodeBusinessKey"
        );

        AssertOrdered(
            method,
            "await _localContext.Db.Ado.BeginTranAsync();",
            "SetChildPurchasePriceMutationLock.AcquireAllAsync(",
            "ProductSetCodeIdentityResolver.CreateIndex(",
            "GetProtectedSetCodeKeysAsync(",
            "totalMissingRequiredFields",
            "Deleteable<ProductSetCode>()",
            "BulkCopyAsync(localBatch)",
            "RecalculateLockedAsync(",
            "CommitTranAsync()"
        );
        Assert.Contains(".Where(x => x.SetType != 1)", method);
        Assert.Contains("await _localContext.Db.Ado.RollbackTranAsync();", method);
        Assert.DoesNotContain("x.SetType != 1 || !x.IsActive || x.IsDeleted", method);
    }

    [Fact]
    public void 全量分店多码同步_总闸内每店原子删除重建()
    {
        var source = ReadSource("DataSyncFullService.cs");
        var coordinator = ExtractMethod(
            source,
            "public async Task<SyncResult> SyncStoreMultiCodeProductsFromHqConcurrentAsync",
            ")> ProcessSingleStoreMultiCodeAsync("
        );
        var worker = ExtractMethod(
            source,
            ")> ProcessSingleStoreMultiCodeAsync(",
            "public async Task<SyncResult> SyncProductSetCodesFromHqAsync"
        );

        Assert.Contains("ProcessSingleStoreMultiCodeAsync(", coordinator);
        AssertOrdered(
            worker,
            "await localDb.Ado.BeginTranAsync();",
            "SetChildPurchasePriceMutationLock.AcquireAllAsync(localDb)",
            "GetProtectedStoreMultiCodeKeysAsync(localDb)",
            "Deleteable<StoreMultiCodeProduct>()",
            "RetryBulkInsertAsync(localDb, localBatch, 3)",
            "RecalculateStoreGroupsLockedAsync(",
            "await localDb.Ado.CommitTranAsync();"
        );
        Assert.Contains("await localDb.Ado.RollbackTranAsync();", worker);
    }

    [Fact]
    public void 全量关系同步_HQ二型必须清成本并在业务锁内统一重算()
    {
        var source = ReadSource("DataSyncFullService.cs");
        var storeMethod = ExtractMethod(
            source,
            "public async Task<SyncResult> SyncStoreMultiCodeProductsFromHqConcurrentAsync",
            ")> ProcessSingleStoreMultiCodeAsync("
        );
        var setMethod = ExtractMethod(
            source,
            "public async Task<SyncResult> SyncProductSetCodesFromHqAsync",
            "private static string GetSetCodeBusinessKey"
        );

        Assert.Contains("SetType = 2", setMethod);
        Assert.Contains("SetPurchasePrice = null", setMethod);
        Assert.Contains("GetProtectedSetCodeIdsAsync", setMethod);
        AssertOrdered(
            setMethod,
            "SetChildPurchasePriceMutationLock.AcquireAllAsync(",
            "BulkCopyAsync(localBatch)",
            "RecalculateLockedAsync(",
            "CommitTranAsync()"
        );
        var storeWorker = ExtractMethod(
            source,
            ")> ProcessSingleStoreMultiCodeAsync(",
            "public async Task<SyncResult> SyncProductSetCodesFromHqAsync"
        );
        Assert.Contains("RecalculateStoreGroupsLockedAsync(", storeWorker);
    }

    [Fact]
    public void 增量关系同步_HQ二型必须清成本并在业务锁内统一重算()
    {
        var source = ReadServiceSource();
        var storeMethod = ExtractMethod(
            source,
            "public async Task<SyncResult> SyncStoreMultiCodeProductsFromHqIncrementalAsync",
            "public async Task<SyncResult> SyncProductSetCodesFromHqIncrementalAsync"
        );
        var setMethod = ExtractMethod(
            source,
            "public async Task<SyncResult> SyncProductSetCodesFromHqIncrementalAsync",
            "public async Task<SyncResult> SyncStoreClearancePricesFromHqIncrementalAsync"
        );

        Assert.Contains("SetType = 2", setMethod);
        Assert.Contains("SetPurchasePrice = null", setMethod);
        Assert.Contains("GetProtectedSetCodeIdsAsync", setMethod);
        AssertOrdered(
            setMethod,
            "SetChildPurchasePriceMutationLock.AcquireProductsAsync(",
            "BulkCopyAsync(toInsert)",
            "RecalculateLockedAsync(",
            "CommitTranAsync()"
        );
        AssertContainsInOrder(
            storeMethod,
            "SetChildPurchasePriceMutationLock.AcquireProductsAsync(",
            "BulkCopyAsync(toInsert)",
            "RecalculateStoreGroupsLockedAsync(",
            "CommitTranAsync()"
        );
    }

    [Fact]
    public void HQ普通多码保护集合_必须覆盖停用和软删除Type1()
    {
        foreach (var source in new[]
        {
            ReadSource("DataSyncFullService.cs"),
            ReadServiceSource(),
        })
        {
            var start = source.IndexOf(
                "private static async Task<HashSet<string>> GetProtectedSetCodeIdsAsync",
                StringComparison.Ordinal
            );
            var end = source.IndexOf(
                "public async Task<SyncResult> SyncStoreClearancePricesFromHq",
                start,
                StringComparison.Ordinal
            );
            if (end < 0)
            {
                end = source.IndexOf(
                    "public async Task<SyncResult> SyncDomesticProductsFromHqAsync",
                    start,
                    StringComparison.Ordinal
                );
            }

            Assert.True(start >= 0 && end > start, "未找到 Type1 保护集合辅助方法");
            var helpers = source[start..end];
            Assert.Equal(3, CountOccurrences(helpers, ".Where(item => item.SetType == 1)"));
            Assert.DoesNotContain("item.IsActive", helpers);
            Assert.DoesNotContain("item.IsDeleted", helpers);
        }
    }

    [Fact]
    public void 仓库成本同步_写入ImportPrice后必须在同批产品锁内重算()
    {
        AssertWarehouseMethodRecalculatesLocked(
            ReadSource("DataSyncFullService.cs"),
            "public async Task<SyncResult> SyncWarehouseProductsFromHqAsync(\n            int hqBatchSize,"
        );
        AssertWarehouseMethodRecalculatesLocked(
            ReadServiceSource(),
            "public async Task<SyncResult> SyncWarehouseProductsFromHqIncrementalAsync"
        );
    }

    [Fact]
    public void 成本重算遇到业务锁冲突_必须保留BUSY错误码并回滚当前事务()
    {
        foreach (var source in new[]
        {
            ReadSource("DataSyncFullService.cs"),
            ReadServiceSource(),
        })
        {
            Assert.Contains("SetChildPurchasePriceMutationLock.BusyErrorCode", source);
            Assert.Contains("RollbackTranAsync()", source);
        }
    }

    [Fact]
    public void 套装多码增量同步_必须在分页写入前完成整窗身份预检并使用双索引解析()
    {
        var method = ExtractMethod(
            ReadServiceSource(),
            "public async Task<SyncResult> SyncProductSetCodesFromHqIncrementalAsync",
            "public async Task<SyncResult> SyncStoreClearancePricesFromHqIncrementalAsync"
        );

        AssertOrdered(
            method,
            "ProductSetCodeIdentityResolver.PreflightSource(",
            "ProductSetCodeIdentityResolver.CreateIndex(",
            "for (var page = 1; page <= pages; page++)",
            "identityIndex.Resolve("
        );
        Assert.Contains("conflictingProductCodes", method);
        Assert.Contains("ProductSetCodeIdentityMatchKind.Conflict", method);
        Assert.Contains("ProductSetCodeIdentityMatchKind.GuidOnly", method);
        Assert.Contains("IsCrossParentGuidOnly", method);
        Assert.Contains("existing.SetProductCode = item.SetProductCode;", method);
        Assert.Contains("row.SetProductCode", method);
        Assert.DoesNotContain(
            ".GroupBy(GetSetCodeBusinessKey, StringComparer.OrdinalIgnoreCase)\n                        .Select(group => group.Last())",
            method
        );
    }

    [Fact]
    public void 套装多码增量同步_GUID迁移后必须立即重建索引并避免重复更新同一实体()
    {
        var method = ExtractMethod(
            ReadServiceSource(),
            "public async Task<SyncResult> SyncProductSetCodesFromHqIncrementalAsync",
            "public async Task<SyncResult> SyncStoreClearancePricesFromHqIncrementalAsync"
        );

        Assert.Contains("identityIndex.Reindex(existing, previousIdentity);", method);
        Assert.Contains("ReferenceEqualityComparer.Instance", method);
    }

    [Fact]
    public void 套装多码全量同步_身份预检必须发生在事务删除之前()
    {
        var method = ExtractMethod(
            ReadSource("DataSyncFullService.cs"),
            "public async Task<SyncResult> SyncProductSetCodesFromHqAsync",
            "private static string GetSetCodeBusinessKey"
        );

        AssertOrdered(
            method,
            "ProductSetCodeIdentityResolver.PreflightSource(",
            "await _localContext.Db.Ado.BeginTranAsync();",
            "SetChildPurchasePriceMutationLock.AcquireAllAsync(",
            "ProductSetCodeIdentityResolver.CreateIndex(",
            "ProductSetCodeIdentityMatchKind.Conflict",
            "Deleteable<ProductSetCode>()"
        );
        Assert.Contains("PRODUCT_SET_CODE_IDENTITY_CONFLICT", method);
        Assert.Contains("BuildProductSetCodeSourcePages(", method);
        Assert.Contains("System.Data.IsolationLevel.Serializable", method);
        Assert.Contains("activeHqRows", method);
        Assert.Contains("activeProductCodes", method);
        Assert.Equal(2, method.Split(".With(SqlWith.Null)", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("CreateConcurrentConnection", method);
        Assert.DoesNotContain("ValidateProductSetCodeSourcePageSnapshot(", method);
        Assert.DoesNotContain(".Skip(skip)", method);
    }

    [Fact]
    public void 成本同步失败结果_必须分别累计BUSY错误数量()
    {
        var incremental = ReadServiceSource();
        var full = ReadSource("DataSyncFullService.cs");

        Assert.Contains("result.BusyErrorCount = busyErrors;", incremental);
        Assert.Contains("result.BusyErrorCount = 1;", incremental);
        Assert.Contains("result.BusyErrorCount = storeErrors.Count", full);
        Assert.Contains("result.BusyErrorCount = 1;", full);
    }

    private static void AssertWarehouseMethodRecalculatesLocked(string source, string startMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"未找到仓库同步方法: {startMarker}");
        var method = source[start..];
        AssertContainsInOrder(
            method,
            "AcquireProductsAsync(",
            "ImportPrice",
            "RecalculateLockedAsync("
        );
    }

    private static void AssertTombstoneProtectedUpdate(string method)
    {
        if (method.Contains("var tombstoneKeys", StringComparison.Ordinal))
        {
            Assert.Contains(".Where(row => row.IsDeleted)", method);
            Assert.Contains(".Where(row => !row.IsDeleted)", method);
            AssertContainsInOrder(
                method,
                "existingByKey.TryGetValue(",
                "if (tombstoneKeys.Contains(",
                "continue;",
                "toInsert.Add(item)"
            );
        }
        else
        {
            AssertContainsInOrder(
                method,
                "var resolution = identityIndex.Resolve(",
                "var existing = resolution.MatchedRow;",
                "if (existing.IsDeleted)",
                "continue;",
                "toInsert.Add(item)"
            );
        }

        Assert.Contains(".Db.Updateable(toUpdate)", method);
        Assert.DoesNotContain("BulkUpdateAsync(toUpdate)", method);

        var updateColumnsStart = method.IndexOf(".UpdateColumns(", StringComparison.Ordinal);
        var updateColumnsEnd = method.IndexOf(
            ".ExecuteCommandAsync()",
            updateColumnsStart,
            StringComparison.Ordinal
        );
        Assert.True(updateColumnsStart >= 0 && updateColumnsEnd > updateColumnsStart);
        var updateColumns = method[updateColumnsStart..updateColumnsEnd];
        Assert.DoesNotContain("IsDeleted", updateColumns);
        Assert.DoesNotContain("CreatedAt", updateColumns);
        Assert.DoesNotContain("CreatedBy", updateColumns);
    }

    private static void AssertOrdered(string source, params string[] fragments)
    {
        var previous = -1;
        foreach (var fragment in fragments)
        {
            var current = source.IndexOf(fragment, previous + 1, StringComparison.Ordinal);
            Assert.True(current > previous, $"未按预期顺序找到代码片段: {fragment}");
            previous = current;
        }
    }

    private static void AssertContainsInOrder(string source, params string[] fragments)
    {
        var previous = -1;
        foreach (var fragment in fragments)
        {
            var current = source.IndexOf(fragment, previous + 1, StringComparison.Ordinal);
            Assert.True(current > previous, $"未按预期顺序找到代码片段: {fragment}");
            previous = current;
        }
    }

    private static int CountOccurrences(string source, string fragment)
    {
        var count = 0;
        var start = 0;
        while ((start = source.IndexOf(fragment, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += fragment.Length;
        }

        return count;
    }

    private static string ExtractMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0, $"未找到起始方法: {startMarker}");
        Assert.True(end > start, $"未找到结束方法: {endMarker}");
        return source[start..end];
    }

    private static string ReadServiceSource()
    {
        return ReadSource("DataSyncIncrementalService.cs");
    }

    private static string ReadSource(string fileName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(
                current.FullName,
                $"services/backend/BlazorApp.Api/Services/React/{fileName}"
            );
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            current = current.Parent;
        }

        throw new FileNotFoundException($"未找到 {fileName}");
    }
}
