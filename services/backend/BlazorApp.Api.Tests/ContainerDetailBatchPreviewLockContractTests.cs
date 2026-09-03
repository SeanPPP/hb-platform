using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>
/// SQLite 不支持 SQL Server 锁提示；此契约测试防止该生产并发保护被后续重构意外移除。
/// </summary>
public sealed class ContainerDetailBatchPreviewLockContractTests
{
    [Fact]
    public void 批量预览执行_必须在同一事务内先锁范围和关联同步行再校验令牌()
    {
        var source = ReadSource("Services/React/ContainerReactService.cs");
        var scopedExecution = Slice(
            source,
            "private async Task<int> ExecuteScopedBatchUpdateUnderContainerLockAsync(",
            "private static void EnsureContainerCostInputs"
        );

        AssertInOrder(
            scopedExecution,
            "await _context.Db.Ado.BeginTranAsync()",
            "ContainerMutationLock.AcquireContainersAsync",
            "AcquireContainerDetailScopeHoldLockAsync(containerGuid)",
            "ResolveContainerDetailBatchScopeHguidsAsync",
            "AcquireContainerDetailConcurrencyRowLocksAsync(scopedHguids)",
            "var lockedHguids = await ResolveContainerDetailBatchScopeHguidsAsync",
            "lockAssociatedProducts: true",
            "EnsureBatchPreviewAsync",
            "BatchUpdateDetailsAttemptAsync",
            "await _context.Db.Ado.CommitTranAsync()"
        );
        Assert.Contains("[dbo].[WarehouseProduct]", source);
        Assert.Contains("[dbo].[DomesticProduct]", source);
        Assert.Contains("[dbo].[Product]", source);
        Assert.Contains("[dbo].[StoreRetailPrice]", source);
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", source);
        Assert.Contains("ORDER BY {keyColumn}", source);
    }

    [Fact]
    public void 进口价同步_必须先取得套装商品AppLock再读取关联行_并显式传入Attempt复用()
    {
        var source = ReadSource("Services/React/ContainerReactService.cs");
        var attempt = Slice(
            source,
            "private async Task<ContainerDetailBatchUpdateResultDto> BatchUpdateDetailsAttemptAsync(",
            "private const int ContainerProductCaseBatchSize"
        );
        AssertInOrder(
            attempt,
            "var tokenProductCodes = updates",
            "RequiresRelatedSnapshotLock(update)",
            "SetChildPurchasePriceMutationLock",
            "var rowLockedTokenProductCodes = repairMissingStoreRelations",
            "AcquireContainerDetailConcurrencyRowLocksAsync(",
            "lockAssociatedProducts: true"
        );
        Assert.Contains("ProductSetCode", attempt, StringComparison.Ordinal);
        Assert.Contains("StoreMultiCodeProduct", attempt, StringComparison.Ordinal);

        var scoped = Slice(
            source,
            "private async Task<int> ExecuteScopedBatchUpdateUnderContainerLockAsync(",
            "private static bool HaveSameNormalizedKeys"
        );
        AssertInOrder(
            scoped,
            "var updates = buildUpdates(container, details)",
            "var relatedSyncProductCodes = updates",
            "update.SkipRelatedProductSync != true",
            "var scopedImportLock = relatedSyncProductCodes.Count == 0",
            "SetChildPurchasePriceMutationLock.AcquireProductsAsync",
            "lockAssociatedProducts: true",
            "preAcquiredSetChildPurchasePriceLock: scopedImportLock"
        );
        Assert.Contains("relatedSyncProductCodes", scoped, StringComparison.Ordinal);
        Assert.Contains("ProductCode 不允许由本接口更新", attempt, StringComparison.Ordinal);
    }

    [Fact]
    public void 纯明细批量更新不得进入商品锁域_Web携带复合基线时仍须锁内校验()
    {
        var source = ReadSource("Services/React/ContainerReactService.cs");
        var helper = Slice(
            source,
            "private static bool RequiresRelatedSnapshotLock(",
            "private static bool TryGetFieldToken("
        );

        Assert.Contains("IsRelatedSyncField(intent.Field)", helper, StringComparison.Ordinal);
        Assert.Contains("update.SkipRelatedProductSync != true", helper, StringComparison.Ordinal);
        Assert.Contains(
            "TryGetFieldToken(update.ExpectedServerFieldTokens, intent.Field, out _)",
            helper,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void partialBusy商品_关联读取和后续商品同步必须被完整隔离_纯明细字段可继续()
    {
        var source = ReadSource("Services/React/ContainerReactService.cs");
        var attempt = Slice(
            source,
            "private async Task<ContainerDetailBatchUpdateResultDto> BatchUpdateDetailsAttemptAsync(",
            "private const int ContainerProductCaseBatchSize"
        );

        Assert.Contains("rowLockedTokenProductCodes", attempt, StringComparison.Ordinal);
        Assert.Contains("IsRelatedSyncField", attempt, StringComparison.Ordinal);
        Assert.Contains("busyImportProductCodes.Contains", attempt, StringComparison.Ordinal);
        Assert.Contains("纯货柜明细字段", attempt, StringComparison.Ordinal);
        AssertInOrder(
            attempt,
            "var relatedSyncDetailUpdates = validDetailUpdates",
            "var productCodes = relatedSyncDetailUpdates",
            "CaptureSnapshotsAsync(productCodes)"
        );
    }

    private static void AssertInOrder(string source, params string[] markers)
    {
        var offset = 0;
        foreach (var marker in markers)
        {
            var next = source.IndexOf(marker, offset, StringComparison.Ordinal);
            Assert.True(next >= 0, $"未在预期顺序找到: {marker}");
            offset = next + marker.Length;
        }
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"未找到方法片段: {startMarker}");
        return source[start..end];
    }

    private static string ReadSource(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "services", "backend", "BlazorApp.Api", relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException($"无法定位后端源码: {relativePath}");
    }
}
