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

namespace BlazorApp.Api.Features.DataSync.Translation;

/// <summary>
/// DataSyncTranslationStore 保留旧同步步骤的顺序，仅负责本切片的持久化与读取。
/// </summary>
internal sealed class DataSyncTranslationStore : DataSyncSliceBase
{
    public DataSyncTranslationStore(DataSyncSliceContext context)
        : base(context)
    {
    }

        public async Task<SyncResult> TranslateAllProductNamesAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "",
            };

            try
            {
                Logger.LogInformation("开始批量翻译所有仓库商品名称");

                // 获取所有需要翻译的商品（现在从 Product 表获取）
                // 注意：WarehouseProduct 中的 ProductName 和 EnglishProductName 字段已移除
                var productsToTranslate = await LocalContext
                    .Db.Queryable<Product>()
                    .Where(p => p.ProductName != null && p.ProductName != "")
                    .Where(p => SqlFunc.IsNull(p.EnglishName, "") == "")
                    .ToListAsync();

                if (!productsToTranslate.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "没有需要翻译的仓库商品";
                    return result;
                }

                // 过滤包含中文的商品名称
                var chineseProducts = new List<Product>();
                foreach (var product in productsToTranslate)
                {
                    if (TranslationService.ContainsChinese(product.ProductName))
                    {
                        chineseProducts.Add(product);
                    }
                }

                if (!chineseProducts.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "没有包含中文的仓库商品需要翻译";
                    return result;
                }

                Logger.LogInformation("找到 {Count} 个需要翻译的仓库商品", chineseProducts.Count);

                // 提取中文名称进行批量翻译
                var chineseNames = chineseProducts.Select(p => p.ProductName).ToList();
                var translations = await TranslationService.BatchTranslateToEnglishAsync(
                    chineseNames
                );

                // 更新商品英文名称（现在更新 Product 表）
                var translatedProducts = new List<Product>();
                foreach (var product in chineseProducts)
                {
                    if (translations.ContainsKey(product.ProductName))
                    {
                        var englishName = translations[product.ProductName];
                        if (
                            !string.IsNullOrEmpty(englishName)
                            && englishName != product.ProductName
                            && !string.Equals(
                                product.EnglishName,
                                englishName,
                                StringComparison.Ordinal
                            )
                        )
                        {
                            product.EnglishName = englishName;
                            translatedProducts.Add(product);
                        }
                    }
                }

                var updatedCount = await SaveTranslatedProductsAsync(translatedProducts);

                result.UpdatedCount = updatedCount;
                result.IsSuccess = true;
                result.Message = $"批量翻译完成，成功翻译 {updatedCount} 个仓库商品名称";
                Logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "批量翻译仓库商品名称时发生错误");
                result.Message = $"翻译失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        public async Task<SyncResult> TranslateProductNamesAsync(
            string mode,
            string? productCodeFilter = null
        )
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "",
            };

            try
            {
                Logger.LogInformation(
                    "开始选择性翻译仓库商品名称，模式: {Mode}, 过滤器: {Filter}",
                    mode,
                    productCodeFilter ?? "无"
                );

                // 构建查询条件（修改为从 Product 表查询）
                var query = LocalContext
                    .Db.Queryable<Product>()
                    .Where(p => p.ProductName != null && p.ProductName != "");

                // 添加商品编码过滤
                if (!string.IsNullOrWhiteSpace(productCodeFilter))
                {
                    query = query.Where(p =>
                        p.ProductCode != null && p.ProductCode.Contains(productCodeFilter)
                    );
                }

                // 根据翻译模式添加条件
                switch (mode.ToLower())
                {
                    case "untranslated":
                        // 只翻译未翻译的商品
                        query = query.Where(p => SqlFunc.IsNull(p.EnglishName, "") == "");
                        break;
                    case "all_chinese":
                        // 翻译所有包含中文的商品（无论是否已有英文名称）
                        // 在后面过滤中文
                        break;
                    case "force_all":
                        // 强制重新翻译所有商品
                        break;
                    default:
                        result.Message = $"无效的翻译模式: {mode}";
                        return result;
                }

                var productsToCheck = await query.ToListAsync();

                if (!productsToCheck.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "没有符合条件的仓库商品需要翻译";
                    return result;
                }

                // 根据模式过滤需要翻译的商品
                var productsToTranslate = new List<Product>();
                foreach (var product in productsToCheck)
                {
                    bool shouldTranslate = false;

                    switch (mode.ToLower())
                    {
                        case "untranslated":
                            shouldTranslate = TranslationService.ContainsChinese(
                                product.ProductName
                            );
                            break;
                        case "all_chinese":
                            shouldTranslate = TranslationService.ContainsChinese(
                                product.ProductName
                            );
                            break;
                        case "force_all":
                            shouldTranslate = true;
                            break;
                    }

                    if (shouldTranslate)
                    {
                        productsToTranslate.Add(product);
                    }
                }

                if (!productsToTranslate.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "没有需要翻译的仓库商品";
                    return result;
                }

                Logger.LogInformation(
                    "找到 {Count} 个需要翻译的仓库商品",
                    productsToTranslate.Count
                );

                // 提取商品名称进行批量翻译
                var namesToTranslate = productsToTranslate.Select(p => p.ProductName).ToList();
                var translations = await TranslationService.BatchTranslateToEnglishAsync(
                    namesToTranslate
                );

                // 更新仓库商品英文名称
                var translatedProducts = new List<Product>();
                foreach (var product in productsToTranslate)
                {
                    if (translations.ContainsKey(product.ProductName))
                    {
                        var englishName = translations[product.ProductName];
                        if (
                            !string.IsNullOrEmpty(englishName)
                            && !string.Equals(
                                product.EnglishName,
                                englishName,
                                StringComparison.Ordinal
                            )
                        )
                        {
                            product.EnglishName = englishName;
                            translatedProducts.Add(product);
                        }
                    }
                }

                var updatedCount = await SaveTranslatedProductsAsync(translatedProducts);

                result.UpdatedCount = updatedCount;
                result.IsSuccess = true;
                result.Message = $"选择性翻译完成，成功翻译 {updatedCount} 个仓库商品名称";
                Logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "选择性翻译仓库商品名称时发生错误");
                result.Message = $"翻译失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }


        private async Task<int> SaveTranslatedProductsAsync(List<Product> translatedProducts)
        {
            var products = translatedProducts
                .Where(product => !string.IsNullOrWhiteSpace(product.ProductCode))
                .GroupBy(product => product.ProductCode!, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (products.Count == 0)
            {
                return 0;
            }

            var productCodes = products.Select(product => product.ProductCode!).ToList();
            var occurredAtUtc = DateTime.UtcNow;
            var actorName = CurrentUserService.GetCurrentUsername();
            foreach (var product in products)
            {
                product.UpdatedAt = occurredAtUtc;
                product.UpdatedBy = string.IsNullOrWhiteSpace(actorName) ? "System" : actorName;
            }

            await LocalContext.Db.Ado.BeginTranAsync();
            try
            {
                var beforeSnapshots = await ChangeHistoryService.CaptureSnapshotsAsync(
                    productCodes
                );
                await LocalContext.Db.Updateable(products)
                    .UpdateColumns(product => new
                    {
                        product.EnglishName,
                        product.UpdatedAt,
                        product.UpdatedBy,
                    })
                    .ExecuteCommandAsync();
                var afterSnapshots = await ChangeHistoryService.CaptureSnapshotsAsync(productCodes);
                await ChangeHistoryService.RecordChangesAsync(
                    beforeSnapshots,
                    afterSnapshots,
                    HistoryContextFactory.Create(
                        "ProductTranslation",
                        Guid.NewGuid(),
                        occurredAtUtc
                    )
                );
                await LocalContext.Db.Ado.CommitTranAsync();
                return products.Count;
            }
            catch
            {
                await LocalContext.Db.Ado.RollbackTranAsync();
                throw;
            }
        }
}
