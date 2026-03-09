using System.Diagnostics;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class WarehouseCategoryReactService : IWarehouseCategoryReactService
    {
        private readonly SqlSugarContext _context;
        private readonly IMapper _mapper;
        private readonly ILogger<WarehouseCategoryReactService> _logger;

        public WarehouseCategoryReactService(
            SqlSugarContext context,
            IMapper mapper,
            ILogger<WarehouseCategoryReactService> logger
        )
        {
            _context = context;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<List<WarehouseCategoryDto>> GetTreeAsync()
        {
            var sw = Stopwatch.StartNew();
            var all = await _context
                .WarehouseCategoryDb.AsQueryable()
                .OrderBy(x => x.SortOrder)
                .OrderBy(x => x.CategoryName)
                .ToListAsync();

            var map = new Dictionary<string, WarehouseCategory>(StringComparer.Ordinal);
            foreach (var c in all)
            {
                if (!string.IsNullOrWhiteSpace(c.CategoryGUID) && !map.ContainsKey(c.CategoryGUID))
                {
                    map[c.CategoryGUID] = c;
                }
            }

            foreach (var c in all)
            {
                if (
                    !string.IsNullOrWhiteSpace(c.ParentGUID)
                    && map.TryGetValue(c.ParentGUID, out var p)
                )
                {
                    p.Children.Add(c);
                    c.Parent = p;
                }
            }

            var roots = all.Where(x => string.IsNullOrWhiteSpace(x.ParentGUID)).ToList();
            sw.Stop();
            _logger.LogInformation(
                "WarehouseCategory tree built: total={Total}, roots={Roots}, elapsedMs={Elapsed}",
                all.Count,
                roots.Count,
                sw.ElapsedMilliseconds
            );

            var dtoRoots = _mapper.Map<List<WarehouseCategoryDto>>(roots);
            if (dtoRoots.Count > 0)
            {
                _logger.LogInformation(
                    "First root: {Name} ({Guid}), children={ChildCount}",
                    dtoRoots[0].CategoryName,
                    dtoRoots[0].CategoryGUID,
                    dtoRoots[0].Children?.Count ?? 0
                );
            }
            return dtoRoots;
        }

        public async Task<PagedResult<WarehouseCategoryDto>> GetListAsync(
            WarehouseCategoryFilterDto filter
        )
        {
            var query = _context.WarehouseCategoryDb.AsQueryable();
            if (!string.IsNullOrWhiteSpace(filter.CategoryName))
                query = query.Where(c => c.CategoryName.Contains(filter.CategoryName));
            if (!string.IsNullOrWhiteSpace(filter.ChineseName))
                query = query.Where(c =>
                    c.ChineseName != null && c.ChineseName.Contains(filter.ChineseName)
                );
            if (filter.IsActive.HasValue)
                query = query.Where(c => c.IsActive == filter.IsActive.Value);
            if (!string.IsNullOrWhiteSpace(filter.ParentGUID))
                query = query.Where(c => c.ParentGUID == filter.ParentGUID);

            query = ApplySorting(query, filter.SortBy, filter.SortDescending);

            var total = await query.CountAsync();
            var items = await query
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();
            return new PagedResult<WarehouseCategoryDto>
            {
                Items = _mapper.Map<List<WarehouseCategoryDto>>(items),
                Total = total,
                Page = filter.PageNumber,
                PageSize = filter.PageSize,
            };
        }

        public async Task<WarehouseCategoryDto> CreateAsync(CreateWarehouseCategoryDto dto)
        {
            var entity = _mapper.Map<WarehouseCategory>(dto);
            entity.CategoryGUID = Guid.NewGuid().ToString();
            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;
            var result = await _context.WarehouseCategoryDb.AsInsertable(entity).ExecuteCommandAsync();
            if (result <= 0)
                throw new InvalidOperationException("Create failed");
            return _mapper.Map<WarehouseCategoryDto>(entity);
        }

        public async Task<WarehouseCategoryDto> UpdateAsync(UpdateWarehouseCategoryDto dto)
        {
            var existing = await _context.WarehouseCategoryDb.GetByIdAsync(dto.CategoryGUID);
            if (existing == null)
                throw new KeyNotFoundException(dto.CategoryGUID);
            if (dto.ParentGUID == dto.CategoryGUID)
                throw new InvalidOperationException("Parent cannot be self");
            _mapper.Map(dto, existing);
            existing.UpdatedAt = DateTime.UtcNow;
            var ok = await _context.WarehouseCategoryDb.UpdateAsync(existing);
            if (!ok)
                throw new InvalidOperationException("Update failed");
            return _mapper.Map<WarehouseCategoryDto>(existing);
        }

        public async Task<bool> DeleteAsync(string categoryGuid)
        {
            if (
                await _context
                    .WarehouseCategoryDb.AsQueryable()
                    .AnyAsync(c => c.ParentGUID == categoryGuid)
            )
                throw new InvalidOperationException("Has children");
            var hasProducts = await _context.Db.Queryable<WarehouseProduct>()
                .InnerJoin<Product>((w, p) => w.ProductCode == p.ProductCode)
                .Where((w, p) => p.WarehouseCategoryGUID == categoryGuid)
                .AnyAsync();
            if (hasProducts)
                throw new InvalidOperationException("Has products");
            return await _context.WarehouseCategoryDb.DeleteByIdAsync(categoryGuid);
        }

        public async Task<bool> BatchMoveAsync(BatchMoveCategoriesDto dto)
        {
            var result = await _context.Db.Ado.UseTranAsync(async () =>
            {
                foreach (var id in dto.CategoryGuids)
                {
                    await _context
                        .WarehouseCategoryDb.AsUpdateable()
                        .SetColumns(c => new WarehouseCategory
                        {
                            ParentGUID = dto.NewParentGuid,
                            UpdatedAt = DateTime.UtcNow,
                        })
                        .Where(c => c.CategoryGUID == id)
                        .ExecuteCommandAsync();
                }
            });
            if (!result.IsSuccess)
            {
                _logger.LogError(result.ErrorException, "BatchMove failed");
            }
            return result.IsSuccess;
        }

        public async Task<int> BatchToggleActiveAsync(BatchToggleActiveDto dto)
        {
            var affected = 0;
            foreach (var id in dto.CategoryGuids)
            {
                affected += await _context
                    .WarehouseCategoryDb.AsUpdateable()
                    .SetColumns(c => new WarehouseCategory
                    {
                        IsActive = dto.IsActive,
                        UpdatedAt = DateTime.UtcNow,
                    })
                    .Where(c => c.CategoryGUID == id)
                    .ExecuteCommandAsync();
            }
            return affected;
        }

        public async Task<bool> BatchSortAsync(BatchSortRequestDto dto)
        {
            var result = await _context.Db.Ado.UseTranAsync(async () =>
            {
                foreach (var item in dto.Items)
                {
                    await _context
                        .WarehouseCategoryDb.AsUpdateable()
                        .SetColumns(c => new WarehouseCategory
                        {
                            SortOrder = item.SortOrder,
                            UpdatedAt = DateTime.UtcNow,
                        })
                        .Where(c => c.CategoryGUID == item.CategoryGuid)
                        .ExecuteCommandAsync();
                }
            });
            if (!result.IsSuccess)
            {
                _logger.LogError(result.ErrorException, "BatchSort failed");
            }
            return result.IsSuccess;
        }

        public async Task<WarehouseProductPagedResultDto> GetProductsByCategoryAsync(
            string categoryGuid,
            WarehouseProductFilterDto filter
        )
        {
            var sw = Stopwatch.StartNew();
            var query = _context.WarehouseProductDb.AsQueryable().Includes(p => p.Product);

            if (!string.IsNullOrWhiteSpace(categoryGuid))
            {
                // 获取当前分类及其所有子分类的GUID
                var categoryIds = GetAllSubCategoryIds(categoryGuid);

                query = query.Where(w =>
                    SqlSugar.SqlFunc.Subqueryable<Product>()
                        .Where(p => p.ProductCode == w.ProductCode && categoryIds.Contains(p.WarehouseCategoryGUID))
                        .Any()
                );
            }

            if (!string.IsNullOrWhiteSpace(filter.ProductName))
            {
                var keyword = filter.ProductName.Trim();
                query = query.Where(w =>
                    SqlSugar.SqlFunc.Subqueryable<Product>()
                        .Where(p => p.ProductCode == w.ProductCode && p.ProductName != null && p.ProductName.Contains(keyword))
                        .Any()
                );
            }
            if (filter.IsActive.HasValue)
            {
                query = query.Where(w => w.IsActive == filter.IsActive.Value);
            }

            var total = await query.CountAsync();
            var items = await query
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();
            var dtos = _mapper.Map<List<WarehouseProductListDto>>(items);
            sw.Stop();
            _logger.LogInformation(
                "Warehouse products fetched: categoryGuid={CategoryGuid}, subCategoriesCount={SubCount}, total={Total}, page={Page}, pageSize={PageSize}, elapsedMs={Elapsed}",
                categoryGuid,
                !string.IsNullOrWhiteSpace(categoryGuid) ? GetAllSubCategoryIds(categoryGuid).Count - 1 : 0,
                total,
                filter.PageNumber,
                filter.PageSize,
                sw.ElapsedMilliseconds
            );
            return new WarehouseProductPagedResultDto
            {
                Items = dtos,
                Total = total,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize,
            };
        }

        private List<string> GetAllSubCategoryIds(string categoryGuid)
        {
            try
            {
                // 一次性查询所有分类以构建树
                var allCategories = _context.WarehouseCategoryDb.AsQueryable().ToList();
                var result = new List<string> { categoryGuid };
                GetSubCategoriesRecursive(categoryGuid, allCategories, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get subcategories for {CategoryGuid}", categoryGuid);
                return new List<string> { categoryGuid };
            }
        }

        private void GetSubCategoriesRecursive(string parentGuid, List<WarehouseCategory> allCategories, List<string> result)
        {
            var children = allCategories.Where(c => c.ParentGUID == parentGuid).ToList();
            foreach (var child in children)
            {
                if (!string.IsNullOrEmpty(child.CategoryGUID) && !result.Contains(child.CategoryGUID))
                {
                    result.Add(child.CategoryGUID);
                    GetSubCategoriesRecursive(child.CategoryGUID, allCategories, result);
                }
            }
        }

        public async Task<int> BatchAssignProductsAsync(BatchAssignProductsRequestDto dto)
        {
            var affected = await _context
                .ProductDb.AsUpdateable()
                .SetColumns(p => new Product
                {
                    WarehouseCategoryGUID = dto.CategoryGuid,
                    UpdatedAt = DateTime.UtcNow,
                })
                .Where(p => p.ProductCode != null && dto.ProductCodes.Contains(p.ProductCode))
                .ExecuteCommandAsync();
            return affected;
        }

        public async Task<int> BatchUnassignProductsAsync(BatchUnassignProductsRequestDto dto)
        {
            var affected = await _context
                .ProductDb.AsUpdateable()
                .SetColumns(p => new Product
                {
                    WarehouseCategoryGUID = null,
                    UpdatedAt = DateTime.UtcNow,
                })
                .Where(p => p.ProductCode != null && dto.ProductCodes.Contains(p.ProductCode))
                .ExecuteCommandAsync();
            return affected;
        }

        private ISugarQueryable<WarehouseCategory> ApplySorting(
            ISugarQueryable<WarehouseCategory> query,
            string? sortBy,
            bool sortDescending
        )
        {
            sortBy = sortBy?.ToLower();
            return sortBy switch
            {
                "categoryname" => sortDescending
                    ? query.OrderByDescending(c => c.CategoryName)
                    : query.OrderBy(c => c.CategoryName),
                "chinesename" => sortDescending
                    ? query.OrderByDescending(c => c.ChineseName)
                    : query.OrderBy(c => c.ChineseName),
                "sortorder" => sortDescending
                    ? query.OrderByDescending(c => c.SortOrder)
                    : query.OrderBy(c => c.SortOrder),
                "createdat" => sortDescending
                    ? query.OrderByDescending(c => c.CreatedAt)
                    : query.OrderBy(c => c.CreatedAt),
                _ => sortDescending
                    ? query.OrderByDescending(c => c.CategoryName)
                    : query.OrderBy(c => c.CategoryName),
            };
        }
    }
}
