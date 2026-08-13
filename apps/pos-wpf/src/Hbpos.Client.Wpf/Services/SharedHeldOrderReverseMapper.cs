using System.Globalization;
using System.IO;
using Hbpos.Client.Wpf.Models;
using Hbpos.Contracts.Catalog;

namespace Hbpos.Client.Wpf.Services;

/// <summary>
/// canonical 无效时拒绝恢复（fail-closed）。消息不含 payload 明细。
/// </summary>
public sealed class SharedHeldOrderReverseMappingException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public interface ISharedHeldOrderReverseMapper
{
    /// <summary>
    /// 把冻结的 SharedSaleCartV1 canonical 反向映射为 WPF 普通 sale 快照：
    /// 精确保留 unit price（整数 cents）、catalog/manual provenance、manual 金额/百分比、
    /// promotion 折扣金额与来源；只允许 kind=sale；数量允许正有限小数。
    /// </summary>
    PosCartSnapshot Map(SharedHeldOrderCanonicalPayload payload, string storeCode);
}

public sealed class SharedHeldOrderReverseMapper : ISharedHeldOrderReverseMapper
{
    public PosCartSnapshot Map(SharedHeldOrderCanonicalPayload payload, string storeCode)
    {
        try
        {
            SharedHeldOrderCanonicalValidator.Validate(payload);
        }
        catch (SharedHeldOrderCanonicalValidationException exception)
        {
            throw new SharedHeldOrderReverseMappingException(
                "共享挂单 canonical 校验失败，拒绝恢复购物车。",
                exception);
        }

        var lines = new List<PosCartLineSnapshot>(payload.PricingState.Lines.Count);
        foreach (var line in payload.PricingState.Lines)
        {
            lines.Add(ToSnapshotLine(line, storeCode));
        }

        return new PosCartSnapshot(lines);
    }

    private static PosCartLineSnapshot ToSnapshotLine(
        SharedHeldOrderPricingLine line,
        string storeCode)
    {
        // canonical validator 已保证 kind=sale、return 字段为 null、金额/数量有界；
        // 这里仍做防御性检查，防止未来校验变更后静默恢复非 sale 行。
        if (!string.Equals(line.Kind, SharedHeldOrderCanonicalConstants.LineKindSale, StringComparison.Ordinal))
        {
            throw new SharedHeldOrderReverseMappingException(
                "共享挂单只允许 kind=sale，拒绝恢复 return/open-item。");
        }

        if (line.Quantity <= 0m
            || line.Quantity > SharedHeldOrderCanonicalConstants.MaxQuantity)
        {
            throw new SharedHeldOrderReverseMappingException(
                "共享挂单行数量必须是正有限数。");
        }

        if (line.UnitPriceCents < 0)
        {
            throw new SharedHeldOrderReverseMappingException(
                "共享挂单行单价不能为负。");
        }

        var priceSource = ResolvePriceSource(line, out var priceSourceLabel);
        var (discountAmount, discountPercent, discountSource) = ToDiscount(line);
        return new PosCartLineSnapshot(
            storeCode,
            line.ProductCode,
            line.SyncProvenance?.ReferenceCode,
            line.DisplayName,
            line.LookupCode,
            line.ItemNumber,
            ProductImage: null,
            line.Quantity,
            CentsToMoney(line.UnitPriceCents),
            discountAmount,
            discountPercent,
            priceSource,
            priceSourceLabel,
            Kind: CartLineKind.Sale,
            ReturnSourceKey: string.Empty,
            OriginalOrderGuid: null,
            OriginalOrderLineGuid: null,
            ReturnReason: null,
            DiscountSource: discountSource,
            IsManualPrice: string.Equals(
                line.BasePriceSource,
                SharedHeldOrderCanonicalConstants.BasePriceSourceManual,
                StringComparison.Ordinal),
            CatalogDiscountBasisPoints: line.CatalogDiscountBasisPoints);
    }

    /// <summary>
    /// provenance 携带源设备 PriceSourceKind 时原样保留（catalog/manual provenance 事实）；
    /// 缺失时按 manual 标记稳定 fallback 标签，金额 cents 仍精确。
    /// </summary>
    private static PriceSourceKind ResolvePriceSource(
        SharedHeldOrderPricingLine line,
        out string label)
    {
        if (line.SyncProvenance is { } provenance
            && provenance.PriceSource is >= 0 and <= 4)
        {
            var kind = (PriceSourceKind)provenance.PriceSource;
            label = PriceSourceLabel(kind);
            return kind;
        }

        label = string.Equals(
            line.BasePriceSource,
            SharedHeldOrderCanonicalConstants.BasePriceSourceManual,
            StringComparison.Ordinal)
            ? "Manual Price"
            : PriceSourceLabel(PriceSourceKind.ProductBase);
        return PriceSourceKind.ProductBase;
    }

    private static (decimal Amount, decimal? Percent, PosCartLineDiscountSource Source) ToDiscount(
        SharedHeldOrderPricingLine line)
    {
        var gross = decimal.Round(
            line.Quantity * CentsToMoney(line.UnitPriceCents),
            2,
            MidpointRounding.AwayFromZero);
        if (line.DiscountState.Mode == SharedHeldOrderCanonicalConstants.DiscountNone
            && line.CatalogDiscountBasisPoints > 0)
        {
            var catalogDiscount = decimal.Round(
                gross * line.CatalogDiscountBasisPoints / 10_000m,
                2,
                MidpointRounding.AwayFromZero);
            return (
                Math.Clamp(catalogDiscount, 0m, gross),
                line.CatalogDiscountBasisPoints / 100m,
                PosCartLineDiscountSource.Catalog);
        }

        return line.DiscountState.Mode switch
        {
            SharedHeldOrderCanonicalConstants.DiscountNone =>
                (0m, null, PosCartLineDiscountSource.None),
            SharedHeldOrderCanonicalConstants.DiscountManualAmount =>
                (CentsToMoney(line.DiscountState.Cents!.Value), null, PosCartLineDiscountSource.Manual),
            SharedHeldOrderCanonicalConstants.DiscountManualPercent =>
                (
                    decimal.Round(
                        gross * line.DiscountState.BasisPoints!.Value / 10_000m,
                        2,
                        MidpointRounding.AwayFromZero),
                    line.DiscountState.BasisPoints.Value / 100m,
                    PosCartLineDiscountSource.Manual),
            SharedHeldOrderCanonicalConstants.DiscountPromotion =>
                (CentsToMoney(line.DiscountState.Cents!.Value), null, PosCartLineDiscountSource.Promotion),
            _ => throw new SharedHeldOrderReverseMappingException(
                $"共享挂单折扣类型无效: {line.DiscountState.Mode}")
        };
    }

    /// <summary>整数 cents -> 元：除以 100 精确，再按 AwayFromZero 规整两位小数。</summary>
    private static decimal CentsToMoney(long cents)
    {
        return decimal.Round(cents / 100m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>稳定的展示标签（WPF canonical 不含 label；label 仅展示用，不影响金额）。</summary>
    private static string PriceSourceLabel(PriceSourceKind kind)
    {
        return kind switch
        {
            PriceSourceKind.ProductBase => "Product Base",
            PriceSourceKind.StoreRetailPrice => "Store Retail Price",
            PriceSourceKind.ProductSetCode => "Product Set Code",
            PriceSourceKind.StoreMultiCodeProduct => "Store Multi-Code Product",
            PriceSourceKind.StoreClearancePrice => "Store Clearance Price",
            _ => kind.ToString()
        };
    }
}
