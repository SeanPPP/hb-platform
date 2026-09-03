using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services.React;

/// <summary>
/// 货柜明细字段级乐观并发的统一指纹与判定入口。
/// 指纹只描述当前业务字段值，不保存服务端状态，因此可在普通、套装及多码商品的同一读取快照中稳定复算。
/// </summary>
internal static class ContainerDetailFieldConcurrencyGuard
{
    internal const string TokenVersion = "v1";
    internal const string ConflictCode = "CONCURRENT_FIELD_UPDATE";

    internal static readonly string[] EditableFields =
    {
        "调整浮率", "国内价格", "进口价格", "运输成本", "贴牌价格", "单件装箱数", "中包数",
        "单件体积", "装柜数量", "合计装柜体积", "合计装柜金额", "IsActive",
        "ProductCategoryGUID", "备注", "商品名称", "英文名称",
    };

    internal static string CreateToken(
        string hguid,
        string field,
        object? value,
        object? relatedValue
    )
    {
        var canonical = string.Join(
            "|",
            TokenVersion,
            NormalizeText(hguid),
            NormalizeText(field),
            NormalizeValue(value),
            NormalizeValue(relatedValue)
        );
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"{TokenVersion}.{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    /// <summary>
    /// 同值重试视为幂等成功；显式覆盖只接受用户刚确认的当前令牌，避免二次修改被旧确认覆盖。
    /// </summary>
    internal static ContainerDetailFieldConcurrencyResolution Resolve(
        string hguid,
        string field,
        string? expectedToken,
        string? overrideAcknowledgement,
        string currentToken,
        object? serverValue,
        object? submittedValue,
        bool hasRelatedSyncValue = false,
        bool relatedTargetsAlreadyAtSubmittedValue = false
    )
    {
        if (string.IsNullOrWhiteSpace(expectedToken) || expectedToken == currentToken)
        {
            return ContainerDetailFieldConcurrencyResolution.Allow();
        }

        // 关联主数据参与同步时，展示值相同并不代表所有写入目标都已是提交值。
        // 此时只能由相同令牌或显式确认覆盖放行，防止价格/上下架等共享商品字段被静默覆盖。
        if (
            BusinessValuesEqual(serverValue, submittedValue)
            && (!hasRelatedSyncValue || relatedTargetsAlreadyAtSubmittedValue)
        )
        {
            return ContainerDetailFieldConcurrencyResolution.Allow();
        }

        if (!string.IsNullOrWhiteSpace(overrideAcknowledgement) && overrideAcknowledgement == currentToken)
        {
            return ContainerDetailFieldConcurrencyResolution.Allow(overridden: true);
        }

        return ContainerDetailFieldConcurrencyResolution.Reject(
            new ContainerDetailFieldConflictDto
            {
                HGUID = hguid,
                Field = field,
                Code = ConflictCode,
                Message = "服务器已更新该字段，请采用服务器值或确认保留我的值",
                ServerValue = serverValue,
                SubmittedValue = submittedValue,
                CurrentServerFieldToken = currentToken,
            }
        );
    }

    internal static Dictionary<string, string> CreateDetailTokens(
        ContainerDetailDto detail,
        IEnumerable<StoreRetailPrice>? storeRetailPrices = null,
        IEnumerable<ProductSetCode>? productSetCodes = null,
        IEnumerable<StoreMultiCodeProduct>? storeMultiCodeProducts = null
    )
    {
        return CreateTokens(
            detail.HGUID ?? string.Empty,
            new Dictionary<string, ContainerDetailFieldSnapshot>(StringComparer.Ordinal)
            {
                ["调整浮率"] = new(detail.调整浮率),
                ["国内价格"] = new(detail.国内价格),
                ["进口价格"] = new(
                    detail.进口价格,
                    Composite(
                        detail.WarehouseImportPrice,
                        detail.ServerTokenLocalPurchasePrice,
                        StoreRetailPriceSnapshot(storeRetailPrices),
                        ProductSetCodeSnapshot(productSetCodes),
                        StoreMultiCodeProductSnapshot(storeMultiCodeProducts)
                    )
                ),
                ["运输成本"] = new(detail.运输成本),
                ["贴牌价格"] = new(
                    detail.贴牌价格,
                    Composite(detail.WarehouseOEMPrice, detail.ServerTokenLocalRetailPrice)
                ),
                ["单件装箱数"] = new(detail.单件装箱数),
                ["中包数"] = new(
                    detail.中包数,
                    Composite(detail.中包数, detail.ServerTokenDomesticMiddlePackQuantity)
                ),
                ["单件体积"] = new(detail.单件体积),
                ["装柜数量"] = new(detail.装柜数量),
                ["合计装柜体积"] = new(detail.合计装柜体积),
                ["合计装柜金额"] = new(detail.合计装柜金额),
                ["IsActive"] = new(detail.ServerTokenDetailIsActive, detail.WarehouseIsActive),
                ["ProductCategoryGUID"] = new(
                    detail.ProductCategoryGUID,
                    Composite(detail.ServerTokenTargetCategoryGuid, detail.ServerTokenLocalCategoryGuid)
                ),
                ["备注"] = new(detail.备注),
                // 中文商品名称只写 DomesticProduct.ProductName；本地主档显示名属于英文名同步目标，
                // 不应让它的独立变化错误阻塞中文名称保存。
                ["商品名称"] = new(detail.商品信息?.商品名称),
                ["英文名称"] = new(
                    detail.商品信息?.英文名称,
                    Composite(
                        detail.ServerTokenLocalProductName,
                        detail.ServerTokenLocalEnglishName,
                        detail.ServerTokenDomesticEnglishName
                    )
                ),
            }
        );
    }

    internal static IReadOnlyDictionary<string, ContainerDetailFieldSnapshot> CreateSnapshots(
        ContainerDetail detail,
        WarehouseProduct? warehouseProduct,
        DomesticProduct? domesticProduct,
        Product? localProduct,
        IEnumerable<StoreRetailPrice>? storeRetailPrices = null,
        IEnumerable<ProductSetCode>? productSetCodes = null,
        IEnumerable<StoreMultiCodeProduct>? storeMultiCodeProducts = null
    ) => new Dictionary<string, ContainerDetailFieldSnapshot>(StringComparer.Ordinal)
    {
        ["调整浮率"] = new(detail.AdjustmentRate),
        ["国内价格"] = new(detail.DomesticPrice),
        ["进口价格"] = new(
            detail.ImportPrice,
            Composite(
                warehouseProduct?.ImportPrice,
                localProduct?.PurchasePrice,
                StoreRetailPriceSnapshot(storeRetailPrices),
                ProductSetCodeSnapshot(productSetCodes),
                StoreMultiCodeProductSnapshot(storeMultiCodeProducts)
            )
        ),
        ["运输成本"] = new(detail.TransportCost),
        ["贴牌价格"] = new(
            detail.OEMPrice,
            Composite(warehouseProduct?.OEMPrice, localProduct?.RetailPrice)
        ),
        ["单件装箱数"] = new(detail.PackingQuantity),
        ["中包数"] = new(
            warehouseProduct?.MinOrderQuantity is int warehouseMiddlePack
                ? (decimal?)warehouseMiddlePack
                : domesticProduct?.MiddlePackQuantity is int domesticMiddlePack
                    ? (decimal?)domesticMiddlePack
                    : null,
            Composite(
                warehouseProduct?.MinOrderQuantity is int warehouseMiddlePackValue
                    ? (decimal?)warehouseMiddlePackValue
                    : null,
                domesticProduct?.MiddlePackQuantity is int domesticMiddlePackValue
                    ? (decimal?)domesticMiddlePackValue
                    : null
            )
        ),
        ["单件体积"] = new(detail.UnitVolume),
        ["装柜数量"] = new(detail.LoadingQuantity),
        ["合计装柜体积"] = new(detail.TotalVolume),
        ["合计装柜金额"] = new(detail.TotalAmount),
        ["IsActive"] = new(detail.IsActive, warehouseProduct?.IsActive),
        ["ProductCategoryGUID"] = new(
            detail.TargetWarehouseCategoryGUID ?? localProduct?.WarehouseCategoryGUID,
            Composite(detail.TargetWarehouseCategoryGUID, localProduct?.WarehouseCategoryGUID)
        ),
        ["备注"] = new(detail.Remarks),
        // 与查询投影保持同一字段描述：中文名称仅绑定实际写目标 DomesticProduct。
        ["商品名称"] = new(domesticProduct?.ProductName),
        ["英文名称"] = new(
            localProduct?.ProductName ?? domesticProduct?.EnglishProductName,
            Composite(
                localProduct?.ProductName,
                localProduct?.EnglishName,
                domesticProduct?.EnglishProductName
            )
        ),
    };

    internal static Dictionary<string, string> CreateTokens(
        string hguid,
        IReadOnlyDictionary<string, ContainerDetailFieldSnapshot> snapshots
    )
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in EditableFields)
        {
            snapshots.TryGetValue(field, out var snapshot);
            tokens[field] = CreateToken(hguid, field, snapshot?.Value, snapshot?.RelatedValue);
        }
        return tokens;
    }

    internal static bool BusinessValuesEqual(object? left, object? right) =>
        NormalizeValue(left) == NormalizeValue(right);

    internal static string NormalizeValue(object? value) => value switch
    {
        null => "n",
        decimal decimalValue => $"d:{decimalValue.ToString("G29", CultureInfo.InvariantCulture)}",
        bool boolValue => boolValue ? "b:1" : "b:0",
        string text => $"s:{text.Length}:{text}",
        DateTime dateTime => $"t:{dateTime.ToUniversalTime():O}",
        _ => $"o:{Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty}",
    };

    private static string NormalizeText(string value) => value.Trim();

    private static string Composite(params object?[] values) =>
        string.Join("\u001f", values.Select(NormalizeValue));

    /// <summary>
    /// 分店进货价是同一进口价保存会覆盖的集合资源；按商品编码和主键排序，
    /// 保证普通、套装和多码商品在查询与事务重读时得到同一令牌。
    /// </summary>
    private static string StoreRetailPriceSnapshot(IEnumerable<StoreRetailPrice>? values) =>
        string.Join(
            "\u001e",
            (values ?? Enumerable.Empty<StoreRetailPrice>())
                .OrderBy(item => item.ProductCode, StringComparer.Ordinal)
                .ThenBy(item => item.UUID, StringComparer.Ordinal)
                // RepairMissingStoreRelationsLockedAsync 的候选门店组由 StoreCode 与活动状态决定；
                // 不能只绑定成本值，否则关系范围变化会绕过进口价令牌。
                .Select(item => Composite(
                    item.UUID,
                    item.StoreCode,
                    item.ProductCode,
                    item.PurchasePrice,
                    item.IsActive,
                    item.IsDeleted
                ))
        );

    /// <summary>
    /// 进口价会触发套装子项重算。活动的总部关系既是回写目标，也是 Type1 按零售价分配的计算输入；
    /// 因此任一关系、状态、零售价或既有成本变化都必须令进口价基线失效。
    /// </summary>
    private static string ProductSetCodeSnapshot(IEnumerable<ProductSetCode>? values) =>
        string.Join(
            "\u001e",
            (values ?? Enumerable.Empty<ProductSetCode>())
                // 修复关系时还会读取停用/软删的 Type1 历史行以识别墓碑，所有受管类型均需绑定。
                .Where(item => item.SetType == 1 || item.SetType == 2)
                .OrderBy(item => item.ProductCode, StringComparer.Ordinal)
                .ThenBy(item => item.SetCodeId, StringComparer.Ordinal)
                .Select(item => Composite(
                    item.SetCodeId,
                    item.ProductCode,
                    item.SetProductCode,
                    item.SetType,
                    item.SetRetailPrice,
                    item.SetPurchasePrice,
                    item.IsActive,
                    item.IsDeleted
                ))
        );

    /// <summary>
    /// 分店多码成本同样由进口价重算；除回写成本外，零售价、子码及启用状态都会参与结构/分配判定。
    /// </summary>
    private static string StoreMultiCodeProductSnapshot(IEnumerable<StoreMultiCodeProduct>? values) =>
        string.Join(
            "\u001e",
            (values ?? Enumerable.Empty<StoreMultiCodeProduct>())
                // 墓碑、停用和额外门店子项均可能让结构校验拒绝保存，不能从令牌快照中排除。
                .OrderBy(item => item.ProductCode, StringComparer.Ordinal)
                .ThenBy(item => item.StoreCode, StringComparer.Ordinal)
                .ThenBy(item => item.UUID, StringComparer.Ordinal)
                .Select(item => Composite(
                    item.UUID,
                    item.StoreCode,
                    item.ProductCode,
                    item.MultiCodeProductCode,
                    item.MultiCodeRetailPrice,
                    item.PurchasePrice,
                    item.IsActive,
                    item.IsDeleted
                ))
        );
}

