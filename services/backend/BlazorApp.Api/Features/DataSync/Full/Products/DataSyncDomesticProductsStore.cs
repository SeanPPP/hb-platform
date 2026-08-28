using System.Collections.Concurrent;
using System.Data;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.DataSync.Common;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using SqlSugar;

namespace BlazorApp.Api.Features.DataSync.Full.Products;

/// <summary>
/// DataSyncDomesticProductsStore 保留旧同步步骤的顺序，仅负责本切片的持久化与读取。
/// </summary>
internal sealed class DataSyncDomesticProductsStore : DataSyncSliceBase
{
    public DataSyncDomesticProductsStore(DataSyncSliceContext context)
        : base(context)
    {
    }

        public async Task<SyncResult> SyncDomesticProductsFromHqAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "开始同步国内商品数据",
            };
            var batchGuid = Guid.NewGuid();

            try
            {
                Logger.LogInformation("开始从HQ同步国内商品数据（新版本：增量更新模式）");

                // 第1步：获取旧版DIC_商品信息字典表数据，建立货号到商品编码的映射
                var dicProducts = await HqContext
                    .DIC_商品信息字典表Db.AsQueryable()
                    .Where(p =>
                        !string.IsNullOrEmpty(p.H货号) && !string.IsNullOrEmpty(p.H商品编码)
                    )
                    .ToListAsync();
                Logger.LogInformation("从HQ获取到旧版DIC表 {Count} 条商品数据", dicProducts.Count);

                // 建立货号到商品编码的映射表（用于处理货号相同但商品编码不同的情况）
                var itemNumberToProductCodeMap = dicProducts
                    .Where(p =>
                        !string.IsNullOrWhiteSpace(p.H货号)
                        && !string.IsNullOrWhiteSpace(p.H商品编码)
                    )
                    .GroupBy(p => p.H货号!)
                    .ToDictionary(
                        g => g.Key,
                        g => g.First().H商品编码! // 如果同一货号有多条记录，取第一条
                    );
                Logger.LogInformation(
                    "建立货号到商品编码映射表，共 {Count} 个货号",
                    itemNumberToProductCodeMap.Count
                );

                // 第2步：先获取HBSales数据库的商品数据（CPT_DIC_商品信息字典表）
                var hbSalesProducts = await HbSalesContext
                    .CPT_DIC_商品信息字典表Db.AsQueryable()
                    .Where(p => !string.IsNullOrEmpty(p.商品编码))
                    .ToListAsync();
                Logger.LogInformation("从HBSales获取到 {Count} 条商品数据", hbSalesProducts.Count);

                // 第3步：再获取HQ的商品数据（CPT_DIC_商品信息字典表）
                var hqProducts = await HqContext
                    .CPT_DIC_商品信息字典表_HQDb.AsQueryable()
                    .Where(p => !string.IsNullOrEmpty(p.商品编码))
                    .ToListAsync();
                Logger.LogInformation("从HQ获取到 {Count} 条商品数据", hqProducts.Count);

                if (!hbSalesProducts.Any() && !hqProducts.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "HBSales和HQ中都没有商品数据需要同步";
                    return result;
                }

                // 第4步：合并数据，HQ数据优先级更高，但商品编码以DIC表为准
                var mergedProducts = new Dictionary<string, CPT_DIC_商品信息字典表>();
                int productCodeCorrectionCount = 0; // 统计商品编码修正次数

                // 先添加HBSales数据
                foreach (var product in hbSalesProducts)
                {
                    if (!string.IsNullOrEmpty(product.商品编码))
                    {
                        // 🔥 检查是否需要根据货号修正商品编码
                        var correctProductCode = product.商品编码;
                        if (
                            !string.IsNullOrEmpty(product.HB货号)
                            && itemNumberToProductCodeMap.TryGetValue(
                                product.HB货号,
                                out var dicProductCode
                            )
                        )
                        {
                            if (dicProductCode != product.商品编码)
                            {
                                Logger.LogInformation(
                                    "商品编码修正: 货号 {ItemNumber} 的商品编码从 {OldCode} 修正为 {NewCode}（DIC表）",
                                    product.HB货号,
                                    product.商品编码,
                                    dicProductCode
                                );
                                correctProductCode = dicProductCode;
                                product.商品编码 = dicProductCode; // 修正商品编码
                                productCodeCorrectionCount++;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(correctProductCode))
                        {
                            mergedProducts[correctProductCode] = product;
                        }
                    }
                }

                // 再用HQ数据覆盖（HQ数据优先级更高），但保留HBSales的装箱数和体积数据
                foreach (var product in hqProducts)
                {
                    if (!string.IsNullOrEmpty(product.商品编码))
                    {
                        // 🔥 检查是否需要根据货号修正商品编码
                        var correctProductCode = product.商品编码;
                        if (
                            !string.IsNullOrEmpty(product.HB货号)
                            && itemNumberToProductCodeMap.TryGetValue(
                                product.HB货号,
                                out var dicProductCode
                            )
                        )
                        {
                            if (dicProductCode != product.商品编码)
                            {
                                Logger.LogInformation(
                                    "商品编码修正: 货号 {ItemNumber} 的商品编码从 {OldCode} 修正为 {NewCode}（DIC表）",
                                    product.HB货号,
                                    product.商品编码,
                                    dicProductCode
                                );
                                correctProductCode = dicProductCode;
                                product.商品编码 = dicProductCode; // 修正商品编码
                                productCodeCorrectionCount++;
                            }
                        }

                        // 如果已存在HBSales数据，保留其装箱数和体积字段
                        if (string.IsNullOrWhiteSpace(correctProductCode))
                        {
                            continue;
                        }

                        if (mergedProducts.ContainsKey(correctProductCode))
                        {
                            var existingProduct = mergedProducts[correctProductCode];
                            var preservedPackingQuantity = existingProduct.单件装箱数;
                            var preservedUnitVolume = existingProduct.单件体积;

                            // 用HQ数据覆盖
                            mergedProducts[correctProductCode] = product;

                            // 恢复HBSales的装箱数和体积数据（如果有值的话）
                            if (
                                preservedPackingQuantity.HasValue
                                && preservedPackingQuantity.Value > 0
                            )
                            {
                                mergedProducts[correctProductCode].单件装箱数 =
                                    preservedPackingQuantity;
                                Logger.LogDebug(
                                    "商品 {ProductCode} 保留HBSales装箱数: {PackingQuantity}",
                                    correctProductCode,
                                    preservedPackingQuantity
                                );
                            }
                            if (preservedUnitVolume.HasValue && preservedUnitVolume.Value > 0)
                            {
                                mergedProducts[correctProductCode].单件体积 = preservedUnitVolume;
                                Logger.LogDebug(
                                    "商品 {ProductCode} 保留HBSales体积: {UnitVolume}",
                                    correctProductCode,
                                    preservedUnitVolume
                                );
                            }
                        }
                        else
                        {
                            // 如果不存在HBSales数据，直接使用HQ数据
                            mergedProducts[correctProductCode] = product;
                        }
                    }
                }

                if (productCodeCorrectionCount > 0)
                {
                    Logger.LogInformation(
                        "✅ 根据DIC表货号映射，成功修正 {Count} 个商品的商品编码",
                        productCodeCorrectionCount
                    );
                }

                Logger.LogInformation(
                    "合并后共有 {Count} 条唯一商品数据，已保留HBSales的装箱数和体积数据",
                    mergedProducts.Count
                );

                // 第5步：使用AutoMapper批量转换数据
                var sourceProducts = mergedProducts.Values.ToList();
                var localProducts = new List<DomesticProduct>();
                var errorCount = 0;

                try
                {
                    // 批量映射转换（包含商品图片的智能处理）
                    localProducts = Mapper.Map<List<DomesticProduct>>(sourceProducts);
                    Logger.LogInformation(
                        "AutoMapper批量转换完成，共转换 {Count} 个商品（包含图片URL智能处理）",
                        localProducts.Count
                    );

                    // 修复可能存在的重复URL（从源数据库带来的）
                    int fixedUrlCount = 0;
                    foreach (var product in localProducts)
                    {
                        if (!string.IsNullOrWhiteSpace(product.ProductImage))
                        {
                            var originalUrl = product.ProductImage;
                            var fixedUrl = BlazorApp.Api.Utils.ImageUrlHelper.FixDuplicateUrl(
                                originalUrl
                            );

                            if (!string.IsNullOrWhiteSpace(fixedUrl) && fixedUrl != originalUrl)
                            {
                                product.ProductImage = fixedUrl;
                                fixedUrlCount++;
                                Logger.LogDebug(
                                    "修复重复URL: {ProductCode} - {Original} => {Fixed}",
                                    product.ProductCode,
                                    originalUrl,
                                    fixedUrl
                                );
                            }
                        }
                    }

                    if (fixedUrlCount > 0)
                    {
                        Logger.LogInformation(
                            "数据同步时自动修复了 {Count} 个重复的图片URL",
                            fixedUrlCount
                        );
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "AutoMapper批量转换失败");
                    errorCount = sourceProducts.Count;
                    localProducts = new List<DomesticProduct>();
                }

                if (localProducts.Any())
                {
                    // 第6步：获取现有的本地商品数据
                    var existingProducts = await LocalContext
                        .DomesticProductDb.AsQueryable()
                        .Where(p => !string.IsNullOrEmpty(p.ProductCode))
                        .ToListAsync();

                    var existingProductDict = existingProducts.ToDictionary(
                        p => p.ProductCode!,
                        p => p
                    );

                    var toInsert = new List<DomesticProduct>();
                    var toUpdate = new List<DomesticProduct>();

                    // 第7步：根据商品编码分类需要插入和更新的数据
                    foreach (var localProduct in localProducts)
                    {
                        if (!string.IsNullOrEmpty(localProduct.ProductCode))
                        {
                            if (
                                existingProductDict.TryGetValue(
                                    localProduct.ProductCode,
                                    out var existingProduct
                                )
                            )
                            {
                                toUpdate.Add(localProduct);
                            }
                            else
                            {
                                toInsert.Add(localProduct);
                            }
                        }
                    }

                    // 第8步：执行数据库操作
                    await LocalContext.Db.Ado.BeginTranAsync();
                    try
                    {
                        var auditProductCodes = new HashSet<string>(
                            localProducts
                                .Where(product => !string.IsNullOrWhiteSpace(product.ProductCode))
                                .Select(product => product.ProductCode.Trim()),
                            StringComparer.OrdinalIgnoreCase
                        );
                        var beforeSnapshots = await ChangeHistoryService.CaptureSnapshotsAsync(
                            auditProductCodes
                        );
                        var occurredAtUtc = DateTime.UtcNow;
                        int insertedCount = 0;
                        int updatedCount = 0;

                        // 使用大数据方法批量插入新数据
                        if (toInsert.Any())
                        {
                            insertedCount = LocalContext
                                .Db.Fastest<DomesticProduct>()
                                .BulkCopy(toInsert);
                            Logger.LogInformation(
                                "大数据批量插入 {Count} 条新商品数据",
                                insertedCount
                            );
                        }

                        // 使用大数据方法批量更新现有数据
                        if (toUpdate.Any())
                        {
                            updatedCount = LocalContext
                                .Db.Fastest<DomesticProduct>()
                                .BulkUpdate(toUpdate);
                            Logger.LogInformation(
                                "大数据批量更新 {Count} 条商品数据",
                                updatedCount
                            );
                        }

                        var afterSnapshots = await ChangeHistoryService.CaptureSnapshotsAsync(
                            auditProductCodes
                        );
                        await ChangeHistoryService.RecordChangesAsync(
                            beforeSnapshots,
                            afterSnapshots,
                            HistoryContextFactory.Create(
                                "DataSyncLegacyFull",
                                batchGuid,
                                occurredAtUtc
                            )
                        );

                        await LocalContext.Db.Ado.CommitTranAsync();

                        result.AddedCount = insertedCount;
                        result.UpdatedCount = updatedCount;
                        result.ErrorCount = errorCount;
                        result.IsSuccess = true;
                        result.Message =
                            $"国内商品数据同步成功，新增 {insertedCount} 个商品，更新 {updatedCount} 个商品，{errorCount} 个错误";
                        Logger.LogInformation(
                            "国内商品数据同步完成：新增 {AddedCount} 个商品，更新 {UpdatedCount} 个商品，{ErrorCount} 个错误",
                            insertedCount,
                            updatedCount,
                            errorCount
                        );
                    }
                    catch (Exception)
                    {
                        await LocalContext.Db.Ado.RollbackTranAsync();
                        throw;
                    }
                }
                else
                {
                    result.IsSuccess = false;
                    result.Message = "没有有效的商品数据可以同步";
                    result.ErrorCount = errorCount;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "同步国内商品数据时发生错误");
                result.Message = $"同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        public async Task<SyncResult> SyncProductPrefixCodesFromHqAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "开始同步货号前缀数据",
            };

            try
            {
                Logger.LogInformation("开始从HQ同步货号前缀数据（使用批量操作）");

                // 获取HBSales的货号前缀数据
                var hqPrefixCodes = await HbSalesContext.CPT_DIC_货号前缀信息表Db.GetListAsync();
                Logger.LogInformation("从HQ获取到 {Count} 条货号前缀数据", hqPrefixCodes.Count);

                if (!hqPrefixCodes.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "HQ中没有货号前缀数据需要同步";
                    return result;
                }

                // 批量映射数据
                var localPrefixes = new List<ProductPrefixCode>();
                var errorCount = 0;

                foreach (var hqPrefix in hqPrefixCodes)
                {
                    try
                    {
                        var localPrefix = new ProductPrefixCode
                        {
                            PrefixCode = UuidHelper.GenerateUuid7(),
                            SupplierCode = hqPrefix.供应商编码,
                            PrefixName = hqPrefix.HB货号前缀码,
                            PrefixDescription = hqPrefix.前缀描述,
                            IsActive = true,
                            SortOrder = hqPrefix.ID,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                        };

                        localPrefixes.Add(localPrefix);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(
                            ex,
                            "映射货号前缀 {PrefixCode} 时发生错误",
                            hqPrefix.HB货号前缀码
                        );
                        errorCount++;
                    }
                }

                if (localPrefixes.Any())
                {
                    // 开启事务进行批量操作
                    await LocalContext.Db.Ado.BeginTranAsync();
                    try
                    {
                        // 批量清空本地货号前缀表
                        await LocalContext
                            .Db.Deleteable<ProductPrefixCode>()
                            .ExecuteCommandAsync();
                        Logger.LogInformation("已清空本地货号前缀表");

                        // 批量插入新数据
                        var insertedCount = await LocalContext
                            .Db.Insertable(localPrefixes)
                            .PageSize(1000) // 分页批量插入
                            .ExecuteCommandAsync();

                        await LocalContext.Db.Ado.CommitTranAsync();

                        result.AddedCount = insertedCount;
                        result.ErrorCount = errorCount;
                        result.IsSuccess = true;
                        result.Message =
                            $"货号前缀数据同步成功，批量新增 {insertedCount} 个前缀，{errorCount} 个错误";
                        Logger.LogInformation(
                            "货号前缀数据批量同步完成：新增 {AddedCount} 个前缀，{ErrorCount} 个错误",
                            insertedCount,
                            errorCount
                        );
                    }
                    catch (Exception)
                    {
                        await LocalContext.Db.Ado.RollbackTranAsync();
                        throw;
                    }
                }
                else
                {
                    result.IsSuccess = false;
                    result.Message = "没有有效的货号前缀数据可以同步";
                    result.ErrorCount = errorCount;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "同步货号前缀数据时发生错误");
                result.Message = $"同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        public async Task<SyncResult> SyncDomesticSetProductsFromHqAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "开始同步套装商品数据",
            };

            try
            {
                Logger.LogInformation("开始从HQ同步套装商品数据（使用批量操作）");

                // 获取HBSales的套装商品数据
                var hqSetProducts = await HbSalesContext.CPT_DIC_商品套装信息表Db.GetListAsync();
                Logger.LogInformation("从HQ获取到 {Count} 条套装商品数据", hqSetProducts.Count);

                if (!hqSetProducts.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "HQ中没有套装商品数据需要同步";
                    return result;
                }

                // 批量映射数据
                var localSetProducts = new List<DomesticSetProduct>();
                var errorCount = 0;

                foreach (var hqSetProduct in hqSetProducts)
                {
                    try
                    {
                        var localSetProduct = new DomesticSetProduct
                        {
                            SetProductCode = UuidHelper.GenerateUuid7(),
                            ProductCode = hqSetProduct.商品编码 ?? string.Empty,
                            ProductNo = hqSetProduct.商品小货号,
                            SetProductNo = hqSetProduct.商品小货号 ?? string.Empty,
                            SetBarcode = hqSetProduct.条形码,
                            DomesticPrice = hqSetProduct.国内价格,
                            ImportPrice = hqSetProduct.进口价格,
                            OEMPrice = hqSetProduct.贴牌价格,
                            Remarks = hqSetProduct.备注,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                        };

                        localSetProducts.Add(localSetProduct);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(
                            ex,
                            "映射套装商品 {ProductCode} 时发生错误",
                            hqSetProduct.商品编码
                        );
                        errorCount++;
                    }
                }

                if (localSetProducts.Any())
                {
                    // 开启事务进行批量操作
                    await LocalContext.Db.Ado.BeginTranAsync();
                    try
                    {
                        // 批量清空本地套装商品表
                        await LocalContext
                            .Db.Deleteable<DomesticSetProduct>()
                            .ExecuteCommandAsync();
                        Logger.LogInformation("已清空本地套装商品表");

                        // 批量插入新数据
                        var insertedCount = await LocalContext
                            .Db.Insertable(localSetProducts)
                            .PageSize(1000) // 分页批量插入
                            .ExecuteCommandAsync();

                        await LocalContext.Db.Ado.CommitTranAsync();

                        result.AddedCount = insertedCount;
                        result.ErrorCount = errorCount;
                        result.IsSuccess = true;
                        result.Message =
                            $"套装商品数据同步成功，批量新增 {insertedCount} 个套装商品，{errorCount} 个错误";
                        Logger.LogInformation(
                            "套装商品数据批量同步完成：新增 {AddedCount} 个套装商品，{ErrorCount} 个错误",
                            insertedCount,
                            errorCount
                        );
                    }
                    catch (Exception)
                    {
                        await LocalContext.Db.Ado.RollbackTranAsync();
                        throw;
                    }
                }
                else
                {
                    result.IsSuccess = false;
                    result.Message = "没有有效的套装商品数据可以同步";
                    result.ErrorCount = errorCount;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "同步套装商品数据时发生错误");
                result.Message = $"同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }
}
