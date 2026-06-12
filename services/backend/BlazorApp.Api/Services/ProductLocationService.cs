using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;
using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 商品位置关联服务实现
    /// 提供商品与仓库位置之间的关联关系管理，支持多对多关系的增删改查
    /// 包括批量操作、关联查询、存在性验证等功能
    /// </summary>
    public class ProductLocationService : IProductLocationService
    {
        private const int PickingLocationType = 1;
        private const int StorageLocationType = 2;

        // 数据库上下文，用于数据库操作
        private readonly SqlSugarContext _db;
        // AutoMapper对象映射器，用于实体与DTO之间的转换
        private readonly IMapper _mapper;
        // 日志记录器，用于记录操作日志和错误信息
        private readonly ILogger<ProductLocationService> _logger;

        /// <summary>
        /// 构造函数：初始化商品位置关联服务
        /// </summary>
        /// <param name="db">数据库上下文</param>
        /// <param name="mapper">对象映射器</param>
        /// <param name="logger">日志记录器</param>
        public ProductLocationService(SqlSugarContext db, IMapper mapper, ILogger<ProductLocationService> logger)
        {
            _db = db;
            _mapper = mapper;
            _logger = logger;
        }

        /// <summary>
        /// 根据GUID获取商品位置关联信息
        /// 通过唯一标识符查找指定的商品位置关联记录
        /// </summary>
        /// <param name="guid">关联记录GUID</param>
        /// <returns>商品位置关联DTO</returns>
        /// <exception cref="ValidationException">当guid为空时抛出</exception>
        /// <exception cref="KeyNotFoundException">当找不到指定关联记录时抛出</exception>
        public async Task<ProductLocationDto> GetByIdAsync(string guid)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(guid))
                    throw new ValidationException("GUID不能为空");

                var productLocation = await _db.ProductLocationDb.GetByIdAsync(guid);
                if (productLocation == null)
                    throw new KeyNotFoundException($"找不到商品位置映射: {guid}");

                return _mapper.Map<ProductLocationDto>(productLocation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品位置映射失败: {Guid}", guid);
                throw;
            }
        }

        public async Task<PagedResult<ProductLocationDto>> GetAllAsync(ProductLocationFilterDto filter)
        {
            try
            {
                var query = _db.ProductLocationDb.AsQueryable();

                // 应用过滤条件
                if (!string.IsNullOrWhiteSpace(filter.ProductCode))
                    query = query.Where(pl => pl.ProductCode != null && pl.ProductCode.Contains(filter.ProductCode));

                if (!string.IsNullOrWhiteSpace(filter.LocationGuid))
                    query = query.Where(pl => pl.LocationGuid == filter.LocationGuid);

                // 排序
                query = query.OrderBy(pl => pl.ProductCode);

                // 获取总数
                var totalCount = await query.CountAsync();

                // 分页
                var items = await query
                    .Skip((filter.PageNumber - 1) * filter.PageSize)
                    .Take(filter.PageSize)
                    .ToListAsync();

                var dtos = _mapper.Map<List<ProductLocationDto>>(items);

                return new PagedResult<ProductLocationDto>
                {
                    Items = dtos,
                    Total = totalCount,
                    Page = filter.PageNumber,
                    PageSize = filter.PageSize

                }
                ;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品位置映射列表失败");
                throw;
            }
        }

        public async Task<ProductLocationDto> CreateAsync(CreateProductLocationDto createDto)
        {
            try
            {
                ValidateCreateDto(createDto);

                // 检查是否已存在相同的商品位置映射
                if (await ExistsAsync(createDto.ProductCode, createDto.LocationGuid))
                    throw new ValidationException($"商品 {createDto.ProductCode} 与位置 {createDto.LocationGuid} 的映射已存在");

                await ValidateProductLocationRuleAsync(createDto.ProductCode, createDto.LocationGuid);

                var productLocation = _mapper.Map<ProductLocation>(createDto);
                productLocation.Guid = Guid.NewGuid().ToString();

                var result = await _db.ProductLocationDb.InsertAsync(productLocation);
                if (result == false)
                    throw new InvalidOperationException("创建商品位置映射失败");

                _logger.LogInformation("创建商品位置映射成功: {ProductCode}-{LocationGuid}",
                    productLocation.ProductCode, productLocation.LocationGuid);
                return _mapper.Map<ProductLocationDto>(productLocation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建商品位置映射失败");
                throw;
            }
        }

        public async Task<ProductLocationDto> UpdateAsync(UpdateProductLocationDto updateDto)
        {
            try
            {
                ValidateUpdateDto(updateDto);

                var existing = await _db.ProductLocationDb.GetByIdAsync(updateDto.Guid);
                if (existing == null)
                    throw new KeyNotFoundException($"找不到商品位置映射: {updateDto.Guid}");

                // 检查是否已存在相同的商品位置映射（排除当前映射）
                if (await _db.ProductLocationDb.AsQueryable()
                    .AnyAsync(pl => pl.ProductCode == updateDto.ProductCode
                                   && pl.LocationGuid == updateDto.LocationGuid
                                   && pl.Guid != updateDto.Guid))
                    throw new ValidationException($"商品 {updateDto.ProductCode} 与位置 {updateDto.LocationGuid} 的映射已存在");

                await ValidateProductLocationRuleAsync(updateDto.ProductCode, updateDto.LocationGuid, updateDto.Guid);

                _mapper.Map(updateDto, existing);

                var result = await _db.ProductLocationDb.UpdateAsync(existing);
                if (!result)
                    throw new InvalidOperationException("更新商品位置映射失败");

                _logger.LogInformation("更新商品位置映射成功: {Guid}", existing.Guid);
                return _mapper.Map<ProductLocationDto>(existing);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新商品位置映射失败");
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string guid)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(guid))
                    throw new ValidationException("GUID不能为空");

                var result = await _db.ProductLocationDb.DeleteByIdAsync(guid);
                if (result)
                    _logger.LogInformation("删除商品位置映射成功: {Guid}", guid);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除商品位置映射失败: {Guid}", guid);
                throw;
            }
        }

        public async Task<bool> DeleteByProductAndLocationAsync(string productCode, string locationGuid)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productCode))
                    throw new ValidationException("商品代码不能为空");

                if (string.IsNullOrWhiteSpace(locationGuid))
                    throw new ValidationException("位置GUID不能为空");

                var result = await _db.ProductLocationDb.DeleteAsync(pl =>
                    pl.ProductCode == productCode && pl.LocationGuid == locationGuid);

                if (result)
                    _logger.LogInformation("删除商品位置映射成功: {ProductCode}-{LocationGuid}", productCode, locationGuid);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除商品位置映射失败: {ProductCode}-{LocationGuid}", productCode, locationGuid);
                throw;
            }
        }

        /// <summary>
        /// 获取指定商品关联的所有仓库位置
        /// 通过商品代码查找该商品在哪些仓库位置有存放
        /// </summary>
        /// <param name="productCode">商品代码</param>
        /// <returns>位置信息列表</returns>
        /// <exception cref="ValidationException">当productCode为空时抛出</exception>
        public async Task<List<LocationDto>> GetLocationsByProductAsync(string productCode)
        {
            try
            {
                // 验证输入参数
                if (string.IsNullOrWhiteSpace(productCode))
                    throw new ValidationException("商品代码不能为空");

                // 1. 获取与指定商品关联的所有位置GUID
                var locationGuids = await _db.ProductLocationDb.AsQueryable()
                    .Where(pl => pl.ProductCode == productCode)
                    .Select(pl => pl.LocationGuid)
                    .ToListAsync();

                // 2. 如果没有关联位置，返回空列表
                if (!locationGuids.Any())
                    return new List<LocationDto>();

                // 3. 根据位置GUID列表获取位置详细信息
                var locations = await _db.LocationDb.AsQueryable()
                    .In(l => l.LocationGuid, locationGuids)
                    .ToListAsync();

                // 4. 转换为DTO并返回
                return _mapper.Map<List<LocationDto>>(locations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品的位置失败: {ProductCode}", productCode);
                throw;
            }
        }

        /// <summary>
        /// 获取指定仓库位置的所有商品
        /// 通过位置GUID查找该位置存放的所有商品信息
        /// </summary>
        /// <param name="locationGuid">位置GUID</param>
        /// <returns>仓库商品信息列表</returns>
        /// <exception cref="ValidationException">当locationGuid为空时抛出</exception>
        public async Task<List<WarehouseProductDto>> GetProductsByLocationAsync(string locationGuid)
        {
            try
            {
                // 验证输入参数
                if (string.IsNullOrWhiteSpace(locationGuid))
                    throw new ValidationException("位置GUID不能为空");

                // 1. 获取与指定位置关联的所有商品代码
                var productCodes = await _db.ProductLocationDb.AsQueryable()
                    .Where(pl => pl.LocationGuid == locationGuid)
                    .Select(pl => pl.ProductCode)
                    .ToListAsync();

                // 2. 如果没有关联商品，返回空列表
                if (!productCodes.Any())
                    return new List<WarehouseProductDto>();

                // 3. 根据商品代码列表获取商品详细信息
                var products = await _db.WarehouseProductDb.AsQueryable()
                    .In(wp => wp.ProductCode, productCodes)
                    .ToListAsync();

                // 4. 转换为DTO并返回
                return _mapper.Map<List<WarehouseProductDto>>(products);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取位置的商品失败: {LocationGuid}", locationGuid);
                throw;
            }
        }

        public async Task<ProductWithLocationsDto> GetProductWithLocationsAsync(string productCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productCode))
                    throw new ValidationException("商品代码不能为空");

                // 获取商品信息（包括 Product 表的名称）
                var warehouseProduct = await _db.WarehouseProductDb.GetSingleAsync(wp => wp.ProductCode == productCode);
                if (warehouseProduct == null)
                    throw new KeyNotFoundException($"找不到商品: {productCode}");

                // 获取 Product 表中的名称
                var product = await _db.ProductDb.GetSingleAsync(p => p.ProductCode == productCode);
                var productName = product?.ProductName ?? $"商品-{productCode}";

                // 获取该商品关联的所有位置
                var locations = await GetLocationsByProductAsync(productCode);

                return new ProductWithLocationsDto
                {
                    ProductCode = warehouseProduct.ProductCode,
                    ProductName = productName,
                    Locations = locations
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品及其位置失败: {ProductCode}", productCode);
                throw;
            }
        }

        public async Task<LocationWithProductsDto> GetLocationWithProductsAsync(string locationGuid)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(locationGuid))
                    throw new ValidationException("位置GUID不能为空");

                // 获取位置信息
                var location = await _db.LocationDb.GetSingleAsync(l => l.LocationGuid == locationGuid);
                if (location == null)
                    throw new KeyNotFoundException($"找不到位置: {locationGuid}");

                // 获取该位置关联的所有商品
                var products = await GetProductsByLocationAsync(locationGuid);

                return new LocationWithProductsDto
                {
                    LocationGuid = location.LocationGuid ?? string.Empty,
                    LocationCode = location.LocationCode ?? string.Empty,
                    Products = products
                }