internal sealed record ContainerDetailFieldSnapshot(object? Value, object? RelatedValue = null);

internal sealed class ContainerDetailFieldConcurrencyResolution
{
    public bool Allowed { get; private init; }
    public bool Overridden { get; private init; }
    public ContainerDetailFieldConflictDto? Conflict { get; private init; }

    public static ContainerDetailFieldConcurrencyResolution Allow(bool overridden = false) =>
        new() { Allowed = true, Overridden = overridden };

    public static ContainerDetailFieldConcurrencyResolution Reject(
        ContainerDetailFieldConflictDto conflict
    ) => new() { Allowed = false, Conflict = conflict };
}

/// <summary>
/// 令牌强制开关开启后，旧客户端不得继续写入，避免无基线保存覆盖他人修改。
/// </summary>
internal sealed class ContainerDetailConcurrencyTokenRequiredException : InvalidOperationException
{
    internal const string ErrorCode = "CONCURRENCY_TOKEN_REQUIRED";

    internal ContainerDetailConcurrencyTokenRequiredException()
        : base("当前客户端版本不支持并发保存，请升级后再编辑") { }
}

internal sealed class ContainerDetailBatchPreviewConflictException : InvalidOperationException
{
    internal const string ErrorCode = "BATCH_PREVIEW_STALE";
    internal ContainerDetailBatchPreviewConflictException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
