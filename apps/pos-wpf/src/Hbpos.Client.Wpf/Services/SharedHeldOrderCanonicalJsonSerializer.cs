using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Hbpos.Client.Wpf.Models;

namespace Hbpos.Client.Wpf.Services;

public sealed class SharedHeldOrderCanonicalValidationException : FormatException
{
    public SharedHeldOrderCanonicalValidationException(string message)
        : base(message)
    {
    }

    public SharedHeldOrderCanonicalValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// canonical 严格校验：字段集合、类型、union 形状与数值范围全部冻结，
/// 与 iPad PricingCartStateSnapshot 语义一致。
/// </summary>
public static class SharedHeldOrderCanonicalValidator
{
    private static readonly HashSet<string> BasePriceSources =
        new(StringComparer.Ordinal)
        {
            SharedHeldOrderCanonicalConstants.BasePriceSourceCatalog,
            SharedHeldOrderCanonicalConstants.BasePriceSourceManual
        };

    public static void Validate(SharedHeldOrderCanonicalPayload payload)
    {
        if (payload.Version != SharedHeldOrderCanonicalPayload.CurrentVersion)
        {
            throw new SharedHeldOrderCanonicalValidationException("payload.version 必须是 1");
        }

        var pricingState = payload.PricingState;
        if (!string.Equals(pricingState.Mode, SharedHeldOrderCanonicalConstants.SaleMode, StringComparison.Ordinal))
        {
            throw new SharedHeldOrderCanonicalValidationException("挂单 canonical 只允许 sale 模式");
        }

        if (pricingState.Revision is < 1 or > (int)SharedHeldOrderCanonicalConstants.MaxQuantity)
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "revision 必须是 1 到 " + SharedHeldOrderCanonicalConstants.MaxQuantity);
        }

        RequireIso(pricingState.AsOfIso, "asOfIso");

