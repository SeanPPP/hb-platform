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
/// DataSyncMasterDataStore 保留旧同步步骤的顺序，仅负责本切片的持久化与读取。
/// </summary>
internal sealed class DataSyncMasterDataStore : DataSyncSliceBase
{
    public DataSyncMasterDataStore(DataSyncSliceContext context)
        : base(context)
    {
    }

        public async Task<SyncResult> SyncSuppliersFromHqAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "",
                AddedCount = 0,
                UpdatedCount = 0,
                ErrorCount = 0,
            };

            try
            {
                Logger.LogInformation("🚀 开始从HQ数据库同步供应商数据（AutoMapper模式）...");

                // 1. 检查HQ数据库连接
                HqContext.CheckConnection();

                // 2. 从HQ数据库获取供应商数据 (使用批量操作)
                const int batchSize = 5000; // 每批处理5000条记录
                var totalProcessed = 0;
                var totalAdded = 0;
                var totalUpdated = 0;
                var totalErrors = 0;
                var pageNumber = 1;

                while (true)
                {
                    var hqSuppliersBatch = await HqContext
                        .CBP_DIC_国内供应商信息表Db.AsQueryable()
                        .Skip((pageNumber - 1) * batchSize)
                        .Take(batchSize)
                        .ToListAsync();

                    if (!hqSuppliersBatch.Any())
                        break; // 没有更多数据

                    Logger.LogInformation(
                        $"从HQ数据库获取到第 {pageNumber} 批供应商，共 {hqSuppliersBatch.Count} 条"
                    );

                    // 🚀 使用AutoMapper将HQ实体转换为本地实体列表
                    var chinaSuppliers = Mapper.Map<List<ChinaSupplier>>(hqSuppliersBatch);

                    Logger.LogInformation(
                        $"AutoMapper转换完成，生成 {chinaSuppliers.Count} 个ChinaSupplier对象"
                    );

                    // 🚀 使用Storageable处理Insert/Update逻辑
                    try
                    {
                        var storageResult = await LocalContext
                            .Db.Storageable(chinaSuppliers)
                            .WhereColumns(x => x.SupplierCode) // 基于供应商编码进行判断
                            .ToStorageAsync();

                        // 执行插入和更新
                        var insertResult = storageResult.AsInsertable.ExecuteCommand();
                        var updateResult = storageResult.AsUpdateable.ExecuteCommand();

                        totalAdded += insertResult;
                        totalUpdated += updateResult;

                        Logger.LogInformation(
                            $"第 {pageNumber} 批供应商AutoMapper处理完成 - 新增: {insertResult}, 更新: {updateResult}"
                        );

                        // 🚀 输出前3个转换结果的示例（用于调试）
                        foreach (var supplier in chinaSuppliers.Take(3))
                        {
                            Logger.LogDebug(
                                $"   示例供应商: {supplier.SupplierCode} - {supplier.SupplierName} (状态: {supplier.Status})"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"第 {pageNumber} 批供应商AutoMapper处理失败");
                        totalErrors += hqSuppliersBatch.Count;
                    }

                    totalProcessed += hqSuppliersBatch.Count;
                    pageNumber++;
                }

                result.AddedCount = totalAdded;
                result.UpdatedCount = totalUpdated;
                result.ErrorCount = totalErrors;
                result.IsSuccess = totalErrors == 0;
                result.Message =
                    $"供应商同步完成（使用AutoMapper转换）！总共处理: {totalProcessed}, 新增: {totalAdded}, 更新: {totalUpdated}, 错误: {totalErrors}";
                Logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "同步供应商数据时发生错误");
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

        private async Task SyncSingleSupplierAsync(
            CBP_DIC_国内供应商信息表 hqSupplier,
            List<ChinaSupplier> localSuppliers,
            SyncResult result
        )
        {
            // 查找本地是否已存在该供应商（根据供应商编码）
            var existingSupplier = localSuppliers.FirstOrDefault(s =>
                s.SupplierCode == hqSupplier.H供应商编码
            );

            if (existingSupplier == null)
            {
                // 新增供应商
                var newSupplier = new ChinaSupplier
                {
                    Guid = hqSupplier.HGUID ?? UuidHelper.GenerateUuid7(),
                    SupplierCode = hqSupplier.H供应商编码,
                    SupplierName = hqSupplier.H供应商名称,
                    ShopNumber = hqSupplier.H商铺编号,
                    ContactPerson = hqSupplier.H联系人,
                    Phone = hqSupplier.H电话,
                    Email = hqSupplier.HEMAIL地址,
                    StorefrontPhoto = hqSupplier.H商户门头照片,
                    Remarks = hqSupplier.备注,
                    Status = hqSupplier.状态,
                    FGC_Creator = hqSupplier.FGC_Creator,
                    FGC_CreateDate = hqSupplier.FGC_CreateDate,
                    FGC_LastModifier = hqSupplier.FGC_LastModifier,
                    FGC_LastModifyDate = hqSupplier.FGC_LastModifyDate,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                };

                await LocalContext.ChinaSupplierDb.InsertAsync(newSupplier);
                result.AddedCount++;
                Logger.LogInformation(
                    $"新增供应商: {newSupplier.SupplierCode} - {newSupplier.SupplierName}"
                );
            }
            else
            {
                // 更新现有供应商（只更新关键信息）
                bool needUpdate = false;

                if (existingSupplier.SupplierName != hqSupplier.H供应商名称)
                {
                    existingSupplier.SupplierName = hqSupplier.H供应商名称;
                    needUpdate = true;
                }

                if (existingSupplier.ShopNumber != hqSupplier.H商铺编号)
                {
                    existingSupplier.ShopNumber = hqSupplier.H商铺编号;
                    needUpdate = true;
                }

                if (existingSupplier.ContactPerson != hqSupplier.H联系人)
                {
                    existingSupplier.ContactPerson = hqSupplier.H联系人;
                    needUpdate = true;
                }

                if (existingSupplier.Phone != hqSupplier.H电话)
                {
                    existingSupplier.Phone = hqSupplier.H电话;
                    needUpdate = true;
                }

                if (existingSupplier.Email != hqSupplier.HEMAIL地址)
                {
                    existingSupplier.Email = hqSupplier.HEMAIL地址;
                    needUpdate = true;
                }

                if (existingSupplier.Remarks != hqSupplier.备注)
                {
                    existingSupplier.Remarks = hqSupplier.备注;
                    needUpdate = true;
                }

                if (existingSupplier.Status != hqSupplier.状态)
                {
                    existingSupplier.Status = hqSupplier.状态;
                    needUpdate = true;
                }

                if (needUpdate)
                {
                    existingSupplier.FGC_LastModifier = hqSupplier.FGC_LastModifier;
                    existingSupplier.FGC_LastModifyDate = hqSupplier.FGC_LastModifyDate;
                    existingSupplier.UpdatedAt = DateTime.Now;
                    await LocalContext.ChinaSupplierDb.UpdateAsync(existingSupplier);
                    result.UpdatedCount++;
                    Logger.LogInformation(
                        $"更新供应商: {existingSupplier.SupplierCode} - {existingSupplier.SupplierName}"
                    );
                }
            }
        }

        public async Task<SyncResult> SyncCategoriesFromHqAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "",
                AddedCount = 0,
                UpdatedCount = 0,
                ErrorCount = 0,
            };

            try
            {
                Logger.LogInformation("🚀 开始从HQ数据库同步商品分类数据（AutoMapper模式）...");

                // 1. 检查HQ数据库连接
                HqContext.CheckConnection();

                // 2. 从HQ数据库获取商品分类数据 (使用批量操作)
                const int batchSize = 5000; // 每批处理5000条记录
                var totalProcessed = 0;
                var totalAdded = 0;
                var totalUpdated = 0;
                var totalErrors = 0;
                var pageNumber = 1;

                while (true)
                {
                    var hqCategoriesBatch = await HqContext
                        .CBP_DIC_商品分类码表Db.AsQueryable()
                        .Skip((pageNumber - 1) * batchSize)
                        .Take(batchSize)
                        .ToListAsync();

                    if (!hqCategoriesBatch.Any())
                        break; // 没有更多数据

                    Logger.LogInformation(
                        $"从HQ数据库获取到第 {pageNumber} 批商品分类，共 {hqCategoriesBatch.Count} 条"
                    );

                    // 🚀 使用AutoMapper将HQ实体转换为本地实体列表
                    var warehouseCategories = Mapper.Map<List<WarehouseCategory>>(
                        hqCategoriesBatch
                    );

                    Logger.LogInformation(
                        $"AutoMapper转换完成，生成 {warehouseCategories.Count} 个WarehouseCategory对象"
                    );

                    // 🚀 使用Storageable处理Insert/Update逻辑
                    try
                    {
                        var storageResult = await LocalContext
                            .Db.Storageable(warehouseCategories)
                            .WhereColumns(x => x.CategoryGUID) // 基于分类GUID进行判断
                            .ToStorageAsync();

                        // 执行插入和更新
                        var insertResult = storageResult.AsInsertable.ExecuteCommand();
                        var updateResult = storageResult.AsUpdateable.ExecuteCommand();

                        totalAdded += insertResult;
                        totalUpdated += updateResult;

                        Logger.LogInformation(
                            $"第 {pageNumber} 批商品分类AutoMapper处理完成 - 新增: {insertResult}, 更新: {updateResult}"
                        );

                        // 🚀 输出前3个转换结果的示例（用于调试）
                        foreach (var category in warehouseCategories.Take(3))
                        {
                            Logger.LogDebug(
                                $"   示例分类: {category.CategoryName} - {category.ChineseName} (父级: {category.ParentGUID})"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"第 {pageNumber} 批商品分类AutoMapper处理失败");
                        totalErrors += hqCategoriesBatch.Count;
                    }

                    totalProcessed += hqCategoriesBatch.Count;
                    pageNumber++;
                }

                result.AddedCount = totalAdded;
                result.UpdatedCount = totalUpdated;
                result.ErrorCount = totalErrors;
                result.IsSuccess = totalErrors == 0;
                result.Message =
                    $"商品分类同步完成（使用AutoMapper转换）！总共处理: {totalProcessed}, 新增: {totalAdded}, 更新: {totalUpdated}, 错误: {totalErrors}";
                Logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "同步商品分类数据时发生错误");
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

        private async Task SyncSingleCategoryAsync(
            CBP_DIC_商品分类码表 hqCategory,
            List<WarehouseCategory> localCategories,
            SyncResult result
        )
        {
            // 查找本地是否已存在该商品分类（根据GUID）
            var existingCategory = localCategories.FirstOrDefault(c =>
                c.CategoryGUID == hqCategory.HGUID
            );

            if (existingCategory == null)
            {
                // 新增商品分类
                var newCategory = new WarehouseCategory
                {
                    CategoryGUID = hqCategory.HGUID ?? UuidHelper.GenerateUuid7(),
                    ParentGUID = hqCategory.H父级GUID,
                    CategoryName = hqCategory.H类别名称 ?? "",
                    ChineseName = hqCategory.H中文名称,
                    IsActive = true, // 默认启用
                    CreatedBy = hqCategory.FGC_Creator,
                    UpdatedBy = hqCategory.FGC_LastModifier,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                };

                await LocalContext.WarehouseCategoryDb.InsertAsync(newCategory);
                result.AddedCount++;
                Logger.LogInformation($"新增商品分类: {newCategory.CategoryName}");
            }
            else
            {
                // 更新现有商品分类（只更新关键信息）
                bool needUpdate = false;

                if (existingCategory.ParentGUID != hqCategory.H父级GUID)
                {
                    existingCategory.ParentGUID = hqCategory.H父级GUID;
                    needUpdate = true;
                }

                if (existingCategory.CategoryName != hqCategory.H类别名称)
                {
                    existingCategory.CategoryName = hqCategory.H类别名称 ?? "";
                    needUpdate = true;
                }

                if (existingCategory.ChineseName != hqCategory.H中文名称)
                {
                    existingCategory.ChineseName = hqCategory.H中文名称;
                    needUpdate = true;
                }

                if (needUpdate)
                {
                    existingCategory.UpdatedBy = hqCategory.FGC_LastModifier;
                    existingCategory.UpdatedAt = DateTime.Now;
                    await LocalContext.WarehouseCategoryDb.UpdateAsync(existingCategory);
                    result.UpdatedCount++;
                    Logger.LogInformation($"更新商品分类: {existingCategory.CategoryName}");
                }
            }
        }
}
