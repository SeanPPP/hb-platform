using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Services.React
{
    public class ProductCategoryReactService : IProductCategoryReactService
    {
        private readonly SqlSugarContext _db;
        private readonly IMapper _mapper;
        private readonly ILogger<ProductCategoryReactService> _logger;

        public ProductCategoryReactService(
            SqlSugarContext db,
            IMapper mapper,
            ILogger<ProductCategoryReactService> logger
        )
        {
            _db = db;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<List<ProductCategoryDto>> GetTreeAsync()
        {
            var all = await _db.Db.Queryable<ProductCategory>()
                .OrderBy(x => x.SortOrder)
                .OrderBy(x => x.CategoryName)
                .ToListAsync();

            var dtos = _mapper.Map<List<ProductCategoryDto>>(all);
            return BuildTree(dtos);
        }

        public async Task<PagedResultDto<ProductCategoryDto>> GetListAsync(
            ProductCategoryFilterDto filter
        )
        {
            var query = _db.Db.Queryable<ProductCategory>();

            if (!string.IsNullOrWhiteSpace(filter.CategoryName))
                query = query.Where(x => x.CategoryName.Contains(filter.CategoryName));

            if (filter.IsActive.HasValue)
                query = query.Where(x => x.IsActive == filter.IsActive.Value);

            if (!string.IsNullOrWhiteSpace(filter.ParentGUID))
                query = query.Where(x => x.ParentGUID == filter.ParentGUID);

            var totalCount = await query.CountAsync();

            query = filter.SortBy switch
            {
                "SortOrder" => filter.SortDescending
                    ? query.OrderByDescending(x => x.SortOrder)
                    : query.OrderBy(x => x.SortOrder),
                "IsActive" => filter.SortDescending
                    ? query.OrderByDescending(x => x.IsActive)
                    : query.OrderBy(x => x.IsActive),
                _ => filter.SortDescending
                    ? query.OrderByDescending(x => x.CategoryName)
                    : query.OrderBy(x => x.CategoryName),
            };

            var items = await query
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();

            var dtos = _mapper.Map<List<ProductCategoryDto>>(items);

            return new PagedResultDto<ProductCategoryDto>
            {
                Data = dtos,
                TotalCount = totalCount,
                PageIndex = filter.PageNumber,
                PageSize = filter.PageSize,
            };
        }

        public async Task<ProductCategoryDto> CreateAsync(CreateProductCategoryDto dto)
        {
            var entity = _mapper.Map<ProductCategory>(dto);
            entity.CreatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(dto.ParentGUID))
            {
                var parent = await _db.Db.Queryable<ProductCategory>()
                    .FirstAsync(x => x.CategoryGUID == dto.ParentGUID);
                if (parent == null)
                    throw new ArgumentException($"父级分类 {dto.ParentGUID} 不存在");
            }

            await _db.Db.Insertable(entity).ExecuteCommandAsync();
            return _mapper.Map<ProductCategoryDto>(entity);
        }

        public async Task<ProductCategoryDto> UpdateAsync(UpdateProductCategoryDto dto)
        {
            var entity = await _db.Db.Queryable<ProductCategory>()
                .FirstAsync(x => x.CategoryGUID == dto.CategoryGUID);
            if (entity == null)
                throw new ArgumentException($"分类 {dto.CategoryGUID} 不存在");

            if (dto.ParentGUID == dto.CategoryGUID)
                throw new ArgumentException("不能将自己设为父级分类");

            if (!string.IsNullOrWhiteSpace(dto.ParentGUID))
            {
                if (await IsDescendantAsync(dto.CategoryGUID, dto.ParentGUID))
                    throw new ArgumentException("不能将子级分类设为父级，会造成循环引用");

                var parent = await _db.Db.Queryable<ProductCategory>()
                    .FirstAsync(x => x.CategoryGUID == dto.ParentGUID);
                if (parent == null)
                    throw new ArgumentException($"父级分类 {dto.ParentGUID} 不存在");
            }

            _mapper.Map(dto, entity);
            entity.UpdatedAt = DateTime.UtcNow;

            await _db.Db.Updateable(entity).ExecuteCommandAsync();
            return _mapper.Map<ProductCategoryDto>(entity);
        }

        public async Task<bool> DeleteAsync(string categoryGuid)
        {
            var hasChildren = await _db.Db.Queryable<ProductCategory>()
                .AnyAsync(x => x.ParentGUID == categoryGuid);
            if (hasChildren)
                throw new ArgumentException("该分类下存在子分类，无法删除");

            var count = await _db.Db.Deleteable<ProductCategory>()
                .Where(x => x.CategoryGUID == categoryGuid)
                .ExecuteCommandAsync();
            return count > 0;
        }

        public async Task<bool> BatchMoveAsync(BatchMoveCategoriesDto dto)
        {
            if (!string.IsNullOrWhiteSpace(dto.NewParentGuid))
            {
                foreach (var guid in dto.CategoryGuids)
                {
                    if (guid == dto.NewParentGuid)
                        throw new ArgumentException(
                            $"分类 {guid} 不能移动到自身下"
                        );

                    if (await IsDescendantAsync(guid, dto.NewParentGuid))
                        throw new ArgumentException(
                            $"目标父级 {dto.NewParentGuid} 是分类 {guid} 的子级，会造成循环引用"
                        );
                }

                var parentExists = await _db.Db.Queryable<ProductCategory>()
                    .AnyAsync(x => x.CategoryGUID == dto.NewParentGuid);
                if (!parentExists)
                    throw new ArgumentException($"目标父级分类 {dto.NewParentGuid} 不存在");
            }

            try
            {
                await _db.Db.Ado.BeginTranAsync();
                try
                {
                    foreach (var guid in dto.CategoryGuids)
                    {
                        await _db.Db.Updateable<ProductCategory>()
                            .SetColumns(x => x.ParentGUID == dto.NewParentGuid)
                            .SetColumns(x => x.UpdatedAt == DateTime.UtcNow)
                            .Where(x => x.CategoryGUID == guid)
                            .ExecuteCommandAsync();
                    }
                    await _db.Db.Ado.CommitTranAsync();
                    return true;
                }
                catch
                {
                    await _db.Db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ProductCategory] 批量移动失败");
                throw;
            }
        }

        public async Task<int> BatchToggleActiveAsync(BatchToggleActiveDto dto)
        {
            var count = 0;
            try
            {
                await _db.Db.Ado.BeginTranAsync();
                try
                {
                    foreach (var guid in dto.CategoryGuids)
                    {
                        var affected = await _db.Db.Updateable<ProductCategory>()
                            .SetColumns(x => x.IsActive == dto.IsActive)
                            .SetColumns(x => x.UpdatedAt == DateTime.UtcNow)
                            .Where(x => x.CategoryGUID == guid)
                            .ExecuteCommandAsync();
                        count += affected;
                    }
                    await _db.Db.Ado.CommitTranAsync();
                }
                catch
                {
                    await _db.Db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ProductCategory] 批量启用/禁用失败");
                throw;
            }
            return count;
        }

        public async Task<bool> BatchSortAsync(BatchSortRequestDto dto)
        {
            try
            {
                await _db.Db.Ado.BeginTranAsync();
                try
                {
                    foreach (var item in dto.Items)
                    {
                        await _db.Db.Updateable<ProductCategory>()
                            .SetColumns(x => x.SortOrder == item.SortOrder)
                            .SetColumns(x => x.UpdatedAt == DateTime.UtcNow)
                            .Where(x => x.CategoryGUID == item.CategoryGuid)
                            .ExecuteCommandAsync();
                    }
                    await _db.Db.Ado.CommitTranAsync();
                    return true;
                }
                catch
                {
                    await _db.Db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ProductCategory] 批量排序失败");
                throw;
            }
        }

        private static List<ProductCategoryDto> BuildTree(List<ProductCategoryDto> all)
        {
            var lookup = all.ToDictionary(x => x.CategoryGUID);
            var roots = new List<ProductCategoryDto>();

            foreach (var item in all)
            {
                if (
                    string.IsNullOrWhiteSpace(item.ParentGUID)
                    || !lookup.ContainsKey(item.ParentGUID)
                )
                {
                    roots.Add(item);
                }
                else
                {
                    lookup[item.ParentGUID].Children.Add(item);
                }
            }

            return roots;
        }

        private async Task<bool> IsDescendantAsync(
            string ancestorGuid,
            string targetGuid
        )
        {
            var target = await _db.Db.Queryable<ProductCategory>()
                .FirstAsync(x => x.CategoryGUID == targetGuid);
            if (target == null)
                return false;

            var current = target;
            var visited = new HashSet<string>();
            while (
                !string.IsNullOrWhiteSpace(current.ParentGUID)
                && !visited.Contains(current.ParentGUID)
            )
            {
                if (current.ParentGUID == ancestorGuid)
                    return true;
                visited.Add(current.ParentGUID);
                current = await _db.Db.Queryable<ProductCategory>()
                    .FirstAsync(x => x.CategoryGUID == current.ParentGUID);
                if (current == null)
                    break;
            }
            return false;
        }
    }
}
