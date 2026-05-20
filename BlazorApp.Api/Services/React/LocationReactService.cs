using System.Linq.Expressions;
using System.Text.RegularExpressions;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class LocationReactService : ILocationReactService
    {
        private const int PickingLocationType = 1;
        private const int StorageLocationType = 2;
        private const string LocationCodePattern = @"^[A-Z]-\d{2}-\d{2}-\d{2}$";

        private readonly SqlSugarContext _context;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<LocationReactService> _logger;

        public LocationReactService(
            SqlSugarContext context,
            ICurrentUserService currentUserService,
            ILogger<LocationReactService> logger
        )
        {
            _context = context;
            _currentUserService = currentUserService;
            _logger = logger;
        }

        public async Task<PagedListReactDto<LocationReactDto>> GetPagedListAsync(
            LocationReactFilterDto filter
        )
        {
            try
            {
                var query = _context.Db.Queryable<Location>().Where(l => !l.IsDeleted);

                if (filter.LocationType.HasValue)
                {
                    query = query.Where(l => l.LocationType == filter.LocationType.Value);
                }

                if (filter.IsUsed.HasValue)
                {
                    var usedLocationGuids = await _context
                        .Db.Queryable<ProductLocation>()
                        .Where(pl => !pl.IsDeleted)
                        .Select(pl => pl.LocationGuid)
                        .Distinct()
                        .ToListAsync();

                    if (filter.IsUsed.Value)
                    {
                        query = query.Where(l => usedLocationGuids.Contains(l.LocationGuid));
                    }
                    else
                    {
                        query = query.Where(l => !usedLocationGuids.Contains(l.LocationGuid));
                    }
                }

                if (filter.Filters != null && filter.Filters.Any())
                {
                    foreach (var kv in filter.Filters)
                    {
                        var key = kv.Key?.ToLower();
                        var values =
                            kv.Value?.Where(v => !string.IsNullOrWhiteSpace(v)).ToList()
                            ?? new List<string>();
                        if (!values.Any())
                            continue;

                        switch (key)
                        {
                            case "locationcode":
                                var codeLowers = values.Select(v => v.ToLower()).ToList();
                                query = query.Where(l =>
                                    l.LocationCode != null
                                    && codeLowers.Any(v => l.LocationCode.ToLower().Contains(v))
                                );
                                break;
                            case "locationbarcode":
                                var barcodeLowers = values.Select(v => v.ToLower()).ToList();
                                query = query.Where(l =>
                                    l.LocationBarcode != null
                                    && barcodeLowers.Any(v =>
                                        l.LocationBarcode.ToLower().Contains(v)
                                    )
                                );
                                break;
                            case "status":
                                var statusInts = values
                                    .Select(v => int.TryParse(v, out var i) ? i : -1)
                                    .Where(i => i >= 0)
                                    .ToList();
                                if (statusInts.Any())
                                    query = query.Where(l =>
                                        l.Status.HasValue && statusInts.Contains(l.Status.Value)
                                    );
                                break;
                            case "locationtype":
                                var typeInts = values
                                    .Select(v => int.TryParse(v, out var i) ? i : -1)
                                    .Where(i => i >= 0)
                                    .ToList();
                                if (typeInts.Any())
                                    query = query.Where(l =>
                                        l.LocationType.HasValue
                                        && typeInts.Contains(l.LocationType.Value)
                                    );
                                break;
                            case "updatedby":
                                var byLowers = values.Select(v => v.ToLower()).ToList();
                                query = query.Where(l =>
                                    l.UpdatedBy != null
                                    && byLowers.Any(v => l.UpdatedBy.ToLower().Contains(v))
                                );
                                break;
                            case "updatedat":
                                if (
                                    values.Count >= 1
                                    && DateTime.TryParse(values[0], out var dateStart)
                                )
                                {
                                    query = query.Where(l => l.UpdatedAt >= dateStart);
                                }
                                if (
                                    values.Count >= 2
                                    && DateTime.TryParse(values[1], out var dateEnd)
                                )
                                {
                                    query = query.Where(l => l.UpdatedAt <= dateEnd);
                                }
                                break;
                        }
                    }
                }

                var total = await query.Clone().CountAsync();

                var sortBy = filter.SortBy ?? "LocationCode";
                var sortDirection = filter.SortDirection ?? "asc";
                var orderByExpression = CreateOrderByExpression(sortBy);
                query =
                    sortDirection.ToLower() == "desc"
                        ? query.OrderByDescending(orderByExpression)
                        : query.OrderBy(orderByExpression);

                var locations = await query.ToPageListAsync(filter.PageNumber, filter.PageSize);

                var result = new List<LocationReactDto>();
                foreach (var loc in locations)
                {
                    var dto = MapToDto(loc);
                    await LoadProductsAsync(dto);
                    result.Add(dto);
                }

                return new PagedListReactDto<LocationReactDto>
                {
                    Items = result,
                    Total = total,
                    PageNumber = filter.PageNumber,
                    PageSize = filter.PageSize,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取货位列表失败");
                throw;
            }
        }

        public async Task<ApiResponse<LocationReactDto>> GetByIdAsync(string locationGuid)
        {
            try
            {
                var location = await _context
                    .Db.Queryable<Location>()
                    .Where(l => l.LocationGuid == locationGuid && !l.IsDeleted)
                    .FirstAsync();

                if (location == null)
                {
                    return ApiResponse<LocationReactDto>.Error("货位不存在", "NOT_FOUND");
                }

                var dto = MapToDto(location);
                await LoadProductsAsync(dto);

                return ApiResponse<LocationReactDto>.OK(dto, "获取货位详情成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取货位详情失败: {LocationGuid}", locationGuid);
                return ApiResponse<LocationReactDto>.Error(
                    "获取货位详情失败",
                    "DATABASE_ERROR",
                    ex.Message
                );
            }
        }

        public async Task<ApiResponse<LocationReactDto>> CreateAsync(CreateLocationReactDto dto)
        {
            try
            {
                var validationError = ValidateLocationInput(dto.LocationCode, dto.LocationType, dto.Status);
                if (validationError != null)
                {
                    return ApiResponse<LocationReactDto>.Error(validationError.Value.Message, validationError.Value.ErrorCode);
                }

                var locationCode = NormalizeLocationCode(dto.LocationCode);
                var existingList = await _context
                    .Db.Queryable<Location>()
                    .Where(l => l.LocationCode == locationCode && !l.IsDeleted)
                    .ToListAsync();

                if (existingList.Count > 0)
                {
                    return ApiResponse<LocationReactDto>.Error("货位代码已存在", "CONFLICT");
                }

                var locationBarcode = await GenerateUniqueLocationBarcodeAsync();
                var username = _currentUserService.GetCurrentUsername();
                var location = new Location
                {
                    LocationGuid = Guid.NewGuid().ToString(),
                    LocationCode = locationCode,
                    LocationBarcode = locationBarcode,
                    LocationType = dto.LocationType,
                    Status = dto.Status ?? 1,
                    CreatedBy = username,
                    UpdatedBy = username,
                    UpdatedAt = DateTime.UtcNow,
                };

                await _context.Db.Insertable(location).ExecuteCommandAsync();

                var result = MapToDto(location);
                return ApiResponse<LocationReactDto>.OK(result, "创建货位成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建货位失败");
                return ApiResponse<LocationReactDto>.Error(
                    "创建货位失败",
                    "DATABASE_ERROR",
                    ex.Message
                );
            }
        }

        public async Task<ApiResponse<LocationReactDto>> UpdateAsync(
            string locationGuid,
            UpdateLocationReactDto dto
        )
        {
            try
            {
                var locationList = await _context
                    .Db.Queryable<Location>()
                    .Where(l => l.LocationGuid == locationGuid && !l.IsDeleted)
                    .ToListAsync();

                if (locationList.Count == 0)
                {
                    return ApiResponse<LocationReactDto>.Error("货位不存在", "NOT_FOUND");
                }

                var location = locationList[0];

                var validationError = ValidateLocationInput(dto.LocationCode, dto.LocationType, dto.Status);
                if (validationError != null)
                {
                    return ApiResponse<LocationReactDto>.Error(validationError.Value.Message, validationError.Value.ErrorCode);
                }

                var locationCode = NormalizeLocationCode(dto.LocationCode);
                var duplicateList = await _context
                    .Db.Queryable<Location>()
                    .Where(l =>
                        l.LocationCode == locationCode
                        && l.LocationGuid != locationGuid
                        && !l.IsDeleted
                    )
                    .ToListAsync();

                if (duplicateList.Count > 0)
                {
                    return ApiResponse<LocationReactDto>.Error(
                        "货位代码已被其他货位使用",
                        "CONFLICT"
                    );
                }

                var username = _currentUserService.GetCurrentUsername();
                location.LocationCode = locationCode;
                if (string.IsNullOrWhiteSpace(location.LocationBarcode))
                {
                    location.LocationBarcode = await GenerateUniqueLocationBarcodeAsync();
                }
                location.LocationType = dto.LocationType;
                location.Status = dto.Status;
                location.UpdatedBy = username;
                location.UpdatedAt = DateTime.UtcNow;

                await _context.Db.Updateable(location).ExecuteCommandAsync();

                return await GetByIdAsync(locationGuid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新货位失败: {LocationGuid}", locationGuid);
                return ApiResponse<LocationReactDto>.Error(
                    "更新货位失败",
                    "DATABASE_ERROR",
                    ex.Message
                );
            }
        }

        public async Task<ApiResponse<bool>> DeleteAsync(string locationGuid)
        {
            try
            {
                var locationList = await _context
                    .Db.Queryable<Location>()
                    .Where(l => l.LocationGuid == locationGuid && !l.IsDeleted)
                    .ToListAsync();

                if (locationList.Count == 0)
                {
                    return ApiResponse<bool>.Error("货位不存在", "NOT_FOUND");
                }

                var location = locationList[0];

                var hasProducts = await _context
                    .Db.Queryable<ProductLocation>()
                    .AnyAsync(pl => pl.LocationGuid == locationGuid && !pl.IsDeleted);

                if (hasProducts)
                {
                    return ApiResponse<bool>.Error("该货位下存在关联商品，无法删除", "CONFLICT");
                }

                var username = _currentUserService.GetCurrentUsername();
                location.IsDeleted = true;
                location.UpdatedBy = username;
                location.UpdatedAt = DateTime.UtcNow;

                await _context.Db.Updateable(location).ExecuteCommandAsync();

                return ApiResponse<bool>.OK(true, "删除货位成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除货位失败: {LocationGuid}", locationGuid);
                return ApiResponse<bool>.Error("删除货位失败", "DATABASE_ERROR", ex.Message);
            }
        }

        public async Task<List<LocationLookupItemDto>> LookupAsync(string keyword)
        {
            var trimmed = keyword?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return new List<LocationLookupItemDto>();
            }

            var lowered = trimmed.ToLower();
            var locations = await _context
                .Db.Queryable<Location>()
                .Where(l =>
                    !l.IsDeleted
                    && (
                        (l.LocationCode != null && l.LocationCode.ToLower().Contains(lowered))
                        || (l.LocationBarcode != null && l.LocationBarcode.ToLower().Contains(lowered))
                    )
                )
                .OrderBy(l => l.LocationCode)
                .Take(20)
                .ToListAsync();

            if (locations.Count == 0)
            {
                return new List<LocationLookupItemDto>();
            }

            var locationGuids = locations.Select(item => item.LocationGuid).ToList();
            var counts = await _context
                .Db.Queryable<ProductLocation>()
                .Where(pl => !pl.IsDeleted && locationGuids.Contains(pl.LocationGuid!))
                .GroupBy(pl => pl.LocationGuid)
                .Select(pl => new { LocationGuid = pl.LocationGuid, Count = SqlFunc.AggregateCount(pl.Guid) })
                .ToListAsync();

            var countMap = counts.ToDictionary(item => item.LocationGuid ?? string.Empty, item => item.Count);

            return locations.Select(location => new LocationLookupItemDto
            {
                LocationGuid = location.LocationGuid,
                LocationCode = location.LocationCode,
                LocationBarcode = location.LocationBarcode,
                Status = location.Status,
                LocationType = location.LocationType,
                ProductCount = countMap.TryGetValue(location.LocationGuid, out var count) ? count : 0,
            }).ToList();
        }

        public async Task<ApiResponse<LocationReactDto>> BindProductAsync(
            string locationGuid,
            string productIdentifier
        )
        {
            var transactionStarted = false;
            try
            {
                var location = await _context
                    .Db.Queryable<Location>()
                    .Where(l => l.LocationGuid == locationGuid && !l.IsDeleted)
                    .FirstAsync();
                if (location == null)
                {
                    return ApiResponse<LocationReactDto>.Error("货位不存在", "NOT_FOUND");
                }

                var productResult = await ResolveProductAsync(productIdentifier);
                if (!productResult.Success || productResult.Data == null)
                {
                    return ApiResponse<LocationReactDto>.Error(
                        productResult.Message,
                        productResult.ErrorCode,
                        productResult.Details
                    );
                }

                var productCode = productResult.Data.ProductCode;
                var warehouseProduct = await _context
                    .Db.Queryable<WarehouseProduct>()
                    .Where(w => w.ProductCode == productCode && !w.IsDeleted)
                    .FirstAsync();
                if (warehouseProduct == null)
                {
                    return ApiResponse<LocationReactDto>.Error("仓库商品不存在", "NOT_FOUND");
                }

                var existingSameBinding = await _context
                    .Db.Queryable<ProductLocation>()
                    .AnyAsync(pl =>
                        pl.LocationGuid == locationGuid
                        && pl.ProductCode == productCode
                        && !pl.IsDeleted
                    );
                if (existingSameBinding)
                {
                    return await GetByIdAsync(locationGuid);
                }

                if (location.LocationType == PickingLocationType)
                {
                    var existingLocationProduct = await _context
                        .Db.Queryable<ProductLocation>()
                        .Where(pl => pl.LocationGuid == locationGuid && !pl.IsDeleted)
                        .FirstAsync();

                    if (existingLocationProduct != null)
                    {
                        return ApiResponse<LocationReactDto>.Error(
                            "该配货位已绑定商品，请解绑后继续绑定新的货位",
                            "PICKING_LOCATION_ALREADY_BOUND",
                            new
                            {
                                locationGuid,
                                existingProductCode = existingLocationProduct.ProductCode,
                            }
                        );
                    }

                    var existingPickingLocation = await _context
                        .Db.Queryable<ProductLocation, Location>((pl, l) => new JoinQueryInfos(
                            JoinType.Inner,
                            pl.LocationGuid == l.LocationGuid
                        ))
                        .Where((pl, l) =>
                            pl.ProductCode == productCode
                            && !pl.IsDeleted
                            && !l.IsDeleted
                            && l.LocationType == PickingLocationType
                        )
                        .Select((pl, l) => new { pl.LocationGuid, l.LocationCode })
                        .FirstAsync();

                    if (existingPickingLocation != null)
                    {
                        return ApiResponse<LocationReactDto>.Error(
                            "该商品已绑定配货位，请解绑后继续绑定新的货位",
                            "PRODUCT_PICKING_LOCATION_ALREADY_BOUND",
                            new
                            {
                                productCode,
                                existingPickingLocation.LocationGuid,
                                existingPickingLocation.LocationCode,
                            }
                        );
                    }
                }
                else if (location.LocationType != StorageLocationType)
                {
                    return ApiResponse<LocationReactDto>.Error(
                        "货位类型只能是1=配货位或2=存货位",
                        "INVALID_LOCATION_TYPE"
                    );
                }

                await _context.Db.Ado.BeginTranAsync();
                transactionStarted = true;

                var username = _currentUserService.GetCurrentUsername();
                await _context
                    .Db.Insertable(new ProductLocation
                    {
                        Guid = Guid.NewGuid().ToString(),
                        ProductCode = productCode,
                        LocationGuid = locationGuid,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = username,
                        UpdatedAt = DateTime.UtcNow,
                        UpdatedBy = username,
                    })
                    .ExecuteCommandAsync();

                await _context.Db.Ado.CommitTranAsync();
                return await GetByIdAsync(locationGuid);
            }
            catch (Exception ex)
            {
                if (transactionStarted)
                {
                    await _context.Db.Ado.RollbackTranAsync();
                }
                _logger.LogError(ex, "绑定货位商品失败: {LocationGuid} {ProductIdentifier}", locationGuid, productIdentifier);
                return ApiResponse<LocationReactDto>.Error("绑定商品失败", "DATABASE_ERROR", ex.Message);
            }
        }

        public async Task<ApiResponse<LocationProductResolveDto>> ResolveProductAsync(
            string productIdentifier
        )
        {
            var keyword = productIdentifier?.Trim();
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return ApiResponse<LocationProductResolveDto>.Error(
                    "商品货号或条码不能为空",
                    "VALIDATION_ERROR"
                );
            }

            try
            {
                var matches = new Dictionary<string, (Product Product, string MatchedBy, string MatchedValue)>();

                var products = await _context
                    .Db.Queryable<Product>()
                    .Where(p =>
                        !p.IsDeleted
                        && (
                            p.ProductCode == keyword
                            || p.ItemNumber == keyword
                            || p.Barcode == keyword
                        )
                    )
                    .ToListAsync();

                foreach (var product in products)
                {
                    AddProductResolveMatch(matches, product, keyword);
                }

                var setProductCodes = await _context
                    .Db.Queryable<ProductSetCode>()
                    .Where(psc =>
                        !psc.IsDeleted
                        && (
                            psc.SetItemNumber == keyword
                            || psc.SetBarcode == keyword
                            || psc.SetProductCode == keyword
                        )
                    )
                    .Select(psc => psc.ProductCode)
                    .Distinct()
                    .ToListAsync();

                var multiProductCodes = await _context
                    .Db.Queryable<StoreMultiCodeProduct>()
                    .Where(smp =>
                        !smp.IsDeleted
                        && (
                            smp.MultiBarcode == keyword
                            || smp.MultiCodeProductCode == keyword
                            || smp.StoreMultiCodeProductCode == keyword
                        )
                    )
                    .Select(smp => smp.ProductCode)
                    .Distinct()
                    .ToListAsync();

                var relatedProductCodes = setProductCodes
                    .Concat(multiProductCodes)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct()
                    .ToList();

                if (relatedProductCodes.Count > 0)
                {
                    var relatedProducts = await _context
                        .Db.Queryable<Product>()
                        .Where(p => relatedProductCodes.Contains(p.ProductCode) && !p.IsDeleted)
                        .ToListAsync();

                    foreach (var product in relatedProducts)
                    {
                        if (!string.IsNullOrWhiteSpace(product.ProductCode)
                            && !matches.ContainsKey(product.ProductCode))
                        {
                            matches[product.ProductCode] = (product, "barcode", keyword);
                        }
                    }
                }

                if (matches.Count == 0)
                {
                    return ApiResponse<LocationProductResolveDto>.Error(
                        "商品不存在",
                        "NOT_FOUND",
                        new { productIdentifier = keyword }
                    );
                }

                if (matches.Count > 1)
                {
                    return ApiResponse<LocationProductResolveDto>.Error(
                        "商品货号或条码匹配到多个商品，请使用商品编码绑定",
                        "AMBIGUOUS_PRODUCT",
                        new
                        {
                            productIdentifier = keyword,
                            productCodes = matches.Keys.ToList(),
                        }
                    );
                }

                var match = matches.Values.First();
                var dto = new LocationProductResolveDto
                {
                    ProductCode = match.Product.ProductCode ?? string.Empty,
                    ItemNumber = match.Product.ItemNumber,
                    Barcode = match.Product.Barcode,
                    ProductName = match.Product.ProductName,
                    ProductImage = match.Product.ProductImage,
                    MatchedBy = match.MatchedBy,
                    MatchedValue = match.MatchedValue,
                };

                return ApiResponse<LocationProductResolveDto>.OK(dto, "商品解析成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析货位绑定商品失败: {ProductIdentifier}", productIdentifier);
                return ApiResponse<LocationProductResolveDto>.Error(
                    "解析商品失败",
                    "DATABASE_ERROR",
                    ex.Message
                );
            }
        }

        public async Task<ApiResponse<LocationReactDto>> UnbindProductAsync(
            string locationGuid,
            string productCode
        )
        {
            try
            {
                var location = await _context
                    .Db.Queryable<Location>()
                    .Where(l => l.LocationGuid == locationGuid && !l.IsDeleted)
                    .FirstAsync();
                if (location == null)
                {
                    return ApiResponse<LocationReactDto>.Error("货位不存在", "NOT_FOUND");
                }

                var affected = await _context
                    .Db.Deleteable<ProductLocation>()
                    .Where(pl => pl.LocationGuid == locationGuid && pl.ProductCode == productCode)
                    .ExecuteCommandAsync();

                if (affected == 0)
                {
                    return ApiResponse<LocationReactDto>.Error("货位商品关联不存在", "NOT_FOUND");
                }

                return await GetByIdAsync(locationGuid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解绑货位商品失败: {LocationGuid} {ProductCode}", locationGuid, productCode);
                return ApiResponse<LocationReactDto>.Error("解绑商品失败", "DATABASE_ERROR", ex.Message);
            }
        }

        private async Task LoadProductsAsync(LocationReactDto dto)
        {
            var productLocations = await _context
                .Db.Queryable<ProductLocation>()
                .Where(pl => pl.LocationGuid == dto.LocationGuid && !pl.IsDeleted)
                .ToListAsync();

            if (productLocations.Count == 0)
                return;

            var productCodes = productLocations
                .Select(pl => pl.ProductCode)
                .Where(pc => !string.IsNullOrEmpty(pc))
                .ToList();

            if (productCodes.Count == 0)
                return;

            var products = await _context
                .Db.Queryable<Product>()
                .Where(p => productCodes.Contains(p.ProductCode) && !p.IsDeleted)
                .ToListAsync();

            dto.Products = products
                .Select(p => new LocationReactProductDto
                {
                    ProductCode = p.ProductCode,
                    ItemNumber = p.ItemNumber,
                    Barcode = p.Barcode,
                    ProductName = p.ProductName,
                    ProductImage = p.ProductImage,
                })
                .ToList();
        }

        private async Task<string> GenerateUniqueLocationBarcodeAsync()
        {
            var timestamp = DateTime.Now;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var barcode12 = timestamp.AddSeconds(attempt).ToString("yyMMddHHmmss");
                var barcode = barcode12 + CalculateEan13CheckDigit(barcode12);
                var exists = await _context
                    .Db.Queryable<Location>()
                    .AnyAsync(l => l.LocationBarcode == barcode && !l.IsDeleted);

                if (!exists)
                {
                    return barcode;
                }
            }

            throw new InvalidOperationException("生成货位条码失败，请稍后重试");
        }

        private static int CalculateEan13CheckDigit(string barcode12)
        {
            var sum = 0;
            for (var i = 0; i < barcode12.Length; i++)
            {
                var digit = barcode12[i] - '0';
                sum += i % 2 == 0 ? digit : digit * 3;
            }

            return (10 - sum % 10) % 10;
        }

        private static (string Message, string ErrorCode)? ValidateLocationInput(
            string? locationCode,
            int? locationType,
            int? status
        )
        {
            var normalizedCode = NormalizeLocationCode(locationCode);
            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                return ("货位代码不能为空", "VALIDATION_ERROR");
            }

            if (!Regex.IsMatch(normalizedCode, LocationCodePattern))
            {
                return ("货位代码格式不正确，应为 A-00-00-01 格式：字母A-Z + 00-99 + 00-99 + 00-99", "INVALID_LOCATION_CODE");
            }

            if (locationType != PickingLocationType && locationType != StorageLocationType)
            {
                return ("货位类型只能是1=配货位或2=存货位", "INVALID_LOCATION_TYPE");
            }

            if (status.HasValue && status.Value != 0 && status.Value != 1)
            {
                return ("货位状态只能是0或1", "INVALID_STATUS");
            }

            return null;
        }

        private static string NormalizeLocationCode(string? locationCode)
        {
            return locationCode?.Trim().ToUpperInvariant() ?? string.Empty;
        }

        private static void AddProductResolveMatch(
            Dictionary<string, (Product Product, string MatchedBy, string MatchedValue)> matches,
            Product product,
            string keyword
        )
        {
            if (string.IsNullOrWhiteSpace(product.ProductCode))
            {
                return;
            }

            var matchedBy = product.ProductCode == keyword
                ? "productCode"
                : product.ItemNumber == keyword
                    ? "itemNumber"
                    : "barcode";

            matches[product.ProductCode] = (product, matchedBy, keyword);
        }

        private static LocationReactDto MapToDto(Location location)
        {
            return new LocationReactDto
            {
                LocationGuid = location.LocationGuid,
                LocationCode = location.LocationCode,
                LocationBarcode = location.LocationBarcode,
                Status = location.Status,
                LocationType = location.LocationType,
                UpdatedAt = location.UpdatedAt,
                UpdatedBy = location.UpdatedBy,
            };
        }

        private static Expression<Func<Location, object>> CreateOrderByExpression(string sortBy)
        {
            return sortBy switch
            {
                "LocationCode" => l => l.LocationCode,
                "LocationBarcode" => l => l.LocationBarcode,
                "Status" => l => l.Status,
                "LocationType" => l => l.LocationType,
                "UpdatedAt" => l => l.UpdatedAt,
                _ => l => l.LocationCode,
            };
        }
    }
}
