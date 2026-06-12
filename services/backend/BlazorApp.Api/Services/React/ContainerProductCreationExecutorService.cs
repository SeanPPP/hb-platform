using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 货柜明细创建新商品执行服务。
    /// </summary>
    public class ContainerProductCreationExecutorService
        : IContainerProductCreationExecutorService
    {
        private readonly SqlSugarContext _context;
        private readonly HBSalesSqlSugarContext _hbSalesContext;
        private readonly IProductWarehouseReactService _productWarehouseService;
        private readonly ILogger<ContainerProductCreationExecutorService> _logger;

        public ContainerProductCreationExecutorService(
            SqlSugarContext context,
            HBSalesSqlSugarContext hbSalesContext,
            IProductWarehouseReactService productWarehouseService,
            ILogger<ContainerProductCreationExecutorService> logger
        )
        {
            _context = context;
            _hbSalesContext = hbSalesContext;
            _productWarehouseService = productWarehouseService;
            _logger = logger;
        }

        public async Task<ContainerProductCreationResultDto> ExecuteAsync(
            ContainerProductCreationJobRequestDto request,
            CancellationToken cancellationToken = default
        )
        {
            var result = new ContainerProductCreationResultDto();
            var normalizedDetailHguids = NormalizeDetailHguids(request.DetailHguids);

            if (string.IsNullOrWhiteSpace(request.ContainerGuid))
            {
                AddError(result, null, null, null, "MISSING_CONTAINER_GUID", "货柜 GUID 不能为空");
                return FinalizeResult(result);
            }

            if (normalizedDetailHguids.Count == 0)
            {
                AddError(result, null, null, null, "MISSING_DETAIL_HGUIDS", "明细 GUID 不能为空");
                return FinalizeResult(result);
            }

            var rows = await LoadRowsAsync(request.ContainerGuid.Trim(), normalizedDetailHguids);
            var rowsByDetail = rows
                .Where(row => !string.IsNullOrWhiteSpace(row.DetailHguid))
                .GroupBy(row => row.DetailHguid!)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var detailHguid in normalizedDetailHguids)
            {
                if (!rowsByDetail.ContainsKey(detailHguid))
                {
                    AddSkipped(result, null, null, detailHguid, "DETAIL_NOT_FOUND", "货柜明细不存在或不属于当前货柜");
                }
            }

            var productCodes = rows
                .Select(row => row.ProductCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var itemNumbers = rows
                .Select(row => row.ItemNumber)
                .Where(itemNumber => !string.IsNullOrWhiteSpace(itemNumber))
                .Select(itemNumber => itemNumber!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var existingProductCodes = productCodes.Count == 0
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : (
                    await _context.Db.Queryable<Product>()
                        .Where(product => product.ProductCode != null && productCodes.Contains(product.ProductCode))
                        .Select(product => product.ProductCode)
                        .ToListAsync()
                )
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var existingWarehouseProductCodes = productCodes.Count == 0
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : (
                    await _context.Db.Queryable<WarehouseProduct>()
                        .Where(product => product.ProductCode != null && productCodes.Contains(product.ProductCode))
                        .Select(product => product.ProductCode)
                        .ToListAsync()
                )
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var existingItemNumbers = itemNumbers.Count == 0
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : (
                    await _context.Db.Queryable<Product>()
                        .Where(product => product.ItemNumber != null && itemNumbers.Contains(product.ItemNumber))
                        .Select(product => product.ItemNumber)
                        .ToListAsync()
                )
                    .Where(itemNumber => !string.IsNullOrWhiteSpace(itemNumber))
                    .Select(itemNumber => itemNumber!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var setRelationsByProductCode = productCodes.Count == 0
                ? new Dictionary<string, List<DomesticSetProduct>>(StringComparer.OrdinalIgnoreCase)
                : (
                    await _context.Db.Queryable<DomesticSetProduct>()
                        .Where(item => productCodes.Contains(item.ProductCode) && !item.IsDeleted)
                        .ToListAsync()
                )
                    .GroupBy(item => item.ProductCode, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var linkedSetChildDetailHguids = await EnsureSetRelationsFromContainerChildrenAsync(
                request.ContainerGuid.Trim(),
                rows,
                existingProductCodes,
                setRelationsByProductCode
            );

            var createItems = new List<CreateItemDto>();
            var sourceRows = new Dictionary<string, ContainerProductCreationSourceRow>(
                StringComparer.OrdinalIgnoreCase
            );
            var batchProductCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var batchItemNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                // 已存在的套装主商品不再按重复商品跳过；这里只补齐子码链路，不更新主商品主档或价格。
                if (
                    await TryCompleteExistingSetProductCodesAsync(
                        row,
                        existingProductCodes,
                        setRelationsByProductCode,
                        result
                    )
                )
                {
                    continue;
                }

                if (!TryBuildCreateItem(
                    row,
                    existingProductCodes,
                    existingWarehouseProductCodes,
                    existingItemNumbers,
                    batchProductCodes,
                    batchItemNumbers,
                    setRelationsByProductCode,
                    linkedSetChildDetailHguids,
                    result,
                    out var createItem
                ))
                {
                    continue;
                }

                createItems.Add(createItem);
                if (!string.IsNullOrWhiteSpace(createItem.ProductCode))
                {
                    sourceRows[createItem.ProductCode!] = row;
                }
            }

            if (createItems.Count == 0)
            {
                return FinalizeResult(result);
            }

            try
            {
                var batchResult = await _productWarehouseService.BatchCreateAsync(createItems);
                if (!batchResult.Success)
                {
                    foreach (var error in batchResult.Errors)
                    {
                        AddError(result, null, null, null, "WAREHOUSE_BATCH_FAILED", error);
                    }
                    AddError(result, null, null, null, "WAREHOUSE_BATCH_FAILED", batchResult.Message);
                    return FinalizeResult(result);
                }

                var skippedItemNumbers = batchResult.SkippedItems
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var item in createItems)
                {
                    if (string.IsNullOrWhiteSpace(item.ProductCode))
                    {
                        continue;
                    }

                    if (skippedItemNumbers.Contains(item.ItemNumber))
                    {
                        continue;
                    }

                    if (sourceRows.TryGetValue(item.ProductCode!, out var row))
                    {
                        result.Created.Add(new ContainerProductCreationResultItemDto
                        {
                            ProductCode = item.ProductCode,
                            ItemNumber = item.ItemNumber,
                            DetailHguid = row.DetailHguid,
                            Message = "创建成功",
                        });
                    }
                }

                foreach (var skippedItem in batchResult.SkippedItems)
                {
                    AddSkipped(result, null, skippedItem, null, "WAREHOUSE_SKIPPED", skippedItem);
                }

                foreach (var error in batchResult.Errors)
                {
                    AddError(result, null, null, null, "WAREHOUSE_BATCH_FAILED", error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "货柜创建新商品批量写入失败: {OperationId}", request.OperationId);
                AddError(result, null, null, null, "WAREHOUSE_BATCH_EXCEPTION", ex.Message);
            }

            return FinalizeResult(result);
        }

        private async Task<List<ContainerProductCreationSourceRow>> LoadRowsAsync(
            string containerGuid,
            List<string> detailHguids
        )
        {
            return await _context.Db.Queryable<ContainerDetail>()
                .LeftJoin<DomesticProduct>((detail, domestic) => detail.ProductCode == domestic.ProductCode)
                .Where((detail, domestic) =>
                    detail.ContainerCode == containerGuid
                    && detailHguids.Contains(detail.DetailCode)
                    && !detail.IsDeleted
                )
                .Select((detail, domestic) => new ContainerProductCreationSourceRow
                {
                    DetailHguid = detail.DetailCode,
                    ProductCode = detail.ProductCode,
                    ContainerProductType = detail.ProductType,
                    MixedGroupCode = detail.MixedGroupCode,
                    SetQuantity = detail.SetQuantity,
                    DomesticPrice = detail.DomesticPrice,
                    ImportPrice = detail.ImportPrice,
                    OEMPrice = detail.OEMPrice,
                    Volume = detail.UnitVolume,
                    ItemNumber = domestic.HBProductNo,
                    ChineseName = domestic.ProductName,
                    EnglishName = domestic.EnglishProductName,
                    Barcode = domestic.Barcode,
                    ImageUrl = domestic.ProductImage,
                    DomesticProductType = domestic.ProductType,
                })
                .ToListAsync();
        }

        private async Task<bool> TryCompleteExistingSetProductCodesAsync(
            ContainerProductCreationSourceRow row,
            HashSet<string> existingProductCodes,
            Dictionary<string, List<DomesticSetProduct>> setRelationsByProductCode,
            ContainerProductCreationResultDto result
        )
        {
            var productCode = row.ProductCode?.Trim();
            var itemNumber = row.ItemNumber?.Trim();
            var productType = NormalizeContainerProductType(row.ContainerProductType, row.DomesticProductType);

            if (
                productType != ContainerProductCreationProductType.Set
                || string.IsNullOrWhiteSpace(productCode)
                || !existingProductCodes.Contains(productCode)
            )
            {
                return false;
            }

            var productTypeChanged = await EnsureExistingSetProductTypeAsync(productCode);

            // 已存在套装主商品每次按主商品编码实时查子项表，避免继续依赖货柜同组明细或旧缓存。
            var setRelations = await EnsureSetRelationsFromSetChildTableAsync(
                productCode,
                itemNumber,
                setRelationsByProductCode
            );
            if (setRelations.Count == 0)
            {
                AddSkipped(result, productCode, itemNumber, row.DetailHguid, "SET_CHILD_NOT_FOUND", "未找到套装子项，已跳过");
                return true;
            }

            // 按 DomesticSetProduct 检查子码三层完整性，缺 ProductSetCode 或分店多码时只补缺失层级。
            var changed = await EnsureProductSetCodesAndStoreMultiCodesAsync(productCode, setRelations);
            result.Created.Add(new ContainerProductCreationResultItemDto
            {
                ProductCode = productCode,
                ItemNumber = itemNumber,
                DetailHguid = row.DetailHguid,
                Message = changed || productTypeChanged ? "套装子码已补齐" : "套装子码已完整",
            });
            return true;
        }

        private async Task<bool> EnsureExistingSetProductTypeAsync(string productCode)
        {
            var product = await _context.Db.Queryable<Product>()
                .Where(item => item.ProductCode == productCode && !item.IsDeleted)
                .FirstAsync();
            if (product == null || product.ProductType == 1)
            {
                return false;
            }

            // 已存在套装主商品只修正 POS 商品类型，避免再次创建时改动价格、名称、图片等主档字段。
            product.ProductType = 1;
            product.UpdatedAt = DateTime.Now;
            await _context.Db.Updateable(product)
                .UpdateColumns(item => new { item.ProductType, item.UpdatedAt })
                .ExecuteCommandAsync();
            return true;
        }

        private async Task<List<DomesticSetProduct>> EnsureSetRelationsFromSetChildTableAsync(
            string productCode,
            string? productNo,
            Dictionary<string, List<DomesticSetProduct>> setRelationsByProductCode
        )
        {
            var localRelations = await LoadLocalSetRelationsAsync(productCode);
            if (localRelations.Count > 0)
            {
                setRelationsByProductCode[productCode] = localRelations;
                return localRelations;
            }

            // 本地子项表为空时，按主商品编码实时从 HBSales 套装子项表拉取并落本地。
            var hqRelations = await LoadSetRelationsFromHqAsync(productCode, productNo);
            if (hqRelations.Count == 0)
            {
                setRelationsByProductCode.Remove(productCode);
                return hqRelations;
            }

            await _context.Db.Insertable(hqRelations).ExecuteCommandAsync();
            setRelationsByProductCode[productCode] = hqRelations;
            return hqRelations;
        }

        private async Task<List<DomesticSetProduct>> LoadLocalSetRelationsAsync(string productCode)
        {
            return await _context.Db.Queryable<DomesticSetProduct>()
                .Where(item => item.ProductCode == productCode && !item.IsDeleted)
                .ToListAsync();
        }

        private async Task<List<DomesticSetProduct>> LoadSetRelationsFromHqAsync(
            string productCode,
            string? productNo
        )
        {
            var hqRows = await _hbSalesContext.Db.Queryable<CPT_DIC_商品套装信息表>()
                .Where(row =>
                    row.商品编码 == productCode
                    && row.使用状态 == 1
                    && (
                        !string.IsNullOrEmpty(row.HGUID)
                        || !string.IsNullOrEmpty(row.条形码)
                        || !string.IsNullOrEmpty(row.商品小货号)
                    )
                )
                .ToListAsync();

            return hqRows
                .Select(row => MapHqSetRelation(row, productCode, productNo))
                .Where(relation =>
                    !string.IsNullOrWhiteSpace(relation.SetProductCode)
                    && !string.IsNullOrWhiteSpace(relation.SetProductNo)
                )
                .GroupBy(relation => relation.SetProductCode, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private static DomesticSetProduct MapHqSetRelation(
            CPT_DIC_商品套装信息表 row,
            string productCode,
            string? productNo
        )
        {
            var setProductCode =
                row.商品小货号?.Trim()
                ?? row.条形码?.Trim()
                ?? row.HGUID?.Trim()
                ?? UuidHelper.GenerateUuid7();
            var setProductNo =
                row.商品小货号?.Trim()
                ?? row.条形码?.Trim()
                ?? setProductCode;

            return new DomesticSetProduct
            {
                SetProductCode = setProductCode,
                ProductCode = productCode,
                ProductNo = productNo?.Trim(),
                SetProductNo = setProductNo,
                SetBarcode = row.条形码?.Trim(),
                DomesticPrice = row.国内价格,
                ImportPrice = row.进口价格,
                OEMPrice = row.贴牌价格,
                Remarks = row.备注?.Trim(),
                IsDeleted = false,
            };
        }

        private async Task<bool> EnsureProductSetCodesAndStoreMultiCodesAsync(
            string productCode,
            List<DomesticSetProduct> setRelations
        )
        {
            var now = DateTime.Now;
            var changed = false;
            var validRelations = setRelations
                .Where(relation =>
                    !string.IsNullOrWhiteSpace(relation.SetProductCode)
                    && !string.IsNullOrWhiteSpace(relation.SetProductNo)
                )
                .GroupBy(relation => relation.SetProductCode!, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (validRelations.Count == 0)
            {
                return false;
            }

            var setProductCodes = validRelations
                .Select(relation => relation.SetProductCode!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var mainPurchasePrice = await _context.Db.Queryable<Product>()
                .Where(product => product.ProductCode == productCode && !product.IsDeleted)
                .Select(product => product.PurchasePrice)
                .FirstAsync();
            // 套装子码进货价按子码零售价比例分摊主商品进货价，避免沿用子项自身进货价导致本地/HQ 不一致。
            var allocatedPurchasePrices = SetChildPurchasePriceAllocator.AllocateByRetailRatio(
                validRelations,
                mainPurchasePrice,
                relation => relation.SetProductCode,
                relation => relation.OEMPrice
            );
            var existingSetCodes = await _context.Db.Queryable<ProductSetCode>()
                .Where(code => code.ProductCode == productCode && setProductCodes.Contains(code.SetProductCode))
                .ToListAsync();
            var existingSetCodeMap = existingSetCodes.ToDictionary(
                code => code.SetProductCode,
                code => code,
                StringComparer.OrdinalIgnoreCase
            );
            var productSetCodesToInsert = new List<ProductSetCode>();
            var productSetCodesToUpdate = new List<ProductSetCode>();

            foreach (var relation in validRelations)
            {
                var setPurchasePrice = ResolveAllocatedPurchasePrice(relation, allocatedPurchasePrices);
                if (existingSetCodeMap.TryGetValue(relation.SetProductCode!, out var existingSetCode))
                {
                    if (ShouldRefreshSetCode(existingSetCode, relation, setPurchasePrice))
                    {
                        ApplySetCodeValues(existingSetCode, relation, setPurchasePrice, now);
                        productSetCodesToUpdate.Add(existingSetCode);
                        changed = true;
                    }
                    continue;
                }

                productSetCodesToInsert.Add(BuildProductSetCode(productCode, relation, setPurchasePrice, now));
                changed = true;
            }

            if (productSetCodesToInsert.Count > 0)
            {
                await _context.Db.Insertable(productSetCodesToInsert).ExecuteCommandAsync();
            }

            if (productSetCodesToUpdate.Count > 0)
            {
                await _context.Db.Updateable(productSetCodesToUpdate).ExecuteCommandAsync();
            }

            var activeStoreCodes = await _context.Db.Queryable<Store>()
                .Where(store => store.IsActive && !store.IsDeleted && store.StoreCode != null)
                .Select(store => store.StoreCode!)
                .ToListAsync();
            if (activeStoreCodes.Count == 0)
            {
                return changed;
            }

            var existingStoreMultiCodes = await _context.Db.Queryable<StoreMultiCodeProduct>()
                .Where(item =>
                    item.ProductCode == productCode
                    && item.MultiCodeProductCode != null
                    && setProductCodes.Contains(item.MultiCodeProductCode)
                    && item.StoreCode != null
                    && activeStoreCodes.Contains(item.StoreCode)
                )
                .ToListAsync();
            var existingStoreMultiCodeMap = existingStoreMultiCodes.ToDictionary(
                item => BuildStoreMultiCodeKey(item.StoreCode, item.MultiCodeProductCode),
                item => item,
                StringComparer.OrdinalIgnoreCase
            );
            var storeMultiCodesToInsert = new List<StoreMultiCodeProduct>();
            var storeMultiCodesToUpdate = new List<StoreMultiCodeProduct>();

            foreach (var relation in validRelations)
            {
                var setPurchasePrice = ResolveAllocatedPurchasePrice(relation, allocatedPurchasePrices);
                foreach (var storeCode in activeStoreCodes)
                {
                    var key = BuildStoreMultiCodeKey(storeCode, relation.SetProductCode);
                    if (existingStoreMultiCodeMap.TryGetValue(key, out var existingStoreMultiCode))
                    {
                        if (ShouldRefreshStoreMultiCode(existingStoreMultiCode, productCode, storeCode, relation, setPurchasePrice))
                        {
                            ApplyStoreMultiCodeValues(existingStoreMultiCode, productCode, storeCode, relation, setPurchasePrice, now);
                            storeMultiCodesToUpdate.Add(existingStoreMultiCode);
                            changed = true;
                        }
                        continue;
                    }

                    storeMultiCodesToInsert.Add(BuildStoreMultiCode(productCode, storeCode, relation, setPurchasePrice, now));
                    changed = true;
                }
            }

            if (storeMultiCodesToInsert.Count > 0)
            {
                await _context.Db.Insertable(storeMultiCodesToInsert).PageSize(1000).ExecuteCommandAsync();
            }

            if (storeMultiCodesToUpdate.Count > 0)
            {
                await _context.Db.Updateable(storeMultiCodesToUpdate).ExecuteCommandAsync();
            }

            return changed;
        }

        private static ProductSetCode BuildProductSetCode(
            string productCode,
            DomesticSetProduct relation,
            decimal? setPurchasePrice,
            DateTime now
        )
        {
            var setCode = new ProductSetCode
            {
                SetCodeId = relation.SetProductCode!,
            };
            ApplySetCodeValues(setCode, relation, setPurchasePrice, now);
            setCode.ProductCode = productCode;
            return setCode;
        }

        private static void ApplySetCodeValues(
            ProductSetCode setCode,
            DomesticSetProduct relation,
            decimal? setPurchasePrice,
            DateTime now
        )
        {
            setCode.ProductCode = relation.ProductCode;
            setCode.SetProductCode = relation.SetProductCode!;
            setCode.SetItemNumber = relation.SetProductNo;
            setCode.SetBarcode = relation.SetBarcode;
            setCode.SetPurchasePrice = setPurchasePrice;
            setCode.SetRetailPrice = relation.OEMPrice;
            setCode.SetQuantity = 1;
            setCode.SetType = 1;
            setCode.IsActive = true;
            setCode.IsDeleted = false;
            setCode.UpdatedAt = now;
            if (setCode.CreatedAt == default)
            {
                setCode.CreatedAt = now;
            }
        }

        private static StoreMultiCodeProduct BuildStoreMultiCode(
            string productCode,
            string storeCode,
            DomesticSetProduct relation,
            decimal? setPurchasePrice,
            DateTime now
        )
        {
            var storeMultiCode = new StoreMultiCodeProduct
            {
                UUID = UuidHelper.GenerateUuid7(),
            };
            ApplyStoreMultiCodeValues(storeMultiCode, productCode, storeCode, relation, setPurchasePrice, now);
            return storeMultiCode;
        }

        private static void ApplyStoreMultiCodeValues(
            StoreMultiCodeProduct storeMultiCode,
            string productCode,
            string storeCode,
            DomesticSetProduct relation,
            decimal? setPurchasePrice,
            DateTime now
        )
        {
            storeMultiCode.StoreCode = storeCode;
            storeMultiCode.ProductCode = productCode;
            storeMultiCode.MultiCodeProductCode = relation.SetProductCode;
            storeMultiCode.StoreMultiCodeProductCode = storeCode + relation.SetProductCode;
            storeMultiCode.MultiBarcode = relation.SetBarcode;
            storeMultiCode.PurchasePrice = setPurchasePrice;
            storeMultiCode.MultiCodeRetailPrice = relation.OEMPrice;
            storeMultiCode.DiscountRate = null;
            storeMultiCode.IsAutoPricing = false;
            storeMultiCode.IsSpecialProduct = false;
            storeMultiCode.IsActive = true;
            storeMultiCode.IsDeleted = false;
            storeMultiCode.UpdatedAt = now;
            if (storeMultiCode.CreatedAt == default)
            {
                storeMultiCode.CreatedAt = now;
            }
        }

        private static decimal? ResolveAllocatedPurchasePrice(
            DomesticSetProduct relation,
            Dictionary<string, decimal> allocatedPurchasePrices
        )
        {
            return relation.SetProductCode != null
                && allocatedPurchasePrices.TryGetValue(relation.SetProductCode, out var allocatedPurchasePrice)
                ? allocatedPurchasePrice
                : relation.ImportPrice;
        }

        private static bool ShouldRefreshSetCode(
            ProductSetCode setCode,
            DomesticSetProduct relation,
            decimal? setPurchasePrice
        )
        {
            return setCode.IsDeleted
                || !setCode.IsActive
                || setCode.ProductCode != relation.ProductCode
                || setCode.SetProductCode != relation.SetProductCode
                || setCode.SetItemNumber != relation.SetProductNo
                || setCode.SetBarcode != relation.SetBarcode
                || setCode.SetPurchasePrice != setPurchasePrice
                || setCode.SetRetailPrice != relation.OEMPrice
                || setCode.SetQuantity != 1
                || setCode.SetType != 1;
        }

        private static bool ShouldRefreshStoreMultiCode(
            StoreMultiCodeProduct storeMultiCode,
            string productCode,
            string storeCode,
            DomesticSetProduct relation,
            decimal? setPurchasePrice
        )
        {
            return storeMultiCode.IsDeleted
                || !storeMultiCode.IsActive
                || storeMultiCode.StoreCode != storeCode
                || storeMultiCode.ProductCode != productCode
                || storeMultiCode.MultiCodeProductCode != relation.SetProductCode
                || storeMultiCode.StoreMultiCodeProductCode != storeCode + relation.SetProductCode
                || storeMultiCode.MultiBarcode != relation.SetBarcode
                || storeMultiCode.PurchasePrice != setPurchasePrice
                || storeMultiCode.MultiCodeRetailPrice != relation.OEMPrice;
        }

        private static string BuildStoreMultiCodeKey(string? storeCode, string? multiCodeProductCode)
        {
            return $"{storeCode?.Trim()}|{multiCodeProductCode?.Trim()}";
        }

        private async Task<HashSet<string>> EnsureSetRelationsFromContainerChildrenAsync(
            string containerGuid,
            List<ContainerProductCreationSourceRow> rows,
            HashSet<string> existingProductCodes,
            Dictionary<string, List<DomesticSetProduct>> setRelationsByProductCode
        )
        {
            var linkedSetChildDetailHguids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var setMainRows = rows
                .Where(row =>
                    NormalizeContainerProductType(row.ContainerProductType, row.DomesticProductType)
                    == ContainerProductCreationProductType.Set
                )
                .Where(row =>
                    !string.IsNullOrWhiteSpace(row.ProductCode)
                    && !existingProductCodes.Contains(row.ProductCode.Trim())
                    && !string.IsNullOrWhiteSpace(row.ItemNumber)
                    && !string.IsNullOrWhiteSpace(row.MixedGroupCode)
                )
                .ToList();

            if (setMainRows.Count == 0)
            {
                return linkedSetChildDetailHguids;
            }

            var mixedGroupCodes = setMainRows
                .Select(row => row.MixedGroupCode!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var childRows = await LoadSetChildRowsAsync(containerGuid, mixedGroupCodes);
            var childRowsByGroup = childRows
                .Where(row => !string.IsNullOrWhiteSpace(row.MixedGroupCode))
                .GroupBy(row => row.MixedGroupCode!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var setChildProductCodes = childRows
                .Select(row => row.ProductCode?.Trim())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var existingSetProductCodes = setChildProductCodes.Count == 0
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : (
                    await _context.Db.Queryable<DomesticSetProduct>()
                        .Where(item => setChildProductCodes.Contains(item.SetProductCode) && !item.IsDeleted)
                        .Select(item => item.SetProductCode)
                        .ToListAsync()
                )
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var setRelationsToInsert = new List<DomesticSetProduct>();
            var pendingSetProductCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var mainRow in setMainRows)
            {
                var mainProductCode = mainRow.ProductCode!.Trim();
                var mainItemNumber = mainRow.ItemNumber!.Trim();
                var mixedGroupCode = mainRow.MixedGroupCode!.Trim();

                if (!childRowsByGroup.TryGetValue(mixedGroupCode, out var sameGroupChildren))
                {
                    continue;
                }

                var existingRelations = setRelationsByProductCode.TryGetValue(mainProductCode, out var relations)
                    ? relations
                    : new List<DomesticSetProduct>();
                if (existingRelations.Count > 0)
                {
                    MarkLinkedSetChildRows(sameGroupChildren, existingRelations, linkedSetChildDetailHguids);
                    continue;
                }

                var newRelations = new List<DomesticSetProduct>();
                foreach (var childRow in sameGroupChildren)
                {
                    var setProductCode = childRow.ProductCode?.Trim();
                    var setProductNo = childRow.ItemNumber?.Trim();
                    if (
                        string.IsNullOrWhiteSpace(setProductCode)
                        || string.IsNullOrWhiteSpace(setProductNo)
                        || existingSetProductCodes.Contains(setProductCode)
                        || !pendingSetProductCodes.Add(setProductCode)
                    )
                    {
                        continue;
                    }

                    // 从同货柜同混装组的套装子项补齐国内套装关系，后续批量创建会复用它生成商品子码和分店子码。
                    newRelations.Add(new DomesticSetProduct
                    {
                        SetProductCode = setProductCode,
                        ProductCode = mainProductCode,
                        ProductNo = mainItemNumber,
                        SetProductNo = setProductNo,
                        SetBarcode = childRow.Barcode,
                        DomesticPrice = childRow.DomesticPrice,
                        ImportPrice = childRow.ImportPrice,
                        OEMPrice = childRow.OEMPrice,
                        IsDeleted = false,
                    });
                    if (!string.IsNullOrWhiteSpace(childRow.DetailHguid))
                    {
                        linkedSetChildDetailHguids.Add(childRow.DetailHguid);
                    }
                }

                if (newRelations.Count > 0)
                {
                    setRelationsToInsert.AddRange(newRelations);
                    setRelationsByProductCode[mainProductCode] = newRelations;
                }
            }

            if (setRelationsToInsert.Count > 0)
            {
                await _context.Db.Insertable(setRelationsToInsert).ExecuteCommandAsync();
            }

            return linkedSetChildDetailHguids;
        }

        private async Task<List<ContainerProductCreationSourceRow>> LoadSetChildRowsAsync(
            string containerGuid,
            List<string> mixedGroupCodes
        )
        {
            if (mixedGroupCodes.Count == 0)
            {
                return new List<ContainerProductCreationSourceRow>();
            }

            return await _context.Db.Queryable<ContainerDetail>()
                .LeftJoin<DomesticProduct>((detail, domestic) => detail.ProductCode == domestic.ProductCode)
                .Where((detail, domestic) =>
                    detail.ContainerCode == containerGuid
                    && detail.MixedGroupCode != null
                    && mixedGroupCodes.Contains(detail.MixedGroupCode)
                    && detail.ProductType == "套装子商品"
                    && !detail.IsDeleted
                )
                .Select((detail, domestic) => new ContainerProductCreationSourceRow
                {
                    DetailHguid = detail.DetailCode,
                    ProductCode = detail.ProductCode,
                    ContainerProductType = detail.ProductType,
                    MixedGroupCode = detail.MixedGroupCode,
                    SetQuantity = detail.SetQuantity,
                    DomesticPrice = detail.DomesticPrice,
                    ImportPrice = detail.ImportPrice,
                    OEMPrice = detail.OEMPrice,
                    Volume = detail.UnitVolume,
                    ItemNumber = domestic.HBProductNo,
                    ChineseName = domestic.ProductName,
                    EnglishName = domestic.EnglishProductName,
                    Barcode = domestic.Barcode,
                    ImageUrl = domestic.ProductImage,
                    DomesticProductType = domestic.ProductType,
                })
                .ToListAsync();
        }

        private static void MarkLinkedSetChildRows(
            List<ContainerProductCreationSourceRow> sameGroupChildren,
            List<DomesticSetProduct> existingRelations,
            HashSet<string> linkedSetChildDetailHguids
        )
        {
            var existingSetProductCodes = existingRelations
                .Select(relation => relation.SetProductCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var childRow in sameGroupChildren)
            {
                var childProductCode = childRow.ProductCode?.Trim();
                if (
                    !string.IsNullOrWhiteSpace(childProductCode)
                    && existingSetProductCodes.Contains(childProductCode)
                    && !string.IsNullOrWhiteSpace(childRow.DetailHguid)
                )
                {
                    linkedSetChildDetailHguids.Add(childRow.DetailHguid);
                }
            }
        }

        private static bool TryBuildCreateItem(
            ContainerProductCreationSourceRow row,
            HashSet<string> existingProductCodes,
            HashSet<string> existingWarehouseProductCodes,
            HashSet<string> existingItemNumbers,
            HashSet<string> batchProductCodes,
            HashSet<string> batchItemNumbers,
            Dictionary<string, List<DomesticSetProduct>> setRelationsByProductCode,
            HashSet<string> linkedSetChildDetailHguids,
            ContainerProductCreationResultDto result,
            out CreateItemDto createItem
        )
        {
            createItem = new CreateItemDto();
            var productCode = row.ProductCode?.Trim();
            var itemNumber = row.ItemNumber?.Trim();

            if (string.IsNullOrWhiteSpace(productCode))
            {
                AddSkipped(result, null, itemNumber, row.DetailHguid, "MISSING_PRODUCT_CODE", "商品编码不能为空");
                return false;
            }

            if (string.IsNullOrWhiteSpace(itemNumber))
            {
                AddSkipped(result, productCode, null, row.DetailHguid, "MISSING_ITEM_NUMBER", "货号不能为空");
                return false;
            }

            var productType = NormalizeContainerProductType(row.ContainerProductType, row.DomesticProductType);
            if (productType == ContainerProductCreationProductType.SetChild)
            {
                if (
                    !string.IsNullOrWhiteSpace(row.DetailHguid)
                    && linkedSetChildDetailHguids.Contains(row.DetailHguid)
                )
                {
                    return false;
                }

                AddSkipped(result, productCode, itemNumber, row.DetailHguid, "MISSING_SET_RELATION", "套装子商品不单独创建；请选择对应套装主商品生成子码");
                return false;
            }

            if (string.IsNullOrWhiteSpace(row.ChineseName))
            {
                AddSkipped(result, productCode, itemNumber, row.DetailHguid, "MISSING_CHINESE_NAME", "商品名称不能为空");
                return false;
            }

            if (string.IsNullOrWhiteSpace(row.EnglishName))
            {
                AddSkipped(result, productCode, itemNumber, row.DetailHguid, "MISSING_ENGLISH_NAME", "英文名称不能为空");
                return false;
            }

            if (!row.ImportPrice.HasValue || row.ImportPrice.Value <= 0)
            {
                AddSkipped(result, productCode, itemNumber, row.DetailHguid, "INVALID_IMPORT_PRICE", "进口价格必须大于 0");
                return false;
            }

            if (!row.OEMPrice.HasValue || row.OEMPrice.Value <= 0)
            {
                AddSkipped(result, productCode, itemNumber, row.DetailHguid, "INVALID_OEM_PRICE", "零售价必须大于 0");
                return false;
            }

            if (existingProductCodes.Contains(productCode))
            {
                AddSkipped(result, productCode, itemNumber, row.DetailHguid, "DUPLICATE_PRODUCT_CODE", "本地商品已存在");
                return false;
            }

            if (!batchProductCodes.Add(productCode))
            {
                AddSkipped(result, productCode, itemNumber, row.DetailHguid, "DUPLICATE_PRODUCT_CODE", "本次提交中商品编码重复");
                return false;
            }

            if (existingItemNumbers.Contains(itemNumber))
            {
                AddSkipped(result, productCode, itemNumber, row.DetailHguid, "DUPLICATE_ITEM_NUMBER", "本地货号已存在");
                return false;
            }

            if (!batchItemNumbers.Add(itemNumber))
            {
                AddSkipped(result, productCode, itemNumber, row.DetailHguid, "DUPLICATE_ITEM_NUMBER", "本次提交中货号重复");
                return false;
            }

            if (existingWarehouseProductCodes.Contains(productCode))
            {
                AddSkipped(result, productCode, itemNumber, row.DetailHguid, "DUPLICATE_WAREHOUSE_PRODUCT", "仓库商品已存在");
                return false;
            }

            if (
                productType == ContainerProductCreationProductType.Set
                && !setRelationsByProductCode.ContainsKey(productCode)
            )
            {
                AddSkipped(result, productCode, itemNumber, row.DetailHguid, "MISSING_SET_RELATION", "缺少套装关系，已跳过");
                return false;
            }

            createItem = new CreateItemDto
            {
                ProductCode = productCode,
                ItemNumber = itemNumber,
                Barcode = row.Barcode,
                ChineseName = row.ChineseName ?? itemNumber,
                EnglishName = row.EnglishName,
                DomesticPrice = row.DomesticPrice,
                OEMPrice = row.OEMPrice.Value,
                ImportPrice = row.ImportPrice.Value,
                Volume = row.Volume,
                ImageUrl = row.ImageUrl,
                ProductType = productType == ContainerProductCreationProductType.Set ? 1 : 0,
                IsSetProduct = productType == ContainerProductCreationProductType.Set,
            };
            return true;
        }

        private static ContainerProductCreationProductType NormalizeContainerProductType(
            string? containerProductType,
            int? domesticProductType
        )
        {
            var normalized = containerProductType?.Trim();
            if (string.Equals(normalized, "套装子商品", StringComparison.OrdinalIgnoreCase))
            {
                return ContainerProductCreationProductType.SetChild;
            }

            if (string.Equals(normalized, "套装商品", StringComparison.OrdinalIgnoreCase))
            {
                return ContainerProductCreationProductType.Set;
            }

            return domesticProductType == 1
                ? ContainerProductCreationProductType.Set
                : ContainerProductCreationProductType.Normal;
        }

        private static List<string> NormalizeDetailHguids(IEnumerable<string>? detailHguids)
        {
            return (detailHguids ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static ContainerProductCreationResultDto FinalizeResult(
            ContainerProductCreationResultDto result
        )
        {
            result.CreatedCount = result.Created.Count;
            result.SkippedCount = result.Skipped.Count;
            result.FailedCount = result.Errors.Count;
            return result;
        }

        private static void AddSkipped(
            ContainerProductCreationResultDto result,
            string? productCode,
            string? itemNumber,
            string? detailHguid,
            string reasonCode,
            string message
        )
        {
            result.Skipped.Add(new ContainerProductCreationResultItemDto
            {
                ProductCode = productCode,
                ItemNumber = itemNumber,
                DetailHguid = detailHguid,
                ReasonCode = reasonCode,
                Message = message,
            });
        }

        private static void AddError(
            ContainerProductCreationResultDto result,
            string? productCode,
            string? itemNumber,
            string? detailHguid,
            string reasonCode,
            string message
        )
        {
            result.Errors.Add(new ContainerProductCreationResultItemDto
            {
                ProductCode = productCode,
                ItemNumber = itemNumber,
                DetailHguid = detailHguid,
                ReasonCode = reasonCode,
                Message = message,
            });
        }

        private enum ContainerProductCreationProductType
        {
            Normal,
            Set,
            SetChild,
        }

        private sealed class ContainerProductCreationSourceRow
        {
            public string? DetailHguid { get; set; }
            public string? ProductCode { get; set; }
            public string? ContainerProductType { get; set; }
            public string? MixedGroupCode { get; set; }
            public decimal? SetQuantity { get; set; }
            public decimal? DomesticPrice { get; set; }
            public decimal? ImportPrice { get; set; }
            public decimal? OEMPrice { get; set; }
            public decimal? Volume { get; set; }
            public string? ItemNumber { get; set; }
            public string? ChineseName { get; set; }
            public string? EnglishName { get; set; }
            public string? Barcode { get; set; }
            public string? ImageUrl { get; set; }
            public int? DomesticProductType { get; set; }
        }
    }
}
