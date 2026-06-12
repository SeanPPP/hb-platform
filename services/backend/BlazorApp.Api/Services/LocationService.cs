using System.ComponentModel.DataAnnotations;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 位置服务实现
    /// 提供仓库位置的增删改查、状态管理、商品关联统计等核心业务功能
    /// </summary>
    public class LocationService : ILocationService
    {
        // 数据库上下文，用于数据库操作
        private readonly SqlSugarContext _db;

        // AutoMapper对象映射器，用于实体与DTO之间的转换
        private readonly IMapper _mapper;

        // 日志记录器，用于记录操作日志和错误信息
        private readonly ILogger<LocationService> _logger;

        /// <summary>
        /// 构造函数：初始化位置服务
        /// </summary>
        /// <param name="db">数据库上下文</param>
        /// <param name="mapper">对象映射器</param>
        /// <param name="logger">日志记录器</param>
        public LocationService(SqlSugarContext db, IMapper mapper, ILogger<LocationService> logger)
        {
            _db = db;
            _mapper = mapper;
            _logger = logger;
        }

        /// <summary>
        /// 根据GUID获取位置信息
        /// 通过唯一标识符查找指定的仓库位置
        /// </summary>
        /// <param name="locationGuid">位置GUID</param>
        /// <returns>位置信息DTO</returns>
        /// <exception cref="ValidationException">当locationGuid为空时抛出</exception>
        /// <exception cref="KeyNotFoundException">当找不到指定位置时抛出</exception>
        public async Task<LocationDto> GetByGuidAsync(string locationGuid)
        {
            try
            {
                // 验证输入参数
                if (string.IsNullOrWhiteSpace(locationGuid))
                    throw new ValidationException("LocationGuid不能为空");

                // 根据GUID查询位置信息
                var location = await _db.LocationDb.GetSingleAsync(l =>
                    l.LocationGuid == locationGuid
                );
                if (location == null)
                    throw new KeyNotFoundException($"找不到位置: {locationGuid}");

                // 将实体转换为DTO并返回
                return _mapper.Map<LocationDto>(location);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据GUID获取位置失败: {LocationGuid}", locationGuid);
                throw;
            }
        }

        /// <summary>
        /// 根据位置代码获取位置信息
        /// 通过位置代码查找指定的仓库位置，支持业务代码快速查找
        /// </summary>
        /// <param name="locationCode">位置代码</param>
        /// <returns>位置信息DTO</returns>
        /// <exception cref="ValidationException">当locationCode为空时抛出</exception>
        /// <exception cref="KeyNotFoundException">当找不到指定位置时抛出</exception>
        public async Task<LocationDto> GetByCodeAsync(string locationCode)
        {
            try
            {
                // 验证输入参数
                if (string.IsNullOrWhiteSpace(locationCode))
                    throw new ValidationException("位置代码不能为空");

                // 根据位置代码查询位置信息
                var location = await _db.LocationDb.GetSingleAsync(l =>
                    l.LocationCode == locationCode
                );
                if (location == null)
                    throw new KeyNotFoundException($"找不到位置: {locationCode}");

                // 将实体转换为DTO并返回
                return _mapper.Map<LocationDto>(location);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据代码获取位置失败: {LocationCode}", locationCode);
                throw;
            }
        }

        /// <summary>
        /// 分页查询位置列表
        /// 支持位置代码、类型、状态等多维度过滤条件和排序
        /// </summary>
        /// <param name="filter">查询过滤条件</param>
        /// <returns>分页结果，包含位置列表、总数、分页信息</returns>
        public async Task<PagedResult<LocationDto>> GetAllAsync(LocationFilterDto filter)
        {
            try
            {
                // 构建基础查询
                var query = _db.LocationDb.AsQueryable();

                // 1. 应用位置代码过滤条件（模糊匹配）
                if (!string.IsNullOrWhiteSpace(filter.LocationCode))
                    query = query.Where(l =>
                        l.LocationCode != null && l.LocationCode.Contains(filter.LocationCode)
                    );

                // 2. 应用位置类型过滤条件
                if (filter.LocationType != null)
                    query = query.Where(l => l.LocationType == filter.LocationType);

                // 3. 应用状态过滤条件
                if (filter.Status.HasValue)
                    query = query.Where(l => l.Status == filter.Status.Value);

                // 4. 应用排序规则
                query = ApplySorting(query, filter.SortBy, filter.SortDescending);

                // 5. 获取符合条件的总记录数
                var totalCount = await query.CountAsync();

                // 6. 执行分页查询
                var items = await query
                    .Skip((filter.PageNumber - 1) * filter.PageSize) // 跳过前面的记录
                    .Take(filter.PageSize) // 取指定数量的记录
                    .ToListAsync();

                // 7. 将实体列表转换为DTO列表
                var dtos = _mapper.Map<List<LocationDto>>(items);

                // 8. 构建分页返回结果
                var pagedResult = new PagedResult<LocationDto>
                {
                    Items = dtos, // 当前页位置列表
                    Total = totalCount, // 总记录数
                    Page = filter.PageNumber, // 当前页码
                    PageSize = filter.PageSize, // 每页大小
                };

                return pagedResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取位置列表失败");
                throw;
            }
        }

        public async Task<LocationDto> CreateAsync(CreateLocationDto createDto)
        {
            try
            {
                ValidateCreateDto(createDto);

                // 检查位置代码是否已存在
                if (
                    await _db
                        .LocationDb.AsQueryable()
                        .AnyAsync(l => l.LocationCode == createDto.LocationCode)
                )
                    throw new ValidationException($"位置代码 {createDto.LocationCode} 已存在");

                var location = _mapper.Map<Location>(createDto);
                location.LocationGuid = Guid.NewGuid().ToString();
                var result = await _db.LocationDb.InsertAsync(location);
                if (result == false)
                    throw new InvalidOperationException("创建位置失败");

                _logger.LogInformation("创建位置成功: {LocationCode}", location.LocationCode);
                return _mapper.Map<LocationDto>(location);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建位置失败");
                throw;
            }
        }

        public async Task<LocationDto> UpdateAsync(UpdateLocationDto updateDto)
        {
            try
            {
                ValidateUpdateDto(updateDto);

                var existing = await _db.LocationDb.GetSingleAsync(l =>
                    l.LocationGuid == updateDto.LocationGuid
                );
                if (existing == null)
                    throw new KeyNotFoundException($"找不到位置: {updateDto.LocationGuid}");

                // 检查位置代码是否已存在（排除当前位置）
                if (
                    await _db
                        .LocationDb.AsQueryable()
                        .AnyAsync(l =>
                            l.LocationCode == updateDto.LocationCode
                            && l.LocationGuid != updateDto.LocationGuid
                        )
                )
                    throw new ValidationException($"位置代码 {updateDto.LocationCode} 已存在");

                _mapper.Map(updateDto, existing);

                var result = await _db.LocationDb.UpdateAsync(existing);
                if (!result)
                    throw new InvalidOperationException("更新位置失败");

                _logger.LogInformation("更新位置成功: {LocationCode}", existing.LocationCode);
                return _mapper.Map<LocationDto>(existing);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新位置失败");
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string locationGuid)
        {
            try
            {
                // 检查是否有关联的商品
                if (
                    await _db
                        .ProductLocationDb.AsQueryable()
                        .AnyAsync(pl =>
                            pl.LocationGuid
                            == _db.LocationDb.AsQueryable()
                                .Where(l => l.LocationGuid == locationGuid)
                                .Select(l => l.LocationGuid)
                                .First()
                        )
                )
                    throw new ValidationException("该位置下有关联的商品，不能删除");

                var result = await _db.LocationDb.DeleteAsync(l => l.LocationGuid == locationGuid);
                if (result)
                    _logger.LogInformation("删除位置成功: {locationGuid}", locationGuid);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除位置失败: {locationGuid}", locationGuid);
                throw;
            }
        }

        public async Task<List<LocationDto>> GetByTypeAsync(int locationType)
        {
            try
            {
                var locations = await _db
                    .LocationDb.AsQueryable()
                    .Where(l => l.LocationType != null && l.LocationType == locationType)
                    .OrderBy(l => l.LocationCode)
                    .ToListAsync();

                return _mapper.Map<List<LocationDto>>(locations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据类型获取位置失败: {LocationType}", locationType);
                throw;
            }
        }

        public async Task<List<LocationDto>> GetActiveLocationsAsync()
        {
            try
            {
                var locations = await _db
                    .LocationDb.AsQueryable()
                    .Where(l => l.Status == 1)
                    .OrderBy(l => l.LocationCode)
                    .ToListAsync();

                return _mapper.Map<List<LocationDto>>(locations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取活跃位置失败");
                throw;
            }
        }

        public async Task<bool> ExistsAsync(string locationCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(locationCode))
                    throw new ValidationException("位置代码不能为空");

                return await _db
                    .LocationDb.AsQueryable()
                    .AnyAsync(l => l.LocationCode == locationCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查位置代码是否存在失败: {LocationCode}", locationCode);
                throw;
            }
        }

        public async Task<bool> ToggleStatusAsync(string LocationGuid)
        {
            try
            {
                var existing = await _db.LocationDb.GetSingleAsync(l =>
                    l.LocationGuid == LocationGuid
                );
                if (existing == null)
                    throw new KeyNotFoundException($"找不到位置: {LocationGuid}");

                existing.Status = existing.Status == 1 ? 0 : 1;

                var result = await _db.LocationDb.UpdateAsync(existing);
                if (result)
                    _logger.LogInformation(
                        "切换位置状态成功: {LocationGuid} -> {Status}",
                        LocationGuid,
                        existing.Status
                    );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切换位置状态失败: {LocationGuid}", LocationGuid);
                throw;
            }
        }

        public async Task<List<LocationWithProductCountDto>> GetLocationsWithProductCountsAsync()
        {
            try
            {
                // 获取所有位置
                var locations = await _db
                    .LocationDb.AsQueryable()
                    .Where(l => l.Status == 1)
                    .OrderBy(l => l.LocationCode)
                    .ToListAsync();

                var locationDtos = _mapper.Map<List<LocationDto>>(locations);

                // 获取每个位置的商品数量
                var result = new List<LocationWithProductCountDto>();
                foreach (var location in locationDtos)
                {
                    var productCount = await _db
                        .ProductLocationDb.AsQueryable()
                        .Where(pl => pl.LocationGuid == location.LocationGuid)
                        .CountAsync();

                    result.Add(
                        new LocationWithProductCountDto
                        {
                            LocationGUID = location.LocationGuid ?? string.Empty,
                            LocationCode = location.LocationCode ?? string.Empty,
                            LocationName = string.Empty, // Location实体中没有此属性
                            Description = string.Empty, // Location实体中没有此属性
                            IsActive = (location.Status == 1), // 将Status转换为IsActive
                            CreatedAt = DateTime.UtcNow, // Location实体中没有CreatedAt属性
                            UpdatedAt = DateTime.UtcNow, // Location实体中没有UpdatedAt属性
                            ProductCount = productCount,
                        }
                    );
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取位置及商品数量失败");
                throw;
            }
        }

        private void ValidateCreateDto(CreateLocationDto dto)
        {
            if (dto == null)
                throw new ValidationException("创建数据不能为空");

            if (string.IsNullOrWhiteSpace(dto.LocationCode))
                throw new ValidationException("位置代码不能为空");

            if (string.IsNullOrWhiteSpace(dto.LocationType))
                throw new ValidationException("位置类型不能为空");
        }

        private void ValidateUpdateDto(UpdateLocationDto dto)
        {
            if (dto == null)
                throw new ValidationException("更新数据不能为空");

            if (string.IsNullOrWhiteSpace(dto.LocationCode))
                throw new ValidationException("位置代码不能为空");

            if (string.IsNullOrWhiteSpace(dto.LocationType))
                throw new ValidationException("位置类型不能为空");
        }

        private ISugarQueryable<Location> ApplySorting(
            ISugarQueryable<Location> query,
            string? sortBy,
            bool sortDescending
        )
        {
            sortBy = sortBy?.ToLower();

            return sortBy switch
            {
                "locationcode" => sortDescending
                    ? query.OrderByDescending(l => l.LocationCode)
                    : query.OrderBy(l => l.LocationCode),
                "locationtype" => sortDescending
                    ? query.OrderByDescending(l => l.LocationType)
                    : query.OrderBy(l => l.LocationType),
                "status" => sortDescending
                    ? query.OrderByDescending(l => l.Status)
                    : query.OrderBy(l => l.Status),
                _ => sortDescending
                    ? query.OrderByDescending(l => l.LocationCode)
                    : query.OrderBy(l => l.LocationCode),
            };
        }
    }
}
