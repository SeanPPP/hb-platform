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
    /// 仓库类别服务实现类，提供仓库类别的增删改查等操作
    /// </summary>
    public class WarehouseCategoryService : IWarehouseCategoryService
    {
        private readonly SqlSugarContext _db;
        private readonly IMapper _mapper;
        private readonly ILogger<WarehouseCategoryService> _logger;

        /// <summary>
        /// 构造函数，注入依赖项
        /// </summary>
        /// <param name="db">数据库上下文</param>
        /// <param name="mapper">对象映射器</param>
        /// <param name="logger">日志记录器</param>
        public WarehouseCategoryService(SqlSugarContext db, IMapper mapper, ILogger<WarehouseCategoryService> logger)
        {
            _db = db;
            _mapper = mapper;
            _logger = logger;
        }

        /// <summary>
        /// 根据GUID获取仓库类别
        /// </summary>
        /// <param name="categoryGuid">类别GUID</param>
        /// <returns>仓库类别DTO</returns>
        /// <exception cref="ValidationException">当categoryGuid为空时抛出</exception>
        /// <exception cref="KeyNotFoundException">当找不到指定类别时抛出</exception>
        public async Task<WarehouseCategoryDto> GetByIdAsync(string categoryGuid)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(categoryGuid))
                    throw new ValidationException("CategoryGUID不能为空");

                var category = await _db.WarehouseCategoryDb.GetByIdAsync(categoryGuid);
                if (category == null)
                    throw new KeyNotFoundException($"找不到类别: {categoryGuid}");

                return _mapper.Map<WarehouseCategoryDto>(category);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取仓库类别失败: {CategoryGuid}", categoryGuid);
                throw;
            }
        }

        /// <summary>
        /// 获取所有仓库类别
        /// </summary>
        /// <returns>所有仓库类别列表</returns>
        public async Task<List<WarehouseCategoryDto>> GetAllAsync()
        {
            try
            {
                var categories = await _db.WarehouseCategoryDb.AsQueryable()
                    .OrderBy(c => c.SortOrder)
                    .OrderBy(c => c.CategoryName)
                    .ToListAsync();

                return _mapper.Map<List<WarehouseCategoryDto>>(categories);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取所有仓库类别失败");
                throw;
            }
        }

        /// <summary>
        /// 获取所有仓库类别（支持分页和过滤）
        /// </summary>
        /// <param name="filter">过滤条件DTO</param>
        /// <returns>分页结果</returns>
        public async Task<PagedResult<WarehouseCategoryDto>> GetAllAsync(WarehouseCategoryFilterDto filter)
        {
            try
            {
                var query = _db.WarehouseCategoryDb.AsQueryable();

                // 1. 应用分类名称过滤条件（模糊匹配）
                if (!string.IsNullOrWhiteSpace(filter.CategoryName))
                    query = query.Where(c => c.CategoryName.Contains(filter.CategoryName));

                // 2. 应用中文名称过滤条件（模糊匹配）
                if (!string.IsNullOrWhiteSpace(filter.ChineseName))
                    query = query.Where(c => c.ChineseName != null && c.ChineseName.Contains(filter.ChineseName));

                // 3. 应用启用状态过滤条件
                if (filter.IsActive.HasValue)
                    query = query.Where(c => c.IsActive == filter.IsActive.Value);

                // 4. 应用父分类过滤条件（查找指定父分类下的子分类）
                if (!string.IsNullOrWhiteSpace(filter.ParentGUID))
                    query = query.Where(c => c.ParentGUID == filter.ParentGUID);

                // 5. 应用排序规则
                query = ApplySorting(query, filter.SortBy, filter.SortDescending);

                // 6. 获取符合条件的总记录数
                var totalCount = await query.CountAsync();

                // 7. 执行分页查询
                var items = await query
                    .Skip((filter.PageNumber - 1) * filter.PageSize)   // 跳过前面的记录
                    .Take(filter.PageSize)                             // 取指定数量的记录
                    .ToListAsync();

                // 8. 将实体列表转换为DTO列表
                var dtos = _mapper.Map<List<WarehouseCategoryDto>>(items);

                // 9. 构建分页返回结果
                var pagedResult = new PagedResult<WarehouseCategoryDto>
                {
                    Items = dtos,                   // 当前页分类列表
                    Total = totalCount,             // 总记录数
                    Page = filter.PageNumber,       // 当前页码
                    PageSize = filter.PageSize      // 每页大小
                };

                return pagedResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取仓库类别列表失败");
                throw;
            }
        }

        /// <summary>
        /// 创建新的仓库类别
        /// </summary>
        /// <param name="createDto">创建仓库类别DTO</param>
        /// <returns>创建的仓库类别DTO</returns>
        /// <exception cref="ValidationException">当输入数据验证失败时抛出</exception>
        /// <exception cref="InvalidOperationException">当创建失败时抛出</exception>
        public async Task<WarehouseCategoryDto> CreateAsync(CreateWarehouseCategoryDto createDto)
        {
            try
            {
                ValidateCreateDto(createDto);

                // 1. 映射DTO到实体并设置基本属性
                var category = _mapper.Map<WarehouseCategory>(createDto);
                category.CategoryGUID = Guid.NewGuid().ToString();     // 生成唯一标识符
                category.CreatedAt = DateTime.UtcNow;                  // 设置创建时间
                category.UpdatedAt = DateTime.UtcNow;                  // 设置更新时间

                // 2. 执行数据库插入操作
                var result = await _db.Db.Insertable(category).ExecuteCommandAsync();
                if (result <= 0)
                    throw new InvalidOperationException("创建仓库类别失败");

                _logger.LogInformation("创建仓库类别成功: {CategoryName}", category.CategoryName);
                return _mapper.Map<WarehouseCategoryDto>(category);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建仓库类别失败");
                throw;
            }
        }

        /// <summary>
        /// 更新仓库类别
        /// </summary>
        /// <param name="updateDto">更新仓库类别DTO</param>
        /// <returns>更新后的仓库类别DTO</returns>
        /// <exception cref="ValidationException">当输入数据验证失败或存在循环引用时抛出</exception>
        /// <exception cref="KeyNotFoundException">当找不到指定类别时抛出</exception>
        /// <exception cref="InvalidOperationException">当更新失败时抛出</exception>
        public async Task<WarehouseCategoryDto> UpdateAsync(UpdateWarehouseCategoryDto updateDto)
        {
            try
            {
                ValidateUpdateDto(updateDto);

                var existing = await _db.WarehouseCategoryDb.GetByIdAsync(updateDto.CategoryGUID);
                if (existing == null)
                    throw new KeyNotFoundException($"找不到类别: {updateDto.CategoryGUID}");

                // 1. 检查循环引用，防止分类结构出现环形依赖
                if (updateDto.ParentGUID == updateDto.CategoryGUID)
                    throw new ValidationException("类别不能作为自己的父类别");

                // 2. 映射更新数据到现有实体
                _mapper.Map(updateDto, existing);
                existing.UpdatedAt = DateTime.UtcNow;                  // 更新修改时间

                // 3. 执行数据库更新操作
                var result = await _db.WarehouseCategoryDb.UpdateAsync(existing);
                if (!result)
                    throw new InvalidOperationException("更新仓库类别失败");

                _logger.LogInformation("更新仓库类别成功: {CategoryName}", existing.CategoryName);
                return _mapper.Map<WarehouseCategoryDto>(existing);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新仓库类别失败");
                throw;
            }
        }

        /// <summary>
        /// 删除仓库类别
        /// </summary>
        /// <param name="categoryGuid">要删除的类别GUID</param>
        /// <returns>删除成功返回true，否则返回false</returns>
        /// <exception cref="ValidationException">当categoryGuid为空或该类别下有子类别/产品时抛出</exception>
        public async Task<bool> DeleteAsync(string categoryGuid)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(categoryGuid))
                    throw new ValidationException("CategoryGUID不能为空");

                // 检查是否有子类别
                if (await HasChildCategoriesAsync(categoryGuid))
                    throw new ValidationException("该类别下有子类别，不能删除");

                // 检查是否有产品
                if (await HasProductsAsync(categoryGuid))
                    throw new ValidationException("该类别下有产品，不能删除");

                var result = await _db.WarehouseCategoryDb.DeleteByIdAsync(categoryGuid);
                if (result)
                    _logger.LogInformation("删除仓库类别成功: {CategoryGuid}", categoryGuid);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除仓库类别失败: {CategoryGuid}", categoryGuid);
                throw;
            }
        }

        /// <summary>
        /// 获取指定父类别的所有子类别
        /// </summary>
        /// <param name="parentGuid">父类别GUID</param>
        /// <returns>子类别列表</returns>
        public async Task<List<WarehouseCategoryDto>> GetChildrenAsync(string parentGuid)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(parentGuid))
                    return new List<WarehouseCategoryDto>();

                var children = await _db.WarehouseCategoryDb.AsQueryable()
                    .Where(c => c.ParentGUID == parentGuid)
                    .OrderBy(c => c.SortOrder)
                    .OrderBy(c => c.CategoryName)
                    .ToListAsync();

                return _mapper.Map<List<WarehouseCategoryDto>>(children);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取子类别失败: {ParentGuid}", parentGuid);
                throw;
            }
        }

        /// <summary>
        /// 获取所有活跃的仓库类别
        /// </summary>
        /// <returns>活跃的仓库类别列表</returns>
        public async Task<List<WarehouseCategoryDto>> GetActiveCategoriesAsync()
        {
            try
            {
                var categories = await _db.WarehouseCategoryDb.AsQueryable()
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.SortOrder)
                    .OrderBy(c => c.CategoryName)
                    .ToListAsync();

                return _mapper.Map<List<WarehouseCategoryDto>>(categories);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取活跃类别失败");
                throw;
            }
        }

        /// <summary>
        /// 检查指定类别是否有子类别
        /// </summary>
        /// <param name="categoryGuid">类别GUID</param>
        /// <returns>有子类别返回true，否则返回false</returns>
        public async Task<bool> HasChildCategoriesAsync(string categoryGuid)
        {
            return await _db.WarehouseCategoryDb.AsQueryable()
                .AnyAsync(c => c.ParentGUID == categoryGuid);
        }

        /// <summary>
        /// 检查指定类别下是否有产品
        /// </summary>
        /// <param name="categoryGuid">类别GUID</param>
        /// <returns>有产品返回true，否则返回false</returns>
        public async Task<bool> HasProductsAsync(string categoryGuid)
        {
            var hasProducts = await _db.Db.Queryable<WarehouseProduct>()
                .InnerJoin<Product>((w, p) => w.ProductCode == p.ProductCode)
                .Where((w, p) => p.WarehouseCategoryGUID == categoryGuid)
                .AnyAsync();
            return hasProducts;
        }

        /// <summary>
        /// 验证创建仓库类别DTO
        /// </summary>
        /// <param name="dto">创建仓库类别DTO</param>
        /// <exception cref="ValidationException">当验证失败时抛出</exception>
        private void ValidateCreateDto(CreateWarehouseCategoryDto dto)
        {
            if (dto == null)
                throw new ValidationException("创建数据不能为空");

            if (string.IsNullOrWhiteSpace(dto.CategoryName))
                throw new ValidationException("类别名称不能为空");
        }

        /// <summary>
        /// 验证更新仓库类别DTO
        /// </summary>
        /// <param name="dto">更新仓库类别DTO</param>
        /// <exception cref="ValidationException">当验证失败时抛出</exception>
        private void ValidateUpdateDto(UpdateWarehouseCategoryDto dto)
        {
            if (dto == null)
                throw new ValidationException("更新数据不能为空");

            if (string.IsNullOrWhiteSpace(dto.CategoryGUID))
                throw new ValidationException("CategoryGUID不能为空");

            if (string.IsNullOrWhiteSpace(dto.CategoryName))
                throw new ValidationException("类别名称不能为空");
        }

        /// <summary>
        /// 应用排序规则到查询
        /// </summary>
        /// <param name="query">查询对象</param>
        /// <param name="sortBy">排序字段</param>
        /// <param name="sortDescending">是否降序</param>
        /// <returns>排序后的查询对象</returns>
        private ISugarQueryable<WarehouseCategory> ApplySorting(ISugarQueryable<WarehouseCategory> query, string? sortBy, bool sortDescending)
        {
            sortBy = sortBy?.ToLower();

            return sortBy switch
            {
                "categoryname" => sortDescending ? query.OrderByDescending(c => c.CategoryName) : query.OrderBy(c => c.CategoryName),
                "chinesename" => sortDescending ? query.OrderByDescending(c => c.ChineseName) : query.OrderBy(c => c.ChineseName),
                "sortorder" => sortDescending ? query.OrderByDescending(c => c.SortOrder) : query.OrderBy(c => c.SortOrder),
                "createdat" => sortDescending ? query.OrderByDescending(c => c.CreatedAt) : query.OrderBy(c => c.CreatedAt),
                _ => sortDescending ? query.OrderByDescending(c => c.CategoryName) : query.OrderBy(c => c.CategoryName)
            };
        }
    }
}
