using System.Data;
using System.Diagnostics;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

public sealed class PreorderReactService : IPreorderReactService, IPreorderGateService
{
    private readonly ISqlSugarClient _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuthorizationService _authorization;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<PreorderReactService> _logger;
    private readonly TimeProvider _timeProvider;

    public PreorderReactService(
        SqlSugarContext context,
        ICurrentUserService currentUser,
        IAuthorizationService authorization,
        IHttpContextAccessor httpContextAccessor,
        ILogger<PreorderReactService> logger,
        TimeProvider? timeProvider = null
    )
    {
        _db = context.Db;
        _currentUser = currentUser;
        _authorization = authorization;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PagedResult<PreorderTemplateSummaryDto>> GetTemplatesAsync(
        PreorderTemplateQueryDto query
    )
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var keyword = query.Keyword?.Trim();
        var source = _db.Queryable<PreorderTemplate>()
            .Where(item => !item.IsDeleted)
            .WhereIF(!string.IsNullOrWhiteSpace(keyword), item => item.Name.Contains(keyword!));
        var total = await source.CountAsync();
        var templates = await source
            .OrderByDescending(item => item.UpdatedAt)
            .ToPageListAsync(page, pageSize);
        var templateGuids = templates.Select(item => item.TemplateGuid).ToList();
        var itemTemplateGuids = templateGuids.Count == 0
            ? new List<string>()
            : await _db.Queryable<PreorderTemplateItem>()
                .Where(item => !item.IsDeleted && templateGuids.Contains(item.TemplateGuid))
                .Select(item => item.TemplateGuid)
                .ToListAsync();
        var storeTemplateGuids = templateGuids.Count == 0
            ? new List<string>()
            : await _db.Queryable<PreorderTemplateStore>()
                .Where(item => !item.IsDeleted && templateGuids.Contains(item.TemplateGuid))
                .Select(item => item.TemplateGuid)
                .ToListAsync();
        var activationTemplateGuids = templateGuids.Count == 0
            ? new List<string>()
            : await _db.Queryable<PreorderActivation>()
                .Where(item => !item.IsDeleted && templateGuids.Contains(item.TemplateGuid))
                .Select(item => item.TemplateGuid)
                .ToListAsync();
        var itemCounts = itemTemplateGuids.GroupBy(item => item).ToDictionary(group => group.Key, group => group.Count());
        var storeCounts = storeTemplateGuids.GroupBy(item => item).ToDictionary(group => group.Key, group => group.Count());
        var activationCounts = activationTemplateGuids.GroupBy(item => item).ToDictionary(group => group.Key, group => group.Count());
        var rows = templates.Select(template => MapTemplateSummary(
            template,
            itemCounts.GetValueOrDefault(template.TemplateGuid),
            storeCounts.GetValueOrDefault(template.TemplateGuid),
            activationCounts.GetValueOrDefault(template.TemplateGuid)
        )).ToList();

        return new PagedResult<PreorderTemplateSummaryDto>
        {
            Items = rows,
            Total = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<PreorderTemplateDetailDto> GetTemplateAsync(string templateGuid)
    {
        var template = await RequireTemplateAsync(templateGuid);
        return await MapTemplateDetailAsync(template);
    }

    public async Task<PreorderTemplateDetailDto> CreateTemplateAsync(
        SavePreorderTemplateDto request
    )
    {
        var validated = await ValidateTemplateRequestAsync(request);
        var now = UtcNow();
        var template = new PreorderTemplate
        {
            Name = request.Name.Trim(),
            IsEnabled = request.IsEnabled,
            Notes = NormalizeOptional(request.Notes),
            Revision = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var transaction = await _db.Ado.UseTranAsync(async () =>
        {
            await _db.Insertable(template).ExecuteCommandAsync();
            await InsertTemplateChildrenAsync(template.TemplateGuid, validated, now);
        });
        if (!transaction.IsSuccess)
        {
            throw transaction.ErrorException ?? new InvalidOperationException("创建 Preorder 模板失败");
        }

        return await MapTemplateDetailAsync(template);
    }

    public async Task<PreorderTemplateDetailDto> UpdateTemplateAsync(
        string templateGuid,
        SavePreorderTemplateDto request
    )
    {
        var validated = await ValidateTemplateRequestAsync(request);
        if (!request.ExpectedRevision.HasValue)
        {
            throw Conflict("模板已被其他用户修改，请刷新后重试", "PREORDER_TEMPLATE_CHANGED");
        }

        var resource = $"PreorderTemplate:{templateGuid}";
        await using var processLock = await PreorderMutationLock.AcquireProcessAsync(resource);
        PreorderTemplate? existing = null;
        var transaction = await _db.Ado.UseTranAsync(async () =>
        {
            await PreorderMutationLock.AcquireDatabaseAsync(_db, resource);
            existing = await RequireTemplateAsync(templateGuid);
            if (request.ExpectedRevision.Value != existing.Revision)
            {
                throw Conflict("模板已被其他用户修改，请刷新后重试", "PREORDER_TEMPLATE_CHANGED");
            }
            var now = UtcNow();
            var actor = _currentUser.GetCurrentUsername();
            var nextRevision = checked(existing.Revision + 1);
            // 关键逻辑：版本号参与 UPDATE 条件，防止两个管理员互相覆盖配置。
            var affected = await _db.Updateable<PreorderTemplate>()
                .SetColumns(item => item.Name == request.Name.Trim())
                .SetColumns(item => item.IsEnabled == request.IsEnabled)
                .SetColumns(item => item.Notes == NormalizeOptional(request.Notes))
                .SetColumns(item => item.Revision == nextRevision)
                .SetColumns(item => item.UpdatedAt == now)
                .SetColumns(item => item.UpdatedBy == actor)
                .Where(item =>
                    item.TemplateGuid == existing.TemplateGuid
                    && item.Revision == existing.Revision
                    && !item.IsDeleted
                )
                .ExecuteCommandAsync();
            if (affected != 1)
            {
                throw Conflict("模板已被其他用户修改，请刷新后重试", "PREORDER_TEMPLATE_CHANGED");
            }

            // 模板 revision 的旧配置必须保留审计，不能物理删除后丢失商品、MOQ 和默认分店历史。
            await _db.Updateable<PreorderTemplateItem>()
                .SetColumns(item => item.IsDeleted == true)
                .SetColumns(item => item.UpdatedAt == now)
                .SetColumns(item => item.UpdatedBy == actor)
                .Where(item => !item.IsDeleted && item.TemplateGuid == existing.TemplateGuid)
                .ExecuteCommandAsync();
            await _db.Updateable<PreorderTemplateStore>()
                .SetColumns(item => item.IsDeleted == true)
                .SetColumns(item => item.UpdatedAt == now)
                .SetColumns(item => item.UpdatedBy == actor)
                .Where(item => !item.IsDeleted && item.TemplateGuid == existing.TemplateGuid)
                .ExecuteCommandAsync();
            await InsertTemplateChildrenAsync(existing.TemplateGuid, validated, now);
            existing.Name = request.Name.Trim();
            existing.IsEnabled = request.IsEnabled;
            existing.Notes = NormalizeOptional(request.Notes);
            existing.Revision = nextRevision;
            existing.UpdatedAt = now;
            existing.UpdatedBy = actor;
        });
        if (!transaction.IsSuccess)
        {
            if (transaction.ErrorException is PreorderBusinessException business)
            {
                throw business;
            }
            throw transaction.ErrorException ?? new InvalidOperationException("更新 Preorder 模板失败");
        }

        return await MapTemplateDetailAsync(existing!);
    }

    public async Task<ResolvePreorderItemsResultDto> ResolveItemsAsync(
        ResolvePreorderItemsRequestDto request
    )
    {
        if (request.Rows.Count == 0)
        {
            throw BadRequest("至少粘贴一行商品", "PREORDER_INVALID_REQUEST");
        }

        var normalizedNumbers = request.Rows
            .Select(row => row.ItemNumber.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var products = normalizedNumbers.Count == 0
            ? new List<Product>()
            : await _db.Queryable<Product>()
                .Where(item => !item.IsDeleted && normalizedNumbers.Contains(item.ItemNumber!))
                .ToListAsync();
        var productCodes = products
            .Select(item => item.ProductCode)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var warehouseProducts = productCodes.Count == 0
            ? new List<WarehouseProduct>()
            : await _db.Queryable<WarehouseProduct>()
                .Where(item => !item.IsDeleted && productCodes.Contains(item.ProductCode))
                .ToListAsync();
        var warehouseByProduct = warehouseProducts.ToDictionary(
            item => item.ProductCode,
            StringComparer.OrdinalIgnoreCase
        );
        var conflictingInputs = request.Rows
            .Where(row => !string.IsNullOrWhiteSpace(row.ItemNumber))
            .GroupBy(row => row.ItemNumber.Trim(), StringComparer.Ordinal)
            .Where(group => group.Select(row => row.MinimumOrderQuantity).Distinct().Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var mergedInputs = new HashSet<(string ItemNumber, int MinimumOrderQuantity)>();

        var result = new ResolvePreorderItemsResultDto();
        foreach (var row in request.Rows)
        {
            var normalized = row.ItemNumber.Trim();
            // 相同货号与 MOQ 只保留第一次出现；不同 MOQ 由下方冲突分支逐行返回错误。
            if (normalized.Length > 0
                && row.MinimumOrderQuantity > 0
                && !conflictingInputs.Contains(normalized)
                && !mergedInputs.Add((normalized, row.MinimumOrderQuantity)))
            {
                continue;
            }
            var output = new ResolvedPreorderItemRowDto
            {
                LineNumber = row.LineNumber,
                ItemNumber = normalized,
                MinimumOrderQuantity = row.MinimumOrderQuantity,
            };
            if (normalized.Length == 0 || row.MinimumOrderQuantity <= 0)
            {
                output.Status = "Invalid";
                output.ErrorCode = "PREORDER_INVALID_REQUEST";
                output.Message = normalized.Length == 0 ? "货号不能为空" : "MOQ 必须为正整数";
            }
            else if (conflictingInputs.Contains(normalized))
            {
                output.Status = "Conflict";
                output.ErrorCode = "PREORDER_MOQ_CONFLICT";
                output.Message = "同一货号存在不同 MOQ";
            }
            else
            {
                var matches = products
                    // 关键逻辑：最终在内存中按 Ordinal 精确比较，避免不同数据库排序规则导致大小写语义漂移。
                    .Where(item => string.Equals(item.ItemNumber?.Trim(), normalized, StringComparison.Ordinal))
                    .ToList();
                if (matches.Count != 1 || string.IsNullOrWhiteSpace(matches[0].ProductCode))
                {
                    output.Status = matches.Count == 0 ? "NotFound" : "Ambiguous";
                    output.ErrorCode = matches.Count == 0 ? "PREORDER_ITEM_NOT_FOUND" : "PREORDER_ITEM_AMBIGUOUS";
                    output.Message = matches.Count == 0 ? "找不到对应商品" : "该货号对应多个商品";
                }
                else
                {
                    var product = matches[0];
                    warehouseByProduct.TryGetValue(product.ProductCode!, out var warehouse);
                    output.Status = "Resolved";
                    output.ProductCode = product.ProductCode;
                    output.ProductName = product.ProductName;
                    output.ProductImage = product.ProductImage;
                    output.ImportPrice = warehouse?.ImportPrice ?? product.PurchasePrice ?? 0m;
                    output.RetailPrice = warehouse?.OEMPrice ?? product.RetailPrice ?? 0m;
                }
            }
            result.Rows.Add(output);
        }
        return result;
    }

    public async Task<IReadOnlyList<PreorderActivationSummaryDto>> GetTemplateActivationsAsync(
        string templateGuid
    )
    {
        await RequireTemplateAsync(templateGuid);
        var rows = await _db.Queryable<PreorderActivation>()
            .Where(item => !item.IsDeleted && item.TemplateGuid == templateGuid)
            .OrderByDescending(item => item.PeriodNumber)
            .ToListAsync();
        return await MapActivationSummariesAsync(rows);
    }

    public async Task<PreorderActivationSummaryDto> ActivateAsync(
        string templateGuid,
        ActivatePreorderTemplateDto request
    )
    {
        var start = NormalizeUtc(request.StartAtUtc);
        var end = NormalizeUtc(request.EndAtUtc);
        if (start >= end)
        {
            throw BadRequest("结束时间必须晚于开始时间", "PREORDER_INVALID_REQUEST");
        }
        if (end <= UtcNow())
        {
            throw BadRequest("结束时间必须晚于当前时间", "PREORDER_INVALID_REQUEST");
        }
        if (!request.ExpectedRevision.HasValue)
        {
            throw Conflict("模板已被其他用户修改，请刷新后重试", "PREORDER_TEMPLATE_CHANGED");
        }

        PreorderActivation? created = null;
        var resource = $"PreorderTemplate:{templateGuid}";
        await using var processLock = await PreorderMutationLock.AcquireProcessAsync(resource);
        await RequireTemplateAsync(templateGuid);
        var storeGuids = (request.StoreGuids ?? await _db.Queryable<PreorderTemplateStore>()
                .Where(item => !item.IsDeleted && item.TemplateGuid == templateGuid)
                .Select(item => item.StoreGuid)
                .ToListAsync())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var stores = await LoadStoresAsync(storeGuids);
        if (stores.Count != storeGuids.Count || stores.Count == 0)
        {
            throw BadRequest("激活批次必须选择有效分店", "PREORDER_INVALID_REQUEST");
        }
        var storeResources = PreorderMutationLock.NormalizeAndOrderResources(
            stores.Select(item =>
                PreorderGateEvaluator.GetStoreLockResourceByStoreGuid(item.StoreGUID)
            )
        );
        var canonicalStoreGuids = BuildCanonicalStoreGuidByResource(
            stores.Select(item => item.StoreGUID)
        );
        await using var storeLocks = await PreorderMutationLock.AcquireProcessesAsync(
            storeResources
        );
        var transaction = await _db.Ado.UseTranAsync(async () =>
        {
            await PreorderMutationLock.AcquireDatabaseAsync(_db, resource);
            var template = await RequireTemplateAsync(templateGuid);
            if (template.Revision != request.ExpectedRevision.Value)
            {
                throw Conflict("模板已被其他用户修改，请刷新后重试", "PREORDER_TEMPLATE_CHANGED");
            }
            if (!template.IsEnabled)
            {
                throw BadRequest("未启用的模板不能激活", "PREORDER_TEMPLATE_DISABLED");
            }

            if (request.StoreGuids == null)
            {
                // 锁前候选只用于确定 Store process locks；默认集合必须在 DB template lock 内重读确认。
                var lockedDefaultStoreGuids = (await _db.Queryable<PreorderTemplateStore>()
                        .Where(item => !item.IsDeleted && item.TemplateGuid == templateGuid)
                        .Select(item => item.StoreGuid)
                        .ToListAsync())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!lockedDefaultStoreGuids.SetEquals(storeGuids))
                {
                    throw Conflict(
                        "模板已被其他用户修改，请刷新后重试",
                        "PREORDER_TEMPLATE_CHANGED"
                    );
                }
            }

            foreach (var storeResource in storeResources)
            {
                await PreorderMutationLock.AcquireDatabaseAsync(
                    _db,
                    storeResource,
                    canonicalStoreGuids[storeResource]
                );
            }
            // StoreGate 取得后必须重读，并用 fresh Store 写快照，禁止停用或删除分店穿透激活。
            stores = await LoadStoresAsync(storeGuids);
            var freshStoreGuids = stores
                .Select(item => item.StoreGUID)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (stores.Count != storeGuids.Count
                || !freshStoreGuids.SetEquals(storeGuids))
            {
                throw BadRequest("激活批次必须选择有效分店", "PREORDER_INVALID_REQUEST");
            }
            if (end <= UtcNow())
            {
                throw BadRequest("结束时间必须晚于当前时间", "PREORDER_INVALID_REQUEST");
            }
            var overlaps = await _db.Queryable<PreorderActivation>()
                .Where(item =>
                    !item.IsDeleted
                    && item.TemplateGuid == templateGuid
                    && item.Status != PreorderActivationStatuses.Cancelled
                    && item.StartAtUtc < end
                    && start < item.EndAtUtc
                )
                .AnyAsync();
            if (overlaps)
            {
                throw Conflict("同一模板的激活时间不能重叠", "PREORDER_ACTIVATION_OVERLAP");
            }

            var templateItems = await _db.Queryable<PreorderTemplateItem>()
                .Where(item => !item.IsDeleted && item.TemplateGuid == templateGuid)
                .OrderBy(item => item.SortOrder)
                .ToListAsync();
            if (templateItems.Count == 0)
            {
                throw BadRequest("模板没有商品，不能激活", "PREORDER_INVALID_REQUEST");
            }

            var products = await LoadTemplateProductSnapshotsAsync(templateItems);
            var period = (await _db.Queryable<PreorderActivation>()
                .Where(item => item.TemplateGuid == templateGuid)
                .MaxAsync(item => (int?)item.PeriodNumber) ?? 0) + 1;
            var now = UtcNow();
            created = new PreorderActivation
            {
                TemplateGuid = template.TemplateGuid,
                TemplateNameSnapshot = template.Name,
                PeriodNumber = period,
                ActivationCode = $"PRE-{now:yyyyMMdd}-{period:D3}-{Guid.NewGuid():N}"[..24].ToUpperInvariant(),
                SourceTemplateRevision = template.Revision,
                StartAtUtc = start,
                EndAtUtc = end,
                EstimatedArrivalDate = ToDatabaseDate(request.EstimatedArrivalDate),
                Status = start <= now ? PreorderActivationStatuses.Active : PreorderActivationStatuses.Scheduled,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await _db.Insertable(created).ExecuteCommandAsync();
            var activationItems = products.Select(item => new PreorderActivationItem
            {
                ActivationGuid = created.ActivationGuid,
                ProductCode = item.ProductCode,
                ItemNumber = item.ItemNumber,
                ProductName = item.ProductName,
                ProductImage = item.ProductImage,
                ImportPrice = item.ImportPrice,
                RetailPrice = item.RetailPrice,
                MinimumOrderQuantity = item.MinimumOrderQuantity,
                SortOrder = item.SortOrder,
                CreatedAt = now,
                UpdatedAt = now,
            }).ToList();
            var activationStores = stores.Select(store => new PreorderActivationStore
            {
                ActivationGuid = created.ActivationGuid,
                StoreGuid = store.StoreGUID,
                StoreCode = store.StoreCode,
                StoreName = store.StoreName,
                CreatedAt = now,
                UpdatedAt = now,
            }).ToList();
            await _db.Insertable(activationItems).ExecuteCommandAsync();
            await _db.Insertable(activationStores).ExecuteCommandAsync();
        });
        if (!transaction.IsSuccess)
        {
            RethrowTransaction(transaction.ErrorException, "创建 Preorder 激活批次失败");
        }

        _logger.LogInformation(
            "Preorder 模板已激活: TemplateGuid={TemplateGuid}, ActivationGuid={ActivationGuid}",
            templateGuid,
            created!.ActivationGuid
        );
        return await MapActivationSummaryAsync(created!);
    }

    public async Task<PreorderActivationDetailDto> GetActivationAsync(
        string activationGuid,
        string? storeCode = null
    )
    {
        var activation = await RequireActivationAsync(activationGuid);
        if (!string.IsNullOrWhiteSpace(storeCode))
        {
            var store = await RequireAccessibleTargetStoreAsync(activationGuid, storeCode);
            return await MapActivationDetailAsync(activation, store);
        }

        return await MapActivationDetailAsync(activation, null);
    }

    public async Task<PreorderActivationDetailDto> UpdateActivationStoresAsync(
        string activationGuid,
        UpdatePreorderActivationStoresDto request
    )
    {
        var normalizedActivationGuid = NormalizeRequired(activationGuid, "激活批次编号不能为空");
        var expectedStoreGuids = NormalizeStoreGuidSet(request.ExpectedStoreGuids);
        var requestedStoreGuids = NormalizeStoreGuidSet(request.StoreGuids);
        if (expectedStoreGuids.Count == 0 || requestedStoreGuids.Count == 0)
        {
            throw BadRequest("激活批次必须至少选择一个分店", "PREORDER_INVALID_REQUEST");
        }

        var activationResource = $"PreorderActivation:{normalizedActivationGuid}";
        // 关键锁顺序：先锁批次，再按规范化资源顺序锁住旧、新分店，和提交/关闭流程保持一致。
        await using var activationLock = await PreorderMutationLock.AcquireProcessAsync(
            activationResource
        );
        await RequireActivationAsync(normalizedActivationGuid);
        var initialTargets = await LoadActivationStoreTargetsFailClosedAsync(
            normalizedActivationGuid
        );
        var requestedStoreIdentities = await _db.Queryable<Store>()
            .Where(item => requestedStoreGuids.Contains(item.StoreGUID))
            .ToListAsync();
        var resolvedIdentityGuids = requestedStoreIdentities
            .Select(item => item.StoreGUID)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var canonicalStoreGuids = BuildCanonicalStoreGuidByResource(
            initialTargets.Select(item => item.StoreGuid)
                .Concat(requestedStoreIdentities.Select(item => item.StoreGUID))
                .Concat(requestedStoreGuids.Where(item => !resolvedIdentityGuids.Contains(item)))
        );
        var storeTargets = canonicalStoreGuids
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new PreorderStoreLockTarget(item.Key, item.Value))
            .ToList();
        await using var storeLocks = await PreorderMutationLock.AcquireProcessesAsync(
            storeTargets.Select(item => item.Resource)
        );

        PreorderActivation? activation = null;
        var transaction = await _db.Ado.UseTranAsync(async () =>
        {
            await PreorderMutationLock.AcquireDatabaseAsync(_db, activationResource);
            foreach (var storeTarget in storeTargets)
            {
                // 旧目标分店可能已被停用或删除，StoreGate 仍需取得，但不存在的身份行允许零命中。
                await PreorderMutationLock.AcquireDatabaseAsync(
                    _db,
                    storeTarget.Resource,
                    storeTarget.CanonicalStoreGuid,
                    requireStoreIdentity: false
                );
            }

            // 进程锁候选只用于确定锁集合；数据库锁内必须重读批次、目标快照和当前分店。
            activation = await RequireActivationAsync(normalizedActivationGuid);
            var currentTargets = await LoadActivationStoreTargetsFailClosedAsync(
                normalizedActivationGuid
            );
            var currentStoreGuids = currentTargets
                .Select(item => item.StoreGuid)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!currentStoreGuids.SetEquals(expectedStoreGuids))
            {
                throw Conflict(
                    "激活批次分店已被其他用户修改，请刷新后重试",
                    "PREORDER_ACTIVATION_STORES_CHANGED"
                );
            }

            var now = UtcNow();
            if (activation.Status is not (PreorderActivationStatuses.Scheduled
                    or PreorderActivationStatuses.Active)
                || activation.EndAtUtc <= now)
            {
                throw Conflict("Preorder 本期未激活或已结束", "PREORDER_NOT_ACTIVE");
            }

            var addedStoreGuids = requestedStoreGuids
                .Where(item => !currentStoreGuids.Contains(item))
                .ToList();
            var addedStores = await LoadStoresAsync(addedStoreGuids);
            var freshAddedStoreGuids = addedStores
                .Select(item => item.StoreGUID)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (addedStores.Count != addedStoreGuids.Count
                || !freshAddedStoreGuids.SetEquals(addedStoreGuids))
            {
                throw BadRequest("激活批次必须选择有效分店", "PREORDER_INVALID_REQUEST");
            }

            var removedStoreGuids = currentStoreGuids
                .Where(item => !requestedStoreGuids.Contains(item, StringComparer.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (removedStoreGuids.Count > 0)
            {
                var orderedStoreGuids = await _db.Queryable<PreorderWarehouseOrder>()
                    .Where(item => !item.IsDeleted && item.ActivationGuid == normalizedActivationGuid)
                    .Select(item => item.StoreGuid)
                    .ToListAsync();
                if (orderedStoreGuids.Any(removedStoreGuids.Contains))
                {
                    throw Conflict(
                        "已有 Preorder 订单的分店不能从激活批次移除",
                        "PREORDER_ACTIVATION_STORE_HAS_ORDER"
                    );
                }
            }

            var actor = _currentUser.GetCurrentUsername();
            var removedTargetGuids = currentTargets
                .Where(item => removedStoreGuids.Contains(item.StoreGuid))
                .Select(item => item.ActivationStoreGuid)
                .ToList();
            if (removedTargetGuids.Count > 0)
            {
                await _db.Updateable<PreorderActivationStore>()
                    .SetColumns(item => item.IsDeleted == true)
                    .SetColumns(item => item.UpdatedAt == now)
                    .SetColumns(item => item.UpdatedBy == actor)
                    .Where(item => removedTargetGuids.Contains(item.ActivationStoreGuid))
                    .ExecuteCommandAsync();
            }

            var addedTargets = addedStores
                .Select(store => new PreorderActivationStore
                {
                    ActivationGuid = normalizedActivationGuid,
                    StoreGuid = store.StoreGUID,
                    StoreCode = store.StoreCode,
                    StoreName = store.StoreName,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = actor,
                    UpdatedBy = actor,
                })
                .ToList();
            if (addedTargets.Count > 0)
            {
                await _db.Insertable(addedTargets).ExecuteCommandAsync();
            }
        });
        if (!transaction.IsSuccess)
        {
            RethrowTransaction(transaction.ErrorException, "更新 Preorder 激活批次分店失败");
        }

        return await MapActivationDetailAsync(activation!, null);
    }

    public async Task<PreorderActivationDetailDto> UpdateActivationEstimatedArrivalDateAsync(
        string activationGuid,
        UpdatePreorderActivationEstimatedArrivalDateDto request
    )
    {
        var normalizedActivationGuid = NormalizeRequired(activationGuid, "激活批次编号不能为空");
        var activationResource = $"PreorderActivation:{normalizedActivationGuid}";
        await using var activationLock = await PreorderMutationLock.AcquireProcessAsync(
            activationResource
        );

        PreorderActivation? activation = null;
        var transaction = await _db.Ado.UseTranAsync(async () =>
        {
            await PreorderMutationLock.AcquireDatabaseAsync(_db, activationResource);
            // 关键逻辑：批次锁内重读并比较日期 CAS，避免两个管理员互相覆盖或清空新值。
            activation = await RequireActivationAsync(normalizedActivationGuid);
            if (ToDateOnly(activation.EstimatedArrivalDate)
                != request.ExpectedEstimatedArrivalDate)
            {
                throw Conflict(
                    "预计到货日期已被其他用户修改，请刷新后重试",
                    "PREORDER_ACTIVATION_ARRIVAL_DATE_CHANGED"
                );
            }

            var now = UtcNow();
            if (activation.Status is not (PreorderActivationStatuses.Scheduled
                    or PreorderActivationStatuses.Active)
                || activation.EndAtUtc <= now)
            {
                throw Conflict("Preorder 本期未激活或已结束", "PREORDER_NOT_ACTIVE");
            }

            var expectedDatabaseDate = ToDatabaseDate(request.ExpectedEstimatedArrivalDate);
            var estimatedArrivalDate = ToDatabaseDate(request.EstimatedArrivalDate);
            var actor = _currentUser.GetCurrentUsername();
            var update = _db.Updateable<PreorderActivation>()
                .SetColumns(item => item.EstimatedArrivalDate == estimatedArrivalDate)
                .SetColumns(item => item.UpdatedAt == now)
                .SetColumns(item => item.UpdatedBy == actor)
                .Where(item =>
                    !item.IsDeleted && item.ActivationGuid == normalizedActivationGuid
                )
                .WhereIF(
                    expectedDatabaseDate.HasValue,
                    item => item.EstimatedArrivalDate == expectedDatabaseDate
                )
                .WhereIF(
                    !expectedDatabaseDate.HasValue,
                    item => item.EstimatedArrivalDate == null
                );
            if (await update.ExecuteCommandAsync() != 1)
            {
                throw Conflict(
                    "预计到货日期已被其他用户修改，请刷新后重试",
                    "PREORDER_ACTIVATION_ARRIVAL_DATE_CHANGED"
                );
            }

            activation.EstimatedArrivalDate = estimatedArrivalDate;
            activation.UpdatedAt = now;
            activation.UpdatedBy = actor;
        });
        if (!transaction.IsSuccess)
        {
            RethrowTransaction(transaction.ErrorException, "更新 Preorder 预计到货日期失败");
        }

        return await MapActivationDetailAsync(activation!, null);
    }

    public async Task<PreorderActivationSummaryDto> CloseActivationAsync(
        string activationGuid,
        ClosePreorderActivationDto request
    )
    {
        var initial = await RequireActivationAsync(activationGuid);
        var templateResource = $"PreorderTemplate:{initial.TemplateGuid}";
        var activationResource = $"PreorderActivation:{activationGuid}";
        await using var templateLock = await PreorderMutationLock.AcquireProcessAsync(templateResource);
        await using var activationLock = await PreorderMutationLock.AcquireProcessAsync(activationResource);
        var storeLockTargets = await ResolveActivationStoreLockTargetsFailClosedAsync(
            activationGuid
        );
        var storeResources = storeLockTargets.Select(item => item.Resource).ToList();
        // 延长、提前关闭都会改变普通订单门禁，固定按模板、批次、分店顺序与 Cart 提交流程互斥。
        await using var storeLocks = await PreorderMutationLock.AcquireProcessesAsync(storeResources);
        PreorderActivation? activation = null;
        var transaction = await _db.Ado.UseTranAsync(async () =>
        {
            await PreorderMutationLock.AcquireDatabaseAsync(_db, templateResource);
            await PreorderMutationLock.AcquireDatabaseAsync(_db, activationResource);
            foreach (var storeTarget in storeLockTargets)
            {
                // 历史目标分店可能已物理删除；此时 applock 仍需取得，但身份行未命中是允许语义。
                await PreorderMutationLock.AcquireDatabaseAsync(
                    _db,
                    storeTarget.Resource,
                    storeTarget.CanonicalStoreGuid,
                    requireStoreIdentity: false
                );
            }
            activation = await RequireActivationAsync(activationGuid);
            if (activation.Status is PreorderActivationStatuses.Closed or PreorderActivationStatuses.Cancelled)
            {
                return;
            }

            var now = UtcNow();
            if (request.EndAtUtc.HasValue)
            {
                // 自然过期即形成历史边界，只允许显式关闭，不能通过延长结束时间重新激活。
                if (activation.EndAtUtc <= now)
                {
                    throw Conflict("Preorder 本期已经结束，不能重新激活", "PREORDER_NOT_ACTIVE");
                }
                var requestedEnd = NormalizeUtc(request.EndAtUtc.Value);
                if (requestedEnd <= now || requestedEnd <= activation.EndAtUtc)
                {
                    throw BadRequest("指定结束时间只能用于延长当前批次", "PREORDER_INVALID_REQUEST");
                }
                var nextOverlap = await _db.Queryable<PreorderActivation>()
                    .Where(item =>
                        !item.IsDeleted
                        && item.TemplateGuid == activation.TemplateGuid
                        && item.ActivationGuid != activation.ActivationGuid
                        && item.Status != PreorderActivationStatuses.Cancelled
                        && item.StartAtUtc < requestedEnd
                        && activation.StartAtUtc < item.EndAtUtc
                    )
                    .AnyAsync();
                if (nextOverlap)
                {
                    throw Conflict("延长后会与同模板其他批次重叠", "PREORDER_ACTIVATION_OVERLAP");
                }
                activation.EndAtUtc = requestedEnd;
            }
            else
            {
                // Scheduled 批次允许直接提前关闭并保留原有效期快照；已开始批次把结束时间收至当前。
                if (now > activation.StartAtUtc)
                {
                    activation.EndAtUtc = now < activation.EndAtUtc ? now : activation.EndAtUtc;
                }
                activation.Status = PreorderActivationStatuses.Closed;
                activation.ClosedAtUtc = now;
            }
            activation.UpdatedAt = now;
            await _db.Updateable(activation).ExecuteCommandAsync();
        });
        if (!transaction.IsSuccess)
        {
            RethrowTransaction(transaction.ErrorException, "更新 Preorder 激活批次失败");
        }
        return await MapActivationSummaryAsync(activation!);
    }

    public async Task<PreorderActivationSummaryDto> CancelActivationAsync(string activationGuid)
    {
        var initial = await RequireActivationAsync(activationGuid);
        var templateResource = $"PreorderTemplate:{initial.TemplateGuid}";
        var activationResource = $"PreorderActivation:{activationGuid}";
        await using var templateLock = await PreorderMutationLock.AcquireProcessAsync(templateResource);
        await using var activationLock = await PreorderMutationLock.AcquireProcessAsync(activationResource);
        var storeLockTargets = await ResolveActivationStoreLockTargetsFailClosedAsync(
            activationGuid
        );
        var storeResources = storeLockTargets.Select(item => item.Resource).ToList();
        // 取消会立即解除所有目标分店的普通订单门禁，锁顺序必须与 Close/Activate 保持一致。
        await using var storeLocks = await PreorderMutationLock.AcquireProcessesAsync(storeResources);
        PreorderActivation? activation = null;
        var transaction = await _db.Ado.UseTranAsync(async () =>
        {
            await PreorderMutationLock.AcquireDatabaseAsync(_db, templateResource);
            await PreorderMutationLock.AcquireDatabaseAsync(_db, activationResource);
            foreach (var storeTarget in storeLockTargets)
            {
                // 取消历史批次不能因目标分店已删除而失败，零行只表示没有现存身份行可锁。
                await PreorderMutationLock.AcquireDatabaseAsync(
                    _db,
                    storeTarget.Resource,
                    storeTarget.CanonicalStoreGuid,
                    requireStoreIdentity: false
                );
            }
            activation = await RequireActivationAsync(activationGuid);
            if (activation.Status == PreorderActivationStatuses.Cancelled)
            {
                return;
            }
            activation.Status = PreorderActivationStatuses.Cancelled;
            activation.ClosedAtUtc = UtcNow();
            activation.UpdatedAt = activation.ClosedAtUtc;
            await _db.Updateable(activation).ExecuteCommandAsync();
        });
        if (!transaction.IsSuccess)
        {
            RethrowTransaction(transaction.ErrorException, "取消 Preorder 激活批次失败");
        }
        return await MapActivationSummaryAsync(activation!);
    }

    private async Task<List<PreorderStoreLockTarget>> ResolveActivationStoreLockTargetsFailClosedAsync(
        string activationGuid
    )
    {
        var storeGuids = await _db.Queryable<PreorderActivationStore>()
            .Where(item => !item.IsDeleted && item.ActivationGuid == activationGuid)
            .Select(item => item.StoreGuid)
            .ToListAsync();
        if (storeGuids.Count == 0)
        {
            throw new PreorderBusinessException(
                "Preorder 目标分店无法确认，请稍后重试",
                "PREORDER_GATE_UNAVAILABLE",
                StatusCodes.Status503ServiceUnavailable
            );
        }

        // 激活快照中的 StoreGuid 不可变；规范化并排序后，所有进程和数据库实例使用完全相同的锁顺序。
        var canonicalStoreGuids = BuildCanonicalStoreGuidByResource(storeGuids);
        return canonicalStoreGuids
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new PreorderStoreLockTarget(item.Key, item.Value))
            .ToList();
    }

    private async Task<List<PreorderActivationStore>> LoadActivationStoreTargetsFailClosedAsync(
        string activationGuid
    )
    {
        var targets = await _db.Queryable<PreorderActivationStore>()
            .Where(item => !item.IsDeleted && item.ActivationGuid == activationGuid)
            .ToListAsync();
        if (targets.Count == 0
            || targets.Any(item => string.IsNullOrWhiteSpace(item.StoreGuid))
            || targets.GroupBy(item => item.StoreGuid, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
        {
            throw new PreorderBusinessException(
                "Preorder 目标分店无法确认，请稍后重试",
                "PREORDER_GATE_UNAVAILABLE",
                StatusCodes.Status503ServiceUnavailable
            );
        }
        return targets;
    }

    private static IReadOnlyDictionary<string, string> BuildCanonicalStoreGuidByResource(
        IEnumerable<string> storeGuids
    )
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var canonicalStoreGuid in storeGuids)
        {
            var resource = PreorderMutationLock.NormalizeAndOrderResources(
                new[]
                {
                    PreorderGateEvaluator.GetStoreLockResourceByStoreGuid(canonicalStoreGuid),
                }
            ).Single();
            if (result.TryGetValue(resource, out var existing)
                && !string.Equals(existing, canonicalStoreGuid, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Preorder StoreGuid 大小写身份不唯一");
            }
            result[resource] = canonicalStoreGuid;
        }
        return result;
    }

    public async Task<IReadOnlyList<PreorderOrderSummaryDto>> GetActivationOrdersAsync(
        string activationGuid
    )
    {
        await RequireActivationAsync(activationGuid);
        var orders = await _db.Queryable<PreorderWarehouseOrder>()
            .Where(item => !item.IsDeleted && item.ActivationGuid == activationGuid)
            .OrderByDescending(item => item.SubmittedAtUtc)
            .ToListAsync();
        var orderGuids = orders.Select(item => item.OrderGuid).ToList();
        var items = orderGuids.Count == 0
            ? new List<PreorderWarehouseOrderItem>()
            : await _db.Queryable<PreorderWarehouseOrderItem>()
                .Where(item => !item.IsDeleted && orderGuids.Contains(item.OrderGuid))
                .ToListAsync();
        var itemsByOrder = items.ToLookup(item => item.OrderGuid, StringComparer.OrdinalIgnoreCase);
        return orders.Select(order => MapOrderSummary(order, itemsByOrder[order.OrderGuid])).ToList();
    }

    public async Task<PreorderActivationStatisticsDto> GetStatisticsAsync(string activationGuid)
    {
        await _db.Ado.BeginTranAsync(IsolationLevel.Serializable);
        try
        {
            // 页面计数、订单和商品矩阵必须来自同一数据库快照，不能在并发提交时互相错位。
            var statistics = await BuildStatisticsSnapshotAsync(activationGuid);
            await _db.Ado.CommitTranAsync();
            return statistics;
        }
        catch
        {
            await _db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private async Task<PreorderActivationStatisticsDto> BuildStatisticsSnapshotAsync(
        string activationGuid
    )
    {
        var activation = await RequireActivationAsync(activationGuid);
        var targetStores = await _db.Queryable<PreorderActivationStore>()
            .Where(item => !item.IsDeleted && item.ActivationGuid == activationGuid)
            .ToListAsync();
        var orders = await _db.Queryable<PreorderWarehouseOrder>()
            .Where(item => !item.IsDeleted && item.ActivationGuid == activationGuid)
            .ToListAsync();
        var respondedStoreCodes = orders
            .Where(item => PreorderRules.IsResponseCompleted(item.Status))
            .Select(item => item.StoreCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new PreorderActivationStatisticsDto
        {
            ActivationGuid = activationGuid,
            TargetStoreCount = targetStores.Count,
            SubmittedCount = orders.Count(item => item.Status == PreorderWarehouseOrderStatuses.Submitted),
            NoDemandCount = orders.Count(item => item.Status == PreorderWarehouseOrderStatuses.NoDemand),
            ProcessingCount = orders.Count(item => item.Status == PreorderWarehouseOrderStatuses.Processing),
            CompletedCount = orders.Count(item => item.Status == PreorderWarehouseOrderStatuses.Completed),
            CancelledCount = orders.Count(item => item.Status == PreorderWarehouseOrderStatuses.Cancelled),
            PendingCount = targetStores.Count(item => !respondedStoreCodes.Contains(item.StoreCode)),
            PendingStores = targetStores
                .Where(item => !respondedStoreCodes.Contains(item.StoreCode))
                .Select(MapStore)
                .ToList(),
        };

        // 整期取消后订单只保留审计，任何历史明细都不再进入有效汇总或矩阵。
        var includeEffectiveQuantities = activation.Status != PreorderActivationStatuses.Cancelled;
        var allOrderGuids = orders.Select(item => item.OrderGuid).ToList();
        var allOrderItems = allOrderGuids.Count == 0
            ? new List<PreorderWarehouseOrderItem>()
            : await _db.Queryable<PreorderWarehouseOrderItem>()
                .Where(item => !item.IsDeleted && allOrderGuids.Contains(item.OrderGuid))
                .ToListAsync();
        var itemsByOrder = allOrderItems.ToLookup(
            item => item.OrderGuid,
            StringComparer.OrdinalIgnoreCase
        );
        result.Orders = orders
            .OrderByDescending(item => item.SubmittedAtUtc)
            .Select(order => MapOrderSummary(order, itemsByOrder[order.OrderGuid]))
            .ToList();

        var effectiveOrderGuids = orders
            .Where(item =>
                includeEffectiveQuantities
                && PreorderRules.IsEffectiveQuantityStatus(item.Status)
            )
            .Select(item => item.OrderGuid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orderItems = allOrderItems
            .Where(item => effectiveOrderGuids.Contains(item.OrderGuid))
            .ToList();
        var activationItems = await _db.Queryable<PreorderActivationItem>()
            .Where(item => !item.IsDeleted && item.ActivationGuid == activationGuid)
            .OrderBy(item => item.SortOrder)
            .ToListAsync();
        var itemsByActivation = orderItems.ToLookup(
            item => item.ActivationItemGuid,
            StringComparer.OrdinalIgnoreCase
        );
        result.Products = activationItems.Select(item =>
        {
            var matching = itemsByActivation[item.ActivationItemGuid];
            return new PreorderProductStatisticsDto
            {
                ActivationItemGuid = item.ActivationItemGuid,
                ProductCode = item.ProductCode,
                ItemNumber = item.ItemNumber,
                ProductName = item.ProductName,
                MinimumOrderQuantity = item.MinimumOrderQuantity,
                OrderingStoreCount = matching.Where(detail => detail.OrderedQuantity > 0).Select(detail => detail.OrderGuid).Distinct().Count(),
                TotalPackCount = matching.Sum(detail => (long)detail.PackCount),
                TotalQuantity = matching.Sum(detail => (long)detail.OrderedQuantity),
                TotalImportAmount = matching.Sum(detail => detail.ImportAmount),
                TotalRetailAmount = matching.Sum(detail => detail.RetailAmount),
            };
        }).ToList();
        var orderByGuid = orders.ToDictionary(item => item.OrderGuid, StringComparer.OrdinalIgnoreCase);
        result.StoreProductQuantities = orderItems.Select(item =>
        {
            var order = orderByGuid[item.OrderGuid];
            return new PreorderStoreProductQuantityDto
            {
                StoreGuid = order.StoreGuid,
                StoreCode = order.StoreCode,
                StoreName = order.StoreName,
                OrderStatus = order.Status,
                ActivationItemGuid = item.ActivationItemGuid,
                ProductCode = item.ProductCode,
                PackCount = item.PackCount,
                OrderedQuantity = item.OrderedQuantity,
            };
        }).ToList();
        return result;
    }

    public async Task<PreorderExportFileDto> ExportAsync(string activationGuid)
    {
        PreorderActivation activation;
        PreorderActivationStatisticsDto statistics;
        List<PreorderOrderSummaryDto> orders;
        List<PreorderOrderSummaryDto> effectiveOrders;
        List<PreorderWarehouseOrderItem> items;
        await _db.Ado.BeginTranAsync(IsolationLevel.Serializable);
        try
        {
            // 四个工作表必须来自同一个数据库快照，避免并发提交/取消时出现无法对账的导出。
            activation = await RequireActivationAsync(activationGuid);
            statistics = await BuildStatisticsSnapshotAsync(activationGuid);
            orders = statistics.Orders.ToList();
            // 订单工作表保留全部状态供审计；商品明细只导出计入有效汇总的订单。
            effectiveOrders = orders.Where(item =>
                activation.Status != PreorderActivationStatuses.Cancelled
                && PreorderRules.IsEffectiveQuantityStatus(item.Status)
            ).ToList();
            var orderGuids = effectiveOrders.Select(item => item.OrderGuid).ToList();
            items = orderGuids.Count == 0
                ? new List<PreorderWarehouseOrderItem>()
                : await _db.Queryable<PreorderWarehouseOrderItem>()
                    .Where(item => !item.IsDeleted && orderGuids.Contains(item.OrderGuid))
                    .ToListAsync();
            await _db.Ado.CommitTranAsync();
        }
        catch
        {
            await _db.Ado.RollbackTranAsync();
            throw;
        }

        using var workbook = new XLWorkbook();
        WriteProductStatisticsSheet(workbook, statistics.Products);
        if (activation.Status == PreorderActivationStatuses.Cancelled)
        {
            // 分店订单表仍保留单号、分店、状态和提交审计，但取消整期后不展示有效数量金额。
            foreach (var order in orders)
            {
                order.SkuCount = 0;
                order.TotalPackCount = 0;
                order.TotalQuantity = 0;
                order.TotalImportAmount = 0;
                order.TotalRetailAmount = 0;
            }
        }
        WriteOrdersSheet(workbook, orders);
        WriteOrderItemsSheet(workbook, effectiveOrders, items);
        WritePendingStoresSheet(workbook, statistics.PendingStores);
        foreach (var sheet in workbook.Worksheets)
        {
            sheet.Columns().AdjustToContents(1, 60);
            sheet.SheetView.FreezeRows(1);
        }
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new PreorderExportFileDto
        {
            Content = stream.ToArray(),
            FileName = $"{SanitizeFileName(activation.TemplateNameSnapshot)}-第{activation.PeriodNumber}期.xlsx",
        };
    }

    public async Task<PreorderOrderSummaryDto> UpdateOrderStatusAsync(
        string orderGuid,
        UpdatePreorderOrderStatusDto request
    )
    {
        var target = request.Status.Trim();
        var allowedTargets = new[]
        {
            PreorderWarehouseOrderStatuses.ReturnedForRevision,
            PreorderWarehouseOrderStatuses.Processing,
            PreorderWarehouseOrderStatuses.Completed,
            PreorderWarehouseOrderStatuses.Cancelled,
        };
        if (!allowedTargets.Contains(target, StringComparer.OrdinalIgnoreCase))
        {
            throw BadRequest("不支持的订单状态", "PREORDER_INVALID_STATUS");
        }
        var canonicalTarget = allowedTargets.First(item => item.Equals(target, StringComparison.OrdinalIgnoreCase));
        var notes = NormalizeOptional(request.WarehouseNotes);
        if (notes?.Length > 1000)
        {
            throw BadRequest("仓库备注不能超过 1000 个字符", "PREORDER_INVALID_REQUEST");
        }
        var order = await RequireOrderAsync(orderGuid);
        if (canonicalTarget == PreorderWarehouseOrderStatuses.ReturnedForRevision)
        {
            if (string.IsNullOrWhiteSpace(notes))
            {
                throw BadRequest("退回修改必须填写原因", "PREORDER_RETURN_REASON_REQUIRED");
            }
            if (string.IsNullOrWhiteSpace(request.ExpectedStatus)
                || !request.ExpectedDraftRevision.HasValue)
            {
                throw Conflict("订单状态已变化，请刷新后重试", "PREORDER_INVALID_STATUS_TRANSITION");
            }

            var initialTarget = await ResolveTargetStoreSnapshotFailClosedAsync(
                order.ActivationGuid,
                order.StoreGuid
            );
            var activationResource = $"PreorderActivation:{order.ActivationGuid}";
            var storeResource = PreorderGateEvaluator.GetStoreLockResourceByStoreGuid(order.StoreGuid);
            // 退回与提交共用 activation -> StoreGate 顺序，避免两个方向持锁形成死锁。
            await using var activationLock = await PreorderMutationLock.AcquireProcessAsync(activationResource);
            await using var storeLock = await PreorderMutationLock.AcquireProcessAsync(storeResource);
            PreorderWarehouseOrder? updatedOrder = null;
            var transaction = await _db.Ado.UseTranAsync(async () =>
            {
                await PreorderMutationLock.AcquireDatabaseAsync(_db, activationResource);
                await PreorderMutationLock.AcquireDatabaseAsync(
                    _db,
                    storeResource,
                    initialTarget.StoreGuid
                );

                // 锁内重读订单、激活期和目标分店，锁外快照只用于确定稳定锁资源。
                var currentOrder = await RequireOrderAsync(orderGuid);
                await RequireActiveActivationAsync(currentOrder.ActivationGuid);
                var currentTarget = await ResolveTargetStoreSnapshotFailClosedAsync(
                    currentOrder.ActivationGuid,
                    currentOrder.StoreGuid
                );
                if (!string.Equals(currentTarget.ActivationStoreGuid, initialTarget.ActivationStoreGuid, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(currentTarget.StoreGuid, initialTarget.StoreGuid, StringComparison.OrdinalIgnoreCase)
                    || !IsValidOrderTransition(currentOrder.Status, canonicalTarget)
                    || !string.Equals(currentOrder.Status, request.ExpectedStatus.Trim(), StringComparison.OrdinalIgnoreCase)
                    || currentOrder.DraftRevision != request.ExpectedDraftRevision.Value)
                {
                    throw Conflict("订单状态已变化，请刷新后重试", "PREORDER_INVALID_STATUS_TRANSITION");
                }

                var nextRevision = checked(currentOrder.DraftRevision + 1);
                var updatedAt = UtcNow();
                var actor = _currentUser.GetCurrentUsername();
                // 状态和 revision 同时参与 CAS，退回不能覆盖门店重提或另一仓库用户的操作。
                var affected = await _db.Updateable<PreorderWarehouseOrder>()
                    .SetColumns(item => item.Status == PreorderWarehouseOrderStatuses.ReturnedForRevision)
                    .SetColumns(item => item.DraftRevision == nextRevision)
                    .SetColumns(item => item.WarehouseNotes == notes)
                    .SetColumns(item => item.UpdatedAt == updatedAt)
                    .SetColumns(item => item.UpdatedBy == actor)
                    .Where(item =>
                        !item.IsDeleted
                        && item.OrderGuid == currentOrder.OrderGuid
                        && item.Status == currentOrder.Status
                        && item.DraftRevision == currentOrder.DraftRevision
                    )
                    .ExecuteCommandAsync();
                if (affected != 1)
                {
                    throw Conflict("订单状态已变化，请刷新后重试", "PREORDER_INVALID_STATUS_TRANSITION");
                }
                currentOrder.Status = PreorderWarehouseOrderStatuses.ReturnedForRevision;
                currentOrder.DraftRevision = nextRevision;
                currentOrder.WarehouseNotes = notes;
                currentOrder.UpdatedAt = updatedAt;
                currentOrder.UpdatedBy = actor;
                updatedOrder = currentOrder;
            });
            if (!transaction.IsSuccess)
            {
                RethrowTransaction(transaction.ErrorException, "退回 Preorder 订单失败");
            }
            return await MapOrderSummaryAsync(updatedOrder!);
        }
        if (canonicalTarget is PreorderWarehouseOrderStatuses.Processing or PreorderWarehouseOrderStatuses.Completed)
        {
            var activationResource = $"PreorderActivation:{order.ActivationGuid}";
            await using var activationLock = await PreorderMutationLock.AcquireProcessAsync(
                activationResource
            );
            PreorderWarehouseOrder? updatedOrder = null;
            var transaction = await _db.Ado.UseTranAsync(async () =>
            {
                await PreorderMutationLock.AcquireDatabaseAsync(_db, activationResource);
                // 取得与取消批次相同的锁后必须重读，避免取消完成前的旧状态穿透。
                var currentOrder = await RequireOrderAsync(orderGuid);
                var activation = await RequireActivationAsync(currentOrder.ActivationGuid);
                if (activation.Status == PreorderActivationStatuses.Cancelled)
                {
                    throw Conflict("Preorder 本期已经取消，订单仅保留审计", "PREORDER_NOT_ACTIVE");
                }
                if (!IsValidOrderTransition(currentOrder.Status, canonicalTarget))
                {
                    throw Conflict("当前订单状态不允许执行此操作", "PREORDER_INVALID_STATUS_TRANSITION");
                }
                if ((!string.IsNullOrWhiteSpace(request.ExpectedStatus)
                        && !string.Equals(currentOrder.Status, request.ExpectedStatus.Trim(), StringComparison.OrdinalIgnoreCase))
                    || (request.ExpectedDraftRevision.HasValue
                        && currentOrder.DraftRevision != request.ExpectedDraftRevision.Value))
                {
                    throw Conflict("订单状态已被其他用户更新，请刷新后重试", "PREORDER_INVALID_STATUS_TRANSITION");
                }

                var originalStatus = currentOrder.Status;
                var updatedAt = UtcNow();
                var actor = _currentUser.GetCurrentUsername();
                var affected = await _db.Updateable<PreorderWarehouseOrder>()
                    .SetColumns(item => item.Status == canonicalTarget)
                    .SetColumns(item => item.WarehouseNotes == notes)
                    .SetColumns(item => item.UpdatedAt == updatedAt)
                    .SetColumns(item => item.UpdatedBy == actor)
                    .Where(item =>
                        !item.IsDeleted
                        && item.OrderGuid == currentOrder.OrderGuid
                        && item.Status == originalStatus
                        && item.DraftRevision == currentOrder.DraftRevision
                    )
                    .ExecuteCommandAsync();
                if (affected != 1)
                {
                    throw Conflict(
                        "订单状态已被其他用户更新，请刷新后重试",
                        "PREORDER_INVALID_STATUS_TRANSITION"
                    );
                }
                currentOrder.Status = canonicalTarget;
                currentOrder.WarehouseNotes = notes;
                currentOrder.UpdatedAt = updatedAt;
                currentOrder.UpdatedBy = actor;
                updatedOrder = currentOrder;
            });
            if (!transaction.IsSuccess)
            {
                RethrowTransaction(transaction.ErrorException, "更新 Preorder 订单状态失败");
            }
            return await MapOrderSummaryAsync(updatedOrder!);
        }

        if (!IsValidOrderTransition(order.Status, canonicalTarget))
        {
            throw Conflict("当前订单状态不允许执行此操作", "PREORDER_INVALID_STATUS_TRANSITION");
        }
        if ((!string.IsNullOrWhiteSpace(request.ExpectedStatus)
                && !string.Equals(order.Status, request.ExpectedStatus.Trim(), StringComparison.OrdinalIgnoreCase))
            || (request.ExpectedDraftRevision.HasValue
                && order.DraftRevision != request.ExpectedDraftRevision.Value))
        {
            throw Conflict("订单状态已被其他用户更新，请刷新后重试", "PREORDER_INVALID_STATUS_TRANSITION");
        }
        var originalStatus = order.Status;
        var updatedAt = UtcNow();
        var actor = _currentUser.GetCurrentUsername();
        // Cancelled 目标不需等待批次锁，但继续使用原状态 CAS 防止覆盖并发更新。
        var affected = await _db.Updateable<PreorderWarehouseOrder>()
            .SetColumns(item => item.Status == canonicalTarget)
            .SetColumns(item => item.WarehouseNotes == notes)
            .SetColumns(item => item.UpdatedAt == updatedAt)
            .SetColumns(item => item.UpdatedBy == actor)
            .Where(item =>
                !item.IsDeleted
                && item.OrderGuid == order.OrderGuid
                && item.Status == originalStatus
                && item.DraftRevision == order.DraftRevision
            )
            .ExecuteCommandAsync();
        if (affected != 1)
        {
            throw Conflict("订单状态已被其他用户更新，请刷新后重试", "PREORDER_INVALID_STATUS_TRANSITION");
        }
        order.Status = canonicalTarget;
        order.WarehouseNotes = notes;
        order.UpdatedAt = updatedAt;
        order.UpdatedBy = actor;
        return await MapOrderSummaryAsync(order);
    }

    public async Task<PreorderActiveResultDto> GetActiveAsync(string storeCode)
    {
        return await PreorderGateEvaluator.ExecuteFailClosedAsync(async () =>
        {
            var currentStore = await RequireAccessibleStoreAsync(storeCode);
            var normalized = currentStore.StoreCode;
            var evaluation = await PreorderGateEvaluator.EvaluateAsync(_db, normalized, UtcNow());
            var summaries = await MapActivationSummariesAsync(evaluation.PendingActivations);
            return new PreorderActiveResultDto
            {
                StoreCode = normalized,
                NormalOrderBlocked = summaries.Count > 0,
                Activations = summaries,
            };
        }, storeCode, _logger);
    }

    public async Task<PreorderGateResult> CheckAsync(string storeCode)
    {
        // 门禁服务是纯数据判断；管理员是否豁免由普通订单入口决定。
        return await PreorderGateEvaluator.ExecuteFailClosedAsync(async () =>
        {
            var evaluation = await PreorderGateEvaluator.EvaluateAsync(_db, storeCode, UtcNow());
            var result = new PreorderGateResult
            {
                IsBlocked = evaluation.IsBlocked,
                PendingCount = evaluation.PendingActivations.Count,
            };
            result.Activations.AddRange(
                await MapActivationSummariesAsync(evaluation.PendingActivations)
            );
            return result;
        }, storeCode, _logger);
    }

    public async Task<PreorderActivationDetailDto> SaveDraftAsync(
        string activationGuid,
        SavePreorderDraftDto request
    )
    {
        var resource = $"PreorderActivation:{activationGuid}";
        PreorderActivationStore? store = null;
        PreorderActivation? activation = null;
        List<PreorderActivationItem>? activationItems = null;
        DraftPersistenceResult? savedDraft = null;
        // 进程锁只覆盖事务写入；返回 DTO 的只读汇总在释放锁后完成，避免阻塞同批次后续保存。
        await using (var processLock = await PreorderMutationLock.AcquireProcessAsync(resource))
        {
            var transaction = await _db.Ado.UseTranAsync(async () =>
            {
                await PreorderMutationLock.AcquireDatabaseAsync(_db, resource);
                activation = await RequireActiveActivationAsync(activationGuid);
                store = await RequireAccessibleTargetStoreAsync(activationGuid, request.StoreCode);
                activationItems = await LoadActivationItemsAsync(activationGuid);
                var order = await FindOrderAsync(activationGuid, store.StoreGuid);
                savedDraft = await PersistDraftAsync(
                    activation,
                    store,
                    request,
                    activationItems,
                    order
                );
            });
            if (!transaction.IsSuccess)
            {
                RethrowTransaction(transaction.ErrorException, "保存 Preorder 草稿失败");
            }
        }
        return await MapActivationDetailAsync(
            activation!,
            store!,
            activationItems!,
            savedDraft!.Order,
            savedDraft.Items
        );
    }

    public async Task<PreorderOrderSummaryDto> SubmitAsync(
        string activationGuid,
        SubmitPreorderDto request
    )
    {
        var submissionId = ResolveSubmissionId();
        var totalStopwatch = Stopwatch.StartNew();
        var submissionPath = "unknown";
        long preLockMs = 0;
        long processActivationLockWaitMs = 0;
        long processStoreLockWaitMs = 0;
        long databaseLockMs = 0;
        long lockedReadMs = 0;
        long itemLoadMs = 0;
        long persistMs = 0;
        long finalTransitionMs = 0;
        long transactionMs = 0;
        var preLockSqlRoundTrips = 0;
        var databaseLockSqlRoundTrips = 0;
        var lockedReadSqlRoundTrips = 0;
        var itemLoadSqlRoundTrips = 0;
        var persistSqlRoundTrips = 0;
        var finalTransitionSqlRoundTrips = 0;
        // 行数表示本次路径实际加载的记录数；幂等路径不加载激活商品，因此保持为 0。
        var activationItemCount = 0;
        var orderItemCount = 0;
        var telemetryLogged = false;

        void LogPerformance()
        {
            if (telemetryLogged)
            {
                return;
            }
            telemetryLogged = true;
            totalStopwatch.Stop();
            // 关键位置：只记录链路 ID、路径、耗时和行数，禁止写入商品明细或认证信息。
            _logger.LogInformation(
                "Preorder 提交性能: SubmissionId={SubmissionId}, Path={Path}, PreLockMs={PreLockMs}, ProcessActivationLockWaitMs={ProcessActivationLockWaitMs}, ProcessStoreLockWaitMs={ProcessStoreLockWaitMs}, DatabaseLockMs={DatabaseLockMs}, LockedReadMs={LockedReadMs}, ItemLoadMs={ItemLoadMs}, PersistMs={PersistMs}, FinalTransitionMs={FinalTransitionMs}, TransactionMs={TransactionMs}, PreLockSqlRoundTrips={PreLockSqlRoundTrips}, DatabaseLockSqlRoundTrips={DatabaseLockSqlRoundTrips}, LockedReadSqlRoundTrips={LockedReadSqlRoundTrips}, ItemLoadSqlRoundTrips={ItemLoadSqlRoundTrips}, PersistSqlRoundTrips={PersistSqlRoundTrips}, FinalTransitionSqlRoundTrips={FinalTransitionSqlRoundTrips}, SqlRoundTrips={SqlRoundTrips}, ActivationItemCount={ActivationItemCount}, OrderItemCount={OrderItemCount}, TotalMs={TotalMs}",
                submissionId,
                submissionPath,
                preLockMs,
                processActivationLockWaitMs,
                processStoreLockWaitMs,
                databaseLockMs,
                lockedReadMs,
                itemLoadMs,
                persistMs,
                finalTransitionMs,
                transactionMs,
                preLockSqlRoundTrips,
                databaseLockSqlRoundTrips,
                lockedReadSqlRoundTrips,
                itemLoadSqlRoundTrips,
                persistSqlRoundTrips,
                finalTransitionSqlRoundTrips,
                preLockSqlRoundTrips
                    + databaseLockSqlRoundTrips
                    + lockedReadSqlRoundTrips
                    + itemLoadSqlRoundTrips
                    + persistSqlRoundTrips
                    + finalTransitionSqlRoundTrips,
                activationItemCount,
                orderItemCount,
                totalStopwatch.ElapsedMilliseconds
            );
        }

        try
        {
            AccessibleStoreSnapshot accessibleStore;
            PreorderActivationStore initialTarget;
            var preLockStopwatch = Stopwatch.StartNew();
            try
            {
                preLockSqlRoundTrips++;
                accessibleStore = await RequireAccessibleStoreAsync(request.StoreCode);
                preLockSqlRoundTrips++;
                initialTarget = await ResolveTargetStoreSnapshotFailClosedAsync(
                    activationGuid,
                    accessibleStore.StoreGuid
                );
            }
            finally
            {
                preLockStopwatch.Stop();
                preLockMs = preLockStopwatch.ElapsedMilliseconds;
            }
            var activationResource = $"PreorderActivation:{activationGuid}";
            var storeResources = PreorderMutationLock.NormalizeAndOrderResources(
                new[]
                {
                    PreorderGateEvaluator.GetStoreLockResourceByStoreGuid(initialTarget.StoreGuid),
                }
            );
            PreorderActivationStore? store = null;
            PreorderWarehouseOrder? resultOrder = null;
            List<PreorderWarehouseOrderItem>? resultItems = null;
            {
                // 与生命周期操作保持 activation -> ordered stores 的锁顺序，避免 Submit 与 Close/Cancel 反向等待。
                var activationLockWaitStopwatch = Stopwatch.StartNew();
                await using var activationLock = await PreorderMutationLock.AcquireProcessAsync(
                    activationResource
                );
                activationLockWaitStopwatch.Stop();
                processActivationLockWaitMs = activationLockWaitStopwatch.ElapsedMilliseconds;
                var storeLockWaitStopwatch = Stopwatch.StartNew();
                await using var storeLocks = await PreorderMutationLock.AcquireProcessesAsync(
                    storeResources
                );
                storeLockWaitStopwatch.Stop();
                processStoreLockWaitMs = storeLockWaitStopwatch.ElapsedMilliseconds;
                var transactionStopwatch = Stopwatch.StartNew();
                try
                {
                    var transaction = await _db.Ado.UseTranAsync(async () =>
                    {
                        var databaseLockStopwatch = Stopwatch.StartNew();
                        try
                        {
                            databaseLockSqlRoundTrips += await PreorderMutationLock.AcquireDatabaseAsync(
                                _db,
                                activationResource
                            );
                            foreach (var storeResource in storeResources)
                            {
                                databaseLockSqlRoundTrips += await PreorderMutationLock.AcquireDatabaseAsync(
                                    _db,
                                    storeResource,
                                    initialTarget.StoreGuid
                                );
                            }
                        }
                        finally
                        {
                            databaseLockStopwatch.Stop();
                            databaseLockMs = databaseLockStopwatch.ElapsedMilliseconds;
                        }

                        // StoreGate 取得后必须重读批次、当前分店、目标快照和订单，等待期间的关闭、改名或响应都不能穿透。
                        PreorderActivation activation;
                        AccessibleStoreSnapshot currentStore;
                        PreorderWarehouseOrder? existing;
                        var lockedReadStopwatch = Stopwatch.StartNew();
                        try
                        {
                            lockedReadSqlRoundTrips++;
                            activation = await RequireActivationAsync(activationGuid);
                            lockedReadSqlRoundTrips++;
                            currentStore = await RequireAccessibleStoreAsync(request.StoreCode);
                            lockedReadSqlRoundTrips++;
                            store = await ResolveTargetStoreSnapshotFailClosedAsync(
                                activationGuid,
                                currentStore.StoreGuid
                            );
                            lockedReadSqlRoundTrips++;
                            existing = await FindOrderAsync(activationGuid, store.StoreGuid);
                        }
                        finally
                        {
                            lockedReadStopwatch.Stop();
                            lockedReadMs = lockedReadStopwatch.ElapsedMilliseconds;
                        }
                        if (!string.Equals(
                                store.ActivationStoreGuid,
                                initialTarget.ActivationStoreGuid,
                                StringComparison.OrdinalIgnoreCase
                            )
                            || !string.Equals(
                                store.StoreGuid,
                                initialTarget.StoreGuid,
                                StringComparison.OrdinalIgnoreCase
                            ))
                        {
                            throw new PreorderBusinessException(
                                "Preorder 目标分店暂时无法确认，请稍后重试",
                                "PREORDER_GATE_UNAVAILABLE",
                                StatusCodes.Status503ServiceUnavailable
                            );
                        }
                        if (existing != null && PreorderRules.IsResponseCompleted(existing.Status))
                        {
                            submissionPath = "idempotent";
                            resultOrder = existing;
                            var itemLoadStopwatch = Stopwatch.StartNew();
                            try
                            {
                                itemLoadSqlRoundTrips++;
                                resultItems = await LoadOrderItemsAsync(existing.OrderGuid);
                            }
                            finally
                            {
                                itemLoadStopwatch.Stop();
                                itemLoadMs += itemLoadStopwatch.ElapsedMilliseconds;
                            }
                            orderItemCount = resultItems.Count;
                            return;
                        }
                        RequireActiveActivation(activation);
                        List<PreorderActivationItem> activationItems;
                        var activationItemLoadStopwatch = Stopwatch.StartNew();
                        try
                        {
                            itemLoadSqlRoundTrips++;
                            activationItems = await LoadActivationItemsAsync(activationGuid);
                        }
                        finally
                        {
                            activationItemLoadStopwatch.Stop();
                            itemLoadMs += activationItemLoadStopwatch.ElapsedMilliseconds;
                        }
                        activationItemCount = activationItems.Count;
                        // 即使复用已保存草稿，提交请求仍必须完整覆盖当前批次商品，不能绕过最终校验。
                        ValidateDraftItems(request.Items, activationItems);

                        if (existing != null
                            && string.Equals(
                                existing.Status,
                                PreorderWarehouseOrderStatuses.Draft,
                                StringComparison.OrdinalIgnoreCase
                            )
                            && request.ExpectedDraftRevision == existing.DraftRevision)
                        {
                            List<PreorderWarehouseOrderItem> persistedItems;
                            var orderItemLoadStopwatch = Stopwatch.StartNew();
                            try
                            {
                                itemLoadSqlRoundTrips++;
                                persistedItems = await LoadOrderItemsAsync(existing.OrderGuid);
                            }
                            finally
                            {
                                orderItemLoadStopwatch.Stop();
                                itemLoadMs += orderItemLoadStopwatch.ElapsedMilliseconds;
                            }
                            var requestedPacks = request.Items.ToDictionary(
                                item => item.ActivationItemGuid,
                                item => item.PackCount,
                                StringComparer.OrdinalIgnoreCase
                            );
                            var activationByGuid = activationItems.ToDictionary(
                                item => item.ActivationItemGuid,
                                StringComparer.OrdinalIgnoreCase
                            );
                            var matchesSavedDraft = persistedItems.Count == request.Items.Count
                                && persistedItems.All(item =>
                                {
                                    if (!requestedPacks.TryGetValue(
                                            item.ActivationItemGuid,
                                            out var packCount
                                        )
                                        || !activationByGuid.TryGetValue(
                                            item.ActivationItemGuid,
                                            out var activationItem
                                        ))
                                    {
                                        return false;
                                    }
                                    var quantity = PreorderRules.CalculateOrderedQuantity(
                                        packCount,
                                        activationItem.MinimumOrderQuantity
                                    );
                                    // 快速路径还需核对所有规范快照字段，异常历史明细必须回退 Upsert 重建。
                                    return packCount == item.PackCount
                                        && string.Equals(
                                            item.ProductCode,
                                            activationItem.ProductCode,
                                            StringComparison.Ordinal
                                        )
                                        && string.Equals(
                                            item.ItemNumber,
                                            activationItem.ItemNumber,
                                            StringComparison.Ordinal
                                        )
                                        && string.Equals(
                                            item.ProductName,
                                            activationItem.ProductName,
                                            StringComparison.Ordinal
                                        )
                                        && string.Equals(
                                            item.ProductImage,
                                            activationItem.ProductImage,
                                            StringComparison.Ordinal
                                        )
                                        && item.MinimumOrderQuantity == activationItem.MinimumOrderQuantity
                                        && item.OrderedQuantity == quantity
                                        && item.ImportPrice == activationItem.ImportPrice
                                        && item.RetailPrice == activationItem.RetailPrice
                                        && item.ImportAmount == activationItem.ImportPrice * quantity
                                        && item.RetailAmount == activationItem.RetailPrice * quantity;
                                });
                            if (matchesSavedDraft)
                            {
                                submissionPath = "fast";
                                // 自动保存后的相同提交直接复用草稿明细，避免重复更新、删除和批量插入。
                                resultOrder = existing;
                                resultItems = persistedItems;
                                orderItemCount = persistedItems.Count;
                            }
                        }

                        if (resultOrder == null)
                        {
                            submissionPath = "slow";
                            // 慢路径复用 StoreGate 锁内已重读的数据，不能回调通用保存流程再次查询状态。
                            DraftPersistenceResult persisted;
                            var persistStopwatch = Stopwatch.StartNew();
                            try
                            {
                                persisted = await PersistDraftAsync(
                                    activation,
                                    store,
                                    request,
                                    activationItems,
                                    existing
                                );
                                persistSqlRoundTrips = persisted.SqlRoundTrips;
                            }
                            finally
                            {
                                persistStopwatch.Stop();
                                persistMs = persistStopwatch.ElapsedMilliseconds;
                            }
                            resultOrder = persisted.Order;
                            resultItems = persisted.Items;
                            orderItemCount = persisted.Items.Count;
                        }
                        var hasDemand = resultItems!.Any(item => item.PackCount > 0);
                        if (!hasDemand && !request.ConfirmNoDemand)
                        {
                            throw BadRequest("全部数量为零时必须确认本期无需求", "PREORDER_CONFIRM_NO_DEMAND_REQUIRED");
                        }
                        var now = UtcNow();
                        resultOrder.Status = hasDemand
                            ? PreorderWarehouseOrderStatuses.Submitted
                            : PreorderWarehouseOrderStatuses.NoDemand;
                        resultOrder.SubmittedAtUtc = now;
                        resultOrder.SubmittedByUserGuid = _currentUser.GetCurrentUserGuid();
                        resultOrder.SubmittedByName = _currentUser.GetCurrentUsername();
                        resultOrder.UpdatedAt = now;
                        var finalTransitionStopwatch = Stopwatch.StartNew();
                        try
                        {
                            finalTransitionSqlRoundTrips++;
                            await _db.Updateable(resultOrder).ExecuteCommandAsync();
                        }
                        finally
                        {
                            finalTransitionStopwatch.Stop();
                            finalTransitionMs = finalTransitionStopwatch.ElapsedMilliseconds;
                        }
                    });
                    if (!transaction.IsSuccess)
                    {
                        RethrowTransaction(transaction.ErrorException, "提交 Preorder 订单失败");
                    }
                }
                finally
                {
                    transactionStopwatch.Stop();
                    transactionMs = transactionStopwatch.ElapsedMilliseconds;
                }
            }
            // 退出进程锁作用域后再记录总耗时，确保 TotalMs 覆盖锁释放。
            _logger.LogInformation(
                "Preorder 已提交: ActivationGuid={ActivationGuid}, StoreCode={StoreCode}, OrderGuid={OrderGuid}",
                activationGuid,
                store!.StoreCode,
                resultOrder!.OrderGuid
            );
            // 正常提交和幂等重试都复用 StoreGate 锁内读取的最终明细，不在返回映射阶段追加查询。
            var result = MapOrderSummary(resultOrder!, resultItems!);
            LogPerformance();
            return result;
        }
        catch
        {
            submissionPath = "failed";
            LogPerformance();
            throw;
        }
    }

    private async Task<PreorderActivationStore> ResolveTargetStoreSnapshotFailClosedAsync(
        string activationGuid,
        string storeGuid
    )
    {
        var activationTargets = await _db.Queryable<PreorderActivationStore>()
            .Where(item => !item.IsDeleted && item.ActivationGuid == activationGuid)
            .ToListAsync();
        if (activationTargets.Count == 0
            || activationTargets.Any(item => string.IsNullOrWhiteSpace(item.StoreGuid))
            || activationTargets
                .GroupBy(item => item.StoreGuid, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
        {
            // 目标快照是 StoreGate 的身份来源；整期为空、空 StoreGuid 或重复 StoreGuid 都必须 fail-closed。
            throw new PreorderBusinessException(
                "Preorder 目标分店暂时无法确认，请稍后重试",
                "PREORDER_GATE_UNAVAILABLE",
                StatusCodes.Status503ServiceUnavailable
            );
        }

        var targets = activationTargets
            .Where(item => string.Equals(
                item.StoreGuid,
                storeGuid,
                StringComparison.OrdinalIgnoreCase
            ))
            .ToList();
        if (targets.Count == 0)
        {
            throw new PreorderBusinessException(
                "本期 Preorder 不对该分店开放",
                "FORBIDDEN",
                StatusCodes.Status403Forbidden
            );
        }
        if (targets.Count != 1
            || string.IsNullOrWhiteSpace(targets[0].ActivationStoreGuid)
            || string.IsNullOrWhiteSpace(targets[0].StoreGuid))
        {
            // 匹配结果仍需单一且具有稳定主键，不能自行选择一行。
            throw new PreorderBusinessException(
                "Preorder 目标分店暂时无法确认，请稍后重试",
                "PREORDER_GATE_UNAVAILABLE",
                StatusCodes.Status503ServiceUnavailable
            );
        }
        return targets[0];
    }

    private async Task<DraftPersistenceResult> PersistDraftAsync(
        PreorderActivation activation,
        PreorderActivationStore store,
        SavePreorderDraftDto request,
        IReadOnlyList<PreorderActivationItem> activationItems,
        PreorderWarehouseOrder? order
    )
    {
        var sqlRoundTrips = 0;
        ValidateDraftItems(request.Items, activationItems);
        if (order != null && !PreorderRules.IsStoreEditableOrderStatus(order.Status))
        {
            throw Conflict("本期 Preorder 已响应", "PREORDER_ALREADY_RESPONDED");
        }
        if (order == null && request.ExpectedDraftRevision != 0)
        {
            throw Conflict("草稿版本已变化，请刷新后重试", "PREORDER_DRAFT_CONFLICT");
        }
        if (order != null && request.ExpectedDraftRevision != order.DraftRevision)
        {
            throw Conflict("草稿版本已变化，请刷新后重试", "PREORDER_DRAFT_CONFLICT");
        }

        var now = UtcNow();
        if (order == null)
        {
            order = new PreorderWarehouseOrder
            {
                ActivationGuid = activation.ActivationGuid,
                StoreGuid = store.StoreGuid,
                StoreCode = store.StoreCode,
                StoreName = store.StoreName,
                OrderNo = $"{activation.ActivationCode}-{store.StoreCode}".ToUpperInvariant(),
                Status = PreorderWarehouseOrderStatuses.Draft,
                DraftRevision = 1,
                CreatedAt = now,
                UpdatedAt = now,
            };
            sqlRoundTrips++;
            await _db.Insertable(order).ExecuteCommandAsync();
        }
        else
        {
            order.DraftRevision = checked(order.DraftRevision + 1);
            order.UpdatedAt = now;
            // 提交也先落完整草稿，保证最终请求是数据真相而不是依赖最后一次自动保存。
            sqlRoundTrips++;
            await _db.Updateable(order).ExecuteCommandAsync();
            sqlRoundTrips++;
            await _db.Deleteable<PreorderWarehouseOrderItem>()
                .Where(item => item.OrderGuid == order.OrderGuid)
                .ExecuteCommandAsync();
        }

        var requestedByItem = request.Items.ToDictionary(
            item => item.ActivationItemGuid,
            item => item.PackCount,
            StringComparer.OrdinalIgnoreCase
        );
        var details = activationItems.Select(item =>
        {
            var packs = requestedByItem.GetValueOrDefault(item.ActivationItemGuid);
            var quantity = PreorderRules.CalculateOrderedQuantity(packs, item.MinimumOrderQuantity);
            return new PreorderWarehouseOrderItem
            {
                OrderGuid = order.OrderGuid,
                ActivationItemGuid = item.ActivationItemGuid,
                ProductCode = item.ProductCode,
                ItemNumber = item.ItemNumber,
                ProductName = item.ProductName,
                ProductImage = item.ProductImage,
                PackCount = packs,
                MinimumOrderQuantity = item.MinimumOrderQuantity,
                OrderedQuantity = quantity,
                ImportPrice = item.ImportPrice,
                RetailPrice = item.RetailPrice,
                ImportAmount = item.ImportPrice * quantity,
                RetailAmount = item.RetailPrice * quantity,
                CreatedAt = now,
                UpdatedAt = now,
            };
        }).ToList();
        if (details.Count > 0)
        {
            sqlRoundTrips++;
            await _db.Insertable(details).ExecuteCommandAsync();
        }
        return new DraftPersistenceResult(order, details, sqlRoundTrips);
    }

    private async Task<List<PreorderActivationItem>> LoadActivationItemsAsync(
        string activationGuid
    ) => await _db.Queryable<PreorderActivationItem>()
        .Where(item => !item.IsDeleted && item.ActivationGuid == activationGuid)
        .OrderBy(item => item.SortOrder)
        .ToListAsync();

    private async Task<List<PreorderWarehouseOrderItem>> LoadOrderItemsAsync(string orderGuid) =>
        await _db.Queryable<PreorderWarehouseOrderItem>()
            .Where(item => !item.IsDeleted && item.OrderGuid == orderGuid)
            .ToListAsync();

    private async Task InsertTemplateChildrenAsync(
        string templateGuid,
        ValidatedTemplateRequest request,
        DateTime now
    )
    {
        var items = request.Items.Select(item => new PreorderTemplateItem
        {
            TemplateGuid = templateGuid,
            ProductCode = item.ProductCode.Trim(),
            MinimumOrderQuantity = item.MinimumOrderQuantity,
            SortOrder = item.SortOrder,
            CreatedAt = now,
            UpdatedAt = now,
        }).ToList();
        var stores = request.Stores.Select(store => new PreorderTemplateStore
        {
            TemplateGuid = templateGuid,
            StoreGuid = store.StoreGUID,
            CreatedAt = now,
            UpdatedAt = now,
        }).ToList();
        await _db.Insertable(items).ExecuteCommandAsync();
        await _db.Insertable(stores).ExecuteCommandAsync();
    }

    private async Task<ValidatedTemplateRequest> ValidateTemplateRequestAsync(
        SavePreorderTemplateDto request
    )
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 150)
        {
            throw BadRequest("模板名称不能为空且不能超过 150 个字符", "PREORDER_INVALID_REQUEST");
        }
        if (request.Items.Count == 0 || request.StoreGuids.Count == 0)
        {
            throw BadRequest("模板至少需要一个商品和一个分店", "PREORDER_INVALID_REQUEST");
        }
        if (request.Items.Any(item => string.IsNullOrWhiteSpace(item.ProductCode) || item.MinimumOrderQuantity <= 0))
        {
            throw BadRequest("商品代码不能为空且 MOQ 必须为正整数", "PREORDER_INVALID_REQUEST");
        }
        if (request.Items.GroupBy(item => item.ProductCode.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw BadRequest("模板内商品不能重复", "PREORDER_INVALID_REQUEST");
        }
        var productCodes = request.Items.Select(item => item.ProductCode.Trim()).ToList();
        var productCount = await _db.Queryable<Product>()
            .Where(item => !item.IsDeleted && productCodes.Contains(item.ProductCode!))
            .CountAsync();
        if (productCount != productCodes.Count)
        {
            throw BadRequest("模板包含不存在或重复的商品", "PREORDER_INVALID_REQUEST");
        }
        var storeGuids = request.StoreGuids
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var stores = await LoadStoresAsync(storeGuids);
        if (storeGuids.Count != request.StoreGuids.Count || stores.Count != storeGuids.Count)
        {
            throw BadRequest("模板包含无效或重复分店", "PREORDER_INVALID_REQUEST");
        }
        return new ValidatedTemplateRequest(request.Items, stores);
    }

    private async Task<List<ProductSnapshot>> LoadTemplateProductSnapshotsAsync(
        IReadOnlyList<PreorderTemplateItem> templateItems
    )
    {
        var codes = templateItems.Select(item => item.ProductCode).ToList();
        var products = await _db.Queryable<Product>()
            .Where(item => !item.IsDeleted && codes.Contains(item.ProductCode!))
            .ToListAsync();
        var warehouseProducts = await _db.Queryable<WarehouseProduct>()
            .Where(item => !item.IsDeleted && codes.Contains(item.ProductCode))
            .ToListAsync();
        var productsByCode = products
            .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
            .ToDictionary(item => item.ProductCode!, StringComparer.OrdinalIgnoreCase);
        var warehouseByCode = warehouseProducts.ToDictionary(item => item.ProductCode, StringComparer.OrdinalIgnoreCase);
        if (productsByCode.Count != templateItems.Count)
        {
            throw BadRequest("模板商品已不存在，不能激活", "PREORDER_INVALID_REQUEST");
        }
        return templateItems.Select(item =>
        {
            var product = productsByCode[item.ProductCode];
            warehouseByCode.TryGetValue(item.ProductCode, out var warehouse);
            return new ProductSnapshot(
                item.ProductCode,
                product.ItemNumber?.Trim() ?? string.Empty,
                product.ProductName,
                product.ProductImage,
                warehouse?.ImportPrice ?? product.PurchasePrice ?? 0m,
                warehouse?.OEMPrice ?? product.RetailPrice ?? 0m,
                item.MinimumOrderQuantity,
                item.SortOrder
            );
        }).ToList();
    }

    private async Task<PreorderTemplateSummaryDto> MapTemplateSummaryAsync(PreorderTemplate template) =>
        MapTemplateSummary(
            template,
            await _db.Queryable<PreorderTemplateItem>().CountAsync(item => !item.IsDeleted && item.TemplateGuid == template.TemplateGuid),
            await _db.Queryable<PreorderTemplateStore>().CountAsync(item => !item.IsDeleted && item.TemplateGuid == template.TemplateGuid),
            await _db.Queryable<PreorderActivation>().CountAsync(item => !item.IsDeleted && item.TemplateGuid == template.TemplateGuid)
        );

    private static PreorderTemplateSummaryDto MapTemplateSummary(
        PreorderTemplate template,
        int itemCount,
        int storeCount,
        int activationCount
    ) => new()
    {
        TemplateGuid = template.TemplateGuid,
        Name = template.Name,
        IsEnabled = template.IsEnabled,
        Revision = template.Revision,
        Notes = template.Notes,
        ItemCount = itemCount,
        StoreCount = storeCount,
        ActivationCount = activationCount,
        UpdatedAt = NormalizeUtc(template.UpdatedAt),
    };

    private async Task<PreorderTemplateDetailDto> MapTemplateDetailAsync(PreorderTemplate template)
    {
        var summary = await MapTemplateSummaryAsync(template);
        var templateItems = await _db.Queryable<PreorderTemplateItem>()
            .Where(item => !item.IsDeleted && item.TemplateGuid == template.TemplateGuid)
            .OrderBy(item => item.SortOrder)
            .ToListAsync();
        var snapshots = await LoadTemplateProductSnapshotsAsync(templateItems);
        var storeGuids = await _db.Queryable<PreorderTemplateStore>()
            .Where(item => !item.IsDeleted && item.TemplateGuid == template.TemplateGuid)
            .Select(item => item.StoreGuid)
            .ToListAsync();
        return new PreorderTemplateDetailDto
        {
            TemplateGuid = summary.TemplateGuid,
            Name = summary.Name,
            IsEnabled = summary.IsEnabled,
            Revision = summary.Revision,
            Notes = summary.Notes,
            ItemCount = summary.ItemCount,
            StoreCount = summary.StoreCount,
            ActivationCount = summary.ActivationCount,
            UpdatedAt = summary.UpdatedAt,
            Items = snapshots.Select(item => new PreorderTemplateItemDto
            {
                ProductCode = item.ProductCode,
                ItemNumber = item.ItemNumber,
                ProductName = item.ProductName,
                ProductImage = item.ProductImage,
                ImportPrice = item.ImportPrice,
                RetailPrice = item.RetailPrice,
                MinimumOrderQuantity = item.MinimumOrderQuantity,
                SortOrder = item.SortOrder,
            }).ToList(),
            Stores = (await LoadStoresAsync(storeGuids)).Select(MapStore).ToList(),
        };
    }

    private async Task<PreorderActivationSummaryDto> MapActivationSummaryAsync(PreorderActivation activation)
    {
        var targets = await _db.Queryable<PreorderActivationStore>()
            .CountAsync(item => !item.IsDeleted && item.ActivationGuid == activation.ActivationGuid);
        var responded = await _db.Queryable<PreorderWarehouseOrder>()
            .CountAsync(item =>
                !item.IsDeleted
                && item.ActivationGuid == activation.ActivationGuid
                && (item.Status == PreorderWarehouseOrderStatuses.Submitted
                    || item.Status == PreorderWarehouseOrderStatuses.NoDemand
                    || item.Status == PreorderWarehouseOrderStatuses.Processing
                    || item.Status == PreorderWarehouseOrderStatuses.Completed
                    || item.Status == PreorderWarehouseOrderStatuses.Cancelled)
            );
        return MapActivationSummary(activation, targets, responded);
    }

    private async Task<List<PreorderActivationSummaryDto>> MapActivationSummariesAsync(
        IReadOnlyList<PreorderActivation> activations
    )
    {
        var activationGuids = activations.Select(item => item.ActivationGuid).ToList();
        if (activationGuids.Count == 0)
        {
            return new List<PreorderActivationSummaryDto>();
        }
        var targetActivationGuids = await _db.Queryable<PreorderActivationStore>()
            .Where(item => !item.IsDeleted && activationGuids.Contains(item.ActivationGuid))
            .Select(item => item.ActivationGuid)
            .ToListAsync();
        var respondedActivationGuids = await _db.Queryable<PreorderWarehouseOrder>()
            .Where(item =>
                !item.IsDeleted
                && activationGuids.Contains(item.ActivationGuid)
                && (item.Status == PreorderWarehouseOrderStatuses.Submitted
                    || item.Status == PreorderWarehouseOrderStatuses.NoDemand
                    || item.Status == PreorderWarehouseOrderStatuses.Processing
                    || item.Status == PreorderWarehouseOrderStatuses.Completed
                    || item.Status == PreorderWarehouseOrderStatuses.Cancelled)
            )
            .Select(item => item.ActivationGuid)
            .ToListAsync();
        var targetCounts = targetActivationGuids.GroupBy(item => item)
            .ToDictionary(group => group.Key, group => group.Count());
        var respondedCounts = respondedActivationGuids.GroupBy(item => item)
            .ToDictionary(group => group.Key, group => group.Count());
        return activations.Select(activation => MapActivationSummary(
            activation,
            targetCounts.GetValueOrDefault(activation.ActivationGuid),
            respondedCounts.GetValueOrDefault(activation.ActivationGuid)
        )).ToList();
    }

    private PreorderActivationSummaryDto MapActivationSummary(
        PreorderActivation activation,
        int targets,
        int responded
    ) => new()
    {
        ActivationGuid = activation.ActivationGuid,
        TemplateGuid = activation.TemplateGuid,
        TemplateName = activation.TemplateNameSnapshot,
        PeriodNumber = activation.PeriodNumber,
        ActivationCode = activation.ActivationCode,
        SourceTemplateRevision = activation.SourceTemplateRevision,
        StartAtUtc = NormalizeUtc(activation.StartAtUtc),
        EndAtUtc = NormalizeUtc(activation.EndAtUtc),
        EstimatedArrivalDate = ToDateOnly(activation.EstimatedArrivalDate),
        Status = GetEffectiveActivationStatus(activation),
        TargetStoreCount = targets,
        RespondedStoreCount = responded,
    };

    private async Task<PreorderActivationDetailDto> MapActivationDetailAsync(
        PreorderActivation activation,
        PreorderActivationStore? store
    )
    {
        var activationItems = await LoadActivationItemsAsync(activation.ActivationGuid);
        var order = store == null ? null : await FindOrderAsync(activation.ActivationGuid, store.StoreGuid);
        var orderItems = order == null
            ? new List<PreorderWarehouseOrderItem>()
            : await LoadOrderItemsAsync(order.OrderGuid);
        return await MapActivationDetailAsync(
            activation,
            store,
            activationItems,
            order,
            orderItems
        );
    }

    private async Task<PreorderActivationDetailDto> MapActivationDetailAsync(
        PreorderActivation activation,
        PreorderActivationStore? store,
        IReadOnlyList<PreorderActivationItem> activationItems,
        PreorderWarehouseOrder? order,
        IReadOnlyList<PreorderWarehouseOrderItem> orderItems
    )
    {
        var summary = await MapActivationSummaryAsync(activation);
        var packsByItem = orderItems.ToDictionary(item => item.ActivationItemGuid, item => item.PackCount, StringComparer.OrdinalIgnoreCase);
        // 分店端只返回自身目标记录；管理员详情没有 store 参数时仍可查看完整目标范围。
        var stores = store == null
            ? await _db.Queryable<PreorderActivationStore>()
                .Where(item => !item.IsDeleted && item.ActivationGuid == activation.ActivationGuid)
                .OrderBy(item => item.StoreCode)
                .ToListAsync()
            : new List<PreorderActivationStore> { store };
        return new PreorderActivationDetailDto
        {
            ActivationGuid = summary.ActivationGuid,
            TemplateGuid = summary.TemplateGuid,
            TemplateName = summary.TemplateName,
            PeriodNumber = summary.PeriodNumber,
            ActivationCode = summary.ActivationCode,
            SourceTemplateRevision = summary.SourceTemplateRevision,
            StartAtUtc = summary.StartAtUtc,
            EndAtUtc = summary.EndAtUtc,
            EstimatedArrivalDate = summary.EstimatedArrivalDate,
            Status = summary.Status,
            TargetStoreCount = summary.TargetStoreCount,
            RespondedStoreCount = summary.RespondedStoreCount,
            StoreCode = store?.StoreCode ?? string.Empty,
            OrderGuid = order?.OrderGuid,
            OrderNo = order?.OrderNo,
            OrderStatus = order?.Status,
            DraftRevision = order?.DraftRevision ?? 0,
            WarehouseNotes = order?.WarehouseNotes,
            Items = activationItems.Select(item =>
            {
                var packs = packsByItem.GetValueOrDefault(item.ActivationItemGuid);
                return new PreorderActivationItemDto
                {
                    ActivationItemGuid = item.ActivationItemGuid,
                    ProductCode = item.ProductCode,
                    ItemNumber = item.ItemNumber,
                    ProductName = item.ProductName,
                    ProductImage = item.ProductImage,
                    ImportPrice = item.ImportPrice,
                    RetailPrice = item.RetailPrice,
                    MinimumOrderQuantity = item.MinimumOrderQuantity,
                    PackCount = packs,
                    OrderedQuantity = PreorderRules.CalculateOrderedQuantity(packs, item.MinimumOrderQuantity),
                };
            }).ToList(),
            Stores = stores.Select(MapStore).ToList(),
        };
    }

    private async Task<PreorderOrderSummaryDto> MapOrderSummaryAsync(PreorderWarehouseOrder order)
    {
        var items = await _db.Queryable<PreorderWarehouseOrderItem>()
            .Where(item => !item.IsDeleted && item.OrderGuid == order.OrderGuid)
            .ToListAsync();
        return MapOrderSummary(order, items);
    }

    private static PreorderOrderSummaryDto MapOrderSummary(
        PreorderWarehouseOrder order,
        IEnumerable<PreorderWarehouseOrderItem> items
    )
    {
        var details = items as IReadOnlyCollection<PreorderWarehouseOrderItem> ?? items.ToList();
        return new PreorderOrderSummaryDto
        {
            OrderGuid = order.OrderGuid,
            ActivationGuid = order.ActivationGuid,
            OrderNo = order.OrderNo,
            StoreGuid = order.StoreGuid,
            StoreCode = order.StoreCode,
            StoreName = order.StoreName,
            Status = order.Status,
            DraftRevision = order.DraftRevision,
            SubmittedBy = order.SubmittedByName,
            SubmittedAt = NormalizeUtc(order.SubmittedAtUtc),
            SkuCount = details.Count(item => item.OrderedQuantity > 0),
            TotalPackCount = details.Sum(item => (long)item.PackCount),
            TotalQuantity = details.Sum(item => (long)item.OrderedQuantity),
            TotalImportAmount = details.Sum(item => item.ImportAmount),
            TotalRetailAmount = details.Sum(item => item.RetailAmount),
            WarehouseNotes = order.WarehouseNotes,
        };
    }

    private async Task<PreorderActivation> RequireActiveActivationAsync(string activationGuid)
    {
        var activation = await RequireActivationAsync(activationGuid);
        RequireActiveActivation(activation);
        return activation;
    }

    private void RequireActiveActivation(PreorderActivation activation)
    {
        if (!PreorderRules.IsActivationActive(
            GetEffectiveActivationStatus(activation),
            activation.StartAtUtc,
            activation.EndAtUtc,
            UtcNow()
        ))
        {
            throw Conflict("Preorder 本期未激活或已结束", "PREORDER_NOT_ACTIVE");
        }
    }

    private string GetEffectiveActivationStatus(PreorderActivation activation)
    {
        if (activation.Status != PreorderActivationStatuses.Scheduled
            && activation.Status != PreorderActivationStatuses.Active)
        {
            // 未知状态必须保留并 fail-closed，不能仅因时间窗口就提升为 Active。
            return activation.Status;
        }
        var now = UtcNow();
        if (activation.EndAtUtc <= now)
        {
            return PreorderActivationStatuses.Closed;
        }
        return activation.StartAtUtc <= now
            ? PreorderActivationStatuses.Active
            : PreorderActivationStatuses.Scheduled;
    }

    private async Task<PreorderTemplate> RequireTemplateAsync(string templateGuid)
    {
        var normalized = NormalizeRequired(templateGuid, "模板编号不能为空");
        var entity = await _db.Queryable<PreorderTemplate>()
            .FirstAsync(item => !item.IsDeleted && item.TemplateGuid == normalized);
        return entity ?? throw NotFound("Preorder 模板不存在");
    }

    private async Task<PreorderActivation> RequireActivationAsync(string activationGuid)
    {
        var normalized = NormalizeRequired(activationGuid, "激活批次编号不能为空");
        var entity = await _db.Queryable<PreorderActivation>()
            .FirstAsync(item => !item.IsDeleted && item.ActivationGuid == normalized);
        return entity ?? throw NotFound("Preorder 激活批次不存在");
    }

    private async Task<PreorderWarehouseOrder> RequireOrderAsync(string orderGuid)
    {
        var normalized = NormalizeRequired(orderGuid, "订单编号不能为空");
        var entity = await _db.Queryable<PreorderWarehouseOrder>()
            .FirstAsync(item => !item.IsDeleted && item.OrderGuid == normalized);
        return entity ?? throw NotFound("Preorder 订单不存在");
    }

    private async Task<PreorderWarehouseOrder?> FindOrderAsync(string activationGuid, string storeGuid) =>
        await _db.Queryable<PreorderWarehouseOrder>()
            .FirstAsync(item => !item.IsDeleted && item.ActivationGuid == activationGuid && item.StoreGuid == storeGuid);

    private async Task<AccessibleStoreSnapshot> RequireAccessibleStoreAsync(string storeCode)
    {
        var normalized = NormalizeRequired(storeCode, "分店代码不能为空");
        var user = _httpContextAccessor.HttpContext?.User;
        var hasGlobalScope = user != null
            && (await _authorization.AuthorizeAsync(
                user,
                null,
                Permissions.Warehouse.ManageOrders
            )).Succeeded;
        var userGuid = hasGlobalScope ? null : _currentUser.GetCurrentUserGuid();
        AccessibleStoreSnapshot? currentStore;
        if (hasGlobalScope)
        {
            currentStore = await _db.Queryable<Store>()
                .Where(item => item.StoreCode == normalized)
                .Select(item => new AccessibleStoreSnapshot
                {
                    StoreGuid = item.StoreGUID,
                    StoreCode = item.StoreCode,
                    IsActive = item.IsActive,
                    IsDeleted = item.IsDeleted,
                    AssignedStoreGuid = item.StoreGUID,
                })
                .FirstAsync();
        }
        else
        {
            // 关键位置：LEFT JOIN 同时保留“不存在”与“存在但未授权”的区分，普通用户一次 SQL 完成门店和授权读取。
            currentStore = await _db.Queryable<Store>()
                .LeftJoin<UserStore>((store, assignment) =>
                    store.StoreGUID == assignment.StoreGUID
                    && assignment.UserGUID == userGuid
                    && !assignment.IsDeleted
                )
                .Where((store, assignment) => store.StoreCode == normalized)
                .Select((store, assignment) => new AccessibleStoreSnapshot
                {
                    StoreGuid = store.StoreGUID,
                    StoreCode = store.StoreCode,
                    IsActive = store.IsActive,
                    IsDeleted = store.IsDeleted,
                    AssignedStoreGuid = assignment.StoreGUID,
                })
                .FirstAsync();
        }
        if (currentStore == null)
        {
            throw new PreorderBusinessException(
                "分店不存在或不可用，无法确认 Preorder 状态",
                "PREORDER_GATE_UNAVAILABLE",
                503
            );
        }

        if (!hasGlobalScope && string.IsNullOrWhiteSpace(currentStore.AssignedStoreGuid))
        {
            throw new PreorderBusinessException("无权访问该分店", "FORBIDDEN", 403);
        }

        // 先完成分店 scope 校验，再报告停用状态，避免向未授权用户泄露分店状态。
        if (currentStore.IsDeleted || !currentStore.IsActive)
        {
            throw new PreorderBusinessException(
                "分店不存在或不可用，无法确认 Preorder 状态",
                "PREORDER_GATE_UNAVAILABLE",
                503
            );
        }
        return currentStore;
    }

    private async Task<PreorderActivationStore> RequireAccessibleTargetStoreAsync(
        string activationGuid,
        string storeCode
    )
    {
        var store = await RequireAccessibleStoreAsync(storeCode);
        var target = await _db.Queryable<PreorderActivationStore>()
            .FirstAsync(item =>
                !item.IsDeleted
                && item.ActivationGuid == activationGuid
                && item.StoreGuid == store.StoreGuid
            );
        return target ?? throw new PreorderBusinessException("本期 Preorder 不对该分店开放", "FORBIDDEN", 403);
    }

    private async Task<List<Store>> LoadStoresAsync(IReadOnlyList<string> storeGuids)
    {
        if (storeGuids.Count == 0)
        {
            return new List<Store>();
        }
        return await _db.Queryable<Store>()
            .Where(item => !item.IsDeleted && item.IsActive && storeGuids.Contains(item.StoreGUID))
            .OrderBy(item => item.StoreCode)
            .ToListAsync();
    }

    private static void ValidateDraftItems(
        IReadOnlyList<SavePreorderDraftItemDto> requested,
        IReadOnlyList<PreorderActivationItem> activationItems
    )
    {
        if (requested.Any(item => item.PackCount < 0 || string.IsNullOrWhiteSpace(item.ActivationItemGuid)))
        {
            throw BadRequest("份数必须为非负整数", "PREORDER_INVALID_REQUEST");
        }
        if (requested.GroupBy(item => item.ActivationItemGuid, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw BadRequest("商品不能重复提交", "PREORDER_INVALID_REQUEST");
        }
        var validIds = activationItems.Select(item => item.ActivationItemGuid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requestedIds = requested.Select(item => item.ActivationItemGuid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // PUT 与 submit 都必须携带完整份数，不能把漏传商品静默解释为 0。
        if (requested.Count != activationItems.Count || !validIds.SetEquals(requestedIds))
        {
            throw BadRequest("请求必须包含本期全部商品份数", "PREORDER_INVALID_REQUEST");
        }
    }

    private static bool IsValidOrderTransition(string current, string target) =>
        current switch
        {
            PreorderWarehouseOrderStatuses.Submitted =>
                target is PreorderWarehouseOrderStatuses.ReturnedForRevision or PreorderWarehouseOrderStatuses.Processing or PreorderWarehouseOrderStatuses.Completed or PreorderWarehouseOrderStatuses.Cancelled,
            PreorderWarehouseOrderStatuses.NoDemand =>
                target is PreorderWarehouseOrderStatuses.ReturnedForRevision,
            PreorderWarehouseOrderStatuses.Processing =>
                target is PreorderWarehouseOrderStatuses.Completed or PreorderWarehouseOrderStatuses.Cancelled,
            _ => false,
        };

    private static PreorderStoreDto MapStore(Store store) => new()
    {
        StoreGuid = store.StoreGUID,
        StoreCode = store.StoreCode,
        StoreName = store.StoreName,
    };

    private static PreorderStoreDto MapStore(PreorderActivationStore store) => new()
    {
        StoreGuid = store.StoreGuid,
        StoreCode = store.StoreCode,
        StoreName = store.StoreName,
    };

    private string ResolveSubmissionId()
    {
        var requested = _httpContextAccessor.HttpContext?
            .Request.Headers["X-Preorder-Submission-Id"]
            .ToString()
            .Trim();
        if (!string.IsNullOrEmpty(requested)
            && requested.Length <= 64
            && requested.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'
            ))
        {
            return requested;
        }

        // 缺失或不安全的外部 ID 一律替换，避免日志注入；完整 GUID 降低并发提交的碰撞概率。
        return Guid.NewGuid().ToString("N");
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private sealed record DraftPersistenceResult(
        PreorderWarehouseOrder Order,
        List<PreorderWarehouseOrderItem> Items,
        int SqlRoundTrips
    );

    private sealed record PreorderStoreLockTarget(
        string Resource,
        string CanonicalStoreGuid
    );

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue ? NormalizeUtc(value.Value) : null;

    private static DateTime? ToDatabaseDate(DateOnly? value) =>
        value?.ToDateTime(TimeOnly.MinValue);

    private static DateOnly? ToDateOnly(DateTime? value) =>
        value.HasValue ? DateOnly.FromDateTime(value.Value) : null;

    private static string NormalizeRequired(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw BadRequest(message, "PREORDER_INVALID_REQUEST");
        }
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> NormalizeStoreGuidSet(IEnumerable<string>? values) =>
        values?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
        ?? new List<string>();

    private static PreorderBusinessException BadRequest(string message, string code) => new(message, code, 400);
    private static PreorderBusinessException Conflict(string message, string code) => new(message, code, 409);
    private static PreorderBusinessException NotFound(string message) => new(message, "NOT_FOUND", 404);

    private static void RethrowTransaction(Exception? exception, string fallback)
    {
        if (exception is PreorderBusinessException business)
        {
            throw business;
        }
        throw exception ?? new InvalidOperationException(fallback);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalid.Contains(character) ? '_' : character));
    }

    private static void WriteProductStatisticsSheet(XLWorkbook workbook, IReadOnlyList<PreorderProductStatisticsDto> rows)
    {
        var sheet = workbook.Worksheets.Add("商品汇总");
        var headers = new[] { "货号", "名称", "MOQ", "订货分店数", "总份数", "总数量", "进口金额", "零售金额" };
        for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            sheet.Cell(index + 2, 1).Value = row.ItemNumber;
            sheet.Cell(index + 2, 2).Value = row.ProductName;
            sheet.Cell(index + 2, 3).Value = row.MinimumOrderQuantity;
            sheet.Cell(index + 2, 4).Value = row.OrderingStoreCount;
            sheet.Cell(index + 2, 5).Value = row.TotalPackCount;
            sheet.Cell(index + 2, 6).Value = row.TotalQuantity;
            sheet.Cell(index + 2, 7).Value = row.TotalImportAmount;
            sheet.Cell(index + 2, 8).Value = row.TotalRetailAmount;
        }
    }

    private static void WriteOrdersSheet(XLWorkbook workbook, IReadOnlyList<PreorderOrderSummaryDto> rows)
    {
        var sheet = workbook.Worksheets.Add("分店订单");
        var headers = new[] { "订单号", "分店", "状态", "提交人", "提交时间", "SKU数", "总份数", "总数量", "进口金额", "零售金额" };
        for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            sheet.Cell(index + 2, 1).Value = row.OrderNo;
            sheet.Cell(index + 2, 2).Value = $"{row.StoreCode} {row.StoreName}";
            sheet.Cell(index + 2, 3).Value = row.Status;
            sheet.Cell(index + 2, 4).Value = row.SubmittedBy ?? string.Empty;
            if (row.SubmittedAt.HasValue) sheet.Cell(index + 2, 5).Value = row.SubmittedAt.Value;
            sheet.Cell(index + 2, 6).Value = row.SkuCount;
            sheet.Cell(index + 2, 7).Value = row.TotalPackCount;
            sheet.Cell(index + 2, 8).Value = row.TotalQuantity;
            sheet.Cell(index + 2, 9).Value = row.TotalImportAmount;
            sheet.Cell(index + 2, 10).Value = row.TotalRetailAmount;
        }
    }

    private static void WriteOrderItemsSheet(
        XLWorkbook workbook,
        IReadOnlyList<PreorderOrderSummaryDto> orders,
        IReadOnlyList<PreorderWarehouseOrderItem> items
    )
    {
        var sheet = workbook.Worksheets.Add("分店商品明细");
        var headers = new[] { "订单号", "分店", "货号", "名称", "MOQ", "份数", "件数", "进口价", "零售价", "进口金额", "零售金额" };
        for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
        var orderByGuid = orders.ToDictionary(item => item.OrderGuid, StringComparer.OrdinalIgnoreCase);
        var rowIndex = 2;
        foreach (var item in items)
        {
            var order = orderByGuid[item.OrderGuid];
            sheet.Cell(rowIndex, 1).Value = order.OrderNo;
            sheet.Cell(rowIndex, 2).Value = order.StoreCode;
            sheet.Cell(rowIndex, 3).Value = item.ItemNumber;
            sheet.Cell(rowIndex, 4).Value = item.ProductName;
            sheet.Cell(rowIndex, 5).Value = item.MinimumOrderQuantity;
            sheet.Cell(rowIndex, 6).Value = item.PackCount;
            sheet.Cell(rowIndex, 7).Value = item.OrderedQuantity;
            sheet.Cell(rowIndex, 8).Value = item.ImportPrice;
            sheet.Cell(rowIndex, 9).Value = item.RetailPrice;
            sheet.Cell(rowIndex, 10).Value = item.ImportAmount;
            sheet.Cell(rowIndex, 11).Value = item.RetailAmount;
            rowIndex++;
        }
    }

    private static void WritePendingStoresSheet(XLWorkbook workbook, IReadOnlyList<PreorderStoreDto> stores)
    {
        var sheet = workbook.Worksheets.Add("未提交分店");
        sheet.Cell(1, 1).Value = "分店代码";
        sheet.Cell(1, 2).Value = "分店名称";
        for (var index = 0; index < stores.Count; index++)
        {
            sheet.Cell(index + 2, 1).Value = stores[index].StoreCode;
            sheet.Cell(index + 2, 2).Value = stores[index].StoreName;
        }
    }

    private sealed record ValidatedTemplateRequest(
        IReadOnlyList<SavePreorderTemplateItemDto> Items,
        IReadOnlyList<Store> Stores
    );

    private sealed record ProductSnapshot(
        string ProductCode,
        string ItemNumber,
        string ProductName,
        string? ProductImage,
        decimal ImportPrice,
        decimal RetailPrice,
        int MinimumOrderQuantity,
        int SortOrder
    );

    private sealed class AccessibleStoreSnapshot
    {
        public string StoreGuid { get; set; } = string.Empty;
        public string StoreCode { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
        public string? AssignedStoreGuid { get; set; }
    }
}
