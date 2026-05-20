using System.Linq.Expressions;
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
                var existingList = await _context
                    .Db.Queryable<Location>()
                    .Where(l => l.LocationCode == dto.LocationCode && !l.IsDeleted)
                    .ToListAsync();

                if (existingList.Count > 0)
                {
                    return ApiResponse<LocationReactDto>.Error("货位代码已存在", "CONFLICT");
                }

                var username = _currentUserService.GetCurrentUsername();
                var location = new Location
                {
                    LocationGuid = Guid.NewGuid().ToString(),
                    LocationCode = dto.LocationCode,
                    LocationBarcode = dto.LocationBarcode,
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

                var duplicateList = await _context
                    .Db.Queryable<Location>()
                    .Where(l =>
                        l.LocationCode == dto.LocationCode
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
                location.LocationCode = dto.LocationCode;
                location.LocationBarcode = dto.LocationBarcode;
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

                var product = await _context
                    .Db.Queryable<Product>()
                    .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                    .FirstAsync();
                if (product == null)
                {
                    return ApiResponse<LocationReactDto>.Error("商品不存在", "NOT_FOUND");
                }

                var warehouseProduct = await _context
                    .Db.Queryable<WarehouseProduct>()
                    .Where(w => w.ProductCode == productCode && !w.IsDeleted)
                    .FirstAsync();
                if (warehouseProduct == null)
                {
                    return ApiResponse<LocationReactDto>.Error("仓库商品不存在", "NOT_FOUND");
                }

                await _context.Db.Ado.BeginTranAsync();
                await _context
                    .Db.Deleteable<ProductLocation>()
                    .Where(pl => pl.ProductCode == productCode)
                    .ExecuteCommandAsync();

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
                await _context.Db.Ado.RollbackTranAsync();
                _logger.LogError(ex, "绑定货位商品失败: {LocationGuid} {ProductCode}", locationGuid, productCode);
                return ApiResponse<LocationReactDto>.Error("绑定商品失败", "DATABASE_ERROR", ex.Message);
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
                    ProductName = p.ProductName,
                    ProductImage = p.ProductImage,
                })
                .ToList();
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
