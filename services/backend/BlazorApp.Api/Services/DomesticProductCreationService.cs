using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SqlSugar;
using ZXing;
using ZXing.Common;
using ZXing.ImageSharp;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 国内商品货号条码批量创建服务实现
    /// </summary>
    public class DomesticProductCreationService : IDomesticProductCreationService
    {
        private readonly SqlSugarContext _context;
        private readonly ItemBarcodeService _itemBarcodeService;
        private readonly ILogger<DomesticProductCreationService> _logger;

        public DomesticProductCreationService(
            SqlSugarContext context,
            ItemBarcodeService itemBarcodeService,
            ILogger<DomesticProductCreationService> logger
        )
        {
            _context = context;
            _itemBarcodeService = itemBarcodeService;
            _logger = logger;
        }

        /// <summary>
        /// 批量创建国内商品
        /// </summary>
        public async Task<ApiResponse<CreateDomesticProductBatchResponse>> CreateBatchAsync(
            CreateDomesticProductBatchRequest request
        )
        {
            try
            {
                var batchNumber = await GenerateBatchNumberAsync();

                var supplier = await _context.ChinaSupplierDb.GetFirstAsync(x =>
                    x.SupplierCode == request.SupplierCode
                );
                var supplierName = supplier?.SupplierName;

                var mainItems = request
                    .Items.Where(i => string.IsNullOrEmpty(i.ParentItemNumber))
                    .ToList();
                var subItems = request
                    .Items.Where(i => !string.IsNullOrEmpty(i.ParentItemNumber))
                    .ToList();

                var normalItems = mainItems.Where(i => i.ProductType != 1).ToList();
                var setItems = mainItems.Where(i => i.ProductType == 1).ToList();

                var allProducts = new List<DomesticProduct>();
                var allSetProducts = new List<DomesticSetProduct>();
                var allLogs = new List<DomesticProductCreationLog>();
                var responseItems = new List<BatchCreatedItemDto>();

                if (normalItems.Any())
                {
                    var codes = await _itemBarcodeService.GenerateBatchItemNumbersAndBarcodesAsync(
                        request.SupplierCode,
                        ProductTypeEnum.Normal,
                        normalItems.Count,
                        request.PrefixName
                    );

                    for (var i = 0; i < normalItems.Count && i < codes.Count; i++)
                    {
                        var item = normalItems[i];
                        var (itemNumber, barcode) = codes[i];
                        var productCode = Guid.NewGuid().ToString();

                        allProducts.Add(
                            new DomesticProduct
                            {
                                ProductCode = productCode,
                                SupplierCode = request.SupplierCode,
                                ProductName = item.ProductName,
                                HBProductNo = itemNumber,
                                Barcode = barcode,
                                ProductType = 0,
                                OEMPrice = item.PrivateLabelPrice,
                                IsActive = true,
                            }
                        );

                        allLogs.Add(
                            new DomesticProductCreationLog
                            {
                                LogId = Guid.NewGuid().ToString(),
                                ProductCode = productCode,
                                SupplierCode = request.SupplierCode,
                                SupplierName = supplierName,
                                HBProductNo = itemNumber,
                                Barcode = barcode,
                                ProductName = item.ProductName,
                                PrefixCode = request.PrefixCode,
                                PrefixName = request.PrefixName,
                                CreationType = "Batch",
                                BatchNumber = batchNumber,
                            }
                        );

                        responseItems.Add(
                            new BatchCreatedItemDto
                            {
                                ProductCode = productCode,
                                HBProductNo = itemNumber,
                                Barcode = barcode,
                                ProductName = item.ProductName ?? "",
                                ProductType = 0,
                                PrivateLabelPrice = item.PrivateLabelPrice,
                                SubItems = new List<SubItemDto>(),
                            }
                        );
                    }
                }

                var nestedSubItemsBySetProductCode = new Dictionary<
                    string,
                    List<CreateBatchItemDto>
                >();
                var expandedSetItemCount = 0;

                if (setItems.Any())
                {
                    var expandedSetItems = setItems
                        .SelectMany(item =>
                            Enumerable
                                .Range(0, Math.Max(item.CreateCount.GetValueOrDefault(1), 1))
                                .Select(_ => item)
                        )
                        .ToList();
                    expandedSetItemCount = expandedSetItems.Count;

                    var codes = await _itemBarcodeService.GenerateBatchItemNumbersAndBarcodesAsync(
                        request.SupplierCode,
                        ProductTypeEnum.Set,
                        expandedSetItems.Count,
                        null
                    );

                    for (var i = 0; i < expandedSetItems.Count && i < codes.Count; i++)
                    {
                        var item = expandedSetItems[i];
                        var (itemNumber, barcode) = codes[i];
                        var productCode = Guid.NewGuid().ToString();

                        allProducts.Add(
                            new DomesticProduct
                            {
                                ProductCode = productCode,
                                SupplierCode = request.SupplierCode,
                                ProductName = item.ProductName,
                                HBProductNo = itemNumber,
                                Barcode = barcode,
                                ProductType = 1,
                                OEMPrice = item.PrivateLabelPrice,
                                IsActive = true,
                            }
                        );

                        allSetProducts.Add(
                            new DomesticSetProduct
                            {
                                SetProductCode = Guid.NewGuid().ToString(),
                                ProductCode = productCode,
                                ProductNo = itemNumber,
                                SetProductNo = itemNumber,
                                SetBarcode = barcode,
                                OEMPrice = item.PrivateLabelPrice,
                                DomesticPrice = item.SetPrice,
                            }
                        );

                        allLogs.Add(
                            new DomesticProductCreationLog
                            {
                                LogId = Guid.NewGuid().ToString(),
                                ProductCode = productCode,
                                SupplierCode = request.SupplierCode,
                                SupplierName = supplierName,
                                HBProductNo = itemNumber,
                                Barcode = barcode,
                                ProductName = item.ProductName,
                                PrefixCode = request.PrefixCode,
                                PrefixName = request.PrefixName,
                                CreationType = "Batch",
                                BatchNumber = batchNumber,
                            }
                        );

                        var createdItem = new BatchCreatedItemDto
                        {
                            ProductCode = productCode,
                            HBProductNo = itemNumber,
                            Barcode = barcode,
                            ProductName = item.ProductName ?? "",
                            ProductType = 1,
                            PrivateLabelPrice = item.PrivateLabelPrice,
                            SetQuantity = item.SetQuantity,
                            SetPrice = item.SetPrice,
                            SubItems = new List<SubItemDto>(),
                        };
                        responseItems.Add(createdItem);

                        // 嵌套子项按真实父商品编码暂存，后续用真实父货号生成子货号条码
                        if (item.SubItems.Any())
                        {
                            nestedSubItemsBySetProductCode[productCode] = item.SubItems.ToList();
                        }
                    }
                }

                foreach (var createdItem in responseItems.Where(x => x.ProductType == 1))
                {
                    var hasNestedSubItems = nestedSubItemsBySetProductCode.TryGetValue(
                        createdItem.ProductCode,
                        out var nestedSubItems
                    );
                    var relatedSubItems = hasNestedSubItems
                        ? nestedSubItems!
                        : subItems.Where(s => s.ParentItemNumber == createdItem.HBProductNo).ToList();

                    if (!relatedSubItems.Any())
                        continue;

                    var subCodes =
                        await _itemBarcodeService.GenerateBatchSetItemNumbersAndBarcodesAsync(
                            createdItem.HBProductNo,
                            ProductTypeEnum.Set,
                            relatedSubItems.Count
                        );

                    for (var i = 0; i < relatedSubItems.Count && i < subCodes.Count; i++)
                    {
                        var subItem = relatedSubItems[i];
                        var (subItemNumber, subBarcode) = subCodes[i];
                        var subProductCode = Guid.NewGuid().ToString();
                        var subProductName = subItem.SubItemProductName ?? subItem.ProductName;

                        allProducts.Add(
                            new DomesticProduct
                            {
                                ProductCode = subProductCode,
                                SupplierCode = request.SupplierCode,
                                ProductName = subProductName,
                                HBProductNo = subItemNumber,
                                Barcode = subBarcode,
                                ProductType = 0,
                                OEMPrice = subItem.PrivateLabelPrice,
                                IsActive = true,
                            }
                        );

                        allSetProducts.Add(
                            new DomesticSetProduct
                            {
                                SetProductCode = Guid.NewGuid().ToString(),
                                ProductCode = createdItem.ProductCode,
                                ProductNo = createdItem.HBProductNo,
                                SetProductNo = subItemNumber,
                                SetBarcode = subBarcode,
                                OEMPrice = subItem.PrivateLabelPrice,
                            }
                        );

                        allLogs.Add(
                            new DomesticProductCreationLog
                            {
                                LogId = Guid.NewGuid().ToString(),
                                ProductCode = subProductCode,
                                SupplierCode = request.SupplierCode,
                                SupplierName = supplierName,
                                HBProductNo = subItemNumber,
                                Barcode = subBarcode,
                                ProductName = subProductName,
                                PrefixCode = request.PrefixCode,
                                PrefixName = request.PrefixName,
                                CreationType = "Batch",
                                BatchNumber = batchNumber,
                                Remark = $"Parent: {createdItem.HBProductNo}",
                            }
                        );

                        createdItem.SubItems.Add(
                            new SubItemDto
                            {
                                ProductCode = subProductCode,
                                HBProductNo = subItemNumber,
                                Barcode = subBarcode,
                                ProductName = subProductName ?? "",
                                PrivateLabelPrice = subItem.PrivateLabelPrice,
                            }
                        );
                    }
                }

                if (allProducts.Any())
                    await _context
                        .Db.Fastest<DomesticProduct>()
                        .AS("DomesticProduct")
                        .PageSize(500)
                        .BulkCopyAsync(allProducts);
                if (allSetProducts.Any())
                    await _context
                        .Db.Fastest<DomesticSetProduct>()
                        .AS("DomesticSetProduct")
                        .PageSize(500)
                        .BulkCopyAsync(allSetProducts);
                if (allLogs.Any())
                    await _context
                        .Db.Fastest<DomesticProductCreationLog>()
                        .AS("DomesticProductCreationLog")
                        .PageSize(500)
                        .BulkCopyAsync(allLogs);

                var response = new CreateDomesticProductBatchResponse
                {
                    BatchNumber = batchNumber,
                    Items = responseItems,
                    TotalCreated = allProducts.Count,
                    NormalProductCount = normalItems.Count,
                    SetProductCount = expandedSetItemCount,
                };

                return ApiResponse<CreateDomesticProductBatchResponse>.OK(response, "批量创建成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建国内商品失败");
                return ApiResponse<CreateDomesticProductBatchResponse>.Error(
                    "批量创建失败: " + ex.Message,
                    "CREATE_BATCH_ERROR"
                );
            }
        }

        /// <summary>
        /// 创建单个商品
        /// </summary>
        private async Task<BatchCreatedItemDto?> CreateSingleProductAsync(
            CreateBatchItemDto item,
            CreateDomesticProductBatchRequest request,
            string batchNumber,
            string? supplierName
        )
        {
            try
            {
                var productType =
                    item.ProductType == 1
                        ? BlazorApp.Shared.DTOs.ProductTypeEnum.Set
                        : BlazorApp.Shared.DTOs.ProductTypeEnum.Normal;

                // 生成货号和条码
                var (itemNumber, barcode) =
                    await _itemBarcodeService.GenerateItemNumberAndBarcodeAsync(
                        request.SupplierCode,
                        productType,
                        item.ProductType == 1 ? null : request.PrefixName
                    );

                // 创建 DomesticProduct
                var domesticProduct = new DomesticProduct
                {
                    ProductCode = Guid.NewGuid().ToString(),
                    SupplierCode = request.SupplierCode,
                    ProductName = item.ProductName,
                    HBProductNo = itemNumber,
                    Barcode = barcode,
                    ProductType = item.ProductType,
                    OEMPrice = item.PrivateLabelPrice,
                    IsActive = true,
                };

                await _context.DomesticProductDb.InsertAsync(domesticProduct);

                // 如果是套装商品，创建 DomesticSetProduct
                if (item.ProductType == 1)
                {
                    var setProduct = new DomesticSetProduct
                    {
                        SetProductCode = Guid.NewGuid().ToString(),
                        ProductCode = domesticProduct.ProductCode,
                        ProductNo = itemNumber,
                        SetProductNo = itemNumber,
                        SetBarcode = barcode,
                        OEMPrice = item.PrivateLabelPrice,
                        DomesticPrice = item.SetPrice,
                    };
                    await _context.DomesticSetProductDb.InsertAsync(setProduct);
                }

                // 创建创建日志
                var creationLog = new DomesticProductCreationLog
                {
                    LogId = Guid.NewGuid().ToString(),
                    ProductCode = domesticProduct.ProductCode,
                    SupplierCode = request.SupplierCode,
                    SupplierName = supplierName,
                    HBProductNo = itemNumber,
                    Barcode = barcode,
                    ProductName = item.ProductName,
                    PrefixCode = request.PrefixCode,
                    PrefixName = request.PrefixName,
                    CreationType = "Batch",
                    BatchNumber = batchNumber,
                };
                await _context.DomesticProductCreationLogDb.InsertAsync(creationLog);

                return new BatchCreatedItemDto
                {
                    ProductCode = domesticProduct.ProductCode,
                    HBProductNo = itemNumber,
                    Barcode = barcode,
                    ProductName = item.ProductName ?? "",
                    ProductType = item.ProductType,
                    PrivateLabelPrice = item.PrivateLabelPrice,
                    SetQuantity = item.SetQuantity,
                    SetPrice = item.SetPrice,
                    SubItems = new List<SubItemDto>(),
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建商品失败: {ProductName}", item.ProductName);
                return null;
            }
        }

        /// <summary>
        /// 创建套装子商品
        /// </summary>
        private async Task<SubItemDto?> CreateSubItemAsync(
            CreateBatchItemDto item,
            CreateDomesticProductBatchRequest request,
            string batchNumber,
            string? supplierName,
            string parentItemNumber,
            string parentProductCode
        )
        {
            try
            {
                // 生成套装子商品货号和条码
                var (itemNumber, barcode) =
                    await _itemBarcodeService.GenerateSetItemNumberAndBarcodeAsync(
                        parentItemNumber,
                        BlazorApp.Shared.DTOs.ProductTypeEnum.Set
                    );

                // 创建 DomesticProduct (子商品作为普通商品类型)
                var domesticProduct = new DomesticProduct
                {
                    ProductCode = Guid.NewGuid().ToString(),
                    SupplierCode = request.SupplierCode,
                    ProductName = item.SubItemProductName ?? item.ProductName,
                    HBProductNo = itemNumber,
                    Barcode = barcode,
                    ProductType = 0, // 子商品作为普通商品
                    OEMPrice = item.PrivateLabelPrice,
                    IsActive = true,
                };

                await _context.DomesticProductDb.InsertAsync(domesticProduct);

                // 创建 DomesticSetProduct
                var setProduct = new DomesticSetProduct
                {
                    SetProductCode = Guid.NewGuid().ToString(),
                    ProductCode = parentProductCode,
                    ProductNo = parentItemNumber, // 关联到父商品货号
                    SetProductNo = itemNumber,
                    SetBarcode = barcode,
                    OEMPrice = item.PrivateLabelPrice,
                };
                await _context.DomesticSetProductDb.InsertAsync(setProduct);

                // 创建创建日志
                var creationLog = new DomesticProductCreationLog
                {
                    LogId = Guid.NewGuid().ToString(),
                    ProductCode = domesticProduct.ProductCode,
                    SupplierCode = request.SupplierCode,
                    SupplierName = supplierName,
                    HBProductNo = itemNumber,
                    Barcode = barcode,
                    ProductName = item.SubItemProductName ?? item.ProductName,
                    PrefixCode = request.PrefixCode,
                    PrefixName = request.PrefixName,
                    CreationType = "Batch",
                    BatchNumber = batchNumber,
                    Remark = $"Parent: {parentItemNumber}",
                };
                await _context.DomesticProductCreationLogDb.InsertAsync(creationLog);

                return new SubItemDto
                {
                    ProductCode = domesticProduct.ProductCode,
                    HBProductNo = itemNumber,
                    Barcode = barcode,
                    ProductName = item.SubItemProductName ?? item.ProductName ?? "",
                    PrivateLabelPrice = item.PrivateLabelPrice,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建套装子商品失败: {ProductName}", item.ProductName);
                return null;
            }
        }

        /// <summary>
        /// 生成批次号
        /// </summary>
        private async Task<string> GenerateBatchNumberAsync()
        {
            var dateStr = DateTime.Now.ToString("yyyyMMdd");
            var prefix = $"B{dateStr}";

            // 获取当天最大的批次号
            var existingBatches = await _context
                .DomesticProductCreationLogDb.GetListAsync(x =>
                    x.BatchNumber != null && x.BatchNumber.StartsWith(prefix)
                )
                .ContinueWith(t => t.Result.Select(x => x.BatchNumber).ToList());

            int maxSeq = 0;
            foreach (var batch in existingBatches)
            {
                if (batch != null && batch.Length > prefix.Length)
                {
                    var seqStr = batch.Substring(prefix.Length);
                    if (int.TryParse(seqStr, out int seq))
                    {
                        if (seq > maxSeq)
                            maxSeq = seq;
                    }
                }
            }

            return $"{prefix}{(maxSeq + 1):D3}";
        }

        /// <summary>
        /// 获取批次列表（分页）
        /// </summary>
        public async Task<ApiResponse<PagedResult<DomesticProductBatchDto>>> GetBatchListAsync(
            int page = 1,
            int pageSize = 20,
            string? supplierCode = null,
            DateTime? startDate = null,
            DateTime? endDate = null
        )
        {
            try
            {
                var query = _context.DomesticProductCreationLogDb.GetList().AsQueryable();

                // 按批次号分组
                var batchGroups = query
                    .Where(x => x.BatchNumber != null)
                    .Where(x => supplierCode == null || x.SupplierCode == supplierCode)
                    .Where(x => startDate == null || x.CreatedAt >= startDate)
                    .Where(x => endDate == null || x.CreatedAt <= endDate)
                    .GroupBy(x => x.BatchNumber)
                    .Select(g => new
                    {
                        BatchNumber = g.Key,
                        SupplierCode = g.First().SupplierCode,
                        SupplierName = g.First().SupplierName,
                        CreatedTime = g.Min(x => x.CreatedAt),
                        NormalProductCount = g.Count(x =>
                            x.Product != null
                            && x.Product.ProductType == 0
                            && (x.Remark == null || !x.Remark.StartsWith("Parent:"))
                        ),
                        SetProductCount = g.Count(x =>
                            x.Product != null && x.Product.ProductType == 1
                        ),
                        TotalCount = g.Count(),
                        Remark = g.First().Remark,
                    })
                    .OrderByDescending(x => x.CreatedTime)
                    .ToList();

                // 分页
                var total = batchGroups.Count;
                var pagedData = batchGroups
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => new DomesticProductBatchDto
                    {
                        BatchNumber = x.BatchNumber ?? "",
                        SupplierCode = x.SupplierCode,
                        SupplierName = x.SupplierName,
                        CreatedTime = x.CreatedTime,
                        NormalProductCount = x.NormalProductCount,
                        SetProductCount = x.SetProductCount,
                        TotalCount = x.TotalCount,
                        Remark = x.Remark,
                    })
                    .ToList();

                var result = new PagedResult<DomesticProductBatchDto>
                {
                    Items = pagedData,
                    Total = total,
                    Page = page,
                    PageSize = pageSize,
                };

                return ApiResponse<PagedResult<DomesticProductBatchDto>>.OK(
                    result,
                    "获取批次列表成功"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取批次列表失败");
                return ApiResponse<PagedResult<DomesticProductBatchDto>>.Error(
                    "获取批次列表失败: " + ex.Message,
                    "GET_BATCH_LIST_ERROR"
                );
            }
        }

        /// <summary>
        /// 获取批次详情
        /// </summary>
        public async Task<ApiResponse<DomesticProductBatchDetailDto>> GetBatchDetailAsync(
            string batchNumber
        )
        {
            try
            {
                var logs = await _context.DomesticProductCreationLogDb.GetListAsync(x =>
                    x.BatchNumber == batchNumber
                );

                if (logs == null || !logs.Any())
                {
                    return ApiResponse<DomesticProductBatchDetailDto>.Error(
                        "批次不存在",
                        "BATCH_NOT_FOUND"
                    );
                }

                var firstLog = logs.First();
                var normalCount = 0;
                var setCount = 0;

                var items = new List<BatchDetailItemDto>();

                foreach (var log in logs)
                {
                    var product = await _context.DomesticProductDb.GetByIdAsync(log.ProductCode);
                    var productType = product?.ProductType ?? 0;

                    // 检查是否是子商品（通过 Remark 中的 Parent 信息判断）
                    var isSubItem = false;
                    string? parentProductCode = null;
                    string? parentHBProductNo = null;

                    if (!string.IsNullOrEmpty(log.Remark) && log.Remark.StartsWith("Parent:"))
                    {
                        isSubItem = true;
                        // 查找父商品
                        var parentItemNumber = log.Remark.Replace("Parent:", "").Trim();
                        var parentLog = logs.FirstOrDefault(l => l.HBProductNo == parentItemNumber);
                        if (parentLog != null)
                        {
                            parentProductCode = parentLog.ProductCode;
                            parentHBProductNo = parentLog.HBProductNo;
                        }
                    }

                    if (productType == 0 && !isSubItem)
                        normalCount++;
                    else if (productType == 1)
                        setCount++;

                    items.Add(
                        new BatchDetailItemDto
                        {
                            ProductCode = log.ProductCode,
                            HBProductNo = log.HBProductNo,
                            Barcode = log.Barcode,
                            ProductName = log.ProductName ?? "",
                            ProductType = isSubItem ? 2 : productType, // 2 = SetSubItem
                            PrivateLabelPrice = product?.OEMPrice,
                            SetQuantity =
                                productType == 1
                                    ? await GetSetQuantityAsync(log.ProductCode)
                                    : null,
                            SetPrice =
                                productType == 1 ? await GetSetPriceAsync(log.ProductCode) : null,
                            ParentProductCode = parentProductCode,
                            ParentHBProductNo = parentHBProductNo,
                        }
                    );
                }

                var result = new DomesticProductBatchDetailDto
                {
                    BatchNumber = batchNumber,
                    SupplierCode = firstLog.SupplierCode,
                    SupplierName = firstLog.SupplierName,
                    CreatedTime = logs.Min(x => x.CreatedAt),
                    Remark = firstLog.Remark,
                    NormalProductCount = normalCount,
                    SetProductCount = setCount,
                    Items = items,
                };

                return ApiResponse<DomesticProductBatchDetailDto>.OK(result, "获取批次详情成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取批次详情失败: {BatchNumber}", batchNumber);
                return ApiResponse<DomesticProductBatchDetailDto>.Error(
                    "获取批次详情失败: " + ex.Message,
                    "GET_BATCH_DETAIL_ERROR"
                );
            }
        }

        /// <summary>
        /// 获取套装数量
        /// </summary>
        private async Task<int?> GetSetQuantityAsync(string productCode)
        {
            try
            {
                var setProducts = await _context.DomesticSetProductDb.GetListAsync(x =>
                    x.ProductCode == productCode && x.ProductNo != x.SetProductNo
                );
                return setProducts?.Count;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取套装价格
        /// </summary>
        private async Task<decimal?> GetSetPriceAsync(string productCode)
        {
            try
            {
                var setProduct = await _context.DomesticSetProductDb.GetFirstAsync(x =>
                    x.ProductCode == productCode && x.ProductNo == x.SetProductNo
                );
                return setProduct?.DomesticPrice;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 导出批次创建结果
        /// </summary>
        public async Task<ApiResponse<DomesticProductBatchExportFileDto>> ExportBatchAsync(
            string batchNumber
        )
        {
            try
            {
                var detailResult = await GetBatchDetailAsync(batchNumber);
                if (!detailResult.Success || detailResult.Data == null)
                {
                    return ApiResponse<DomesticProductBatchExportFileDto>.Error(
                        detailResult.Message ?? "批次不存在",
                        detailResult.ErrorCode ?? "BATCH_NOT_FOUND"
                    );
                }

                var detail = detailResult.Data;
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("批次明细");

                worksheet.Cell(1, 1).Value = "批次号";
                worksheet.Cell(1, 2).Value = detail.BatchNumber;
                worksheet.Cell(1, 3).Value = "供应商";
                worksheet.Cell(1, 4).Value = string.IsNullOrWhiteSpace(detail.SupplierName)
                    ? detail.SupplierCode
                    : $"{detail.SupplierCode} - {detail.SupplierName}";
                worksheet.Cell(2, 1).Value = "创建时间";
                worksheet.Cell(2, 2).Value = detail.CreatedTime.ToString("yyyy-MM-dd HH:mm:ss");
                worksheet.Cell(2, 3).Value = "总数量";
                worksheet.Cell(2, 4).Value = detail.Items.Count;

                var headerRow = 4;
                var headers = new[]
                {
                    "批次号",
                    "供应商",
                    "类型",
                    "父套装货号",
                    "货号",
                    "条码",
                    "商品名称",
                    "贴牌价格",
                    "套装数量",
                    "套装价格",
                    "条码图片",
                };

                for (var i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(headerRow, i + 1).Value = headers[i];
                }

                var headerRange = worksheet.Range(headerRow, 1, headerRow, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                var supplierText = string.IsNullOrWhiteSpace(detail.SupplierName)
                    ? detail.SupplierCode
                    : $"{detail.SupplierCode} - {detail.SupplierName}";
                var sortedItems = OrderBatchDetailItemsForExport(detail.Items);

                var row = headerRow + 1;
                foreach (var item in sortedItems)
                {
                    worksheet.Cell(row, 1).Value = detail.BatchNumber;
                    worksheet.Cell(row, 2).Value = supplierText;
                    worksheet.Cell(row, 3).Value = GetProductTypeLabel(item.ProductType);
                    worksheet.Cell(row, 4).Value =
                        item.ProductType == 2 ? item.ParentHBProductNo ?? "" : "";
                    worksheet.Cell(row, 5).Value = item.HBProductNo;
                    worksheet.Cell(row, 6).Style.NumberFormat.Format = "@";
                    worksheet.Cell(row, 6).Value = item.Barcode ?? "";
                    worksheet.Cell(row, 7).Value = item.ProductName ?? "";
                    worksheet.Cell(row, 8).Value = item.PrivateLabelPrice;
                    worksheet.Cell(row, 9).Value = item.SetQuantity;
                    worksheet.Cell(row, 10).Value = item.SetPrice;

                    var barcodeImage = GenerateBarcodeImagePng(item.Barcode);
                    if (barcodeImage != null)
                    {
                        // 导出时把图片直接嵌入条码图片列，方便仓库/采购直接扫码核对。
                        using var imageStream = new MemoryStream(barcodeImage);
                        worksheet
                            .AddPicture(imageStream, $"Barcode_{row}")
                            .MoveTo(worksheet.Cell(row, 11))
                            .WithSize(180, 45);
                        worksheet.Row(row).Height = 45;
                    }

                    row++;
                }

                worksheet.Columns().AdjustToContents();
                worksheet.Column(11).Width = 28;
                worksheet.SheetView.FreezeRows(headerRow);

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);

                return ApiResponse<DomesticProductBatchExportFileDto>.OK(
                    new DomesticProductBatchExportFileDto
                    {
                        Content = stream.ToArray(),
                        FileName = $"domestic-product-batch-{batchNumber}.xlsx",
                    },
                    "导出成功"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出批次创建结果失败: {BatchNumber}", batchNumber);
                return ApiResponse<DomesticProductBatchExportFileDto>.Error(
                    "导出失败: " + ex.Message,
                    "EXPORT_BATCH_ERROR"
                );
            }
        }

        private static string GetProductTypeLabel(int productType)
        {
            return productType switch
            {
                1 => "套装",
                2 => "套装子项",
                _ => "普通",
            };
        }

        private static List<BatchDetailItemDto> OrderBatchDetailItemsForExport(
            IEnumerable<BatchDetailItemDto> items
        )
        {
            var normalItems = items
                .Where(item => item.ProductType == 0)
                .OrderBy(item => item.HBProductNo)
                .ThenBy(item => item.Barcode)
                .ToList();
            var setItems = items
                .Where(item => item.ProductType == 1)
                .OrderBy(item => item.HBProductNo)
                .ThenBy(item => item.Barcode)
                .ToList();
            var subItemsByParent = items
                .Where(item => item.ProductType == 2 && !string.IsNullOrWhiteSpace(item.ParentHBProductNo))
                .GroupBy(item => item.ParentHBProductNo!.Trim())
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(item => item.HBProductNo)
                        .ThenBy(item => item.Barcode)
                        .ToList()
                );

            var orderedItems = new List<BatchDetailItemDto>();
            var groupedSubItems = new HashSet<BatchDetailItemDto>();
            foreach (var setItem in setItems)
            {
                orderedItems.Add(setItem);
                if (
                    !string.IsNullOrWhiteSpace(setItem.HBProductNo)
                    && subItemsByParent.TryGetValue(setItem.HBProductNo.Trim(), out var subItems)
                )
                {
                    orderedItems.AddRange(subItems);
                    foreach (var subItem in subItems)
                    {
                        groupedSubItems.Add(subItem);
                    }
                }
            }

            var unmatchedSubItems = items
                .Where(item => item.ProductType == 2 && !groupedSubItems.Contains(item))
                .OrderBy(item => item.ParentHBProductNo)
                .ThenBy(item => item.HBProductNo)
                .ThenBy(item => item.Barcode)
                .ToList();
            orderedItems.AddRange(unmatchedSubItems);
            orderedItems.AddRange(normalItems);
            return orderedItems;
        }

        private static byte[]? GenerateBarcodeImagePng(string? barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode))
                return null;

            var writer = new ZXing.ImageSharp.BarcodeWriter<Rgba32>
            {
                Format = BarcodeFormat.CODE_128,
                Options = new EncodingOptions
                {
                    Width = 220,
                    Height = 60,
                    Margin = 4,
                },
            };
            using var image = writer.Write(barcode);
            using var stream = new MemoryStream();
            image.Save(stream, PngFormat.Instance);
            return stream.ToArray();
        }

        /// <summary>
        /// 批量更新私牌价格
        /// </summary>
        public async Task<ApiResponse<object>> UpdatePrivateLabelPriceAsync(
            string batchNumber,
            UpdatePrivateLabelPriceRequest request
        )
        {
            try
            {
                // 验证批次是否存在
                var logs = await _context.DomesticProductCreationLogDb.GetListAsync(x =>
                    x.BatchNumber == batchNumber
                );

                if (logs == null || !logs.Any())
                {
                    return ApiResponse<object>.Error("批次不存在", "BATCH_NOT_FOUND");
                }

                var updatedCount = 0;

                foreach (var item in request.Items)
                {
                    // 更新 DomesticProduct
                    var product = await _context.DomesticProductDb.GetByIdAsync(item.ProductCode);
                    if (product != null)
                    {
                        product.OEMPrice = item.PrivateLabelPrice;
                        await _context.DomesticProductDb.UpdateAsync(product);

                        await UpdateSetProductOemPricesAsync(product, item.PrivateLabelPrice);

                        updatedCount++;
                    }
                }

                return ApiResponse<object>.CreateSuccess($"成功更新 {updatedCount} 个商品的价格");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新私牌价格失败: {BatchNumber}", batchNumber);
                return ApiResponse<object>.Error(
                    "批量更新私牌价格失败: " + ex.Message,
                    "UPDATE_PRICE_ERROR"
                );
            }
        }

        /// <summary>
        /// 更新批次明细商品名称和贴牌价格
        /// </summary>
        public async Task<ApiResponse<object>> UpdateBatchItemsAsync(
            string batchNumber,
            UpdateBatchItemsRequest request
        )
        {
            try
            {
                var logs = await _context.DomesticProductCreationLogDb.GetListAsync(x =>
                    x.BatchNumber == batchNumber
                );

                if (logs == null || !logs.Any())
                {
                    return ApiResponse<object>.Error("批次不存在", "BATCH_NOT_FOUND");
                }

                var logsByProductCode = logs
                    .Where(log => !string.IsNullOrWhiteSpace(log.ProductCode))
                    .ToDictionary(log => log.ProductCode, log => log);
                var updatedCount = 0;

                foreach (var item in request.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.ProductCode))
                    {
                        return ApiResponse<object>.Error("商品编码不能为空", "VALIDATION_ERROR");
                    }

                    if (item.PrivateLabelPrice.HasValue && item.PrivateLabelPrice.Value < 0)
                    {
                        return ApiResponse<object>.Error("贴牌价格不能为负数", "VALIDATION_ERROR");
                    }

                    if (!logsByProductCode.TryGetValue(item.ProductCode, out var log))
                    {
                        return ApiResponse<object>.Error("商品不属于该批次", "VALIDATION_ERROR");
                    }

                    var productName = item.ProductName ?? "";
                    var product = await _context.DomesticProductDb.GetByIdAsync(item.ProductCode);
                    if (product != null)
                    {
                        product.ProductName = productName;
                        product.OEMPrice = item.PrivateLabelPrice;
                        await _context.DomesticProductDb.UpdateAsync(product);

                        await UpdateSetProductOemPricesAsync(product, item.PrivateLabelPrice);

                        updatedCount++;
                    }

                    log.ProductName = productName;
                    await _context.DomesticProductCreationLogDb.UpdateAsync(log);
                }

                return ApiResponse<object>.CreateSuccess($"成功更新 {updatedCount} 个商品");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新批次明细失败: {BatchNumber}", batchNumber);
                return ApiResponse<object>.Error(
                    "更新批次明细失败: " + ex.Message,
                    "UPDATE_BATCH_ITEMS_ERROR"
                );
            }
        }

        /// <summary>
        /// 更新商品对应的套装明细贴牌价格，子项按小货号反查父套装明细
        /// </summary>
        private async Task UpdateSetProductOemPricesAsync(
            DomesticProduct product,
            decimal? privateLabelPrice
        )
        {
            var setProducts = await _context.DomesticSetProductDb.GetListAsync(x =>
                x.ProductCode == product.ProductCode || x.SetProductNo == product.HBProductNo
            );
            foreach (var setProduct in setProducts)
            {
                setProduct.OEMPrice = privateLabelPrice;
                await _context.DomesticSetProductDb.UpdateAsync(setProduct);
            }
        }
    }
}
