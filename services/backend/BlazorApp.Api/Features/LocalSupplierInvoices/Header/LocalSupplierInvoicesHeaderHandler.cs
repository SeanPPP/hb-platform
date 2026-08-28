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
    internal sealed class LocalSupplierInvoicesHeaderHandler
    {
        private readonly LocalSupplierInvoicesDependencies _dependencies;
        private SqlSugarContext _context => _dependencies.Context;
        private HqSqlSugarContext _hqContext => _dependencies.HqContext;
        private IMapper _mapper => _dependencies.Mapper;
        private ILogger _logger => _dependencies.Logger;
        private IAutoPricingService _autoPricingService => _dependencies.AutoPricingService;
        private IWarehouseProductChangeHistoryService _changeHistoryService => _dependencies.ChangeHistoryService;
        private ILocalSupplierInvoiceHqProductSyncService? _hqProductSyncService => _dependencies.HqProductSyncService;

        public LocalSupplierInvoicesHeaderHandler(LocalSupplierInvoicesDependencies dependencies)
        {
            _dependencies = dependencies;
        }

        public async Task<ApiResponse<bool>> UpdateAsync(
            string invoiceGuid,
            UpdateInvoiceRequest dto
        )
        {
            try
            {
                var db = _context.Db;
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    await db.Ado.BeginTranAsync(IsolationLevel.Serializable);
                    try
                    {
                        var currentInvoice = await db.Queryable<StoreLocalSupplierInvoice>()
                            .FirstAsync(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false);
                        if (currentInvoice == null)
                        {
                            await db.Ado.RollbackTranAsync();
                            return ApiResponse<bool>.Error("数据不存在", "NOT_FOUND");
                        }

                        var targetStoreCode = string.IsNullOrWhiteSpace(dto.StoreCode)
                            ? null
                            : dto.StoreCode.Trim();
                        var targetSupplierCode = string.IsNullOrWhiteSpace(dto.SupplierCode)
                            ? null
                            : dto.SupplierCode.Trim();
                        var targetInvoiceNo = dto.InvoiceNo?.Trim();
                        if (dto.InvoiceNo != null && string.IsNullOrWhiteSpace(targetInvoiceNo))
                        {
                            await db.Ado.RollbackTranAsync();
                            return ApiResponse<bool>.Error("单号为必填", "VALIDATION_ERROR");
                        }

                        var storeChanged = targetStoreCode != null
                            && !string.Equals(currentInvoice.StoreCode, targetStoreCode, StringComparison.Ordinal);
                        var supplierChanged = targetSupplierCode != null
                            && !string.Equals(currentInvoice.SupplierCode, targetSupplierCode, StringComparison.Ordinal);
                        var invoiceNoChanged = targetInvoiceNo != null
                            && !string.Equals(currentInvoice.InvoiceNo, targetInvoiceNo, StringComparison.Ordinal);

                        // 编码实际变化时才校验目标，避免历史订单原编码已停用后无法修改其他字段。
                        if (storeChanged)
                        {
                            var validStore = await db.Queryable<Store>()
                                .AnyAsync(x =>
                                    x.StoreCode == targetStoreCode
                                    && x.IsDeleted == false
                                    && x.IsActive
                                );
                            if (!validStore)
                            {
                                await db.Ado.RollbackTranAsync();
                                return ApiResponse<bool>.Error("目标分店不存在或已停用", "INVALID_STORE");
                            }
                        }

                        if (supplierChanged)
                        {
                            var validSupplier = await db.Queryable<HBLocalSupplier>()
                                .AnyAsync(x =>
                                    x.LocalSupplierCode == targetSupplierCode
                                    && x.IsDeleted == false
                                    && x.Status == 1
                                );
                            if (!validSupplier)
                            {
                                await db.Ado.RollbackTranAsync();
                                return ApiResponse<bool>.Error("目标供应商不存在或已停用", "INVALID_SUPPLIER");
                            }
                        }

                        var effectiveStoreCode = storeChanged
                            ? targetStoreCode
                            : currentInvoice.StoreCode;
                        var effectiveSupplierCode = supplierChanged
                            ? targetSupplierCode
                            : currentInvoice.SupplierCode;
                        var effectiveInvoiceNo = invoiceNoChanged
                            ? targetInvoiceNo
                            : currentInvoice.InvoiceNo;
                        if (
                            (storeChanged || supplierChanged || invoiceNoChanged)
                            && !string.IsNullOrWhiteSpace(effectiveStoreCode)
                            && !string.IsNullOrWhiteSpace(effectiveSupplierCode)
                            && !string.IsNullOrWhiteSpace(effectiveInvoiceNo)
                        )
                        {
                            var duplicateExists = await db.Queryable<StoreLocalSupplierInvoice>()
                                .AnyAsync(x =>
                                    x.InvoiceGUID != invoiceGuid
                                    && x.IsDeleted == false
                                    && x.StoreCode == effectiveStoreCode
                                    && x.SupplierCode == effectiveSupplierCode
                                    && x.InvoiceNo == effectiveInvoiceNo
                                );
                            if (duplicateExists)
                            {
                                await db.Ado.RollbackTranAsync();
                                return DuplicateInvoiceError<bool>(
                                    effectiveStoreCode,
                                    effectiveSupplierCode,
                                    effectiveInvoiceNo
                                );
                            }
                        }

                        var now = DateTime.UtcNow;
                        var updater = db.Updateable<StoreLocalSupplierInvoice>()
                            .SetColumnsIF(dto.InvoiceNo != null, x => x.InvoiceNo == targetInvoiceNo)
                            .SetColumnsIF(dto.OrderDate != null, x => x.OrderDate == dto.OrderDate)
                            .SetColumnsIF(dto.InboundDate != null, x => x.InboundDate == dto.InboundDate)
                            .SetColumnsIF(dto.Remarks != null, x => x.Remarks == dto.Remarks)
                            .SetColumnsIF(dto.VoucherImage != null, x => x.VoucherImage == dto.VoucherImage)
                            .SetColumnsIF(dto.FlowStatus != null, x => x.FlowStatus == dto.FlowStatus)
                            .SetColumnsIF(
                                dto.InboundStatus != null,
                                x => x.InboundStatus == dto.InboundStatus
                            )
                            .SetColumnsIF(
                                storeChanged,
                                x => x.StoreCode == targetStoreCode
                            )
                            .SetColumnsIF(
                                supplierChanged,
                                x => x.SupplierCode == targetSupplierCode
                            )
                            .SetColumns(x => x.UpdatedAt == now)
                            .Where(x => x.InvoiceGUID == invoiceGuid);

                        var affected = await updater.ExecuteCommandAsync();

                        if (affected > 0 && (storeChanged || supplierChanged))
                        {
                            var detailUpdater = db.Updateable<StoreLocalSupplierInvoiceDetails>()
                                .SetColumnsIF(storeChanged, x => x.StoreCode == targetStoreCode)
                                .SetColumnsIF(supplierChanged, x => x.SupplierCode == targetSupplierCode)
                                .Where(x => x.InvoiceGUID == invoiceGuid);

                            await detailUpdater.ExecuteCommandAsync();

                            _logger.LogInformation(
                                "[InvoiceUpdate] 级联更新明细: InvoiceGUID={InvoiceGUID}, StoreCode={StoreCode}, SupplierCode={SupplierCode}",
                                invoiceGuid,
                                storeChanged ? targetStoreCode : "(不变)",
                                supplierChanged ? targetSupplierCode : "(不变)"
                            );
                        }

                        await db.Ado.CommitTranAsync();

                        return affected > 0
                            ? ApiResponse<bool>.OK(true)
                            : ApiResponse<bool>.Error("未更新任何字段", "NO_CHANGE");
                    }
                    catch (Exception ex) when (IsSerializationFailure(ex) && attempt == 0)
                    {
                        await db.Ado.RollbackTranAsync();
                        _logger.LogWarning(
                            ex,
                            "更新进货单发生序列化冲突，准备重试: InvoiceGUID={InvoiceGUID}",
                            invoiceGuid
                        );
                    }
                    catch (Exception ex) when (IsSerializationFailure(ex))
                    {
                        await db.Ado.RollbackTranAsync();
                        _logger.LogWarning(
                            ex,
                            "更新进货单连续发生序列化冲突: InvoiceGUID={InvoiceGUID}",
                            invoiceGuid
                        );
                        return ApiResponse<bool>.Error(
                            "更新进货单并发冲突，请重试",
                            "UPDATE_RETRY_REQUIRED"
                        );
                    }
                    catch
                    {
                        await db.Ado.RollbackTranAsync();
                        throw;
                    }
                }

                return ApiResponse<bool>.Error(
                    "更新进货单并发冲突，请重试",
                    "UPDATE_RETRY_REQUIRED"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新进货单失败");
                var msg = ex.InnerException?.Message ?? ex.Message ?? "更新失败";
                return ApiResponse<bool>.Error(msg, "UPDATE_ERROR");
            }
        }

        public async Task<ApiResponse<string>> CreateAsync(CreateInvoiceRequest dto)
        {
            var storeCode = dto.StoreCode?.Trim();
            var supplierCode = dto.SupplierCode?.Trim();
            var invoiceNo = dto.InvoiceNo?.Trim();
            try
            {
                if (
                    string.IsNullOrWhiteSpace(storeCode)
                    || string.IsNullOrWhiteSpace(supplierCode)
                    || string.IsNullOrWhiteSpace(invoiceNo)
                )
                    return ApiResponse<string>.Error(
                        "分店、供应商、单号为必填",
                        "VALIDATION_ERROR"
                    );

                var db = _context.Db;

                var invoiceGuid = UuidHelper.GenerateUuid7();
                var now = DateTime.UtcNow;
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    await db.Ado.BeginTranAsync(IsolationLevel.Serializable);
                    try
                    {
                        var existingInvoice = await db.Queryable<StoreLocalSupplierInvoice>()
                            .Where(x => x.IsDeleted == false)
                            .Where(x => x.StoreCode == storeCode)
                            .Where(x => x.SupplierCode == supplierCode)
                            .Where(x => x.InvoiceNo == invoiceNo)
                            .FirstAsync();

                        if (existingInvoice != null)
                        {
                            await db.Ado.RollbackTranAsync();
                            return DuplicateInvoiceError<string>(storeCode, supplierCode, invoiceNo);
                        }

                        // 主表、明细、总金额更新必须处于同一事务，避免中途失败后留下脏数据。
                        var header = new StoreLocalSupplierInvoice
                        {
                            InvoiceGUID = invoiceGuid,
                            StoreCode = storeCode,
                            SupplierCode = supplierCode,
                            InvoiceNo = invoiceNo,
                            OrderDate = dto.OrderDate,
                            InboundDate = dto.InboundDate,
                            Remarks = dto.Remarks,
                            FlowStatus = 0,
                            InboundStatus = 0,
                            CreatedAt = now,
                            UpdatedAt = now,
                            IsDeleted = false,
                        };

                        await db.Insertable(header).ExecuteCommandAsync();

                        var validItems = (dto.Items ?? new List<PastedDetailItem>())
                            .Where(i => i != null && i.Quantity > 0 && i.Price > 0)
                            .ToList();

                        var detailRows = validItems
                            .Select(i => new StoreLocalSupplierInvoiceDetails
                            {
                                DetailGUID = UuidHelper.GenerateUuid7(),
                                InvoiceGUID = invoiceGuid,
                                StoreCode = storeCode,
                                SupplierCode = supplierCode,
                                StoreProductCode = i.StoreProductCode,
                                ProductCode = i.ProductCode,
                                ItemNumber = i.ItemNumber,
                                Barcode = !string.IsNullOrWhiteSpace(i.Barcode)
                                    ? i.Barcode
                                    : (
                                        LocalSupplierInvoicesBarcodeRules.IsLikelyBarcode(
                                            i.NameOrBarcode
                                        )
                                            ? i.NameOrBarcode
                                            : null
                                    ),
                                ProductName = !string.IsNullOrWhiteSpace(i.ProductName)
                                    ? i.ProductName
                                    : (
                                        string.IsNullOrWhiteSpace(i.Barcode)
                                        && !LocalSupplierInvoicesBarcodeRules.IsLikelyBarcode(
                                            i.NameOrBarcode
                                        )
                                            ? i.NameOrBarcode
                                            : null
                                    ),
                                Quantity = i.Quantity,
                                PurchasePrice = i.Price,
                                LastPurchasePrice = i.LastPurchasePrice,
                                RetailPrice = i.RetailPrice,
                                AutoPricing = i.AutoPricing,
                                PricingFloatRate = i.PricingFloatRate,
                                NewAutoRetailPrice = i.NewAutoRetailPrice,
                                IsSpecialProduct = i.IsSpecialProduct,
                                Amount = i.Price * i.Quantity,
                                CreatedAt = now,
                                UpdatedAt = now,
                                IsDeleted = false,
                            })
                            .ToList();

                        if (detailRows.Count > 0)
                            await db.Insertable(detailRows).ExecuteCommandAsync();

                        var total = detailRows.Sum(x => x.Amount ?? 0);
                        await db.Updateable<StoreLocalSupplierInvoice>()
                            .SetColumns(x => x.TotalAmount == total)
                            .SetColumns(x => x.UpdatedAt == now)
                            .Where(x => x.InvoiceGUID == invoiceGuid)
                            .ExecuteCommandAsync();

                        await db.Ado.CommitTranAsync();
                        return ApiResponse<string>.OK(invoiceGuid);
                    }
                    catch (Exception ex) when (IsUniqueConstraintViolation(ex))
                    {
                        await db.Ado.RollbackTranAsync();
                        _logger.LogWarning(
                            ex,
                            "创建进货单命中数据库唯一约束: StoreCode={StoreCode}, SupplierCode={SupplierCode}, InvoiceNo={InvoiceNo}",
                            storeCode,
                            supplierCode,
                            invoiceNo
                        );
                        return DuplicateInvoiceError<string>(storeCode, supplierCode, invoiceNo);
                    }
                    catch (Exception ex) when (IsSerializationFailure(ex) && attempt == 0)
                    {
                        await db.Ado.RollbackTranAsync();
                        _logger.LogWarning(
                            ex,
                            "创建进货单发生序列化冲突，准备重试: StoreCode={StoreCode}, SupplierCode={SupplierCode}, InvoiceNo={InvoiceNo}",
                            storeCode,
                            supplierCode,
                            invoiceNo
                        );
                    }
                    catch (Exception ex) when (IsSerializationFailure(ex))
                    {
                        await db.Ado.RollbackTranAsync();
                        _logger.LogWarning(
                            ex,
                            "创建进货单连续发生序列化冲突: StoreCode={StoreCode}, SupplierCode={SupplierCode}, InvoiceNo={InvoiceNo}",
                            storeCode,
                            supplierCode,
                            invoiceNo
                        );
                        return ApiResponse<string>.Error(
                            "创建进货单并发冲突，请重试",
                            "CREATE_RETRY_REQUIRED"
                        );
                    }
                    catch
                    {
                        await db.Ado.RollbackTranAsync();
                        throw;
                    }
                }

                return ApiResponse<string>.Error(
                    "创建进货单并发冲突，请重试",
                    "CREATE_RETRY_REQUIRED"
                );
            }
            catch (Exception ex) when (IsUniqueConstraintViolation(ex))
            {
                _logger.LogWarning(
                    ex,
                    "创建进货单命中数据库唯一约束: StoreCode={StoreCode}, SupplierCode={SupplierCode}, InvoiceNo={InvoiceNo}",
                    storeCode,
                    supplierCode,
                    invoiceNo
                );
                return DuplicateInvoiceError<string>(storeCode, supplierCode, invoiceNo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建进货单失败");
                var msg = ex.InnerException?.Message ?? ex.Message ?? "创建失败";
                return ApiResponse<string>.Error(msg, "CREATE_ERROR");
            }
        }

        private static ApiResponse<T> DuplicateInvoiceError<T>(
            string? storeCode,
            string? supplierCode,
            string? invoiceNo
        )
        {
            return ApiResponse<T>.Error(
                $"分店【{storeCode}】、供应商【{supplierCode}】、单号【{invoiceNo}】已存在，不能重复保存",
                "DUPLICATE_INVOICE"
            );
        }

        private static bool IsSerializationFailure(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (
                    current
                    is PostgresException
                    {
                        SqlState: PostgresErrorCodes.SerializationFailure,
                    }
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsUniqueConstraintViolation(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                // SQL Server 唯一索引/唯一约束冲突分别对应 2601/2627。
                if (current is SqlException { Number: 2601 or 2627 })
                {
                    return true;
                }

                // PostgreSQL 唯一约束冲突 SQLSTATE=23505。
                if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    return true;
                }
            }

            return false;
        }

        public async Task<ApiResponse<bool>> DeleteAsync(string invoiceGuid, string updatedBy)
        {
            try
            {
                var db = _context.Db;
                await db.Ado.BeginTranAsync();
                try
                {
                    var now = DateTime.UtcNow;
                    var affectedHeader = await db.Updateable<StoreLocalSupplierInvoice>()
                        .SetColumns(x => x.IsDeleted == true)
                        .SetColumns(x => x.UpdatedAt == now)
                        .SetColumns(x => x.UpdatedBy == updatedBy)
                        .Where(x => x.InvoiceGUID == invoiceGuid)
                        .ExecuteCommandAsync();
                    var affectedDetails = await db.Updateable<StoreLocalSupplierInvoiceDetails>()
                        .SetColumns(x => x.IsDeleted == true)
                        .SetColumns(x => x.UpdatedAt == now)
                        .SetColumns(x => x.UpdatedBy == updatedBy)
                        .Where(x => x.InvoiceGUID == invoiceGuid)
                        .ExecuteCommandAsync();
                    await db.Ado.CommitTranAsync();
                    return ApiResponse<bool>.OK(true, $"已删除单据及 {affectedDetails} 条明细");
                }
                catch (Exception exTran)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(exTran, "删除进货单事务失败");
                    return ApiResponse<bool>.Error("删除失败", "DELETE_ERROR");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除进货单失败");
                return ApiResponse<bool>.Error("删除失败", "DELETE_ERROR");
            }
        }

    }
}
