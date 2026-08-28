using System.Data;
using System.Linq;
using System.Text.Json;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BlazorApp.Api.Features.LocalSupplierInvoices
{
    internal sealed class LocalSupplierInvoicesHqSyncHandler
    {
        private readonly LocalSupplierInvoicesDependencies _dependencies;
        private SqlSugarContext _context => _dependencies.Context;
        private HqSqlSugarContext _hqContext => _dependencies.HqContext;
        private IMapper _mapper => _dependencies.Mapper;
        private ILogger _logger => _dependencies.Logger;
        private IAutoPricingService _autoPricingService => _dependencies.AutoPricingService;
        private IWarehouseProductChangeHistoryService _changeHistoryService => _dependencies.ChangeHistoryService;
        private ILocalSupplierInvoiceHqProductSyncService? _hqProductSyncService => _dependencies.HqProductSyncService;

        public LocalSupplierInvoicesHqSyncHandler(LocalSupplierInvoicesDependencies dependencies)
        {
            _dependencies = dependencies;
        }

        public async Task<SyncResult> PushInvoicesToHqAsync(List<string> invoiceGuids)
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow, IsSuccess = false };

            try
            {
                var hqDb = _hqContext.Db;
                var sourceSnapshot = await ReadSourceSnapshotAsync(invoiceGuids);
                var invoices = sourceSnapshot.Invoices;

                if (!invoices.Any())
                {
                    result.IsSuccess = false;
                    result.Message = "未找到有效的进货单数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                var details = sourceSnapshot.Details;

                var addedInvoiceCount = 0;
                var updatedInvoiceCount = 0;
                var addedDetailCount = 0;
                var updatedDetailCount = 0;
                var completedInvoiceCount = 0;

                for (var invoiceIndex = 0; invoiceIndex < invoices.Count; invoiceIndex++)
                {
                    var invoice = invoices[invoiceIndex];
                    using var processLock = await LocalSupplierInvoiceHqSyncMutationLock.AcquireAsync(
                        invoice.InvoiceGUID
                    );
                    var transactionStarted = false;
                    try
                    {
                        // 每张单据只有一个 HQ 事务边界；跨节点锁也必须绑定这个事务自动释放。
                        await hqDb.Ado.BeginTranAsync();
                        transactionStarted = true;
                        await LocalSupplierInvoiceHqSyncMutationLock.AcquireDatabaseAsync(
                            hqDb,
                            processLock.NormalizedInvoiceKey
                        );
                        var hqEntity = _mapper.Map<RED_进货单主表Store>(invoice);
                        var invoiceHeaderAdded = 0;
                        var invoiceHeaderUpdated = 0;
                        var invoiceDetailAdded = 0;
                        var invoiceDetailUpdated = 0;
                        // 锁等待期间其他请求可能已经提交；存在性只能从当前事务内重新读取。
                        var existing = await ReadHqInvoiceAsync(hqDb, invoice.InvoiceGUID);
                        if (existing != null)
                        {
                            hqEntity.ID = existing.ID;
                            await hqDb.Updateable(hqEntity).ExecuteCommandAsync();
                            invoiceHeaderUpdated++;
                        }
                        else
                        {
                            await hqDb.Insertable(hqEntity).ExecuteCommandAsync();
                            invoiceHeaderAdded++;
                        }

                        var invoiceDetails = details.Where(detail => detail.InvoiceGUID == invoice.InvoiceGUID);
                        foreach (var detail in invoiceDetails)
                        {
                            var hqDetailEntity = _mapper.Map<RED_进货单详情表Store>(detail);
                            // 明细同样不能使用事务外集合，否则并发请求仍会重复插入。
                            var existingDetail = await ReadHqDetailAsync(hqDb, detail.DetailGUID);
                            if (existingDetail != null)
                            {
                                hqDetailEntity.ID = existingDetail.ID;
                                await hqDb.Updateable(hqDetailEntity).ExecuteCommandAsync();
                                invoiceDetailUpdated++;
                            }
                            else
                            {
                                await hqDb.Insertable(hqDetailEntity).ExecuteCommandAsync();
                                invoiceDetailAdded++;
                            }
                        }

                        await hqDb.Ado.CommitTranAsync();
                        transactionStarted = false;
                        addedInvoiceCount += invoiceHeaderAdded;
                        updatedInvoiceCount += invoiceHeaderUpdated;
                        addedDetailCount += invoiceDetailAdded;
                        updatedDetailCount += invoiceDetailUpdated;
                        completedInvoiceCount++;
                    }
                    catch (Exception ex)
                    {
                        // 原始写入异常是本单失败的根因；回滚异常只能作为附加诊断，不能覆盖它。
                        var originalException = ex;
                        Exception? rollbackException = null;
                        if (transactionStarted)
                        {
                            try
                            {
                                await hqDb.Ado.RollbackTranAsync();
                            }
                            catch (Exception rollbackEx)
                            {
                                rollbackException = rollbackEx;
                                _logger.LogError(
                                    rollbackEx,
                                    "推送进货单到HQ回滚失败: {GUID}",
                                    invoice.InvoiceGUID
                                );
                            }
                        }

                        _logger.LogError(
                            originalException,
                            "推送进货单到HQ失败: {GUID}",
                            invoice.InvoiceGUID
                        );
                        // ErrorCount 按未完整提交的单据计数，与 TotalCount 的单据口径一致。
                        if (
                            IsConnectionLevelFailure(originalException)
                            || (rollbackException != null && IsConnectionLevelFailure(rollbackException))
                        )
                        {
                            // 连接已不可用时，当前单与尚未尝试的单据都无法确认提交结果，按失败单据计数后停止。
                            result.ErrorCount += invoices.Count - invoiceIndex;
                            break;
                        }

                        result.ErrorCount++;
                    }
                }

                result.AddedCount = addedInvoiceCount + addedDetailCount;
                result.UpdatedCount = updatedInvoiceCount + updatedDetailCount;
                result.TotalCount = invoices.Count;
                result.IsSuccess = result.ErrorCount == 0 && completedInvoiceCount == invoices.Count;
                result.Message = result.IsSuccess
                    ? $"成功推送 {completedInvoiceCount} 个进货单（主表 新增{addedInvoiceCount}/更新{updatedInvoiceCount}，详情 新增{addedDetailCount}/更新{updatedDetailCount}）"
                    : $"推送失败：{result.ErrorCount} 个进货单未完整提交，已完成 {completedInvoiceCount}/{invoices.Count} 个（主表 新增{addedInvoiceCount}/更新{updatedInvoiceCount}，详情 新增{addedDetailCount}/更新{updatedDetailCount}）";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "推送进货单到HQ异常");
                result.IsSuccess = false;
                result.Message = $"推送异常: {ex.Message}";
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        private static bool IsConnectionLevelFailure(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is SqlException sqlException && sqlException.Class >= 20)
                    return true;
                if (current is NpgsqlException { IsTransient: true })
                    return true;
                if (current is System.Net.Sockets.SocketException or TimeoutException)
                    return true;
            }

            return false;
        }

        private static async Task<RED_进货单主表Store?> ReadHqInvoiceAsync(
            ISqlSugarClient hqDb,
            string? invoiceGuid
        ) => await hqDb.Queryable<RED_进货单主表Store>()
            .Where(h => h.HGUID == invoiceGuid)
            .OrderBy(h => h.ID)
            .FirstAsync();

        private static async Task<RED_进货单详情表Store?> ReadHqDetailAsync(
            ISqlSugarClient hqDb,
            string? detailGuid
        ) => await hqDb.Queryable<RED_进货单详情表Store>()
            .Where(h => h.HGUID == detailGuid)
            .OrderBy(h => h.ID)
            .FirstAsync();

        private async Task<LocalSupplierInvoicePushSourceSnapshot> ReadSourceSnapshotAsync(
            List<string> invoiceGuids
        )
        {
            var localDb = _context.Db;
            var transactionStarted = false;
            try
            {
                // 本地上下文默认可能启用 NOLOCK；可串行化读取事务保证单头与明细来自同一时点。
                await localDb.Ado.BeginTranAsync(IsolationLevel.Serializable);
                transactionStarted = true;
                var invoices = await localDb.Queryable<StoreLocalSupplierInvoice>()
                    .Where(invoice =>
                        invoiceGuids.Contains(invoice.InvoiceGUID) && invoice.IsDeleted == false
                    )
                    .ToListAsync();
                var invoiceGuidList = invoices
                    .Select(invoice => invoice.InvoiceGUID)
                    .Where(guid => !string.IsNullOrWhiteSpace(guid))
                    .Select(guid => guid!)
                    .ToList();
                var details = await localDb.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .Where(detail =>
                        detail.InvoiceGUID != null
                        && invoiceGuidList.Contains(detail.InvoiceGUID)
                        && detail.IsDeleted == false
                    )
                    .ToListAsync();

                await localDb.Ado.CommitTranAsync();
                transactionStarted = false;
                return new LocalSupplierInvoicePushSourceSnapshot(invoices, details);
            }
            catch (Exception ex)
            {
                if (transactionStarted)
                {
                    try
                    {
                        await localDb.Ado.RollbackTranAsync();
                    }
                    catch (Exception rollbackEx)
                    {
                        // 读取快照的原始异常仍是同步失败根因，回滚异常只补充诊断。
                        _logger.LogError(rollbackEx, "读取本地进货单推送快照回滚失败");
                    }
                }

                _logger.LogError(ex, "读取本地进货单推送快照失败");
                throw;
            }
        }

        private sealed record LocalSupplierInvoicePushSourceSnapshot(
            List<StoreLocalSupplierInvoice> Invoices,
            List<StoreLocalSupplierInvoiceDetails> Details
        );
    }
}
