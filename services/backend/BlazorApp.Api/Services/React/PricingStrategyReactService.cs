using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models.HBweb;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class PricingStrategyReactService : IPricingStrategyReactService
    {
        private readonly SqlSugarContext _context;

        public PricingStrategyReactService(SqlSugarContext context)
        {
            _context = context;
        }

        public async Task<GridResponseDto<PricingStrategyListDto>> GetGridAsync(
            GridRequestDto request
        )
        {
            var query = _context
                .PricingStrategyDb.AsQueryable()
                .Includes(s => s.Details)
                .Includes(s => s.Targets);

            if (!string.IsNullOrWhiteSpace(request.GlobalSearch))
            {
                var keyword = request.GlobalSearch.Trim();
                query = query.Where(s => s.Name.Contains(keyword));
            }

            if (request.FilterModel != null)
            {
                if (request.FilterModel.TryGetValue("storeCode", out var storeFilter))
                {
                    var val = storeFilter.Filter;
                    if (!string.IsNullOrEmpty(val))
                    {
                        query = query.Where(s =>
                            SqlFunc
                                .Subqueryable<PricingStrategyTarget>()
                                .Where(t =>
                                    t.StrategyId == s.Id
                                    && t.TargetType == "Store"
                                    && t.TargetCode == val
                                )
                                .Any()
                        );
                    }
                }
                if (request.FilterModel.TryGetValue("supplierCode", out var supplierFilter))
                {
                    var val = supplierFilter.Filter;
                    if (!string.IsNullOrEmpty(val))
                    {
                        query = query.Where(s =>
                            SqlFunc
                                .Subqueryable<PricingStrategyTarget>()
                                .Where(t =>
                                    t.StrategyId == s.Id
                                    && t.TargetType == "Supplier"
                                    && t.TargetCode == val
                                )
                                .Any()
                        );
                    }
                }
            }

            if (request.SortModel != null && request.SortModel.Count > 0)
            {
                foreach (var sm in request.SortModel)
                {
                    if (sm.ColId.Equals("name", System.StringComparison.OrdinalIgnoreCase))
                    {
                        query =
                            sm.Sort == "asc"
                                ? query.OrderBy(s => s.Name)
                                : query.OrderBy(s => s.Name, OrderByType.Desc);
                    }
                    else if (sm.ColId.Equals("priority", System.StringComparison.OrdinalIgnoreCase))
                    {
                        query =
                            sm.Sort == "asc"
                                ? query.OrderBy(s => s.Priority)
                                : query.OrderBy(s => s.Priority, OrderByType.Desc);
                    }
                    else if (
                        sm.ColId.Equals("isEnabled", System.StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        query =
                            sm.Sort == "asc"
                                ? query.OrderBy(s => s.IsEnabled)
                                : query.OrderBy(s => s.IsEnabled, OrderByType.Desc);
                    }
                }
            }

            var total = await query.CountAsync();
            var items = await query.Skip(request.StartRow).Take(request.PageSize).ToListAsync();
            var ids = items.Select(x => x.Id).ToList();
            var allTargets =
                ids.Count > 0
                    ? await _context
                        .PricingStrategyTargetDb.AsQueryable()
                        .Where(t => ids.Contains(t.StrategyId))
                        .ToListAsync()
                    : new List<PricingStrategyTarget>();
            var allDetails =
                ids.Count > 0
                    ? await _context
                        .PricingStrategyDetailDb.AsQueryable()
                        .Where(d => ids.Contains(d.StrategyId))
                        .ToListAsync()
                    : new List<PricingStrategyDetail>();

            var list = items
                .Select(s => new PricingStrategyListDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    Level = s.Level,
                    Priority = s.Priority,
                    IsEnabled = s.IsEnabled,
                    StoreCodes = allTargets
                        .Where(t => t.StrategyId == s.Id && t.TargetType == "Store")
                        .Select(t => t.TargetCode ?? "")
                        .Where(x => !string.IsNullOrEmpty(x))
                        .Distinct()
                        .ToList(),
                    SupplierCodes = allTargets
                        .Where(t => t.StrategyId == s.Id && t.TargetType == "Supplier")
                        .Select(t => t.TargetCode ?? "")
                        .Where(x => !string.IsNullOrEmpty(x))
                        .Distinct()
                        .ToList(),
                    DetailsCount = allDetails.Count(d => d.StrategyId == s.Id),
                    TargetsCount = allTargets.Count(t => t.StrategyId == s.Id),
                })
                .ToList();

            return GridResponseDto<PricingStrategyListDto>.OK(list, total);
        }

        public async Task<ApiResponse<PricingStrategyDetailDto>> GetByIdAsync(string id)
        {
            var s = await _context.PricingStrategyDb.AsQueryable().InSingleAsync(id);
            if (s == null)
            {
                return new ApiResponse<PricingStrategyDetailDto>
                {
                    Success = false,
                    Message = "not found",
                };
            }
            var details = await _context.PricingStrategyDetailDb.GetListAsync(d =>
                d.StrategyId == id
            );
            var targets = await _context.PricingStrategyTargetDb.GetListAsync(t =>
                t.StrategyId == id
            );
            var dto = new PricingStrategyDetailDto
            {
                Id = s.Id,
                Name = s.Name,
                Level = s.Level,
                Priority = s.Priority,
                IsEnabled = s.IsEnabled,
                Details = (details ?? new List<PricingStrategyDetail>())
                    .Select(d => new PricingStrategyRuleDto
                    {
                        Id = d.Id,
                        MinPrice = d.MinPrice,
                        MaxPrice = d.MaxPrice,
                        StartRate = d.StartRate,
                        EndRate = d.EndRate,
                        Algorithm = d.Algorithm ?? string.Empty,
                    })
                    .OrderBy(d => d.MinPrice)
                    .ToList(),
                Targets = (targets ?? new List<PricingStrategyTarget>())
                    .Select(t => new PricingStrategyTargetDto
                    {
                        Id = t.Id,
                        TargetType = t.TargetType,
                        TargetCode = t.TargetCode,
                    })
                    .ToList(),
            };
            return new ApiResponse<PricingStrategyDetailDto>
            {
                Success = true,
                Message = "ok",
                Data = dto,
            };
        }

        public async Task<ApiResponse<PricingStrategyDetailDto>> CreateAsync(
            CreatePricingStrategyDto dto
        )
        {
            var id = UuidHelper.GenerateUuid7();
            var entity = new PricingStrategy
            {
                Id = id,
                Name = dto.Name,
                Level = dto.Level,
                Priority = dto.Priority,
                IsEnabled = dto.IsEnabled,
            };
            await _context.PricingStrategyDb.InsertAsync(entity);

            if (dto.Details != null && dto.Details.Count > 0)
            {
                var rules = dto
                    .Details.Select(d => new PricingStrategyDetail
                    {
                        Id = UuidHelper.GenerateUuid7(),
                        StrategyId = id,
                        MinPrice = d.MinPrice,
                        MaxPrice = d.MaxPrice,
                        StartRate = d.StartRate,
                        EndRate = d.EndRate,
                        Algorithm = d.Algorithm ?? string.Empty,
                    })
                    .ToList();
                await _context.PricingStrategyDetailDb.InsertRangeAsync(rules);
            }
            if (dto.Targets != null && dto.Targets.Count > 0)
            {
                var targets = dto
                    .Targets.Select(t => new PricingStrategyTarget
                    {
                        Id = UuidHelper.GenerateUuid7(),
                        StrategyId = id,
                        TargetType = t.TargetType,
                        TargetCode = t.TargetCode,
                    })
                    .ToList();
                await _context.PricingStrategyTargetDb.InsertRangeAsync(targets);
            }

            return await GetByIdAsync(id);
        }

        public async Task<ApiResponse<PricingStrategyDetailDto>> UpdateAsync(
            string id,
            UpdatePricingStrategyDto dto
        )
        {
            var s = await _context
                .PricingStrategyDb.AsQueryable()
                .Includes(x => x.Details)
                .Includes(x => x.Targets)
                .InSingleAsync(id);
            if (s == null)
            {
                return new ApiResponse<PricingStrategyDetailDto>
                {
                    Success = false,
                    Message = "not found",
                };
            }
            s.Name = dto.Name;
            s.Level = dto.Level;
            s.Priority = dto.Priority;
            s.IsEnabled = dto.IsEnabled;
            await _context.PricingStrategyDb.UpdateAsync(s);

            await _context.PricingStrategyDetailDb.DeleteAsync(d => d.StrategyId == id);
            await _context.PricingStrategyTargetDb.DeleteAsync(t => t.StrategyId == id);

            if (dto.Details != null && dto.Details.Count > 0)
            {
                var rules = dto
                    .Details.Select(d => new PricingStrategyDetail
                    {
                        Id = UuidHelper.GenerateUuid7(),
                        StrategyId = id,
                        MinPrice = d.MinPrice,
                        MaxPrice = d.MaxPrice,
                        StartRate = d.StartRate,
                        EndRate = d.EndRate,
                        Algorithm = d.Algorithm ?? "Linear",
                    })
                    .ToList();
                await _context.PricingStrategyDetailDb.InsertRangeAsync(rules);
            }

            if (dto.Targets != null && dto.Targets.Count > 0)
            {
                var targets = dto
                    .Targets.Select(t => new PricingStrategyTarget
                    {
                        Id = UuidHelper.GenerateUuid7(),
                        StrategyId = id,
                        TargetType = t.TargetType,
                        TargetCode = t.TargetCode,
                    })
                    .ToList();
                await _context.PricingStrategyTargetDb.InsertRangeAsync(targets);
            }

            return await GetByIdAsync(id);
        }

        public async Task<ApiResponse<bool>> DeleteAsync(string id)
        {
            await _context.PricingStrategyDetailDb.DeleteAsync(d => d.StrategyId == id);
            await _context.PricingStrategyTargetDb.DeleteAsync(t => t.StrategyId == id);
            var ok = await _context.PricingStrategyDb.DeleteAsync(s => s.Id == id);
            return new ApiResponse<bool>
            {
                Success = ok,
                Message = ok ? "ok" : "failed",
                Data = ok,
            };
        }
    }
}
