using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 套装商品管理服务
    /// </summary>
    public class DomesticSetProductService : IDomesticSetProductService
    {
        private readonly SqlSugarContext _context;
        private readonly IMapper _mapper;
        private readonly ILogger<DomesticSetProductService> _logger;
        private readonly ItemBarcodeService _itemBarcodeService;

        public DomesticSetProductService(
            SqlSugarContext context,
            IMapper mapper,
            ILogger<DomesticSetProductService> logger,
            ItemBarcodeService itemBarcodeService
        )
        {
            _context = context;
            _mapper = mapper;
            _logger = logger;
            _itemBarcodeService = itemBarcodeService;
        }

        /// <summary>
        /// 获取套装商品分页列表
        /// </summary>
        public async Task<
            ApiResponse<PagedResult<DomesticSetProductDto>>
        > GetDomesticSetProductsAsync(DomesticSetProductQueryDto query)
        {
            try
            {
                var db = _context.Db;

                // 构建查询
                var setProductQuery = db.Queryable<DomesticSetProduct>()
                    .LeftJoin<DomesticProduct>((sp, p) => sp.ProductCode == p.ProductCode)
                    .LeftJoin<ChinaSupplier>((sp, p, s) => p.SupplierCode == s.SupplierCode)
                    .Where((sp, p, s) => !sp.IsDeleted && !p.IsDeleted);

                // 应用搜索条件
                if (!string.IsNullOrWhiteSpace(query.Search))
                {
                    setProductQuery = setProductQuery.Where(
                        (sp, p, s) =>
                            (sp.SetProductNo != null && sp.SetProductNo.Contains(query.Search))
                            || (sp.SetBarcode != null && sp.SetBarcode.Contains(query.Search))
                            || (p.ProductName != null && p.ProductName.Contains(query.Search))
                    );
                }

                if (!string.IsNullOrWhiteSpace(query.ProductCode))
                {
                    setProductQuery = setProductQuery.Where(
                        (sp, p, s) => sp.ProductCode == query.ProductCode
                    );
                }

                if (!string.IsNullOrWhiteSpace(query.SupplierCode))
                {
                    setProductQuery = setProductQuery.Where(
                        (sp, p, s) => p.SupplierCode == query.SupplierCode
                    );
                }

                if (query.MinPrice.HasValue)
                {
                    setProductQuery = setProductQuery.Where(
                        (sp, p, s) => sp.DomesticPrice >= query.MinPrice.Value
                    );
                }

                if (query.MaxPrice.HasValue)
                {
                    setProductQuery = setProductQuery.Where(
                        (sp, p, s) => sp.DomesticPrice <= query.MaxPrice.Value
                    );
                }

                // 应用排序
                if (!string.IsNullOrEmpty(query.SortField))
                {
                    var isDescending =
                        !string.IsNullOrEmpty(query.SortDirection)
                        && query.SortDirection.ToLower() == "desc";

                    setProductQuery = query.SortField.ToLower() switch
                    {
                        "setproductno" => isDescending
                            ? setProductQuery.OrderByDescending((sp, p, s) => sp.SetProductNo)
                            : setProductQuery.OrderBy((sp, p, s) => sp.SetProductNo),
                        "productname" => isDescending
                            ? setProductQuery.OrderByDescending((sp, p, s) => p.ProductName)
                            : setProductQuery.OrderBy((sp, p, s) => p.ProductName),
                        "suppliercode" => isDescending
                            ? setProductQuery.OrderByDescending((sp, p, s) => p.SupplierCode)
                            : setProductQuery.OrderBy((sp, p, s) => p.SupplierCode),
                        "domesticprice" => isDescending
                            ? setProductQuery.OrderByDescending((sp, p, s) => sp.DomesticPrice)
                            : setProductQuery.OrderBy((sp, p, s) => sp.DomesticPrice),
                        "createdat" => isDescending
                            ? setProductQuery.OrderByDescending((sp, p, s) => sp.CreatedAt)
                            : setProductQuery.OrderBy((sp, p, s) => sp.CreatedAt),
                        _ => setProductQuery.OrderByDescending((sp, p, s) => sp.CreatedAt),
                    };
                }
                else
                {
                    setProductQuery = setProductQuery.OrderByDescending((sp, p, s) => sp.CreatedAt);
                }

                // 获取总数
                var totalCount = await setProductQuery.CountAsync();

                // 分页查询
                var setProducts = await setProductQuery
                    .Select(
                        (sp, p, s) =>
                            new DomesticSetProduct
                            {
                                SetProductCode = sp.SetProductCode,
                                ProductCode = sp.ProductCode,
                                ProductNo = sp.ProductNo,
                                SetProductNo = sp.SetProductNo,
                                SetBarcode = sp.SetBarcode,
                                DomesticPrice = sp.DomesticPrice,
                                ImportPrice = sp.ImportPrice,
                                OEMPrice = sp.OEMPrice,
                                Remarks = sp.Remarks,
                                CreatedAt = sp.CreatedAt,
                                UpdatedAt = sp.UpdatedAt,
                                CreatedBy = sp.CreatedBy,
                                UpdatedBy = sp.UpdatedBy,
                                DomesticProduct = new DomesticProduct
                                {
                                    ProductName = p.ProductName,
                                    SupplierCode = p.SupplierCode,
                                    Supplier = new ChinaSupplier { SupplierName = s.SupplierName },
                                },
                            }
                    )
                    .Skip(query.Skip)
                    .Take(query.Take)
                    .ToListAsync();

                // 映射到DTO
                var setProductDtos = _mapper.Map<List<DomesticSetProductDto>>(setProducts);

                var result = new PagedResult<DomesticSetProductDto>
                {
                    Items = setProductDtos,
                    Total = totalCount,
                    Page = query.Page,
                    PageSize = query.PageSize,
                };

                return ApiResponse<PagedResult<DomesticSetProductDto>>.OK(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取套装商品列表失败");
                return ApiResponse<PagedResult<DomesticSetProductDto>>.Error(
                    "获取套装商品列表失败",
                    "GET_SET_PRODUCTS_ERROR"
                );
            }
        }

        /// <summary>
        /// 根据编码获取套装商品详情
        /// </summary>
        public async Task<
            ApiResponse<DomesticSetProductDetailDto>
        > GetDomesticSetProductByCodeAsync(string setProductCode)
        {
            try
            {
                var db = _context.Db;

                var setProduct = await db.Queryable<DomesticSetProduct>()
                    .LeftJoin<DomesticProduct>((sp, p) => sp.ProductCode == p.ProductCode)
                    .LeftJoin<ChinaSupplier>((sp, p, s) => p.SupplierCode == s.SupplierCode)
                    .Where((sp, p, s) => sp.SetProductCode == setProductCode && !sp.IsDeleted)
                    .Select(
                        (sp, p, s) =>
                            new DomesticSetProduct
                            {
                                SetProductCode = sp.SetProductCode,
                                ProductCode = sp.ProductCode,
                                ProductNo = sp.ProductNo,
                                SetProductNo = sp.SetProductNo,
                                SetBarcode = sp.SetBarcode,
                                DomesticPrice = sp.DomesticPrice,
                                ImportPrice = sp.ImportPrice,
                                OEMPrice = sp.OEMPrice,
                                Remarks = sp.Remarks,
                                CreatedAt = sp.CreatedAt,
                                UpdatedAt = sp.UpdatedAt,
                                CreatedBy = sp.CreatedBy,
                                UpdatedBy = sp.UpdatedBy,
                                DomesticProduct = new DomesticProduct
                                {
                                    ProductCode = p.ProductCode,
                                    SupplierCode = p.SupplierCode,
                                    ProductName = p.ProductName,
                                    EnglishProductName = p.EnglishProductName,
                                    HBProductNo = p.HBProductNo,
                                    ProductType = p.ProductType,
                                    Supplier = new ChinaSupplier
                                    {
                                        SupplierCode = s.SupplierCode,
                                        SupplierName = s.SupplierName,
                                    },
                                },
                            }
                    )
                    .FirstAsync();

                if (setProduct == null)
                {
                    return ApiResponse<DomesticSetProductDetailDto>.Error(
                        "套装商品不存在",
                        "SET_PRODUCT_NOT_FOUND"
                    );
                }

                var setProductDetailDto = _mapper.Map<DomesticSetProductDetailDto>(setProduct);

                return ApiResponse<DomesticSetProductDetailDto>.OK(setProductDetailDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "获取套装商品详情失败，SetProductCode: {SetProductCode}",
                    setProductCode
                );
                return ApiResponse<DomesticSetProductDetailDto>.Error(
                    "获取套装商品详情失败",
                    "GET_SET_PRODUCT_DETAIL_ERROR"
                );
            }
        }

        /// <summary>
        /// 根据商品编码获取套装商品列表
        /// </summary>
        public async Task<
            ApiResponse<List<DomesticSetProductDto>>
        > GetSetProductsByProductCodeAsync(string productCode)
        {
            try
            {
                var db = _context.Db;

                var setProducts = await db.Queryable<DomesticSetProduct>()
                    .LeftJoin<DomesticProduct>((sp, p) => sp.ProductCode == p.ProductCode)
                    .Where((sp, p) => sp.ProductCode == productCode && !sp.IsDeleted)
                    .OrderBy((sp, p) => sp.SetProductNo)
                    .Select(
                        (sp, p) =>
                            new DomesticSetProduct
                            {
                                SetProductCode = sp.SetProductCode,
                                ProductCode = sp.ProductCode,
                                ProductNo = sp.ProductNo,
                                SetProductNo = sp.SetProductNo,
                                SetBarcode = sp.SetBarcode,
                                DomesticPrice = sp.DomesticPrice,
                                ImportPrice = sp.ImportPrice,
                                OEMPrice = sp.OEMPrice,
                                Remarks = sp.Remarks,
                                CreatedAt = sp.CreatedAt,
                                DomesticProduct = new DomesticProduct
                                {
                                    ProductName = p.ProductName,
                                },
                            }
                    )
                    .ToListAsync();

                var setProductDtos = _mapper.Map<List<DomesticSetProductDto>>(setProducts);
                return ApiResponse<List<DomesticSetProductDto>>.OK(setProductDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "获取商品套装列表失败，ProductCode: {ProductCode}",
                    productCode
                );
                return ApiResponse<List<DomesticSetProductDto>>.Error(
                    "获取商品套装列表失败",
                    "GET_PRODUCT_SET_PRODUCTS_ERROR"
                );
            }
        }

        /// <summary>
        /// 根据供应商编码获取套装商品列表
        /// </summary>
        public async Task<
            ApiResponse<List<DomesticSetProductDto>>
        > GetSetProductsBySupplierCodeAsync(string supplierCode)
        {
            try
            {
                var db = _context.Db;

                var setProducts = await db.Queryable<DomesticSetProduct>()
                    .LeftJoin<DomesticProduct>((sp, p) => sp.ProductCode == p.ProductCode)
                    .Where(
                        (sp, p) => p.SupplierCode == supplierCode && !sp.IsDeleted && !p.IsDeleted
                    )
                    .OrderBy((sp, p) => p.ProductName)
                    .OrderBy((sp, p) => sp.SetProductNo)
                    .Select(
                        (sp, p) =>
                            new DomesticSetProduct
                            {
                                SetProductCode = sp.SetProductCode,
                                ProductCode = sp.ProductCode,
                                SetProductNo = sp.SetProductNo,
                                DomesticPrice = sp.DomesticPrice,
                                DomesticProduct = new DomesticProduct
                                {
                                    ProductName = p.ProductName,
                                },
                            }
                    )
                    .ToListAsync();

                var setProductDtos = _mapper.Map<List<DomesticSetProductDto>>(setProducts);
                return ApiResponse<List<DomesticSetProductDto>>.OK(setProductDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "获取供应商套装商品列表失败，SupplierCode: {SupplierCode}",
                    supplierCode
                );
                return ApiResponse<List<DomesticSetProductDto>>.Error(
                    "获取供应商套装商品列表失败",
                    "GET_SUPPLIER_SET_PRODUCTS_ERROR"
                );
            }
        }

        /// <summary>
        /// 创建套装商品
        /// </summary>
        public async Task<ApiResponse<DomesticSetProductDto>> CreateDomesticSetProductAsync(
            CreateDomesticSetProductDto dto
        )
        {
            try
            {
                var db = _context.Db;

                // 检查商品是否存在
                var product = await db.Queryable<DomesticProduct>()
                    .Where(p => p.ProductCode == dto.ProductCode && !p.IsDeleted)
                    .FirstAsync();

                if (product == null)
                {
                    return ApiResponse<DomesticSetProductDto>.Error(
                        "商品不存在",
                        "PRODUCT_NOT_FOUND"
                    );
                }

                // 检查商品是否为套装/多码商品类型
                if (product.ProductType == 0)
                {
                    return ApiResponse<DomesticSetProductDto>.Error(
                        "只有套装类型的商品才能创建套装商品",
                        "INVALID_PRODUCT_TYPE"
                    );
                }

                // 创建新套装商品
                var setProduct = _mapper.Map<DomesticSetProduct>(dto);
                setProduct.SetProductCode = UuidHelper.GenerateUuid7();

                // 设置商品货号（如果未提供，使用商品的HB货号）
                if (string.IsNullOrWhiteSpace(setProduct.ProductNo))
                {
                    setProduct.ProductNo = product.HBProductNo;
                }

                // 生成套装货号（如果未提供）
                if (string.IsNullOrWhiteSpace(setProduct.SetProductNo))
                {
                    var setProductNoResponse = await GenerateNextSetProductNoAsync(
                        product.HBProductNo!
                    );
                    if (!setProductNoResponse.Success)
                    {
                        return ApiResponse<DomesticSetProductDto>.Error(
                            "生成套装货号失败",
                            "GENERATE_SET_PRODUCT_NO_ERROR"
                        );
                    }
                    setProduct.SetProductNo = setProductNoResponse.Data ?? string.Empty;
                }
                else
                {
                    // 检查套装货号是否已存在
                    var existingSetProduct = await db.Queryable<DomesticSetProduct>()
                        .Where(sp => sp.SetProductNo == setProduct.SetProductNo && !sp.IsDeleted)
                        .FirstAsync();

                    if (existingSetProduct != null)
                    {
                        return ApiResponse<DomesticSetProductDto>.Error(
                            "套装货号已存在",
                            "SET_PRODUCT_NO_EXISTS"
                        );
                    }
                }

                // 生成套装条形码（如果未提供）
                if (string.IsNullOrWhiteSpace(setProduct.SetBarcode))
                {
                    var barcodeResponse = await GenerateSetProductBarcodeAsync(
                        product.SupplierCode!
                    );
                    if (!barcodeResponse.Success)
                    {
                        return ApiResponse<DomesticSetProductDto>.Error(
                            "生成套装条码失败",
                            "GENERATE_SET_BARCODE_ERROR"
                        );
                    }
                    setProduct.SetBarcode = barcodeResponse.Data;
                }
                else
                {
                    // 检查套装条形码是否已存在
                    var existingBarcode = await db.Queryable<DomesticSetProduct>()
                        .Where(sp => sp.SetBarcode == setProduct.SetBarcode && !sp.IsDeleted)
                        .FirstAsync();

                    if (existingBarcode != null)
                    {
                        return ApiResponse<DomesticSetProductDto>.Error(
                            "套装条形码已存在",
                            "SET_BARCODE_EXISTS"
                        );
                    }
                }

                setProduct.CreatedAt = DateTime.Now;
                setProduct.UpdatedAt = DateTime.Now;
                setProduct.CreatedBy = "System"; // TODO: 从当前用户获取
                setProduct.UpdatedBy = "System";

                await db.Insertable(setProduct).ExecuteCommandAsync();

                var setProductDto = _mapper.Map<DomesticSetProductDto>(setProduct);

                _logger.LogInformation(
                    "创建套装商品成功，SetProductCode: {SetProductCode}",
                    setProduct.SetProductCode
                );
                return ApiResponse<DomesticSetProductDto>.OK(setProductDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建套装商品失败");
                return ApiResponse<DomesticSetProductDto>.Error(
                    "创建套装商品失败",
                    "CREATE_SET_PRODUCT_ERROR"
                );
            }
        }

        /// <summary>
        /// 更新套装商品
        /// </summary>
        public async Task<ApiResponse<DomesticSetProductDto>> UpdateDomesticSetProductAsync(
            string setProductCode,
            UpdateDomesticSetProductDto dto
        )
        {
            try
            {
                var db = _context.Db;

                var setProduct = await db.Queryable<DomesticSetProduct>()
                    .Where(sp => sp.SetProductCode == setProductCode && !sp.IsDeleted)
                    .FirstAsync();

                if (setProduct == null)
                {
                    return ApiResponse<DomesticSetProductDto>.Error(
                        "套装商品不存在",
                        "SET_PRODUCT_NOT_FOUND"
                    );
                }

                // 更新套装商品信息
                _mapper.Map(dto, setProduct);
                setProduct.UpdatedAt = DateTime.Now;
                setProduct.UpdatedBy = "System"; // TODO: 从当前用户获取

                await db.Updateable(setProduct).ExecuteCommandAsync();

                var setProductDto = _mapper.Map<DomesticSetProductDto>(setProduct);

                _logger.LogInformation(
                    "更新套装商品成功，SetProductCode: {SetProductCode}",
                    setProductCode
                );
                return ApiResponse<DomesticSetProductDto>.OK(setProductDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "更新套装商品失败，SetProductCode: {SetProductCode}",
                    setProductCode
                );
                return ApiResponse<DomesticSetProductDto>.Error(
                    "更新套装商品失败",
                    "UPDATE_SET_PRODUCT_ERROR"
                );
            }
        }

        /// <summary>
        /// 删除套装商品
        /// </summary>
        public async Task<ApiResponse<bool>> DeleteDomesticSetProductAsync(string setProductCode)
        {
            try
            {
                var db = _context.Db;

                var setProduct = await db.Queryable<DomesticSetProduct>()
                    .Where(sp => sp.SetProductCode == setProductCode && !sp.IsDeleted)
                    .FirstAsync();

                if (setProduct == null)
                {
                    return ApiResponse<bool>.Error("套装商品不存在", "SET_PRODUCT_NOT_FOUND");
                }

                // 软删除
                setProduct.IsDeleted = true;
                setProduct.UpdatedAt = DateTime.Now;
                setProduct.UpdatedBy = "System"; // TODO: 从当前用户获取

                await db.Updateable(setProduct).ExecuteCommandAsync();

                _logger.LogInformation(
                    "删除套装商品成功，SetProductCode: {SetProductCode}",
                    setProductCode
                );
                return ApiResponse<bool>.OK(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "删除套装商品失败，SetProductCode: {SetProductCode}",
                    setProductCode
                );
                return ApiResponse<bool>.Error("删除套装商品失败", "DELETE_SET_PRODUCT_ERROR");
            }
        }

        /// <summary>
        /// 生成下一个套装货号
        /// </summary>
        public async Task<ApiResponse<string>> GenerateNextSetProductNoAsync(string baseItemNumber)
        {
            try
            {
                var (setItemNumber, _) = await _itemBarcodeService.GenerateSetItemNumberAndBarcodeAsync(
                    baseItemNumber,
                    ProductTypeEnum.Set
                );
                return ApiResponse<string>.OK(setItemNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "生成套装货号失败，BaseItemNumber: {BaseItemNumber}",
                    baseItemNumber
                );
                return ApiResponse<string>.Error(
                    "生成套装货号失败",
                    "GENERATE_SET_PRODUCT_NO_ERROR"
                );
            }
        }

        /// <summary>
        /// 生成套装商品条形码
        /// </summary>
        public async Task<ApiResponse<string>> GenerateSetProductBarcodeAsync(string supplierCode)
        {
            try
            {
                var (_, barcode) = await _itemBarcodeService.GenerateItemNumberAndBarcodeAsync(
                    supplierCode,
                    ProductTypeEnum.Set
                );
                return ApiResponse<string>.OK(barcode);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "生成套装条码失败，SupplierCode: {SupplierCode}",
                    supplierCode
                );
                return ApiResponse<string>.Error("生成套装条码失败", "GENERATE_SET_BARCODE_ERROR");
            }
        }

        /// <summary>
        /// 检查套装货号是否存在
        /// </summary>
        public async Task<ApiResponse<bool>> CheckSetProductNoExistsAsync(
            string setProductNo,
            string? excludeSetProductCode = null
        )
        {
            try
            {
                var db = _context.Db;

                var query = db.Queryable<DomesticSetProduct>()
                    .Where(sp => sp.SetProductNo == setProductNo && !sp.IsDeleted);

                if (!string.IsNullOrWhiteSpace(excludeSetProductCode))
                {
                    query = query.Where(sp => sp.SetProductCode != excludeSetProductCode);
                }

                var exists = await query.AnyAsync();
                return ApiResponse<bool>.OK(exists);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "检查套装货号是否存在失败，SetProductNo: {SetProductNo}",
                    setProductNo
                );
                return ApiResponse<bool>.Error(
                    "检查套装货号是否存在失败",
                    "CHECK_SET_PRODUCT_NO_EXISTS_ERROR"
                );
            }
        }

        /// <summary>
        /// 检查套装条形码是否存在
        /// </summary>
        public async Task<ApiResponse<bool>> CheckSetBarcodeExistsAsync(
            string setBarcode,
            string? excludeSetProductCode = null
        )
        {
            try
            {
                var db = _context.Db;

                var query = db.Queryable<DomesticSetProduct>()
                    .Where(sp => sp.SetBarcode == setBarcode && !sp.IsDeleted);

                if (!string.IsNullOrWhiteSpace(excludeSetProductCode))
                {
                    query = query.Where(sp => sp.SetProductCode != excludeSetProductCode);
                }

                var exists = await query.AnyAsync();
                return ApiResponse<bool>.OK(exists);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "检查套装条形码是否存在失败，SetBarcode: {SetBarcode}",
                    setBarcode
                );
                return ApiResponse<bool>.Error(
                    "检查套装条形码是否存在失败",
                    "CHECK_SET_BARCODE_EXISTS_ERROR"
                );
            }
        }

        /// <summary>
        /// 批量创建套装商品
        /// </summary>
        public async Task<
            ApiResponse<List<DomesticSetProductDto>>
        > BatchCreateDomesticSetProductsAsync(BatchCreateDomesticSetProductDto dto)
        {
            try
            {
                var db = _context.Db;

                // 检查商品是否存在
                var product = await db.Queryable<DomesticProduct>()
                    .Where(p => p.ProductCode == dto.ProductCode && !p.IsDeleted)
                    .FirstAsync();

                if (product == null)
                {
                    return ApiResponse<List<DomesticSetProductDto>>.Error(
                        "商品不存在",
                        "PRODUCT_NOT_FOUND"
                    );
                }

                // 检查商品是否为套装类型
                if (product.ProductType != 2)
                {
                    return ApiResponse<List<DomesticSetProductDto>>.Error(
                        "只有套装类型的商品才能创建套装商品",
                        "INVALID_PRODUCT_TYPE"
                    );
                }

                var setItemNumberBarcodeList = await _itemBarcodeService.GenerateBatchSetItemNumbersAndBarcodesAsync(
                    product.HBProductNo!,
                    ProductTypeEnum.Set,
                    dto.Count
                );

                // 批量创建套装商品
                var setProducts = new List<DomesticSetProduct>();
                var now = DateTime.Now;

                for (int i = 0; i < dto.SetProducts.Count && i < setItemNumberBarcodeList.Count; i++)
                {
                    var setProductItem = dto.SetProducts[i];
                    var setProduct = _mapper.Map<DomesticSetProduct>(setProductItem);

                    setProduct.SetProductCode = UuidHelper.GenerateUuid7();
                    setProduct.ProductCode = dto.ProductCode;
                    setProduct.ProductNo = product.HBProductNo;
                    setProduct.SetProductNo = setItemNumberBarcodeList[i].itemNumber;
                    setProduct.SetBarcode = setItemNumberBarcodeList[i].barcode;

                    setProduct.CreatedAt = now;
                    setProduct.UpdatedAt = now;
                    setProduct.CreatedBy = "System"; // TODO: 从当前用户获取
                    setProduct.UpdatedBy = "System";

                    setProducts.Add(setProduct);
                }

                await db.Insertable(setProducts).ExecuteCommandAsync();

                var setProductDtos = _mapper.Map<List<DomesticSetProductDto>>(setProducts);

                _logger.LogInformation(
                    "批量创建套装商品成功，ProductCode: {ProductCode}, Count: {Count}",
                    dto.ProductCode,
                    setProducts.Count
                );
                return ApiResponse<List<DomesticSetProductDto>>.OK(setProductDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建套装商品失败");
                return ApiResponse<List<DomesticSetProductDto>>.Error(
                    "批量创建套装商品失败",
                    "BATCH_CREATE_SET_PRODUCTS_ERROR"
                );
            }
        }

        /// <summary>
        /// 批量删除套装商品
        /// </summary>
        public async Task<ApiResponse<bool>> BatchDeleteDomesticSetProductsAsync(
            List<string> setProductCodes
        )
        {
            try
            {
                var db = _context.Db;

                // 检查套装商品是否存在
                var setProducts = await db.Queryable<DomesticSetProduct>()
                    .Where(sp => setProductCodes.Contains(sp.SetProductCode) && !sp.IsDeleted)
                    .ToListAsync();

                if (setProducts.Count != setProductCodes.Count)
                {
                    return ApiResponse<bool>.Error(
                        "部分套装商品不存在",
                        "SOME_SET_PRODUCTS_NOT_FOUND"
                    );
                }

                // 批量软删除
                var now = DateTime.Now;
                foreach (var setProduct in setProducts)
                {
                    setProduct.IsDeleted = true;
                    setProduct.UpdatedAt = now;
                    setProduct.UpdatedBy = "System"; // TODO: 从当前用户获取
                }

                await db.Updateable(setProducts).ExecuteCommandAsync();

                _logger.LogInformation("批量删除套装商品成功，Count: {Count}", setProducts.Count);
                return ApiResponse<bool>.OK(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除套装商品失败");
                return ApiResponse<bool>.Error(
                    "批量删除套装商品失败",
                    "BATCH_DELETE_SET_PRODUCTS_ERROR"
                );
            }
        }

        /// <summary>
        /// 复制套装商品结构
        /// </summary>
        public async Task<ApiResponse<List<DomesticSetProductDto>>> CopySetProductStructureAsync(
            string sourceProductCode,
            string targetProductCode
        )
        {
            try
            {
                var db = _context.Db;

                // 检查源商品和目标商品是否存在
                var sourceProduct = await db.Queryable<DomesticProduct>()
                    .Where(p => p.ProductCode == sourceProductCode && !p.IsDeleted)
                    .FirstAsync();

                var targetProduct = await db.Queryable<DomesticProduct>()
                    .Where(p => p.ProductCode == targetProductCode && !p.IsDeleted)
                    .FirstAsync();

                if (sourceProduct == null || targetProduct == null)
                {
                    return ApiResponse<List<DomesticSetProductDto>>.Error(
                        "源商品或目标商品不存在",
                        "PRODUCT_NOT_FOUND"
                    );
                }

                // 检查目标商品是否为套装类型
                if (targetProduct.ProductType != 2)
                {
                    return ApiResponse<List<DomesticSetProductDto>>.Error(
                        "目标商品必须是套装类型",
                        "INVALID_TARGET_PRODUCT_TYPE"
                    );
                }

                // 获取源商品的套装商品列表
                var sourceSetProducts = await db.Queryable<DomesticSetProduct>()
                    .Where(sp => sp.ProductCode == sourceProductCode && !sp.IsDeleted)
                    .ToListAsync();

                if (!sourceSetProducts.Any())
                {
                    return ApiResponse<List<DomesticSetProductDto>>.Error(
                        "源商品没有套装商品",
                        "NO_SOURCE_SET_PRODUCTS"
                    );
                }

                var setItemNumberBarcodeList = await _itemBarcodeService.GenerateBatchSetItemNumbersAndBarcodesAsync(
                    targetProduct.HBProductNo!,
                    ProductTypeEnum.Set,
                    sourceSetProducts.Count
                );

                // 复制套装商品结构
                var newSetProducts = new List<DomesticSetProduct>();
                var now = DateTime.Now;

                for (int i = 0; i < sourceSetProducts.Count; i++)
                {
                    var sourceSetProduct = sourceSetProducts[i];
                    var newSetProduct = new DomesticSetProduct
                    {
                        SetProductCode = UuidHelper.GenerateUuid7(),
                        ProductCode = targetProductCode,
                        ProductNo = targetProduct.HBProductNo,
                        SetProductNo = setItemNumberBarcodeList[i].itemNumber,
                        SetBarcode = setItemNumberBarcodeList[i].barcode,
                        DomesticPrice = sourceSetProduct.DomesticPrice,
                        ImportPrice = sourceSetProduct.ImportPrice,
                        OEMPrice = sourceSetProduct.OEMPrice,
                        Remarks = sourceSetProduct.Remarks,
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = "System", // TODO: 从当前用户获取
                        UpdatedBy = "System",
                    };

                    newSetProducts.Add(newSetProduct);
                }

                await db.Insertable(newSetProducts).ExecuteCommandAsync();

                var setProductDtos = _mapper.Map<List<DomesticSetProductDto>>(newSetProducts);

                _logger.LogInformation(
                    "复制套装商品结构成功，SourceProductCode: {SourceProductCode}, TargetProductCode: {TargetProductCode}, Count: {Count}",
                    sourceProductCode,
                    targetProductCode,
                    newSetProducts.Count
                );
                return ApiResponse<List<DomesticSetProductDto>>.OK(setProductDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "复制套装商品结构失败，SourceProductCode: {SourceProductCode}, TargetProductCode: {TargetProductCode}",
                    sourceProductCode,
                    targetProductCode
                );
                return ApiResponse<List<DomesticSetProductDto>>.Error(
                    "复制套装商品结构失败",
                    "COPY_SET_PRODUCT_STRUCTURE_ERROR"
                );
            }
        }

        /// <summary>
        /// 获取套装商品价格统计
        /// </summary>
        public async Task<
            ApiResponse<Dictionary<string, decimal?>>
        > GetSetProductPriceStatisticsAsync(string? productCode = null, string? supplierCode = null)
        {
            try
            {
                var db = _context.Db;

                var query = db.Queryable<DomesticSetProduct>()
                    .LeftJoin<DomesticProduct>((sp, p) => sp.ProductCode == p.ProductCode)
                    .Where((sp, p) => !sp.IsDeleted && sp.DomesticPrice.HasValue);

                if (!string.IsNullOrWhiteSpace(productCode))
                {
                    query = query.Where((sp, p) => sp.ProductCode == productCode);
                }

                if (!string.IsNullOrWhiteSpace(supplierCode))
                {
                    query = query.Where((sp, p) => p.SupplierCode == supplierCode);
                }

                var prices = await query.Select((sp, p) => sp.DomesticPrice!.Value).ToListAsync();

                var result = new Dictionary<string, decimal?>();

                if (prices.Any())
                {
                    result["Min"] = prices.Min();
                    result["Max"] = prices.Max();
                    result["Average"] = prices.Average();
                    result["Count"] = prices.Count;
                }
                else
                {
                    result["Min"] = null;
                    result["Max"] = null;
                    result["Average"] = null;
                    result["Count"] = 0;
                }

                return ApiResponse<Dictionary<string, decimal?>>.OK(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "获取套装商品价格统计失败，ProductCode: {ProductCode}, SupplierCode: {SupplierCode}",
                    productCode,
                    supplierCode
                );
                return ApiResponse<Dictionary<string, decimal?>>.Error(
                    "获取套装商品价格统计失败",
                    "GET_SET_PRODUCT_PRICE_STATISTICS_ERROR"
                );
            }
        }
    }
}