        var promotions = pricingState.Promotions;
        if (promotions.Count > SharedHeldOrderCanonicalConstants.MaxPromotionCount)
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "promotions 数量不能超过 " + SharedHeldOrderCanonicalConstants.MaxPromotionCount);
        }

        var promotionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var promotion in promotions)
        {
            ValidatePromotion(promotion, promotionIds);
        }

        var lines = pricingState.Lines;
        if (lines.Count is < 1 or > SharedHeldOrderCanonicalConstants.MaxLineCount)
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "lines 必须包含 1 到 " + SharedHeldOrderCanonicalConstants.MaxLineCount + " 条");
        }

        var lineIds = new HashSet<string>(StringComparer.Ordinal);
        long totalGrossCents = 0;
        foreach (var line in lines)
        {
            var gross = ValidateLine(line, lineIds, promotionIds);
            if (gross is { } lineGross)
            {
                if (totalGrossCents > SharedHeldOrderCanonicalConstants.MaxTotalCents - lineGross)
                {
                    throw new SharedHeldOrderCanonicalValidationException(
                        "所有行 rounded gross 合计不能超过 " + SharedHeldOrderCanonicalConstants.MaxTotalCents);
                }

                totalGrossCents += lineGross;
            }
        }
    }

    private static void ValidatePromotion(
        SharedHeldOrderPromotionDefinition promotion,
        HashSet<string> promotionIds)
    {
        RequireText(promotion.Id, "promotion.id");
        if (promotion.Id.Length > SharedHeldOrderCanonicalConstants.MaxCodeLength)
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "promotion.id 不能超过 " + SharedHeldOrderCanonicalConstants.MaxCodeLength + " 字符");
        }

        if (!promotionIds.Add(promotion.Id))
        {
            throw new SharedHeldOrderCanonicalValidationException("promotion.id 必须唯一: " + promotion.Id);
        }

        RequireText(promotion.Name, "promotion.name");
        if (promotion.Name.Length > SharedHeldOrderCanonicalConstants.MaxNameLength)
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "promotion.name 不能超过 " + SharedHeldOrderCanonicalConstants.MaxNameLength + " 字符");
        }

        RequireIso(promotion.EffectiveStartIso, "promotion.effectiveStartIso");
        RequireIso(promotion.EffectiveEndIso, "promotion.effectiveEndIso");
        if (DateTimeOffset.Compare(
                DateTimeOffset.Parse(promotion.EffectiveStartIso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                DateTimeOffset.Parse(promotion.EffectiveEndIso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)) > 0)
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "promotion.effectiveEndIso 不能早于 effectiveStartIso");
        }

        if (promotion.ApplyQuantity is < 1 or > (int)SharedHeldOrderCanonicalConstants.MaxQuantity)
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "promotion.applyQuantity 必须是 1 到 " + SharedHeldOrderCanonicalConstants.MaxQuantity);
        }

        if (promotion.Priority is < 0 or > (int)SharedHeldOrderCanonicalConstants.MaxQuantity)
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "promotion.priority 必须是 0 到 " + SharedHeldOrderCanonicalConstants.MaxQuantity);
        }

        if (promotion.FixedPriceCents is < 0 or > SharedHeldOrderCanonicalConstants.MaxCents)
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "promotion.fixedPriceCents 必须是 0 到 " + SharedHeldOrderCanonicalConstants.MaxCents);
        }

        if (promotion.MaxApplicationsPerOrder is int maxApplications
            && maxApplications is < 1 or > (int)SharedHeldOrderCanonicalConstants.MaxQuantity)
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "promotion.maxApplicationsPerOrder 必须为 null 或 1 到 " + SharedHeldOrderCanonicalConstants.MaxQuantity);
        }

        var products = promotion.Products;
        if (products.Count is < 1 or > SharedHeldOrderCanonicalConstants.MaxPromotionProducts)
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "promotion.products 必须包含 1 到 " + SharedHeldOrderCanonicalConstants.MaxPromotionProducts + " 项");
        }

        foreach (var product in products)
        {
            RequireText(product.ProductCode, "promotion.product.productCode");
            if (product.ProductCode.Length > SharedHeldOrderCanonicalConstants.MaxCodeLength)
            {
                throw new SharedHeldOrderCanonicalValidationException(
                    "promotion.product.productCode 不能超过 " + SharedHeldOrderCanonicalConstants.MaxCodeLength + " 字符");
            }

            if (product.UnitWeight is < 0 or > SharedHeldOrderCanonicalConstants.MaxUnitWeight)
            {
                throw new SharedHeldOrderCanonicalValidationException(
                    "promotion.product.unitWeight 必须是 0 到 " + SharedHeldOrderCanonicalConstants.MaxUnitWeight);
            }
        }
    }

    private static long? ValidateLine(
        SharedHeldOrderPricingLine line,
        HashSet<string> lineIds,
        HashSet<string> promotionIds)
    {
        RequireText(line.LineId, "line.lineId");
        if (line.LineId.Length > SharedHeldOrderCanonicalConstants.MaxCodeLength)
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "line.lineId 不能超过 " + SharedHeldOrderCanonicalConstants.MaxCodeLength + " 字符");
        }

        if (!lineIds.Add(line.LineId))
        {
            throw new SharedHeldOrderCanonicalValidationException("line.lineId 必须唯一: " + line.LineId);
        }

        RequireText(line.ProductCode, "line.productCode");
        if (line.ProductCode.Length > SharedHeldOrderCanonicalConstants.MaxCodeLength)
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "line.productCode 不能超过 " + SharedHeldOrderCanonicalConstants.MaxCodeLength + " 字符");
        }

        if (line.ItemNumber is { Length: > SharedHeldOrderCanonicalConstants.MaxCodeLength })
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "line.itemNumber 不能超过 " + SharedHeldOrderCanonicalConstants.MaxCodeLength + " 字符");
        }

        RequireText(line.LookupCode, "line.lookupCode");
        if (line.LookupCode.Length > SharedHeldOrderCanonicalConstants.MaxCodeLength)
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "line.lookupCode 不能超过 " + SharedHeldOrderCanonicalConstants.MaxCodeLength + " 字符");
        }

        RequireText(line.DisplayName, "line.displayName");
        if (line.DisplayName.Length > SharedHeldOrderCanonicalConstants.MaxNameLength)
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "line.displayName 不能超过 " + SharedHeldOrderCanonicalConstants.MaxNameLength + " 字符");
        }

        if (line.Quantity is <= 0 or > SharedHeldOrderCanonicalConstants.MaxQuantity)
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "line.quantity 必须是 0 到 " + SharedHeldOrderCanonicalConstants.MaxQuantity + " 之间的正数");
        }

        if (line.UnitPriceCents is < 0 or > SharedHeldOrderCanonicalConstants.MaxCents)
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "line.unitPriceCents 必须是 0 到 " + SharedHeldOrderCanonicalConstants.MaxCents);
        }

        var gross = decimal.Round(
            line.Quantity * line.UnitPriceCents,
            0,
            MidpointRounding.AwayFromZero);
        if (gross > SharedHeldOrderCanonicalConstants.MaxTotalCents)
        {
            throw new SharedHeldOrderCanonicalValidationException(
                "line rounded gross 不能超过 " + SharedHeldOrderCanonicalConstants.MaxTotalCents);
        }

        if (!BasePriceSources.Contains(line.BasePriceSource))
        {
            throw new SharedHeldOrderCanonicalValidationException("普通共享 sale 只允许 catalog 或 manual");
        }

        if (!string.Equals(line.Kind, SharedHeldOrderCanonicalConstants.LineKindSale, StringComparison.Ordinal))
        {
            throw new SharedHeldOrderCanonicalValidationException("line.kind 必须是 sale；return/open-item 拒绝");
        }

        if (line.ReturnSourceKey is not null)
        {
            throw new SharedHeldOrderCanonicalValidationException("line.returnSourceKey 必须是 null");
        }

        if (line.OriginalOrderGuid is not null)
        {
            throw new SharedHeldOrderCanonicalValidationException("line.originalOrderGuid 必须是 null");
        }

        if (line.OriginalOrderDetailGuid is not null)
        {
            throw new SharedHeldOrderCanonicalValidationException("line.originalOrderDetailGuid 必须是 null");
        }

        if (line.SyncProvenance is { } provenance)
        {
            if (provenance.ReferenceCode is { Length: > SharedHeldOrderCanonicalConstants.MaxReferenceLength })
            {
                throw new SharedHeldOrderCanonicalValidationException(
                    "syncProvenance.referenceCode 不能超过 " + SharedHeldOrderCanonicalConstants.MaxReferenceLength + " 字符");
            }

            if (provenance.PriceSource is < 0 or > 4)
            {
                throw new SharedHeldOrderCanonicalValidationException("syncProvenance.priceSource 必须是 0..4");
            }
        }

        ValidateDiscountState(line.DiscountState, (long)gross, promotionIds);
        return (long)gross;
    }

    private static void ValidateDiscountState(
        SharedHeldOrderDiscountState discountState,
        long gross,
        HashSet<string> promotionIds)
    {
        switch (discountState.Mode)
        {
            case SharedHeldOrderCanonicalConstants.DiscountNone:
                if (discountState.Cents is not null
                    || discountState.BasisPoints is not null
                    || discountState.PromotionIds is not null)
                {
                    throw new SharedHeldOrderCanonicalValidationException("none 折扣不能携带其他字段");
                }

                break;
            case SharedHeldOrderCanonicalConstants.DiscountManualAmount:
                if (discountState.Cents is not { } amountCents
                    || amountCents is < 0 or > SharedHeldOrderCanonicalConstants.MaxCents)
                {
                    throw new SharedHeldOrderCanonicalValidationException(
                        "manual-amount 折扣必须带 0 到 " + SharedHeldOrderCanonicalConstants.MaxCents + " cents");
                }

                if (amountCents > gross)
                {
                    throw new SharedHeldOrderCanonicalValidationException("manual-amount 折扣不能超过行 gross");
                }

                if (discountState.BasisPoints is not null || discountState.PromotionIds is not null)
                {
                    throw new SharedHeldOrderCanonicalValidationException("manual-amount 折扣不能携带 basisPoints/promotionIds");
                }

                break;
            case SharedHeldOrderCanonicalConstants.DiscountManualPercent:
                if (discountState.BasisPoints is not { } basisPoints
                    || basisPoints is < 1 or > SharedHeldOrderCanonicalConstants.MaxBasisPoints)
                {
                    throw new SharedHeldOrderCanonicalValidationException(
                        "manual-percent 折扣必须带 1.." + SharedHeldOrderCanonicalConstants.MaxBasisPoints + " basisPoints");
                }

                if (discountState.Cents is not null || discountState.PromotionIds is not null)
                {
                    throw new SharedHeldOrderCanonicalValidationException("manual-percent 折扣不能携带 cents/promotionIds");
                }

                break;
            case SharedHeldOrderCanonicalConstants.DiscountPromotion:
                if (discountState.Cents is not { } promotionCents
                    || promotionCents is < 0 or > SharedHeldOrderCanonicalConstants.MaxCents)
                {
                    throw new SharedHeldOrderCanonicalValidationException(
                        "promotion 折扣必须带 0 到 " + SharedHeldOrderCanonicalConstants.MaxCents + " cents");
                }

                if (promotionCents > gross)
                {
                    throw new SharedHeldOrderCanonicalValidationException("promotion 折扣不能超过行 gross");
                }

                if (discountState.PromotionIds is null || discountState.PromotionIds.Count == 0)
                {
                    throw new SharedHeldOrderCanonicalValidationException("promotion 折扣必须带 promotionIds");
                }

                var promotionIdSet = new HashSet<string>(StringComparer.Ordinal);
                foreach (var promotionId in discountState.PromotionIds)
                {
                    RequireText(promotionId, "promotionIds 元素");
                    if (promotionId.Length > SharedHeldOrderCanonicalConstants.MaxCodeLength)
                    {
                        throw new SharedHeldOrderCanonicalValidationException(
                            "promotionIds 元素不能超过 " + SharedHeldOrderCanonicalConstants.MaxCodeLength + " 字符");
                    }

                    if (!promotionIdSet.Add(promotionId))
                    {
                        throw new SharedHeldOrderCanonicalValidationException("promotionIds 必须唯一: " + promotionId);
                    }

                    if (!promotionIds.Contains(promotionId))
                    {
                        throw new SharedHeldOrderCanonicalValidationException(
                            "promotionIds 必须引用冻结 promotions: " + promotionId);
                    }
                }

                if (discountState.BasisPoints is not null)
                {
                    throw new SharedHeldOrderCanonicalValidationException("promotion 折扣不能携带 basisPoints");
                }

                break;
            default:
                throw new SharedHeldOrderCanonicalValidationException($"未知折扣类型: {discountState.Mode}");
        }
    }

    private static void RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SharedHeldOrderCanonicalValidationException($"{field} 不能为空");
        }
    }

    private static void RequireIso(string value, string field)
    {
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed)
            || parsed.Offset != TimeSpan.Zero)
        {
            throw new SharedHeldOrderCanonicalValidationException($"{field} 必须是 UTC ISO-8601 时间");
        }
    }
}