;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取位置及其商品失败: {LocationGuid}", locationGuid);
                throw;
            }
        }

        /// <summary>
        /// 批量创建商品位置关联
        /// 为一个商品批量关联多个仓库位置，自动跳过已存在的关联
        /// </summary>
        /// <param name="batchDto">批量创建DTO，包含商品代码和位置GUID列表</param>
        /// <returns>true表示操作成功，false表示失败</returns>
        /// <exception cref="ValidationException">当输入数据验证失败时抛出</exception>
        public async Task<bool> BatchCreateAsync(BatchProductLocationDto batchDto)
        {
            try
            {
                // 验证输入参数
                if (batchDto == null)
                    throw new ValidationException("批量创建数据不能为空");

                if (string.IsNullOrWhiteSpace(batchDto.ProductCode))
                    throw new ValidationException("商品代码不能为空");

                if (batchDto.LocationGuids == null || !batchDto.LocationGuids.Any())
                    throw new ValidationException("位置GUID列表不能为空");

                // 1. 构建需要创建的关联记录列表
                var productLocations = new List<ProductLocation>();
                foreach (var locationGuid in batchDto.LocationGuids)
                {
                    // 检查是否已存在相同的商品位置映射，避免重复创建
                    if (!await ExistsAsync(batchDto.ProductCode, locationGuid))
                    {
                        await ValidateProductLocationRuleAsync(batchDto.ProductCode, locationGuid);

                        productLocations.Add(new ProductLocation
                        {
                            Guid = Guid.NewGuid().ToString(),
                            ProductCode = batchDto.ProductCode,
                            LocationGuid = locationGuid
                        });
                    }
                }

                // 2. 批量插入新的关联记录
                if (productLocations.Any())
                {
                    var result = await _db.ProductLocationDb.InsertRangeAsync(productLocations);
                    if (result)
                        _logger.LogInformation("批量创建商品位置映射成功，共创建 {Count} 条记录", productLocations.Count);

                    return result;
                }

                // 3. 如果没有新记录需要创建，返回成功
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建商品位置映射失败");
                throw;
            }
        }

        public async Task<bool> BatchDeleteByProductAsync(string productCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productCode))
                    throw new ValidationException("商品代码不能为空");

                var result = await _db.ProductLocationDb.DeleteAsync(pl => pl.ProductCode == productCode);
                if (result)
                    _logger.LogInformation("批量删除商品 {ProductCode} 的位置映射成功", productCode);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除商品的位置映射失败: {ProductCode}", productCode);
                throw;
            }
        }

        public async Task<bool> BatchDeleteByLocationAsync(string locationGuid)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(locationGuid))
                    throw new ValidationException("位置GUID不能为空");

                var result = await _db.ProductLocationDb.DeleteAsync(pl => pl.LocationGuid == locationGuid);
                if (result)
                    _logger.LogInformation("批量删除位置 {LocationGuid} 的商品映射成功", locationGuid);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除位置的商品映射失败: {LocationGuid}", locationGuid);
                throw;
            }
        }

        public async Task<bool> ExistsAsync(string productCode, string locationGuid)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productCode))
                    throw new ValidationException("商品代码不能为空");

                if (string.IsNullOrWhiteSpace(locationGuid))
                    throw new ValidationException("位置GUID不能为空");

                return await _db.ProductLocationDb.AsQueryable()
                    .AnyAsync(pl => pl.ProductCode == productCode && pl.LocationGuid == locationGuid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查商品位置映射是否存在失败: {ProductCode}-{LocationGuid}", productCode, locationGuid);
                throw;
            }
        }

        private void ValidateCreateDto(CreateProductLocationDto dto)
        {
            if (dto == null)
                throw new ValidationException("创建数据不能为空");

            if (string.IsNullOrWhiteSpace(dto.ProductCode))
                throw new ValidationException("商品代码不能为空");

            if (string.IsNullOrWhiteSpace(dto.LocationGuid))
                throw new ValidationException("位置GUID不能为空");
        }

        private void ValidateUpdateDto(UpdateProductLocationDto dto)
        {
            if (dto == null)
                throw new ValidationException("更新数据不能为空");

            if (string.IsNullOrWhiteSpace(dto.Guid))
                throw new ValidationException("GUID不能为空");

            if (string.IsNullOrWhiteSpace(dto.ProductCode))
                throw new ValidationException("商品代码不能为空");

            if (string.IsNullOrWhiteSpace(dto.LocationGuid))
                throw new ValidationException("位置GUID不能为空");
        }

        private async Task ValidateProductLocationRuleAsync(
            string productCode,
            string locationGuid,
            string? excludeGuid = null
        )
        {
            var location = await _db.LocationDb.AsQueryable()
                .Where(l => l.LocationGuid == locationGuid && !l.IsDeleted)
                .FirstAsync();

            if (location == null)
                throw new KeyNotFoundException($"找不到货位: {locationGuid}");

            if (location.LocationType == StorageLocationType)
                return;

            if (location.LocationType != PickingLocationType)
                throw new ValidationException("货位类型只能是1=配货位或2=存货位");

            var locationQuery = _db.ProductLocationDb.AsQueryable()
                .Where(pl => pl.LocationGuid == locationGuid && !pl.IsDeleted);
            if (!string.IsNullOrWhiteSpace(excludeGuid))
                locationQuery = locationQuery.Where(pl => pl.Guid != excludeGuid);

            var existingLocationProduct = await locationQuery.FirstAsync();
            if (existingLocationProduct != null)
                throw new ValidationException("该配货位已绑定商品，请解绑后继续绑定新的货位");

            var productPickingQuery = _db.ProductLocationDb.AsQueryable()
                .InnerJoin<Location>((pl, l) => pl.LocationGuid == l.LocationGuid)
                .Where((pl, l) =>
                    pl.ProductCode == productCode
                    && !pl.IsDeleted
                    && !l.IsDeleted
                    && l.LocationType == PickingLocationType
                );
            if (!string.IsNullOrWhiteSpace(excludeGuid))
                productPickingQuery = productPickingQuery.Where((pl, l) => pl.Guid != excludeGuid);

            var existingProductPicking = await productPickingQuery.FirstAsync();
            if (existingProductPicking != null)
                throw new ValidationException("该商品已绑定配货位，请解绑后继续绑定新的货位");
        }
    }
}
