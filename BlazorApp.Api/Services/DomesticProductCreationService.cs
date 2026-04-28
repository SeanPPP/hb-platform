using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Logging;
using SqlSugar;

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
            ILogger<DomesticProductCreationService> logger)
        {
            _context = context;
            _itemBarcodeService = itemBarcodeService;
            _logger = logger;
        }

        /// <summary>
        /// 批量创建国内商品
        /// </summary>
        public async Task<ApiResponse<CreateDomesticProductBatchResponse>> CreateBatchAsync(
            CreateDomesticProductBatchRequest request)
        {
            try
            {
                // 生成批次号
                var batchNumber = await GenerateBatchNumberAsync();
                
                // 获取供应商名称
                var supplier = await _context.ChinaSupplierDb.GetFirstAsync(x => x.SupplierCode == request.SupplierCode);
                var supplierName = supplier?.SupplierName;

                var response = new CreateDomesticProductBatchResponse
                {
                    BatchNumber = batchNumber,
                    Items = new List<BatchCreatedItemDto>()
                };

                // 按 ParentItemNumber 分组处理，先处理主商品，再处理子商品
                var mainItems = request.Items.Where(i => string.IsNullOrEmpty(i.ParentItemNumber)).ToList();
                var subItems = request.Items.Where(i => !string.IsNullOrEmpty(i.ParentItemNumber)).ToList();

                int normalCount = 0;
                int setCount = 0;

                // 处理主商品
                foreach (var item in mainItems)
                {
                    var createdItem = await CreateSingleProductAsync(item, request, batchNumber, supplierName);
                    if (createdItem != null)
                    {
                        response.Items.Add(createdItem);
                        if (item.ProductType == 0)
                            normalCount++;
                        else if (item.ProductType == 1)
                            setCount++;

                        // 如果是套装商品，处理子商品
                        if (item.ProductType == 1)
                        {
                            var relatedSubItems = subItems
                                .Where(s => s.ParentItemNumber == createdItem.HBProductNo)
                                .ToList();

                            foreach (var subItem in relatedSubItems)
                            {
                                var createdSubItem = await CreateSubItemAsync(
                                    subItem, request, batchNumber, supplierName, 
                                    createdItem.HBProductNo, createdItem.ProductCode);
                                if (createdSubItem != null)
                                {
                                    createdItem.SubItems.Add(createdSubItem);
                                }
                            }
                        }
                    }
                }

                response.TotalCreated = response.Items.Count;
                response.NormalProductCount = normalCount;
                response.SetProductCount = setCount;

                return ApiResponse<CreateDomesticProductBatchResponse>.OK(response, "批量创建成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建国内商品失败");
                return ApiResponse<CreateDomesticProductBatchResponse>.Error("批量创建失败: " + ex.Message, "CREATE_BATCH_ERROR");
            }
        }

        /// <summary>
        /// 创建单个商品
        /// </summary>
        private async Task<BatchCreatedItemDto?> CreateSingleProductAsync(
            CreateBatchItemDto item,
            CreateDomesticProductBatchRequest request,
            string batchNumber,
            string? supplierName)
        {
            try
            {
                var productType = item.ProductType == 1 
                    ? BlazorApp.Shared.DTOs.ProductTypeEnum.Set 
                    : BlazorApp.Shared.DTOs.ProductTypeEnum.Normal;

                // 生成货号和条码
                var (itemNumber, barcode) = await _itemBarcodeService.GenerateItemNumberAndBarcodeAsync(
                    request.SupplierCode,
                    productType,
                    item.ProductType == 1 ? null : request.PrefixName);

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
                    IsActive = true
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
                        DomesticPrice = item.SetPrice
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
                    BatchNumber = batchNumber
                };
                await _context.DomesticProductCreationLogDb.InsertAsync(creationLog);

                return new BatchCreatedItemDto
                {
                    ProductCode = domesticProduct.ProductCode,
                    HBProductNo = itemNumber,
                    Barcode = barcode,
                    ProductName = item.ProductName,
                    ProductType = item.ProductType,
                    PrivateLabelPrice = item.PrivateLabelPrice,
                    SetQuantity = item.SetQuantity,
                    SetPrice = item.SetPrice,
                    SubItems = new List<SubItemDto>()
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
            string parentProductCode)
        {
            try
            {
                // 生成套装子商品货号和条码
                var (itemNumber, barcode) = await _itemBarcodeService.GenerateSetItemNumberAndBarcodeAsync(
                    parentItemNumber,
                    BlazorApp.Shared.DTOs.ProductTypeEnum.Set);

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
                    IsActive = true
                };

                await _context.DomesticProductDb.InsertAsync(domesticProduct);

                // 创建 DomesticSetProduct
                var setProduct = new DomesticSetProduct
                {
                    SetProductCode = Guid.NewGuid().ToString(),
                    ProductCode = domesticProduct.ProductCode,
                    ProductNo = parentItemNumber, // 关联到父商品货号
                    SetProductNo = itemNumber,
                    SetBarcode = barcode,
                    OEMPrice = item.PrivateLabelPrice
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
                    Remark = $"Parent: {parentItemNumber}"
                };
                await _context.DomesticProductCreationLogDb.InsertAsync(creationLog);

                return new SubItemDto
                {
                    ProductCode = domesticProduct.ProductCode,
                    HBProductNo = itemNumber,
                    Barcode = barcode,
                    ProductName = item.SubItemProductName ?? item.ProductName,
                    PrivateLabelPrice = item.PrivateLabelPrice
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
            var existingBatches = await _context.DomesticProductCreationLogDb
                .GetListAsync(x => x.BatchNumber != null && x.BatchNumber.StartsWith(prefix))
                .ContinueWith(t => t.Result.Select(x => x.BatchNumber).ToList());

            int maxSeq = 0;
            foreach (var batch in existingBatches)
            {
                if (batch != null && batch.Length > prefix.Length)
                {
                    var seqStr = batch.Substring(prefix.Length);
                    if (int.TryParse(seqStr, out int seq))
                    {
                        if (seq > maxSeq) maxSeq = seq;
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
            DateTime? endDate = null)
        {
            try
            {
                var query = _context.DomesticProductCreationLogDb
                    .GetList()
                    .AsQueryable();

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
                        NormalProductCount = g.Count(x => x.Product != null && x.Product.ProductType == 0),
                        SetProductCount = g.Count(x => x.Product != null && x.Product.ProductType == 1),
                        TotalCount = g.Count(),
                        Remark = g.First().Remark
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
                        Remark = x.Remark
                    })
                    .ToList();

                var result = new PagedResult<DomesticProductBatchDto>
                {
                    Items = pagedData,
                    Total = total,
                    Page = page,
                    PageSize = pageSize
                };

                return ApiResponse<PagedResult<DomesticProductBatchDto>>.OK(result, "获取批次列表成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取批次列表失败");
                return ApiResponse<PagedResult<DomesticProductBatchDto>>.Error("获取批次列表失败: " + ex.Message, "GET_BATCH_LIST_ERROR");
            }
        }

        /// <summary>
        /// 获取批次详情
        /// </summary>
        public async Task<ApiResponse<DomesticProductBatchDetailDto>> GetBatchDetailAsync(string batchNumber)
        {
            try
            {
                var logs = await _context.DomesticProductCreationLogDb
                    .GetListAsync(x => x.BatchNumber == batchNumber);

                if (logs == null || !logs.Any())
                {
                    return ApiResponse<DomesticProductBatchDetailDto>.Error("批次不存在", "BATCH_NOT_FOUND");
                }

                var firstLog = logs.First();
                var normalCount = 0;
                var setCount = 0;

                var items = new List<BatchDetailItemDto>();

                foreach (var log in logs)
                {
                    var product = await _context.DomesticProductDb.GetByIdAsync(log.ProductCode);
                    var productType = product?.ProductType ?? 0;

                    if (productType == 0)
                        normalCount++;
                    else if (productType == 1)
                        setCount++;

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

                    items.Add(new BatchDetailItemDto
                    {
                        ProductCode = log.ProductCode,
                        HBProductNo = log.HBProductNo,
                        Barcode = log.Barcode,
                        ProductName = log.ProductName ?? "",
                        ProductType = isSubItem ? 2 : productType, // 2 = SetSubItem
                        PrivateLabelPrice = product?.OEMPrice,
                        SetQuantity = productType == 1 ? await GetSetQuantityAsync(log.ProductCode) : null,
                        SetPrice = productType == 1 ? await GetSetPriceAsync(log.ProductCode) : null,
                        ParentProductCode = parentProductCode,
                        ParentHBProductNo = parentHBProductNo
                    });
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
                    Items = items
                };

                return ApiResponse<DomesticProductBatchDetailDto>.OK(result, "获取批次详情成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取批次详情失败: {BatchNumber}", batchNumber);
                return ApiResponse<DomesticProductBatchDetailDto>.Error("获取批次详情失败: " + ex.Message, "GET_BATCH_DETAIL_ERROR");
            }
        }

        /// <summary>
        /// 获取套装数量
        /// </summary>
        private async Task<int?> GetSetQuantityAsync(string productCode)
        {
            try
            {
                var setProducts = await _context.DomesticSetProductDb
                    .GetListAsync(x => x.ProductCode == productCode);
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
                var setProduct = await _context.DomesticSetProductDb
                    .GetFirstAsync(x => x.ProductCode == productCode);
                return setProduct?.DomesticPrice;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 批量更新私牌价格
        /// </summary>
        public async Task<ApiResponse<object>> UpdatePrivateLabelPriceAsync(
            string batchNumber,
            UpdatePrivateLabelPriceRequest request)
        {
            try
            {
                // 验证批次是否存在
                var logs = await _context.DomesticProductCreationLogDb
                    .GetListAsync(x => x.BatchNumber == batchNumber);

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

                        // 更新 DomesticSetProduct
                        var setProducts = await _context.DomesticSetProductDb
                            .GetListAsync(x => x.ProductCode == item.ProductCode);
                        foreach (var setProduct in setProducts)
                        {
                            setProduct.OEMPrice = item.PrivateLabelPrice;
                            await _context.DomesticSetProductDb.UpdateAsync(setProduct);
                        }

                        updatedCount++;
                    }
                }

                return ApiResponse<object>.CreateSuccess($"成功更新 {updatedCount} 个商品的价格");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新私牌价格失败: {BatchNumber}", batchNumber);
                return ApiResponse<object>.Error("批量更新私牌价格失败: " + ex.Message, "UPDATE_PRICE_ERROR");
            }
        }
    }
}