public interface ISharedHeldOrderCanonicalSerializer
{
    string Serialize(SharedHeldOrderCanonicalPayload payload);
    SharedHeldOrderCanonicalPayload Deserialize(string json);
}

/// <summary>
/// 精确 JSON 序列化器：camelCase、整数 cents、3 位毫秒 ISO、key 顺序与
/// iPad JSON.stringify 输出一致；反序列化严格拒绝未知字段与错误类型。
/// </summary>
public sealed class SharedHeldOrderCanonicalJsonSerializer : ISharedHeldOrderCanonicalSerializer
{
    public string Serialize(SharedHeldOrderCanonicalPayload payload)
    {
        SharedHeldOrderCanonicalValidator.Validate(payload);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", payload.Version);
            writer.WritePropertyName("pricingState");
            WritePricingState(writer, payload.PricingState);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public SharedHeldOrderCanonicalPayload Deserialize(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var payload = ParsePayload(document.RootElement);
            SharedHeldOrderCanonicalValidator.Validate(payload);
            return payload;
        }
        catch (SharedHeldOrderCanonicalValidationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new SharedHeldOrderCanonicalValidationException("JSON 格式无效", exception);
        }
    }

    private static void WritePricingState(Utf8JsonWriter writer, SharedHeldOrderPricingState pricingState)
    {
        writer.WriteStartObject();
        writer.WriteNumber("revision", pricingState.Revision);
        writer.WriteString("mode", pricingState.Mode);
        writer.WriteString("asOfIso", pricingState.AsOfIso);
        writer.WritePropertyName("promotions");
        writer.WriteStartArray();
        foreach (var promotion in pricingState.Promotions)
        {
            WritePromotion(writer, promotion);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("lines");
        writer.WriteStartArray();
        foreach (var line in pricingState.Lines)
        {
            WriteLine(writer, line);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WritePromotion(Utf8JsonWriter writer, SharedHeldOrderPromotionDefinition promotion)
    {
        writer.WriteStartObject();
        writer.WriteString("id", promotion.Id);
        writer.WriteString("name", promotion.Name);
        writer.WriteString("effectiveStartIso", promotion.EffectiveStartIso);
        writer.WriteString("effectiveEndIso", promotion.EffectiveEndIso);
        writer.WriteBoolean("isExclusive", promotion.IsExclusive);
        writer.WriteNumber("priority", promotion.Priority);
        writer.WriteNumber("applyQuantity", promotion.ApplyQuantity);
        writer.WriteNumber("fixedPriceCents", promotion.FixedPriceCents);
        if (promotion.MaxApplicationsPerOrder is int maxApplications)
        {
            writer.WriteNumber("maxApplicationsPerOrder", maxApplications);
        }
        else
        {
            writer.WriteNull("maxApplicationsPerOrder");
        }

        writer.WritePropertyName("products");
        writer.WriteStartArray();
        foreach (var product in promotion.Products)
        {
            writer.WriteStartObject();
            writer.WriteString("productCode", product.ProductCode);
            writer.WriteNumber("unitWeight", product.UnitWeight);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteLine(Utf8JsonWriter writer, SharedHeldOrderPricingLine line)
    {
        writer.WriteStartObject();
        writer.WriteString("lineId", line.LineId);
        writer.WriteString("productCode", line.ProductCode);
        WriteNullableString(writer, "itemNumber", line.ItemNumber);
        writer.WriteString("lookupCode", line.LookupCode);
        writer.WriteString("displayName", line.DisplayName);
        writer.WriteNumber("quantity", line.Quantity);
        writer.WriteNumber("unitPriceCents", line.UnitPriceCents);
        writer.WriteString("basePriceSource", line.BasePriceSource);
        if (line.SyncProvenance is { } provenance)
        {
            writer.WritePropertyName("syncProvenance");
            writer.WriteStartObject();
            WriteNullableString(writer, "referenceCode", provenance.ReferenceCode);
            writer.WriteNumber("priceSource", provenance.PriceSource);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("syncProvenance");
        }

        writer.WriteString("kind", line.Kind);
        WriteNullableString(writer, "returnSourceKey", line.ReturnSourceKey);
        writer.WriteNull("originalOrderGuid");
        writer.WriteNull("originalOrderDetailGuid");
        writer.WritePropertyName("discountState");
        WriteDiscountState(writer, line.DiscountState);
        writer.WriteEndObject();
    }

    private static void WriteDiscountState(Utf8JsonWriter writer, SharedHeldOrderDiscountState discountState)
    {
        writer.WriteStartObject();
        writer.WriteString("mode", discountState.Mode);
        switch (discountState.Mode)
        {
            case SharedHeldOrderCanonicalConstants.DiscountManualAmount:
                writer.WriteNumber("cents", discountState.Cents!.Value);
                break;
            case SharedHeldOrderCanonicalConstants.DiscountManualPercent:
                writer.WriteNumber("basisPoints", discountState.BasisPoints!.Value);
                break;
            case SharedHeldOrderCanonicalConstants.DiscountPromotion:
                writer.WriteNumber("cents", discountState.Cents!.Value);
                writer.WritePropertyName("promotionIds");
                writer.WriteStartArray();
                foreach (var promotionId in discountState.PromotionIds!)
                {
                    writer.WriteStringValue(promotionId);
                }

                writer.WriteEndArray();
                break;
        }

        writer.WriteEndObject();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static SharedHeldOrderCanonicalPayload ParsePayload(JsonElement root)
    {
        RejectUnknown(root, ["version", "pricingState"]);
        var version = ReadInt32(root, "version", "payload.version");
        var pricingElement = ReadObject(root, "pricingState", "payload.pricingState");
        RejectUnknown(pricingElement, ["revision", "mode", "asOfIso", "promotions", "lines"]);
        var revision = ReadInt32(pricingElement, "revision", "pricingState.revision");
        var mode = ReadString(pricingElement, "mode", "pricingState.mode");
        var asOfIso = ReadString(pricingElement, "asOfIso", "pricingState.asOfIso");
        var promotions = ReadArray(pricingElement, "promotions", "pricingState.promotions")
            .Select(ParsePromotion)
            .ToArray();
        var lines = ReadArray(pricingElement, "lines", "pricingState.lines")
            .Select(ParseLine)
            .ToArray();

        return new SharedHeldOrderCanonicalPayload(
            version,
            new SharedHeldOrderPricingState(revision, mode, asOfIso, promotions, lines));
    }

    private static SharedHeldOrderPromotionDefinition ParsePromotion(JsonElement element)
    {
        RejectUnknown(
            element,
            [
                "id", "name", "effectiveStartIso", "effectiveEndIso", "isExclusive",
                "priority", "applyQuantity", "fixedPriceCents", "maxApplicationsPerOrder", "products"
            ]);
        var id = ReadString(element, "id", "promotion.id");
        var name = ReadString(element, "name", "promotion.name");
        var effectiveStartIso = ReadString(element, "effectiveStartIso", "promotion.effectiveStartIso");
        var effectiveEndIso = ReadString(element, "effectiveEndIso", "promotion.effectiveEndIso");
        var isExclusive = ReadBoolean(element, "isExclusive", "promotion.isExclusive");
        var priority = ReadInt32(element, "priority", "promotion.priority");
        var applyQuantity = ReadInt32(element, "applyQuantity", "promotion.applyQuantity");
        var fixedPriceCents = ReadInt64(element, "fixedPriceCents", "promotion.fixedPriceCents");
        var maxApplicationsElement = ReadProperty(element, "maxApplicationsPerOrder", "promotion.maxApplicationsPerOrder");
        var maxApplications = maxApplicationsElement.ValueKind == JsonValueKind.Null
            ? (int?)null
            : ReadInt32Value(maxApplicationsElement, "promotion.maxApplicationsPerOrder");
        var products = ReadArray(element, "products", "promotion.products")
            .Select(ParsePromotionProduct)
            .ToArray();
        return new SharedHeldOrderPromotionDefinition(
            id,
            name,
            effectiveStartIso,
            effectiveEndIso,
            isExclusive,
            priority,
            applyQuantity,
            fixedPriceCents,
            maxApplications,
            products);
    }

    private static SharedHeldOrderPromotionProduct ParsePromotionProduct(JsonElement element)
    {
        RejectUnknown(element, ["productCode", "unitWeight"]);
        var productCode = ReadString(element, "productCode", "promotion.product.productCode");
        var unitWeight = ReadDecimal(element, "unitWeight", "promotion.product.unitWeight");
        return new SharedHeldOrderPromotionProduct(productCode, unitWeight);
    }

    private static SharedHeldOrderPricingLine ParseLine(JsonElement element)
    {
        RejectUnknown(
            element,
            [
                "lineId", "productCode", "itemNumber", "lookupCode", "displayName", "quantity",
                "unitPriceCents", "basePriceSource", "syncProvenance", "kind", "returnSourceKey",
                "originalOrderGuid", "originalOrderDetailGuid", "discountState"
            ]);
        var lineId = ReadString(element, "lineId", "line.lineId");
        var productCode = ReadString(element, "productCode", "line.productCode");
        var itemNumber = ReadNullableString(element, "itemNumber", "line.itemNumber");
        var lookupCode = ReadString(element, "lookupCode", "line.lookupCode");
        var displayName = ReadString(element, "displayName", "line.displayName");
        var quantity = ReadDecimal(element, "quantity", "line.quantity");
        var unitPriceCents = ReadInt64(element, "unitPriceCents", "line.unitPriceCents");
        var basePriceSource = ReadString(element, "basePriceSource", "line.basePriceSource");
        var syncProvenance = ParseSyncProvenance(element);
        var kind = ReadString(element, "kind", "line.kind");
        var returnSourceKey = ReadNullableString(element, "returnSourceKey", "line.returnSourceKey");
        var originalOrderGuid = ReadNullableGuid(element, "originalOrderGuid", "line.originalOrderGuid");
        var originalOrderDetailGuid = ReadNullableGuid(element, "originalOrderDetailGuid", "line.originalOrderDetailGuid");
        var discountState = ParseDiscountState(ReadObject(element, "discountState", "line.discountState"));
        return new SharedHeldOrderPricingLine(
            lineId,
            productCode,
            itemNumber,
            lookupCode,
            displayName,
            quantity,
            unitPriceCents,
            basePriceSource,
            syncProvenance,
            kind,
            returnSourceKey,
            originalOrderGuid,
            originalOrderDetailGuid,
            discountState);
    }

    private static SharedHeldOrderLineSyncProvenance? ParseSyncProvenance(JsonElement line)
    {
        if (!line.TryGetProperty("syncProvenance", out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new SharedHeldOrderCanonicalValidationException("syncProvenance 必须是对象");
        }

        RejectUnknown(element, ["referenceCode", "priceSource"]);
        var referenceCode = ReadNullableString(element, "referenceCode", "syncProvenance.referenceCode");
        var priceSource = ReadInt32(element, "priceSource", "syncProvenance.priceSource");
        return new SharedHeldOrderLineSyncProvenance(referenceCode, priceSource);
    }

    private static SharedHeldOrderDiscountState ParseDiscountState(JsonElement element)
    {
        var mode = ReadString(element, "mode", "discountState.mode");
        return mode switch
        {
            SharedHeldOrderCanonicalConstants.DiscountNone => ParseNoneDiscount(element),
            SharedHeldOrderCanonicalConstants.DiscountManualAmount => ParseManualAmountDiscount(element),
            SharedHeldOrderCanonicalConstants.DiscountManualPercent => ParseManualPercentDiscount(element),
            SharedHeldOrderCanonicalConstants.DiscountPromotion => ParsePromotionDiscount(element),
            _ => throw new SharedHeldOrderCanonicalValidationException($"未知折扣类型: {mode}")
        };
    }

    private static SharedHeldOrderDiscountState ParseNoneDiscount(JsonElement element)
    {
        RejectUnknown(element, ["mode"]);
        return new SharedHeldOrderDiscountState(SharedHeldOrderCanonicalConstants.DiscountNone);
    }

    private static SharedHeldOrderDiscountState ParseManualAmountDiscount(JsonElement element)
    {
        RejectUnknown(element, ["mode", "cents"]);
        var cents = ReadInt64(element, "cents", "discountState.cents");
        return new SharedHeldOrderDiscountState(SharedHeldOrderCanonicalConstants.DiscountManualAmount, Cents: cents);
    }

    private static SharedHeldOrderDiscountState ParseManualPercentDiscount(JsonElement element)
    {
        RejectUnknown(element, ["mode", "basisPoints"]);
        var basisPoints = ReadInt32(element, "basisPoints", "discountState.basisPoints");
        return new SharedHeldOrderDiscountState(SharedHeldOrderCanonicalConstants.DiscountManualPercent, BasisPoints: basisPoints);
    }

    private static SharedHeldOrderDiscountState ParsePromotionDiscount(JsonElement element)
    {
        RejectUnknown(element, ["mode", "cents", "promotionIds"]);
        var cents = ReadInt64(element, "cents", "discountState.cents");
        var promotionIds = ReadArray(element, "promotionIds", "discountState.promotionIds")
            .Select(promotionId =>
            {
                if (promotionId.ValueKind != JsonValueKind.String)
                {
                    throw new SharedHeldOrderCanonicalValidationException("promotionIds 元素必须是字符串");
                }

                return promotionId.GetString()!;
            })
            .ToArray();
        return new SharedHeldOrderDiscountState(
            SharedHeldOrderCanonicalConstants.DiscountPromotion,
            Cents: cents,
            PromotionIds: promotionIds);
    }

    private static void RejectUnknown(JsonElement element, IReadOnlyCollection<string> allowedKeys)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new SharedHeldOrderCanonicalValidationException("字段必须是对象");
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!allowedKeys.Contains(property.Name))
            {
                throw new SharedHeldOrderCanonicalValidationException($"未知字段必须拒绝: {property.Name}");
            }
        }
    }

    private static JsonElement ReadProperty(JsonElement element, string propertyName, string field)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            throw new SharedHeldOrderCanonicalValidationException($"{field} 必须出现");
        }

        return value;
    }

    private static JsonElement ReadObject(JsonElement element, string propertyName, string field)
    {
        var value = ReadProperty(element, propertyName, field);
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new SharedHeldOrderCanonicalValidationException($"{field} 必须是对象");
        }

        return value;
    }

    private static IEnumerable<JsonElement> ReadArray(JsonElement element, string propertyName, string field)
    {
        var value = ReadProperty(element, propertyName, field);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new SharedHeldOrderCanonicalValidationException($"{field} 必须是数组");
        }

        return value.EnumerateArray();
    }

