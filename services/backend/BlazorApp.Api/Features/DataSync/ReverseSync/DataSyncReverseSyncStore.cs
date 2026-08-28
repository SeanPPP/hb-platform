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

namespace BlazorApp.Api.Features.DataSync.ReverseSync;

/// <summary>
/// DataSyncReverseSyncStore 保留旧同步步骤的顺序，仅负责本切片的持久化与读取。
/// </summary>
internal sealed class DataSyncReverseSyncStore : DataSyncSliceBase
{
    public DataSyncReverseSyncStore(DataSyncSliceContext context)
        : base(context)
    {
    }

        public async Task<SyncResult> SyncDomesticProductsToHqAsync(DateTime lastUpdateDate)
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "国内商品信息反向同步失败",
            };

            try
            {
                Logger.LogInformation(
                    "开始反向同步国内商品信息到HQ数据库，上次更新时间: {LastUpdateDate}",
                    lastUpdateDate
                );

                // 查询本地DomesticProduct表中需要同步的商品（按更新时间筛选，且商品编码不为空）
                var domesticProducts = await LocalContext
                    .DomesticProductDb.AsQueryable()
                    .Where(dp =>
                        dp.UpdatedAt >= lastUpdateDate
                        && !string.IsNullOrEmpty(dp.ProductCode)
                        && dp.IsActive == true
                    )
                    .ToListAsync();

                if (!domesticProducts.Any())
                {
                    result.IsSuccess = true;
                    result.Message =
                        $"没有找到 {lastUpdateDate:yyyy-MM-dd HH:mm:ss} 之后更新的国内商品信息，无需同步";
                    Logger.LogInformation(result.Message);
                    return result;
                }

                Logger.LogInformation($"找到 {domesticProducts.Count} 个需要反向同步的国内商品");

                int totalUpdated = 0;
                int totalErrors = 0;

                // 分批处理商品
                const int batchSize = 100;
                for (int i = 0; i < domesticProducts.Count; i += batchSize)
                {
                    var batch = domesticProducts.Skip(i).Take(batchSize).ToList();
                    var productCodes = batch
                        .Where(p => !string.IsNullOrEmpty(p.ProductCode))
                        .Select(p => p.ProductCode!)
                        .ToList();

                    if (!productCodes.Any())
                        continue;

                    try
                    {
                        // 查询HQ数据库中对应的商品信息字典记录（通过商品编码匹配）
                        // 移除更新日期限制，因为我们需要处理所有本地商品（包括新建）
                        var hqProducts = await HbSalesContext
                            .CPT_DIC_商品信息字典表Db.AsQueryable()
                            .Where(hp =>
                                !string.IsNullOrEmpty(hp.商品编码)
                                && productCodes.Contains(hp.商品编码)
                            )
                            .ToListAsync();

                        Logger.LogInformation(
                            $"批次 {i / batchSize + 1}: 本地商品 {productCodes.Count} 个，HQ中已存在 {hqProducts.Count} 个，需新建 {productCodes.Count - hqProducts.Count} 个"
                        );

                        // 创建更新映射（通过商品编码）
                        var hqProductDict = hqProducts.ToDictionary(
                            hp => hp.商品编码 ?? "",
                            hp => hp
                        );

                        // 批量准备需要更新的商品数据
                        var batchUpdates = new List<CPT_DIC_商品信息字典表>();
                        var batchInserts = new List<CPT_DIC_商品信息字典表>(); // 新增：批量插入的商品数据

                        foreach (var domesticProduct in batch)
                        {
                            if (string.IsNullOrEmpty(domesticProduct.ProductCode))
                                continue;

                            if (
                                hqProductDict.TryGetValue(
                                    domesticProduct.ProductCode,
                                    out var hqProduct
                                )
                            )
                            {
                                var isUpdated = false;
                                var originalProduct = new CPT_DIC_商品信息字典表();
                                originalProduct = hqProduct;

                                if (originalProduct.使用状态 == null)
                                {
                                    originalProduct.使用状态 = 1;
                                    isUpdated = true;
                                }
                                if (originalProduct.HGUID == null)
                                {
                                    originalProduct.HGUID = Guid.NewGuid().ToString();
                                    isUpdated = true;
                                }
                                if (originalProduct.供应商编码 == null)
                                {
                                    originalProduct.供应商编码 = domesticProduct.SupplierCode;
                                    isUpdated = true;
                                }
                                if (originalProduct.HB货号 == null)
                                {
                                    originalProduct.HB货号 = domesticProduct.HBProductNo;
                                    isUpdated = true;
                                }

                                if (originalProduct.条形码 == null)
                                {
                                    originalProduct.条形码 = domesticProduct.Barcode;
                                    isUpdated = true;
                                }

                                if (originalProduct.FGC_Creator == null)
                                {
                                    originalProduct.FGC_Creator = domesticProduct.CreatedBy;
                                    isUpdated = true;
                                }
                                if (originalProduct.FGC_CreateDate == default)
                                {
                                    originalProduct.FGC_CreateDate = domesticProduct.CreatedAt;
                                    isUpdated = true;
                                }
                                if (originalProduct.FGC_LastModifyDate == default)
                                {
                                    originalProduct.FGC_LastModifyDate = DateTime.Now;
                                    isUpdated = true;
                                }
                                if (originalProduct.FGC_LastModifier == null)
                                {
                                    originalProduct.FGC_LastModifier = domesticProduct.UpdatedBy;
                                    isUpdated = true;
                                }
                                if (originalProduct.FGC_UpdateHelp == null)
                                {
                                    originalProduct.FGC_UpdateHelp = Guid.NewGuid().ToString();
                                    isUpdated = true;
                                }
                                if (originalProduct.使用状态 == null)
                                {
                                    originalProduct.使用状态 = 1;
                                    isUpdated = true;
                                }

                                // 只有当源数据字段不为空时才进行更新
                                if (!string.IsNullOrEmpty(domesticProduct.EnglishProductName))
                                {
                                    originalProduct.英文名称 = domesticProduct.EnglishProductName;
                                    isUpdated = true;
                                }

                                if (!string.IsNullOrEmpty(domesticProduct.ProductName))
                                {
                                    originalProduct.中文名称 = domesticProduct.ProductName;
                                    isUpdated = true;
                                }

                                if (domesticProduct.ProductType > 0)
                                {
                                    originalProduct.商品类型 = domesticProduct.ProductType;
                                    isUpdated = true;
                                }

                                if (
                                    domesticProduct.MiddlePackQuantity.HasValue
                                    && domesticProduct.MiddlePackQuantity.Value > 0
                                )
                                {
                                    originalProduct.中包数量 = domesticProduct
                                        .MiddlePackQuantity
                                        .Value;
                                    isUpdated = true;
                                }

                                if (
                                    domesticProduct.DomesticPrice.HasValue
                                    && domesticProduct.DomesticPrice.Value > 0
                                )
                                {
                                    originalProduct.国内价格 = domesticProduct.DomesticPrice.Value;
                                    isUpdated = true;
                                }

                                if (
                                    domesticProduct.ImportPrice.HasValue
                                    && domesticProduct.ImportPrice.Value > 0
                                )
                                {
                                    originalProduct.进口价格 = domesticProduct.ImportPrice.Value;
                                    isUpdated = true;
                                }

                                if (
                                    domesticProduct.OEMPrice.HasValue
                                    && domesticProduct.OEMPrice.Value > 0
                                )
                                {
                                    originalProduct.贴牌价格 = domesticProduct.OEMPrice.Value;
                                    isUpdated = true;
                                }

                                // 处理商品图片：修复可能的重复URL，如果为空则使用默认地址+货号
                                // 确保HBProductNo不是完整的URL，避免重复拼接
                                string? productImage = domesticProduct.ProductImage;

                                // 先修复可能存在的重复URL（防止污染HQ数据库）
                                if (!string.IsNullOrEmpty(productImage))
                                {
                                    productImage =
                                        BlazorApp.Api.Utils.ImageUrlHelper.FixDuplicateUrl(
                                            productImage
                                        )
                                        ?? productImage;
                                }

                                // 如果为空，则根据货号生成
                                if (
                                    string.IsNullOrEmpty(productImage)
                                    && !string.IsNullOrEmpty(domesticProduct.HBProductNo)
                                    && !domesticProduct.HBProductNo.StartsWith(
                                        "http://",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                    && !domesticProduct.HBProductNo.StartsWith(
                                        "https://",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                )
                                {
                                    productImage =
                                        $"https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/{domesticProduct.HBProductNo}.jpg";
                                }

                                if (!string.IsNullOrEmpty(productImage))
                                {
                                    originalProduct.商品图片 = productImage;
                                    isUpdated = true;
                                }

                                if (
                                    domesticProduct.PackingQuantity.HasValue
                                    && domesticProduct.PackingQuantity.Value > 0
                                )
                                {
                                    originalProduct.单件装箱数 = domesticProduct
                                        .PackingQuantity
                                        .Value;
                                    isUpdated = true;
                                }

                                if (
                                    domesticProduct.UnitVolume.HasValue
                                    && domesticProduct.UnitVolume.Value > 0
                                )
                                {
                                    originalProduct.单件体积 = domesticProduct.UnitVolume.Value;
                                    isUpdated = true;
                                }
                                originalProduct.FGC_LastModifyDate = DateTime.Now;

                                if (isUpdated)
                                {
                                    batchUpdates.Add(originalProduct);
                                }
                            }
                            else
                            {
                                // 如果在HQ数据库中找不到对应的商品记录，则创建新商品
                                Logger.LogInformation(
                                    $"在HQ数据库中未找到对应的商品记录，准备新建: 商品编码={domesticProduct.ProductCode}, HB货号={domesticProduct.HBProductNo}"
                                );

                                var newHqProduct = new CPT_DIC_商品信息字典表
                                {
                                    商品编码 = domesticProduct.ProductCode,
                                    供应商编码 = domesticProduct.SupplierCode,
                                    HB货号 = domesticProduct.HBProductNo,
                                    中文名称 = domesticProduct.ProductName,
                                    英文名称 = domesticProduct.EnglishProductName,
                                    条形码 = domesticProduct.Barcode,
                                    商品类型 = domesticProduct.ProductType,
                                    中包数量 = domesticProduct.MiddlePackQuantity ?? 0,
                                    国内价格 = domesticProduct.DomesticPrice ?? 0,
                                    进口价格 = domesticProduct.ImportPrice ?? 0,
                                    贴牌价格 = domesticProduct.OEMPrice ?? 0,
                                    单件装箱数 = domesticProduct.PackingQuantity ?? 0,
                                    单件体积 = domesticProduct.UnitVolume ?? 0,
                                    使用状态 = domesticProduct.IsActive ? 1 : 0,
                                    HGUID = Guid.NewGuid().ToString(),
                                    FGC_Creator = domesticProduct.CreatedBy ?? "System",
                                    FGC_CreateDate = domesticProduct.CreatedAt,
                                    FGC_LastModifyDate = DateTime.Now,
                                    FGC_LastModifier = domesticProduct.UpdatedBy ?? "System",
                                    FGC_UpdateHelp = Guid.NewGuid().ToString(),
                                };

                                // 处理商品图片：修复可能的重复URL，如果为空则使用默认地址+货号
                                // 确保HBProductNo不是完整的URL，避免重复拼接
                                string? productImage = domesticProduct.ProductImage;

                                // 先修复可能存在的重复URL（防止污染HQ数据库）
                                if (!string.IsNullOrEmpty(productImage))
                                {
                                    productImage =
                                        BlazorApp.Api.Utils.ImageUrlHelper.FixDuplicateUrl(
                                            productImage
                                        )
                                        ?? productImage;
                                }

                                // 如果为空，则根据货号生成
                                if (
                                    string.IsNullOrEmpty(productImage)
                                    && !string.IsNullOrEmpty(domesticProduct.HBProductNo)
                                    && !domesticProduct.HBProductNo.StartsWith(
                                        "http://",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                    && !domesticProduct.HBProductNo.StartsWith(
                                        "https://",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                )
                                {
                                    productImage =
                                        $"https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/{domesticProduct.HBProductNo}.jpg";
                                }
                                newHqProduct.商品图片 = productImage;

                                batchInserts.Add(newHqProduct);
                            }
                        }

                        // 使用大数据方法执行批量更新
                        if (batchUpdates.Any())
                        {
                            try
                            {
                                var batchUpdateCount = HbSalesContext
                                    .Db.Fastest<CPT_DIC_商品信息字典表>()
                                    .BulkUpdate(batchUpdates);

                                totalUpdated += batchUpdateCount;
                                Logger.LogInformation(
                                    $"批次 {i / batchSize + 1} 大数据批量更新完成，更新了 {batchUpdateCount} 个商品"
                                );
                            }
                            catch (Exception batchEx)
                            {
                                Logger.LogError(
                                    batchEx,
                                    $"批次 {i / batchSize + 1} 大数据批量更新失败"
                                );
                                totalErrors += batchUpdates.Count;
                            }
                        }
                        else
                        {
                            Logger.LogInformation(
                                $"批次 {i / batchSize + 1} 没有需要更新的商品数据"
                            );
                        }

                        // 使用大数据方法执行批量插入新商品
                        if (batchInserts.Any())
                        {
                            try
                            {
                                var batchInsertCount = HbSalesContext
                                    .Db.Fastest<CPT_DIC_商品信息字典表>()
                                    .BulkCopy(batchInserts);

                                totalUpdated += batchInsertCount;
                                Logger.LogInformation(
                                    $"批次 {i / batchSize + 1} 大数据批量插入完成，新建了 {batchInsertCount} 个商品"
                                );
                            }
                            catch (Exception batchEx)
                            {
                                Logger.LogError(
                                    batchEx,
                                    $"批次 {i / batchSize + 1} 大数据批量插入失败"
                                );
                                totalErrors += batchInserts.Count;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"批次 {i / batchSize + 1} 处理失败");
                        totalErrors += batch.Count;
                    }
                }

                // 设置结果
                result.UpdatedCount = totalUpdated;
                result.ErrorCount = totalErrors;
                result.IsSuccess = totalErrors == 0;
                result.Message =
                    $"国内商品信息反向同步完成！成功同步（更新+新建）: {totalUpdated} 个，错误: {totalErrors} 个";

                Logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "反向同步国内商品信息时发生错误");
                result.Message = $"反向同步失败: {ex.Message}";
                result.IsSuccess = false;
                result.ErrorCount = 1;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }
}
