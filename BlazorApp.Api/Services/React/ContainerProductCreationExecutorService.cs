using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
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
        private readonly IProductWarehouseReactService _productWarehouseService;
        private readonly ILogger<ContainerProductCreationExecutorService> _logger;

        public ContainerProductCreationExecutorService(
            SqlSugarContext context,
            IProductWarehouseReactService productWarehouseService,
            ILogger<ContainerProductCreationExecutorService> logger
        )
        {
            _context = context;
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

            var createItems = new List<CreateItemDto>();
            var sourceRows = new Dictionary<string, ContainerProductCreationSourceRow>(
                StringComparer.OrdinalIgnoreCase
            );
            var batchProductCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var batchItemNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                if (!TryBuildCreateItem(
                    row,
                    existingProductCodes,
                    existingWarehouseProductCodes,
                    existingItemNumbers,
                    batchProductCodes,
                    batchItemNumbers,
                    setRelationsByProductCode,
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

        private static bool TryBuildCreateItem(
            ContainerProductCreationSourceRow row,
            HashSet<string> existingProductCodes,
            HashSet<string> existingWarehouseProductCodes,
            HashSet<string> existingItemNumbers,
            HashSet<string> batchProductCodes,
            HashSet<string> batchItemNumbers,
            Dictionary<string, List<DomesticSetProduct>> setRelationsByProductCode,
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
                AddSkipped(result, productCode, itemNumber, row.DetailHguid, "INVALID_OEM_PRICE", "贴牌价格必须大于 0");
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

            var productType = NormalizeContainerProductType(row.ContainerProductType, row.DomesticProductType);
            if (productType == ContainerProductCreationProductType.SetChild)
            {
                AddSkipped(result, productCode, itemNumber, row.DetailHguid, "MISSING_SET_RELATION", "套装子商品不单独创建");
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
