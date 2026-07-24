using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services;

/// <summary>
/// 国内套装模板服务。模板始终保存为可编辑快照，不能生成或复用货号、条码与父级价格。
/// </summary>
public sealed class DomesticSetTemplateService : IDomesticSetTemplateService
{
    private const string TemplateNotFoundCode = "DOMESTIC_SET_TEMPLATE_NOT_FOUND";
    private readonly ISqlSugarClient _db;

    public DomesticSetTemplateService(SqlSugarContext context)
    {
        _db = context.Db;
    }

    public async Task<ApiResponse<List<DomesticSetTemplateListItemDto>>> GetTemplatesAsync(
        string supplierCode,
        bool includeInactive = false
    )
    {
        var normalizedSupplierCode = Normalize(supplierCode);
        if (string.IsNullOrEmpty(normalizedSupplierCode))
        {
            return ApiResponse<List<DomesticSetTemplateListItemDto>>.Error(
                "供应商不能为空",
                "DOMESTIC_SET_TEMPLATE_SUPPLIER_REQUIRED"
            );
        }

        var templates = await BuildTemplatesQuery(_db, normalizedSupplierCode, includeInactive)
            .OrderByDescending(template => template.UpdatedAt)
            .ToListAsync();
        var templateIds = templates.Select(template => template.TemplateId).ToList();
        var itemTemplateIds = templateIds.Count == 0
            ? new List<string>()
            : await _db.Queryable<DomesticSetTemplateItem>()
                .Where(item => !item.IsDeleted && templateIds.Contains(item.TemplateId))
                .Select(item => item.TemplateId)
                .ToListAsync();
        var itemCounts = itemTemplateIds
            .GroupBy(templateId => templateId)
            .ToDictionary(group => group.Key, group => group.Count());

        return ApiResponse<List<DomesticSetTemplateListItemDto>>.OK(
            templates
                .Select(template => MapListItem(template, itemCounts.GetValueOrDefault(template.TemplateId)))
                .ToList()
        );
    }

    internal static ISugarQueryable<DomesticSetTemplate> BuildTemplatesQuery(
        ISqlSugarClient db,
        string normalizedSupplierCode,
        bool includeInactive
    )
    {
        var query = db.Queryable<DomesticSetTemplate>()
            .Where(template =>
                !template.IsDeleted
                && template.SupplierCode == normalizedSupplierCode
            );

        if (!includeInactive)
        {
            // 本地布尔值不能与数据库条件合并，否则 SQL Server 会生成无效的 OR 表达式。
            query = query.Where(template => template.IsEnabled);
        }

        return query;
    }

    public async Task<ApiResponse<DomesticSetTemplateDetailDto>> GetTemplateAsync(
        string templateId,
        string supplierCode
    )
    {
        var normalizedSupplierCode = Normalize(supplierCode);
        if (string.IsNullOrEmpty(normalizedSupplierCode))
        {
            return ApiResponse<DomesticSetTemplateDetailDto>.Error(
                "供应商不能为空",
                "DOMESTIC_SET_TEMPLATE_SUPPLIER_REQUIRED"
            );
        }

        var template = await FindTemplateAsync(templateId, normalizedSupplierCode);
        return template == null
            ? ApiResponse<DomesticSetTemplateDetailDto>.Error("模板不存在", TemplateNotFoundCode)
            : ApiResponse<DomesticSetTemplateDetailDto>.OK(await MapDetailAsync(template));
    }

