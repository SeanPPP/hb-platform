using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Services.React;

public partial class ProductHqSyncService
{
    /// <summary>
    /// 只覆盖 HQ 商品字典的 H商品图片；不新增商品，也不触碰腾讯云图地址或其他业务字段。
    /// </summary>
    public async Task<ProductHqImageSyncResultDto> SyncProductImagesAsync(
        IReadOnlyCollection<ProductHqImageUpdateItemDto> items,
        string? updatedBy,
        CancellationToken cancellationToken = default
    )
    {
        var result = new ProductHqImageSyncResultDto { Requested = true };
        if (items == null || items.Count == 0)
        {
            return result;
        }

        if (!await SyncLock.WaitAsync(0, cancellationToken))
        {
            result.Success = false;
            result.FailedCount = items.Count;
            result.ErrorCode = "HQ_IMAGE_SYNC_BUSY";
            result.Errors.Add("HQ 商品同步正在执行，请稍后重试图片同步");
            return result;
        }

        try
        {
            var hqDb = _hqContext.Db;
            hqDb.Ado.CheckConnection();

            var normalizedItems = new List<ProductHqImageUpdateItemDto>();
            var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var productCode = NormalizeCode(item.ProductCode);
                var imageUrl = NormalizeCode(item.ImageUrl);
                if (productCode == null || imageUrl == null)
                {
                    result.FailedCount++;
                    result.Errors.Add("HQ 图片同步项缺少商品编码或图片地址");
                    continue;
                }

                if (!seenCodes.Add(productCode))
                {
                    result.FailedCount++;
                    result.Errors.Add($"HQ 图片同步请求包含重复商品编码: {productCode}");
                    continue;
                }

                normalizedItems.Add(new ProductHqImageUpdateItemDto
                {
                    ProductCode = productCode,
                    ImageUrl = imageUrl,
                });
            }

            var hqRows = new List<DIC_商品信息字典表>();
            foreach (var codeBatch in normalizedItems.Select(item => item.ProductCode).Chunk(HqCodeBatchSize))
            {
                var codes = codeBatch.ToList();
                hqRows.AddRange(
                    await hqDb
                        .Queryable<DIC_商品信息字典表>()
                        .Where(row => row.H商品编码 != null && codes.Contains(row.H商品编码))
                        .ToListAsync()
                );
            }

            var hqRowsByCode = new Dictionary<string, List<DIC_商品信息字典表>>(
                StringComparer.OrdinalIgnoreCase
            );
            foreach (var row in hqRows)
            {
                var code = NormalizeCode(row.H商品编码);
                if (code == null)
                {
                    continue;
                }

                if (!hqRowsByCode.TryGetValue(code, out var rows))
                {
                    rows = new List<DIC_商品信息字典表>();
                    hqRowsByCode[code] = rows;
                }
                rows.Add(row);
            }

            var now = DateTime.Now;
            var effectiveUpdatedBy = NormalizeCode(updatedBy) ?? "HBweb";
            var rowsToUpdate = new List<DIC_商品信息字典表>();
            foreach (var item in normalizedItems)
            {
                if (!hqRowsByCode.TryGetValue(item.ProductCode, out var matches))
                {
                    result.FailedCount++;
                    result.Errors.Add($"HQ 商品不存在: {item.ProductCode}");
                    continue;
                }

                if (matches.Count != 1)
                {
                    result.FailedCount++;
                    result.Errors.Add($"HQ 商品编码存在重复记录: {item.ProductCode}");
                    continue;
                }

                var row = matches[0];
                row.H商品图片 = item.ImageUrl;
                row.FGC_LastModifier = effectiveUpdatedBy;
                row.FGC_LastModifyDate = now;
                rowsToUpdate.Add(row);
            }

            if (rowsToUpdate.Count > 0)
            {
                hqDb.Ado.BeginTran();
                try
                {
                    foreach (var rowBatch in rowsToUpdate.Chunk(100))
                    {
                        var batch = rowBatch.ToList();
                        var affectedRows = await hqDb
                            .Updateable(batch)
                            .UpdateColumns(row => new
                            {
                                row.H商品图片,
                                row.FGC_LastModifier,
                                row.FGC_LastModifyDate,
                            })
                            .ExecuteCommandAsync();
                        if (affectedRows != batch.Count)
                        {
                            throw new InvalidOperationException(
                                $"HQ 图片同步写入数量不一致，预期 {batch.Count}，实际 {affectedRows}"
                            );
                        }
                    }
                    hqDb.Ado.CommitTran();
                    result.UpdatedCount = rowsToUpdate.Count;
                }
                catch
                {
                    hqDb.Ado.RollbackTran();
                    throw;
                }
            }

            result.Success = result.FailedCount == 0;
            if (!result.Success)
            {
                result.ErrorCode = result.UpdatedCount > 0
                    ? "HQ_IMAGE_SYNC_PARTIAL"
                    : "HQ_IMAGE_SYNC_ITEM_ERRORS";
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "仓库批量商品图片同步 HQ 失败");
            result.Success = false;
            result.UpdatedCount = 0;
            result.FailedCount = items.Count;
            result.ErrorCode = "HQ_IMAGE_SYNC_ERROR";
            result.Errors.Add("HQ 图片同步失败，本地图片地址已保留");
            return result;
        }
        finally
        {
            SyncLock.Release();
        }
    }
}
