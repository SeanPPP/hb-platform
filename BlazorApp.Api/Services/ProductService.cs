using System.ComponentModel.DataAnnotations;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    public class ProductService : IProductService
    {
        private readonly SqlSugarContext _db;
        private readonly IMapper _mapper;
        private readonly ILogger<ProductService> _logger;
        private readonly ITranslationService _translationService;

        public ProductService(
            SqlSugarContext db,
            IMapper mapper,
            ILogger<ProductService> logger,
            ITranslationService translationService
        )
        {
            _db = db;
            _mapper = mapper;
            _logger = logger;
            _translationService = translationService;
        }

        public async Task<ProductDto> GetByIdAsync(string productGuid)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productGuid))
                    throw new ValidationException("ProductGUID不能为空");

                var product = await _db.ProductDb.GetByIdAsync(productGuid);
                if (product == null)
                    throw new KeyNotFoundException($"找不到商品: {productGuid}");

                var dto = _mapper.Map<ProductDto>(product);

                // 自动翻译商品名称
                await TranslateProductNameAsync(dto);

                return dto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品失败: {ProductGuid}", productGuid);
                throw;
            }
        }

        public async Task<PagedResult<ProductDto>> GetAllAsync(ProductFilterDto filter)
        {
            try
            {
                var query = _db.ProductDb.AsQueryable();

                // 应用过滤条件
                if (!string.IsNullOrWhiteSpace(filter.ProductName))
                    query = query.Where(p =>
                        p.ProductName != null && p.ProductName.Contains(filter.ProductName)
                    );

                if (!string.IsNullOrWhiteSpace(filter.ProductCode))
                    query = query.Where(p =>
                        p.ProductCode != null && p.ProductCode.Contains(filter.ProductCode)
                    );

                if (!string.IsNullOrWhiteSpace(filter.Barcode))
                    query = query.Where(p =>
                        p.Barcode != null && p.Barcode.Contains(filter.Barcode)
                    );

                if (!string.IsNullOrWhiteSpace(filter.ProductCategoryGUID))
                    query = query.Where(p => p.ProductCategoryGUID == filter.ProductCategoryGUID);

                if (!string.IsNullOrWhiteSpace(filter.WarehouseCategoryGUID))
                    query = query.Where(p =>
                        p.WarehouseCategoryGUID == filter.WarehouseCategoryGUID
                    );

                if (filter.IsActive.HasValue)
                    query = query.Where(p => p.IsActive == filter.IsActive.Value);

                if (filter.IsSpecialProduct.HasValue)
                    query = query.Where(p => p.IsSpecialProduct == filter.IsSpecialProduct.Value);

                if (filter.MinPrice.HasValue)
                    query = query.Where(p => p.RetailPrice >= filter.MinPrice.Value);

                if (filter.MaxPrice.HasValue)
                    query = query.Where(p => p.RetailPrice <= filter.MaxPrice.Value);

                // 排序
                query = ApplySorting(query, filter.SortBy, filter.SortDescending);

                // 获取总数
                var totalCount = await query.CountAsync();

                // 分页
                var items = await query
                    .Skip((filter.PageNumber - 1) * filter.PageSize)
                    .Take(filter.PageSize)
                    .ToListAsync();

                var dtos = _mapper.Map<List<ProductDto>>(items);

                // 自动翻译商品名称
                await TranslateProductNamesAsync(dtos);

                var pagedResult = new PagedResult<ProductDto>
                {
                    Items = dtos,
                    Total = totalCount,
                    Page = filter.PageNumber,
                    PageSize = filter.PageSize,
                };

                return pagedResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品列表失败");
                throw;
            }
        }

        public async Task<ProductDto> CreateAsync(CreateProductDto createDto)
        {
            try
            {
                ValidateCreateDto(createDto);

                // 检查商品编码是否已存在
                if (
                    await _db
                        .ProductDb.AsQueryable()
                        .AnyAsync(p => p.ProductCode == createDto.ProductCode)
                )
                    throw new ValidationException($"商品编码 {createDto.ProductCode} 已存在");

                var product = _mapper.Map<Product>(createDto);
                product.ProductCode = Guid.NewGuid().ToString();
                product.CreatedAt = DateTime.UtcNow;
                product.UpdatedAt = DateTime.UtcNow;

                var result = await _db.ProductDb.InsertAsync(product);
                if (!result)
                    throw new InvalidOperationException("创建商品失败");

                _logger.LogInformation("创建商品成功: {ProductName}", product.ProductName);
                return _mapper.Map<ProductDto>(product);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建商品失败");
                throw;
            }
        }

        public async Task<ProductDto> UpdateAsync(UpdateProductDto updateDto)
        {
            try
            {
                ValidateUpdateDto(updateDto);

                var existing = await _db.ProductDb.GetByIdAsync(updateDto.ProductCode);
                if (existing == null)
                    throw new KeyNotFoundException($"找不到商品: {updateDto.ProductCode}");

                // 检查商品编码是否已存在（排除当前商品）
                if (
                    await _db
                        .ProductDb.AsQueryable()
                        .AnyAsync(p => p.ProductCode == updateDto.ProductCode)
                )
                    throw new ValidationException($"商品编码 {updateDto.ProductCode} 已存在");

                _mapper.Map(updateDto, existing);
                existing.UpdatedAt = DateTime.UtcNow;

                var result = await _db.ProductDb.UpdateAsync(existing);
                if (!result)
                    throw new InvalidOperationException("更新商品失败");

                _logger.LogInformation("更新商品成功: {ProductName}", existing.ProductName);
                return _mapper.Map<ProductDto>(existing);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新商品失败");
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string ProductCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ProductCode))
                    throw new ValidationException("ProductCode");

                var result = await _db.ProductDb.DeleteByIdAsync(ProductCode);
                if (result)
                    _logger.LogInformation("删除商品成功: {ProductCode}", ProductCode);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除商品失败: {ProductCode}", ProductCode);
                throw;
            }
        }

        public async Task<List<ProductDto>> GetByCategoryAsync(string productCategoryGuid)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productCategoryGuid))
                    throw new ValidationException("ProductCategoryGUID不能为空");

                var products = await _db
                    .ProductDb.AsQueryable()
                    .Where(p => p.ProductCategoryGUID == productCategoryGuid)
                    .OrderBy(p => p.ProductName)
                    .ToListAsync();

                var dtos = _mapper.Map<List<ProductDto>>(products);

                // 自动翻译商品名称
                await TranslateProductNamesAsync(dtos);

                return dtos;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "根据商品类别获取商品失败: {ProductCategoryGuid}",
                    productCategoryGuid
                );
                throw;
            }
        }

        public async Task<List<ProductDto>> GetByWarehouseCategoryAsync(
            string warehouseCategoryGuid
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(warehouseCategoryGuid))
                    throw new ValidationException("WarehouseCategoryGUID不能为空");

                var products = await _db
                    .ProductDb.AsQueryable()
                    .Where(p => p.WarehouseCategoryGUID == warehouseCategoryGuid)
                    .OrderBy(p => p.ProductName)
                    .ToListAsync();

                var dtos = _mapper.Map<List<ProductDto>>(products);

                // 自动翻译商品名称
                await TranslateProductNamesAsync(dtos);

                return dtos;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "根据仓库类别获取商品失败: {WarehouseCategoryGuid}",
                    warehouseCategoryGuid
                );
                throw;
            }
        }

        public async Task<List<ProductDto>> SearchByBarcodeAsync(string barcode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(barcode))
                    throw new ValidationException("条码不能为空");

                var products = await _db
                    .ProductDb.AsQueryable()
                    .Where(p => p.Barcode != null && p.Barcode.Contains(barcode))
                    .OrderBy(p => p.ProductName)
                    .ToListAsync();

                var dtos = _mapper.Map<List<ProductDto>>(products);

                // 自动翻译商品名称
                await TranslateProductNamesAsync(dtos);

                return dtos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据条码搜索商品失败: {Barcode}", barcode);
                throw;
            }
        }

        public async Task<bool> ExistsAsync(string productCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productCode))
                    throw new ValidationException("商品编码不能为空");

                return await _db
                    .ProductDb.AsQueryable()
                    .AnyAsync(p => p.ProductCode == productCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查商品编码是否存在失败: {ProductCode}", productCode);
                throw;
            }
        }

        public async Task<bool> ToggleActiveStatusAsync(string ProductCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ProductCode))
                    throw new ValidationException("ProductCode");

                var existing = await _db.ProductDb.GetByIdAsync(ProductCode);
                if (existing == null)
                    throw new KeyNotFoundException($"找不到商品: {ProductCode}");

                existing.IsActive = !existing.IsActive;
                existing.UpdatedAt = DateTime.UtcNow;

                var result = await _db.ProductDb.UpdateAsync(existing);
                if (result)
                    _logger.LogInformation(
                        "切换商品状态成功: {ProductCode} -> {IsActive}",
                        ProductCode,
                        existing.IsActive
                    );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切换商品状态失败: {ProductCode}", ProductCode);
                throw;
            }
        }

        public async Task<List<ProductDto>> GetByCodesAsync(List<string> productCodes)
        {
            try
            {
                if (productCodes == null || !productCodes.Any())
                    throw new ValidationException("商品编码列表不能为空");

                _logger.LogInformation("批量获取 {Count} 个商品的详情", productCodes.Count);

                // 使用IN查询批量获取商品
                var products = await _db
                    .ProductDb.AsQueryable()
                    .Where(p => p.ProductCode != null && productCodes.Contains(p.ProductCode))
                    .ToListAsync();

                var dtos = _mapper.Map<List<ProductDto>>(products);

                // 自动翻译商品名称
                await TranslateProductNamesAsync(dtos);

                _logger.LogInformation("成功获取 {Count} 个商品的详情", dtos.Count);

                return dtos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量获取商品详情失败");
                throw;
            }
        }

        private void ValidateCreateDto(CreateProductDto dto)
        {
            if (dto == null)
                throw new ValidationException("创建数据不能为空");

            if (string.IsNullOrWhiteSpace(dto.ProductCategoryGUID))
                throw new ValidationException("商品类别GUID不能为空");

            if (string.IsNullOrWhiteSpace(dto.ProductCode))
                throw new ValidationException("商品编码不能为空");

            if (string.IsNullOrWhiteSpace(dto.ProductName))
                throw new ValidationException("商品名称不能为空");
        }

        private void ValidateUpdateDto(UpdateProductDto dto)
        {
            if (dto == null)
                throw new ValidationException("更新数据不能为空");

            if (string.IsNullOrWhiteSpace(dto.ProductCode))
                throw new ValidationException("ProductCode");

            if (string.IsNullOrWhiteSpace(dto.ProductCategoryGUID))
                throw new ValidationException("商品类别GUID不能为空");

            if (string.IsNullOrWhiteSpace(dto.ProductCode))
                throw new ValidationException("商品编码不能为空");

            if (string.IsNullOrWhiteSpace(dto.ProductName))
                throw new ValidationException("商品名称不能为空");
        }

        private ISugarQueryable<Product> ApplySorting(
            ISugarQueryable<Product> query,
            string? sortBy,
            bool sortDescending
        )
        {
            sortBy = sortBy?.ToLower();

            return sortBy switch
            {
                "productcode" => sortDescending
                    ? query.OrderByDescending(p => p.ProductCode)
                    : query.OrderBy(p => p.ProductCode),
                "productname" => sortDescending
                    ? query.OrderByDescending(p => p.ProductName)
                    : query.OrderBy(p => p.ProductName),
                "retailprice" => sortDescending
                    ? query.OrderByDescending(p => p.RetailPrice)
                    : query.OrderBy(p => p.RetailPrice),
                "isactive" => sortDescending
                    ? query.OrderByDescending(p => p.IsActive)
                    : query.OrderBy(p => p.IsActive),
                "createdat" => sortDescending
                    ? query.OrderByDescending(p => p.CreatedAt)
                    : query.OrderBy(p => p.CreatedAt),
                _ => sortDescending
                    ? query.OrderByDescending(p => p.ProductName)
                    : query.OrderBy(p => p.ProductName),
            };
        }

        /// <summary>
        /// 自动翻译单个产品名称（如果包含中文）
        /// </summary>
        private async Task TranslateProductNameAsync(ProductDto product)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(product.ProductName))
                    return;

                // 检测是否包含中文
                var containsChinese = await _translationService.DetectChineseAsync(
                    product.ProductName
                );
                if (containsChinese)
                {
                    // 翻译为英文
                    var translatedName = await _translationService.TranslateToEnglishAsync(
                        product.ProductName
                    );
                    if (!string.IsNullOrWhiteSpace(translatedName))
                    {
                        product.ProductName = translatedName;
                        _logger.LogDebug(
                            "商品名称已翻译: {OriginalName} -> {TranslatedName}",
                            product.ProductName,
                            translatedName
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "翻译商品名称失败，使用原名称: {ProductName}",
                    product.ProductName
                );
                // 翻译失败时继续使用原名称，不抛出异常
            }
        }

        /// <summary>
        /// 批量翻译产品名称
        /// </summary>
        private async Task TranslateProductNamesAsync(List<ProductDto> products)
        {
            if (products == null || !products.Any())
                return;

            try
            {
                // 收集需要翻译的产品名称
                var namesToTranslate = products
                    .Where(p => !string.IsNullOrWhiteSpace(p.ProductName))
                    .Select(p => p.ProductName)
                    .Distinct()
                    .ToList();

                if (!namesToTranslate.Any())
                    return;

                // 批量翻译
                var translations = await _translationService.BatchTranslateToEnglishAsync(
                    namesToTranslate
                );

                // 应用翻译结果
                foreach (var product in products)
                {
                    if (
                        !string.IsNullOrWhiteSpace(product.ProductName)
                        && translations.ContainsKey(product.ProductName)
                    )
                    {
                        var translatedName = translations[product.ProductName];
                        if (
                            !string.IsNullOrWhiteSpace(translatedName)
                            && translatedName != product.ProductName
                        )
                        {
                            _logger.LogDebug(
                                "商品名称已翻译: {OriginalName} -> {TranslatedName}",
                                product.ProductName,
                                translatedName
                            );
                            product.ProductName = translatedName;
                        }
                    }
                }

                _logger.LogInformation("成功翻译 {Count} 个商品名称", products.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "批量翻译商品名称失败，使用原名称");
                // 翻译失败时继续使用原名称，不抛出异常
            }
        }
    }
}