    public async Task<ApiResponse<DomesticSetTemplateDetailDto>> CreateAsync(
        SaveDomesticSetTemplateRequest request
    )
    {
        var validation = ValidateRequest(request);
        if (validation != null)
        {
            return ApiResponse<DomesticSetTemplateDetailDto>.Error(
                validation.Value.Message,
                validation.Value.Code
            );
        }

        var supplierCode = Normalize(request.SupplierCode);
        var templateName = Normalize(request.TemplateName);
        var setProductName = Normalize(request.SetProductName);
        var isEnabled = request.IsEnabled ?? true;
        if (isEnabled && await HasEnabledDuplicateAsync(supplierCode, templateName, null))
        {
            return ApiResponse<DomesticSetTemplateDetailDto>.Error(
                "该供应商已有同名启用模板",
                "DOMESTIC_SET_TEMPLATE_NAME_EXISTS"
            );
        }

        var now = DateTime.UtcNow;
        var template = new DomesticSetTemplate
        {
            SupplierCode = supplierCode,
            TemplateName = templateName,
            SetProductName = setProductName,
            IsEnabled = isEnabled,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var transaction = await _db.Ado.UseTranAsync(async () =>
        {
            await _db.Insertable(template).ExecuteCommandAsync();
            await InsertItemsAsync(template.TemplateId, request.SubItems, now);
        });
        if (!transaction.IsSuccess)
        {
            if (IsEnabledTemplateNameUniqueConflict(transaction.ErrorException))
            {
                return ApiResponse<DomesticSetTemplateDetailDto>.Error(
                    "该供应商已有同名启用模板",
                    "DOMESTIC_SET_TEMPLATE_NAME_EXISTS"
                );
            }
            return ApiResponse<DomesticSetTemplateDetailDto>.Error(
                "创建国内套装模板失败",
                "DOMESTIC_SET_TEMPLATE_CREATE_FAILED"
            );
        }

        return ApiResponse<DomesticSetTemplateDetailDto>.OK(await MapDetailAsync(template), "模板创建成功");
    }

    public async Task<ApiResponse<DomesticSetTemplateDetailDto>> UpdateAsync(
        string templateId,
        string supplierCode,
        SaveDomesticSetTemplateRequest request
    )
    {
        var validation = ValidateRequest(request);
        if (validation != null)
        {
            return ApiResponse<DomesticSetTemplateDetailDto>.Error(
                validation.Value.Message,
                validation.Value.Code
            );
        }

        var normalizedSupplierCode = Normalize(supplierCode);
        if (string.IsNullOrEmpty(normalizedSupplierCode))
        {
            return ApiResponse<DomesticSetTemplateDetailDto>.Error(
                "供应商不能为空",
                "DOMESTIC_SET_TEMPLATE_SUPPLIER_REQUIRED"
            );
        }
        if (!string.Equals(Normalize(request.SupplierCode), normalizedSupplierCode, StringComparison.Ordinal))
        {
            return ApiResponse<DomesticSetTemplateDetailDto>.Error(
                "请求中的供应商不一致",
                "DOMESTIC_SET_TEMPLATE_SUPPLIER_MISMATCH"
            );
        }

        var template = await FindTemplateAsync(templateId, normalizedSupplierCode);
        if (template == null)
        {
            return ApiResponse<DomesticSetTemplateDetailDto>.Error("模板不存在", TemplateNotFoundCode);
        }

        var templateName = Normalize(request.TemplateName);
        var nextIsEnabled = request.IsEnabled ?? template.IsEnabled;
        if (
            nextIsEnabled
            && await HasEnabledDuplicateAsync(template.SupplierCode, templateName, template.TemplateId)
        )
        {
            return ApiResponse<DomesticSetTemplateDetailDto>.Error(
                "该供应商已有同名启用模板",
                "DOMESTIC_SET_TEMPLATE_NAME_EXISTS"
            );
        }

        var now = DateTime.UtcNow;
        var setProductName = Normalize(request.SetProductName);
        var transaction = await _db.Ado.UseTranAsync(async () =>
        {
            await _db.Updateable<DomesticSetTemplate>()
                .SetColumns(item => item.TemplateName == templateName)
                .SetColumns(item => item.SetProductName == setProductName)
                .SetColumns(item => item.IsEnabled == nextIsEnabled)
                .SetColumns(item => item.UpdatedAt == now)
                .Where(item =>
                    item.TemplateId == template.TemplateId
                    && item.SupplierCode == normalizedSupplierCode
                    && !item.IsDeleted
                )
                .ExecuteCommandAsync();
            // 更新时保留旧子项审计；新列表作为新的有序快照插入。
            await _db.Updateable<DomesticSetTemplateItem>()
                .SetColumns(item => item.IsDeleted == true)
                .SetColumns(item => item.UpdatedAt == now)
                .Where(item => item.TemplateId == template.TemplateId && !item.IsDeleted)
                .ExecuteCommandAsync();
            await InsertItemsAsync(template.TemplateId, request.SubItems, now);
        });
        if (!transaction.IsSuccess)
        {
            if (IsEnabledTemplateNameUniqueConflict(transaction.ErrorException))
            {
                return ApiResponse<DomesticSetTemplateDetailDto>.Error(
                    "该供应商已有同名启用模板",
                    "DOMESTIC_SET_TEMPLATE_NAME_EXISTS"
                );
            }
            return ApiResponse<DomesticSetTemplateDetailDto>.Error(
                "修改国内套装模板失败",
                "DOMESTIC_SET_TEMPLATE_UPDATE_FAILED"
            );
        }

        template.TemplateName = templateName;
        template.SetProductName = setProductName;
        template.IsEnabled = nextIsEnabled;
        template.UpdatedAt = now;
        return ApiResponse<DomesticSetTemplateDetailDto>.OK(await MapDetailAsync(template), "模板修改成功");
    }

    public async Task<ApiResponse<object>> DeactivateAsync(string templateId, string supplierCode)
    {
        var normalizedSupplierCode = Normalize(supplierCode);
        if (string.IsNullOrEmpty(normalizedSupplierCode))
        {
            return ApiResponse<object>.Error(
                "供应商不能为空",
                "DOMESTIC_SET_TEMPLATE_SUPPLIER_REQUIRED"
            );
        }

        var template = await FindTemplateAsync(templateId, normalizedSupplierCode);
        if (template == null)
        {
            return ApiResponse<object>.Error("模板不存在", TemplateNotFoundCode);
        }

        var now = DateTime.UtcNow;
        var affected = await _db.Updateable<DomesticSetTemplate>()
            .SetColumns(item => item.IsEnabled == false)
            .SetColumns(item => item.UpdatedAt == now)
            .Where(item =>
                item.TemplateId == template.TemplateId
                && item.SupplierCode == normalizedSupplierCode
                && !item.IsDeleted
            )
            .ExecuteCommandAsync();
        return affected == 1
            ? ApiResponse<object>.CreateSuccess("模板已停用")
            : ApiResponse<object>.Error("模板不存在", TemplateNotFoundCode);
    }

    private async Task<DomesticSetTemplate?> FindTemplateAsync(string templateId, string supplierCode)
    {
        var normalizedTemplateId = Normalize(templateId);
        return string.IsNullOrEmpty(normalizedTemplateId) || string.IsNullOrEmpty(supplierCode)
            ? null
            : await _db.Queryable<DomesticSetTemplate>()
                .FirstAsync(template =>
                    template.TemplateId == normalizedTemplateId
                    && template.SupplierCode == supplierCode
                    && !template.IsDeleted
                );
    }

    private async Task<bool> HasEnabledDuplicateAsync(
        string supplierCode,
        string templateName,
        string? exceptTemplateId
    ) => await _db.Queryable<DomesticSetTemplate>()
        .AnyAsync(template =>
            !template.IsDeleted
            && template.IsEnabled
            && template.SupplierCode == supplierCode
            && template.TemplateName == templateName
            && (exceptTemplateId == null || template.TemplateId != exceptTemplateId)
        );

    private async Task InsertItemsAsync(
        string templateId,
        IReadOnlyList<SaveDomesticSetTemplateItemRequest> subItems,
        DateTime now
    )
    {
        var items = subItems
            .Select(
                (item, index) => new DomesticSetTemplateItem
                {
                    TemplateId = templateId,
                    ProductName = Normalize(item.ProductName),
                    PrivateLabelPrice = item.PrivateLabelPrice!.Value,
                    SortOrder = index,
                    CreatedAt = now,
                    UpdatedAt = now,
                }
            )
            .ToList();
        await _db.Insertable(items).ExecuteCommandAsync();
    }

    private async Task<DomesticSetTemplateDetailDto> MapDetailAsync(DomesticSetTemplate template)
    {
        var items = await _db.Queryable<DomesticSetTemplateItem>()
            .Where(item => !item.IsDeleted && item.TemplateId == template.TemplateId)
            .OrderBy(item => item.SortOrder)
            .ToListAsync();
        return new DomesticSetTemplateDetailDto
        {
            TemplateId = template.TemplateId,
            SupplierCode = template.SupplierCode,
            TemplateName = template.TemplateName,
            SetProductName = template.SetProductName,
            IsEnabled = template.IsEnabled,
            SetQuantity = items.Count,
            UpdatedAt = template.UpdatedAt,
            SubItems = items
                .Select(
                    item => new DomesticSetTemplateItemDto
                    {
                        ProductName = item.ProductName,
                        PrivateLabelPrice = item.PrivateLabelPrice,
                        SortOrder = item.SortOrder,
                    }
                )
                .ToList(),
        };
    }

    private static DomesticSetTemplateListItemDto MapListItem(
        DomesticSetTemplate template,
        int setQuantity
    ) => new()
    {
        TemplateId = template.TemplateId,
        SupplierCode = template.SupplierCode,
        TemplateName = template.TemplateName,
        SetProductName = template.SetProductName,
        IsEnabled = template.IsEnabled,
        SetQuantity = setQuantity,
        UpdatedAt = template.UpdatedAt,
    };

    private static (string Code, string Message)? ValidateRequest(SaveDomesticSetTemplateRequest? request)
    {
        if (request == null)
        {
            return ("DOMESTIC_SET_TEMPLATE_REQUEST_REQUIRED", "模板请求不能为空");
        }
        if (string.IsNullOrEmpty(Normalize(request.SupplierCode)))
        {
            return ("DOMESTIC_SET_TEMPLATE_SUPPLIER_REQUIRED", "供应商不能为空");
        }
        if (string.IsNullOrEmpty(Normalize(request.TemplateName)))
        {
            return ("DOMESTIC_SET_TEMPLATE_TEMPLATE_NAME_REQUIRED", "模板名不能为空");
        }
        if (string.IsNullOrEmpty(Normalize(request.SetProductName)))
        {
            return ("DOMESTIC_SET_TEMPLATE_SET_PRODUCT_NAME_REQUIRED", "套装商品名不能为空");
        }
        if (request.SubItems == null || request.SubItems.Count == 0)
        {
            return ("DOMESTIC_SET_TEMPLATE_ITEMS_REQUIRED", "至少需要一个子项");
        }
        if (request.SubItems.Any(item => item == null || string.IsNullOrEmpty(Normalize(item.ProductName))))
        {
            return ("DOMESTIC_SET_TEMPLATE_ITEM_PRODUCT_NAME_REQUIRED", "子项名称不能为空");
        }
        if (request.SubItems.Any(item => !item.PrivateLabelPrice.HasValue))
        {
            return ("DOMESTIC_SET_TEMPLATE_ITEM_PRICE_REQUIRED", "OEM/PrivateLabel 价格不能为空");
        }
        if (request.SubItems.Any(item => item.PrivateLabelPrice!.Value < 0))
        {
            return ("DOMESTIC_SET_TEMPLATE_ITEM_PRICE_INVALID", "OEM/PrivateLabel 价格不能为负数");
        }
        return null;
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static bool IsEnabledTemplateNameUniqueConflict(Exception? exception)
    {
        const string indexName = "IX_DomesticSetTemplate_EnabledSupplierTemplateName_Unique";
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains(indexName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (
                (message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("2601", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("2627", StringComparison.OrdinalIgnoreCase))
                && message.Contains("DomesticSetTemplate", StringComparison.OrdinalIgnoreCase)
                && message.Contains("SupplierCode", StringComparison.OrdinalIgnoreCase)
                && message.Contains("TemplateName", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }
        return false;
    }
}