    private static string ReadString(JsonElement element, string propertyName, string field)
    {
        var value = ReadProperty(element, propertyName, field);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new SharedHeldOrderCanonicalValidationException($"{field} 必须是字符串");
        }

        return value.GetString()!;
    }

    private static string? ReadNullableString(JsonElement element, string propertyName, string field)
    {
        var value = ReadProperty(element, propertyName, field);
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new SharedHeldOrderCanonicalValidationException($"{field} 必须是字符串或 null");
        }

        return value.GetString();
    }

    private static Guid? ReadNullableGuid(JsonElement element, string propertyName, string field)
    {
        var value = ReadProperty(element, propertyName, field);
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String
            || !Guid.TryParse(value.GetString(), out var guid))
        {
            throw new SharedHeldOrderCanonicalValidationException($"{field} 必须是 GUID 字符串或 null");
        }

        return guid;
    }

    private static int ReadInt32(JsonElement element, string propertyName, string field)
    {
        return ReadInt32Value(ReadProperty(element, propertyName, field), field);
    }

    private static long ReadInt64(JsonElement element, string propertyName, string field)
    {
        return ReadInt64Value(ReadProperty(element, propertyName, field), field);
    }

    private static int ReadInt32Value(JsonElement value, string field)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            throw new SharedHeldOrderCanonicalValidationException($"{field} 必须是整数");
        }

        return number;
    }

    private static long ReadInt64Value(JsonElement value, string field)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
        {
            throw new SharedHeldOrderCanonicalValidationException($"{field} 必须是整数");
        }

        return number;
    }

    private static decimal ReadDecimal(JsonElement element, string propertyName, string field)
    {
        var value = ReadProperty(element, propertyName, field);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number))
        {
            throw new SharedHeldOrderCanonicalValidationException($"{field} 必须是 JSON number");
        }

        return number;
    }

    private static bool ReadBoolean(JsonElement element, string propertyName, string field)
    {
        var value = ReadProperty(element, propertyName, field);
        if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
        {
            throw new SharedHeldOrderCanonicalValidationException($"{field} 必须是布尔值");
        }

        return value.GetBoolean();
    }
}
