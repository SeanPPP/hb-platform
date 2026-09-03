using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ContainerMutationWriterContractTests
{
    [Fact]
    public void 旧版与义乌货柜写入口_必须在事务内获取货柜锁()
    {
        var legacy = ReadSource("Services/ContainerService.cs");
        var yiwu = ReadSource("Services/YiwuContainerService.cs");

        Assert.Contains("ContainerMutationLock.AcquireContainersAsync", legacy);
        Assert.True(
            CountOccurrences(yiwu, "ContainerMutationLock.AcquireContainersAsync") >= 6,
            "义乌的单条、批量明细写入口都必须获取统一货柜锁"
        );
        Assert.DoesNotContain("Task.WhenAll(updateTasks)", yiwu);
        AssertInOrderFrom(
            legacy,
            "public async Task<int> BatchUpdateDetailsAsync(",
            "await _localContext.Db.Ado.BeginTranAsync()",
            "ContainerMutationLock.AcquireContainersAsync",
            "var details = await _localContext.Db.Queryable<ContainerDetail>()"
        );
        AssertInOrderFrom(
            yiwu,
            "private async Task<T> ExecuteLockedContainerMutationAsync<T>(",
            "await _context.Db.Ado.BeginTranAsync()",
            "ContainerMutationLock.AcquireContainersAsync",
            "var result = await action()",
            "await _context.Db.Ado.CommitTranAsync()"
        );
    }

    [Fact]
    public void Hq与增量同步写入口_必须按货柜获取统一锁()
    {
        var hqSync = ReadSource("Services/React/ContainerHqSyncService.cs");
        var incremental = ReadSource("Services/React/DataSyncIncrementalService.cs");
        var slicedStore = ReadSource(
            "Features/DataSync/Full/Stores/DataSyncContainersStore.cs"
        );

        Assert.Contains("ContainerMutationLock.AcquireContainersAsync", hqSync);
        Assert.Contains(".Concat(hqDetails.Select(x => x.主表GUID))", hqSync);
        Assert.Contains("hqDetailCodes.Contains(detail.DetailCode)", hqSync);
        Assert.Contains("ContainerMutationLock.AcquireContainersAsync", incremental);
        Assert.Contains("ContainerMutationLock.AcquireContainersAsync", slicedStore);
        AssertInOrderFrom(
            hqSync,
            "private async Task UpsertBatchAsync(",
            "await db.Ado.BeginTranAsync()",
            "ContainerMutationLock.AcquireContainersAsync",
            "var existingContainers = await db.Queryable<Container>()"
        );
        AssertInOrderFrom(
            incremental,
            "public async Task<SyncResult> SyncContainerDetailsFromHqIncrementalAsync(",
            "await _localContext.Db.Ado.BeginTranAsync()",
            "ContainerMutationLock.AcquireContainersAsync",
            "var lockedExistingDetails = await _localContext.Db"
        );
    }

    [Fact]
    public void 全量货柜明细替换_必须持有全局货柜锁()
    {
        var full = ReadSource("Services/React/DataSyncFullService.cs");
        var slicedStore = ReadSource(
            "Features/DataSync/Full/Stores/DataSyncContainersStore.cs"
        );
        var detailSync = SliceSource(
            full,
            "public async Task<SyncResult> SyncContainerDetailsFromHqAsync(",
            "public async Task<SyncResult> SyncContainersFromHqAsync("
        );

        Assert.Contains("ContainerMutationLock.AcquireAllAsync", full);
        Assert.Contains("ContainerMutationLock.AcquireAllAsync", slicedStore);
        AssertInOrderFrom(
            detailSync,
            "public async Task<SyncResult> SyncContainerDetailsFromHqAsync(",
            "await mutationLockDb.Ado.BeginTranAsync()",
            "ContainerMutationLock.AcquireAllAsync(mutationLockDb)",
            "TRUNCATE TABLE ContainerDetail",
            "var writer = Task.Run",
            "await Task.WhenAll(producers)",
            "channel.Writer.TryComplete();",
            "await writer;",
            "await mutationLockDb.Ado.CommitTranAsync()"
        );
        Assert.Matches(@"mutationLockDb\s*\.Fastest<ContainerDetail>\(\)", detailSync);
        Assert.DoesNotContain("syncLocalDb", detailSync);
        Assert.DoesNotContain("consumerDb", detailSync);
        Assert.DoesNotContain("Task.WhenAll(consumers)", detailSync);
        Assert.DoesNotContain("totalErrors", detailSync);
        Assert.Contains("await semaphore.WaitAsync(writerFailure.Token)", detailSync);
        Assert.Contains("writerFailure.Token.ThrowIfCancellationRequested()", detailSync);
        Assert.Contains("if (semaphoreAcquired)", detailSync);
        Assert.DoesNotContain("channel.Writer.Complete();", detailSync);
        AssertInOrderFrom(
            detailSync,
            "Exception? producerFailure = null;",
            "Exception? writerException = null;",
            "await Task.WhenAll(producers)",
            "await writer;",
            "if (writerException != null",
            "ExceptionDispatchInfo.Capture(writerException).Throw();",
            "if (producerFailure != null)",
            "ExceptionDispatchInfo.Capture(producerFailure).Throw();"
        );
        AssertInOrderFrom(
            slicedStore,
            "public async Task<SyncResult> SyncContainersFromHqAsync()",
            "await LocalContext.Db.Ado.BeginTranAsync()",
            "ContainerMutationLock.AcquireAllAsync(LocalContext.Db)",
            "Deleteable<ContainerDetail>()"
        );
    }

    [Fact]
    public void 跨柜去重的货柜头创建更新_必须在独占总闸后复查并写入()
    {
        var react = ReadSource("Services/React/ContainerReactService.cs");
        var legacy = ReadSource("Services/ContainerService.cs");

        AssertInOrderFrom(
            react,
            "public async Task<bool> UpdateContainerAsync(",
            "await _context.Db.Ado.BeginTranAsync()",
            "ContainerMutationLock.AcquireAllAsync",
            "Db.Queryable<Container>()",
            "duplicateQuery.AnyAsync()",
            "Db.Updateable(container)",
            "await _context.Db.Ado.CommitTranAsync()"
        );
        AssertInOrderFrom(
            react,
            "public async Task<string> CreateContainerAsync(",
            "await _context.Db.Ado.BeginTranAsync()",
            "ContainerMutationLock.AcquireAllAsync",
            "Db.Queryable<Container>()",
            "existsQuery.AnyAsync()",
            "Db.Insertable(container)",
            "await _context.Db.Ado.CommitTranAsync()"
        );
        AssertInOrderFrom(
            legacy,
            "public async Task<bool> UpdateContainerAsync(",
            "await _localContext.Db.Ado.BeginTranAsync()",
            "ContainerMutationLock.AcquireAllAsync",
            "Db.Queryable<Container>()",
            "duplicateQuery.AnyAsync()",
            "Db.Updateable(container)",
            "await _localContext.Db.Ado.CommitTranAsync()"
        );
        AssertInOrderFrom(
            legacy,
            "public async Task<string> CreateContainerAsync(",
            "await _localContext.Db.Ado.BeginTranAsync()",
            "ContainerMutationLock.AcquireAllAsync",
            "Db.Queryable<Container>()",
            "existsQuery.AnyAsync()",
            "Db.Insertable(container)",
            "await _localContext.Db.Ado.CommitTranAsync()"
        );
    }

    [Fact]
    public void Legacy与义乌写控制器_必须统一映射货柜锁冲突()
    {
        var react = ReadSource("Controllers/React/ReactContainerController.cs");
        var legacy = ReadSource("Controllers/ContainerController.cs");
        var yiwu = ReadSource("Controllers/YiwuContainerController.cs");

        AssertInOrderFrom(
            react,
            "public async Task<IActionResult> CreateContainer(",
            "catch (Exception ex) when (ContainerMutationLock.TryResolveConflict",
            "CreateContainerMutationConflictResponse"
        );
        Assert.True(
            CountOccurrences(
                legacy,
                "catch (Exception ex) when (ContainerMutationLock.TryResolveConflict"
            ) >= 3,
            "legacy 创建、更新和批量保存都必须返回统一并发冲突"
        );
        Assert.True(
            CountOccurrences(
                yiwu,
                "catch (Exception ex) when (ContainerMutationLock.TryResolveConflict"
            ) >= 12,
            "义乌所有货柜主表、明细和汇总写入口都必须返回统一并发冲突"
        );
        Assert.Contains("Response.Headers.RetryAfter = \"1\"", legacy);
        Assert.Contains("Response.Headers.RetryAfter = \"1\"", yiwu);
        Assert.Contains("ContainerMutationLock.BusyErrorCode", legacy);
        Assert.Contains("ContainerMutationLock.BusyErrorCode", yiwu);
    }

    [Fact]
    public void 货柜锁写事务回滚失败_不得覆盖原始并发异常()
    {
        var legacy = ReadSource("Services/ContainerService.cs");
        var legacyBatchUpdate = SliceSource(
            legacy,
            "public async Task<int> BatchUpdateDetailsAsync(",
            "public async Task<string> CreateContainerAsync("
        );

        AssertSafeRollbackContract(legacy, minimumCallCount: 5);
        Assert.Contains(
            "await RollbackContainerMutationTransactionSafelyAsync(ex)",
            legacyBatchUpdate
        );
        Assert.DoesNotContain(
            "await _localContext.Db.Ado.RollbackTranAsync()",
            legacyBatchUpdate
        );
        var react = ReadSource("Services/React/ContainerReactService.cs");
        var reactRollback = SliceSource(
            react,
            "private async Task RollbackContainerMutationTransactionSafelyAsync(",
            "private async Task<ContainerDetailBatchUpdateResultDto> BatchUpdateDetailsAttemptAsync("
        );
        AssertSafeRollbackContract(react, minimumCallCount: 1);
        Assert.Contains("ContainerMutationLock.ResetFailedTransaction", reactRollback);
        AssertSafeRollbackContract(
            ReadSource("Services/YiwuContainerService.cs"),
            minimumCallCount: 11
        );
        AssertSafeRollbackContract(
            ReadSource("Services/React/ContainerHqSyncService.cs"),
            minimumCallCount: 1
        );
        AssertSafeRollbackContract(
            ReadSource("Services/React/DataSyncFullService.cs"),
            minimumCallCount: 2
        );
        AssertSafeRollbackContract(
            ReadSource("Services/React/DataSyncIncrementalService.cs"),
            minimumCallCount: 2
        );
        AssertSafeRollbackContract(
            ReadSource("Features/DataSync/Full/Stores/DataSyncContainersStore.cs"),
            minimumCallCount: 3
        );
    }

    [Fact]
    public void React明细写入_事务边界必须由持锁调用方唯一管理()
    {
        var react = ReadSource("Services/React/ContainerReactService.cs");
        var attempt = SliceSource(
            react,
            "private async Task<ContainerDetailBatchUpdateResultDto> BatchUpdateDetailsAttemptAsync(",
            "private const int ContainerProductCaseBatchSize"
        );

        Assert.Contains(
            "_context.Db.Ado.Transaction == null || mutationLock == null",
            attempt
        );
        Assert.DoesNotContain("BeginTranAsync()", attempt);
        Assert.DoesNotContain("CommitTranAsync()", attempt);
        Assert.DoesNotContain("RollbackTranAsync()", attempt);
    }

    [Fact]
    public void 锁冲突_不得被义乌与全量同步的内层批处理捕获()
    {
        var yiwu = ReadSource("Services/YiwuContainerService.cs");
        var full = ReadSource("Services/React/DataSyncFullService.cs");
        const string conflictFilter =
            "when (!ContainerMutationLock.TryResolveConflict(ex, out _))";
        var fullMethodStart = full.IndexOf(
            "public async Task<SyncResult> SyncContainersFromHqAsync(",
            StringComparison.Ordinal
        );
        var fullMethodEnd = full.IndexOf(
            "private async Task RollbackContainerMutationTransactionSafelyAsync(",
            fullMethodStart,
            StringComparison.Ordinal
        );
        Assert.True(fullMethodStart >= 0 && fullMethodEnd > fullMethodStart);
        var fullContainerMethod = full[fullMethodStart..fullMethodEnd];

        Assert.True(
            CountOccurrences(yiwu, conflictFilter) >= 2,
            "义乌批量删除和批量更新只能捕获普通行错误"
        );
        AssertInOrderFrom(
            fullContainerMethod,
            ".BulkCopyAsync(localBatch);",
            "catch (Exception ex)",
            conflictFilter,
            "errors += localBatch.Count"
        );
        Assert.Contains("删除货柜 {containerCode} 失败", yiwu);
        Assert.Contains("批量更新失败: {ex.Message}", yiwu);
    }

    [Fact]
    public void React增量同步_必须单独统计锁竞争且混合错误不得冒充全BUSY()
    {
        var incremental = ReadSource("Services/React/DataSyncIncrementalService.cs");

        Assert.True(
            CountOccurrences(incremental, "var busyErrors = 0;") >= 2,
            "主表和详情增量同步必须各自统计锁竞争"
        );
        Assert.True(
            CountOccurrences(incremental, "busyErrors++;") >= 2,
            "每个锁竞争页都必须计数"
        );
        Assert.True(
            CountOccurrences(incremental, "result.BusyErrorCount = busyErrors;") >= 2,
            "必须把锁竞争页数返回给控制器"
        );
        Assert.True(
            CountOccurrences(incremental, "busyErrors > 0 && busyErrors == errors") >= 2,
            "仅全部错误都是锁竞争时才返回货柜 BUSY 错误码"
        );
    }

    [Fact]
    public void Legacy增量详情_每页提交后必须累计已提交数()
    {
        var slicedStore = ReadSource(
            "Features/DataSync/Full/Stores/DataSyncContainersStore.cs"
        );

        AssertInOrderFrom(
            slicedStore,
            "await LocalContext.Db.Ado.CommitTranAsync();\n                        totalDetails += detailsToUpdate.Count + detailsToAdd.Count;",
            "result.UpdatedCount += detailsToUpdate.Count;",
            "result.AddedCount += detailsToAdd.Count;"
        );
    }

    [Fact]
    public void 分配与整柜提交的内层行错误_不得吞掉货柜或商品锁冲突()
    {
        var react = ReadSource("Services/React/ContainerReactService.cs");
        var executor = ReadSource(
            "Services/React/ContainerProductCreationExecutorService.cs"
        );
        var assignMethod = SliceSource(
            react,
            "public async Task<AssignProductsResultDto> AssignProductsAsync(",
            "public async Task<int> BatchDeleteDetailsAsync("
        );
        var executeMethod = SliceSource(
            executor,
            "public async Task<ContainerProductCreationResultDto> ExecuteAsync(",
            "private async Task<List<ContainerProductCreationSourceRow>> LoadRowsAsync("
        );
        var updateExistingMethod = SliceSource(
            executor,
            "private async Task UpdateExistingProductsForSubmitAsync(",
            "private async Task UpsertActiveStoreRetailPricesAsync("
        );
        var finalizeMethod = SliceSource(
            executor,
            "private async Task<ContainerProductCreationResultDto> FinalizeSubmitResultAsync(",
            "private static void PromoteBlockingSubmitSkipsToErrors("
        );
        const string containerConflictFilter =
            "!ContainerMutationLock.TryResolveConflict(ex";
        const string productConflictFilter =
            "!SetChildPurchasePriceMutationLock.TryResolveConflict(ex";

        Assert.Contains(
            "when (!ContainerMutationLock.TryResolveConflict(exItem, out _))",
            assignMethod
        );
        Assert.Contains("result.Failed.Add(", assignMethod);

        foreach (var method in new[] { executeMethod, updateExistingMethod, finalizeMethod })
        {
            Assert.Contains(containerConflictFilter, method);
            Assert.Contains(productConflictFilter, method);
        }
        Assert.Contains("WAREHOUSE_BATCH_EXCEPTION", executeMethod);
        Assert.Contains("UPDATE_EXISTING_PRODUCTS_FAILED", updateExistingMethod);
        Assert.Contains("COMPLETE_CONTAINER_FAILED", finalizeMethod);
    }

    private static void AssertSafeRollbackContract(string source, int minimumCallCount)
    {
        Assert.True(
            CountOccurrences(
                source,
                "await RollbackContainerMutationTransactionSafelyAsync("
            ) >= minimumCallCount,
            $"统一安全回滚调用不足 {minimumCallCount} 处"
        );
        AssertInOrderFrom(
            source,
            "private async Task RollbackContainerMutationTransactionSafelyAsync(",
            "try",
            "RollbackTranAsync()",
            "catch (Exception rollbackException)",
            "ContainerMutationLock.ResetFailedTransaction",
            "LogWarning("
        );
    }

    private static void AssertInOrderFrom(
        string source,
        string startMarker,
        params string[] markers
    )
    {
        var offset = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(offset >= 0, $"未找到起点: {startMarker}");

        foreach (var marker in markers)
        {
            var next = source.IndexOf(marker, offset, StringComparison.Ordinal);
            Assert.True(next >= 0, $"未在预期顺序找到: {marker}");
            offset = next + marker.Length;
        }
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string SliceSource(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"未找到方法片段: {startMarker}");
        return source[start..end];
    }

    private static string ReadSource(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "services",
                "backend",
                "BlazorApp.Api",
                relativePath
            );
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"无法定位后端源码: {relativePath}");
    }
}
