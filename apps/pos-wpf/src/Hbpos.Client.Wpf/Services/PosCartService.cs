using Hbpos.Client.Wpf.Models;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Wpf.Services;

public sealed class PosCartService
{
    private readonly List<CartLine> _lines = [];
    private readonly List<OrderReturnPaymentCapacityDto> _returnPaymentCapacities = [];
    private IReadOnlyList<CatalogPromotionRuleDto> _automaticPromotionRules = [];
    private Guid? _sharedHeldOrderClaimId;
    private bool _preserveSharedPromotionDiscounts;
    private bool _preserveSharedSnapshotCatalogValues;

    public IReadOnlyList<CartLine> Lines => _lines;

    public IReadOnlyList<OrderReturnPaymentCapacityDto> ReturnPaymentCapacities => _returnPaymentCapacities;

    public decimal TotalAmount => decimal.Round(_lines.Sum(line => line.GrossAmount), 2, MidpointRounding.AwayFromZero);

    public decimal DiscountAmount => decimal.Round(_lines.Sum(line => line.DiscountAmount), 2, MidpointRounding.AwayFromZero);

    public decimal ActualAmount => decimal.Round(_lines.Sum(line => line.ActualAmount), 2, MidpointRounding.AwayFromZero);

    public bool IsEmpty => _lines.Count == 0;

    public bool HasZeroPriceLine => _lines.Any(line => line.HasZeroUnitPrice);

    public bool HasNonIntegerQuantity => _lines.Any(line =>
        _sharedHeldOrderClaimId is null
            ? !IsPositiveIntegerQuantity(line.Quantity)
            : !IsPositiveFiniteDecimalQuantity(line.Quantity));

    public bool HasReturnLine => _lines.Any(line => line.IsReturnLine);

    public event EventHandler? CartChanged;

    public void SetAutomaticPromotionRules(IEnumerable<CatalogPromotionRuleDto> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _automaticPromotionRules = rules
            .Where(rule => rule.Products.Count > 0)
            .ToArray();
        if (!_preserveSharedPromotionDiscounts && !_preserveSharedSnapshotCatalogValues)
        {
            RefreshAutomaticPromotionDiscounts();
        }

        OnCartChanged();
    }

    public CartLine AddItem(SellableItemDto item)
    {
        if (!IsPositiveIntegerQuantity(item.QuantityFactor))
        {
            throw new InvalidOperationException("Cart item quantity must be a positive integer.");
        }

        var existing = FindLineByLookupCode(item.StoreCode, item.LookupCode);

        if (existing is not null)
        {
            if (!IsPositiveIntegerQuantity(existing.Quantity))
            {
                throw new InvalidOperationException("Cart line quantity must be a positive integer.");
            }

            existing.Increase(item.QuantityFactor);
            RefreshDiscountsAndNotify();
            return existing;
        }

        var line = new CartLine(item);
        _lines.Add(line);
        RefreshDiscountsAndNotify();
        return line;
    }

    public CartLine AddConsecutiveItem(SellableItemDto item)
    {
        if (!IsPositiveIntegerQuantity(item.QuantityFactor))
        {
            throw new InvalidOperationException("Cart item quantity must be a positive integer.");
        }

        // 扫码自动加购只看最后一行，避免跨行回溯导致非连续重复商品被合并。
        var lastLine = _lines.LastOrDefault();
        var normalizedLookupCode = CartLine.NormalizeLookupCode(item.LookupCode);
        if (lastLine is not null &&
            !lastLine.IsReturnLine &&
            !lastLine.IsOpenItem &&
            string.Equals(lastLine.StoreCode, item.StoreCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(lastLine.LookupCodeNormalized, normalizedLookupCode, StringComparison.Ordinal))
        {
            if (!IsPositiveIntegerQuantity(lastLine.Quantity))
            {
                throw new InvalidOperationException("Cart line quantity must be a positive integer.");
            }

            lastLine.Increase(item.QuantityFactor);
            RefreshDiscountsAndNotify();
            return lastLine;
        }

        // 最后一行不匹配时必须新建购物车行，保留扫码顺序。
        var line = new CartLine(item);
        _lines.Add(line);
        RefreshDiscountsAndNotify();
        return line;
    }

    public CartLine AddOpenItem(SellableItemDto item, decimal unitPrice)
    {
        if (!IsPositiveIntegerQuantity(item.QuantityFactor))
        {
            throw new InvalidOperationException("Cart item quantity must be a positive integer.");
        }

        if (unitPrice < 0m)
        {
            throw new InvalidOperationException("Open item price must be zero or greater.");
        }

        var line = new CartLine(item, CartLineKind.OpenItem, unitPrice);
        _lines.Add(line);
        RefreshDiscountsAndNotify();
        return line;
    }

    public CartLine AddReturnLine(ReturnCartLineRequest request)
    {
        if (!IsPositiveIntegerQuantity(request.Quantity))
        {
            throw new InvalidOperationException("Return cart line quantity must be a positive integer.");
        }

        var existing = FindReturnLineBySourceKey(request.ReturnSourceKey);
        if (existing is not null)
        {
            existing.IncreaseReturnQuantity(request.Quantity);
            RefreshDiscountsAndNotify();
            return existing;
        }

        var line = new CartLine(request);
        _lines.Add(line);
        RefreshDiscountsAndNotify();
        return line;
    }

    public void AddReturnPaymentCapacities(IEnumerable<OrderReturnPaymentCapacityDto> capacities)
    {
        var capacityList = capacities
            .Where(capacity => capacity.RemainingAmount > 0m)
            .ToList();
        if (capacityList.Count == 0)
        {
            return;
        }

        var changed = false;
        foreach (var capacity in capacityList)
        {
            var existingIndex = _returnPaymentCapacities.FindIndex(existing =>
                existing.Method == capacity.Method &&
                string.Equals(existing.Reference ?? string.Empty, capacity.Reference ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                existing.OriginalOrderGuid == capacity.OriginalOrderGuid);
            if (existingIndex >= 0)
            {
                _returnPaymentCapacities[existingIndex] = capacity;
            }
            else
            {
                _returnPaymentCapacities.Add(capacity);
            }

            changed = true;
        }

        if (changed)
        {
            OnCartChanged();
        }
    }

    public CartLine? FindLineByLookupCode(string storeCode, string lookupCode)
    {
        var normalizedLookupCode = CartLine.NormalizeLookupCode(lookupCode);
        return _lines.FirstOrDefault(line =>
            !line.IsReturnLine &&
            !line.IsOpenItem &&
            string.Equals(line.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase) &&
            line.LookupCodeNormalized == normalizedLookupCode);
    }

    public CartLine? FindReturnLineBySourceKey(string returnSourceKey)
    {
        return _lines.FirstOrDefault(line =>
            line.IsReturnLine &&
            string.Equals(line.ReturnSourceKey, returnSourceKey, StringComparison.OrdinalIgnoreCase));
    }

    public bool UpdateLineFromRemote(SellableItemDto item)
    {
        return UpdateLineFromRemote(item.StoreCode, item.LookupCode, item);
    }

    public bool UpdateLineFromRemote(string storeCode, string lookupCode, SellableItemDto item)
    {
        var line = FindLineByLookupCode(storeCode, lookupCode);

        if (line is null || _preserveSharedSnapshotCatalogValues)
        {
            return false;
        }

        line.UpdateFrom(item);
        RefreshDiscountsAndNotify(preserveFrozenSharedPromotion: true);
        return true;
    }

    public bool UpdateLineFromRemote(CartLine line, SellableItemDto item)
    {
        if (!_lines.Contains(line) || _preserveSharedSnapshotCatalogValues)
        {
            return false;
        }

        line.UpdateFrom(item);
        RefreshDiscountsAndNotify(preserveFrozenSharedPromotion: true);
        return true;
    }

    public bool RemoveLineByLookupCode(string storeCode, string lookupCode)
    {
        var line = FindLineByLookupCode(storeCode, lookupCode);

        if (line is null)
        {
            return false;
        }

        // 绑定共享 claim 的购物车不能通过同步编辑清空：保持行+binding 并阻止操作，
        // 用户必须走已有异步 ClearCart owner-release，否则会遗留服务端 Active。
        if (_sharedHeldOrderClaimId is not null && _lines.Count == 1)
        {
            return false;
        }

        _lines.Remove(line);
        ClearSharedHeldOrderBindingIfCartEmpty();
        RefreshDiscountsAndNotify();
        return true;
    }

    public bool RemoveLine(CartLine line)
    {
        if (!_lines.Contains(line))
        {
            return false;
        }

        // 绑定共享 claim 的购物车不能通过同步编辑清空：保持行+binding 并阻止操作。
        if (_sharedHeldOrderClaimId is not null && _lines.Count == 1)
        {
            return false;
        }

        _lines.Remove(line);
        if (line.IsReturnLine && !_lines.Any(existing => existing.IsReturnLine))
        {
            _returnPaymentCapacities.Clear();
        }

        ClearSharedHeldOrderBindingIfCartEmpty();
        RefreshDiscountsAndNotify();
        return true;
    }

    public bool IncreaseLine(CartLine? line)
    {
        if (line is null || line.IsLocked || !_lines.Contains(line) || !IsPositiveIntegerQuantity(line.Quantity))
        {
            return false;
        }

        line.Increase(1m);
        RefreshDiscountsAndNotify();
        return true;
    }

    public bool DecreaseLine(CartLine? line)
    {
        if (line is null || line.IsLocked || !_lines.Contains(line) || !IsPositiveIntegerQuantity(line.Quantity))
        {
            return false;
        }

        if (!line.Decrease(1m))
        {
            // 绑定共享 claim 的购物车不能通过同步编辑清空：保持行+binding 并阻止操作。
            if (_sharedHeldOrderClaimId is not null && _lines.Count == 1)
            {
                return false;
            }

            _lines.Remove(line);
            if (line.IsReturnLine && !_lines.Any(existing => existing.IsReturnLine))
            {
                _returnPaymentCapacities.Clear();
            }

            ClearSharedHeldOrderBindingIfCartEmpty();
        }

        RefreshDiscountsAndNotify();
        return true;
    }

    public bool SetLineQuantity(CartLine? line, decimal quantity)
    {
        // 数量必须是正有限整数，0/负数/小数一律拒绝：绑定共享 claim 的购物车
        // 永远不会通过 SetLineQuantity 被静默清空并遗留服务端 Active。
        if (line is null || line.IsLocked || !_lines.Contains(line) || !IsPositiveIntegerQuantity(quantity))
        {
            return false;
        }

        line.SetQuantity(quantity);
        RefreshDiscountsAndNotify();
        return true;
    }

    public bool SetLineUnitPrice(CartLine? line, decimal unitPrice)
    {
        if (line is null || line.IsLocked || !_lines.Contains(line) || unitPrice < 0m)
        {
            return false;
        }

        line.SetUnitPrice(unitPrice);
        // 手工改价：base price provenance 标记为 manual，快照/挂单持久化保留。
        line.SetManualPrice(true);
        RefreshDiscountsAndNotify();
        return true;
    }

    public bool SetLineDiscountAmount(CartLine? line, decimal discountAmount)
    {
        if (line is null || line.IsLocked || !_lines.Contains(line) || discountAmount < 0m || discountAmount > line.GrossAmount)
        {
            return false;
        }

        line.SetDiscountAmount(discountAmount);
        RefreshDiscountsAndNotify();
        return true;
    }

    public bool SetLineDiscountPercent(CartLine? line, decimal discountPercent)
    {
        if (line is null || line.IsLocked || !_lines.Contains(line) || discountPercent < 0m || discountPercent > 100m)
        {
            return false;
        }

        line.SetDiscountPercent(discountPercent);
        RefreshDiscountsAndNotify();
        return true;
    }

    internal void ApplyPromotionDiscounts(IEnumerable<PromotionLineDiscount> discounts)
    {
        var incomingDiscounts = discounts
            .GroupBy(discount => discount.Line)
            .ToDictionary(group => group.Key, group => group.Last().DiscountAmount);
        var changed = false;

        foreach (var line in _lines)
        {
            if (line.DiscountSource != CartLineDiscountSource.Promotion)
            {
                continue;
            }

            line.ClearPromotionDiscount();
            changed = true;
        }

        foreach (var entry in incomingDiscounts)
        {
            var line = entry.Key;
            if (!_lines.Contains(line) ||
                line.IsReturnLine ||
                line.IsOpenItem ||
                !IsPositiveIntegerQuantity(line.Quantity) ||
                line.GrossAmount <= 0m)
            {
                continue;
            }

            // 手工折扣必须优先保留，自动促销只能写入没有手工折扣来源的购物车行。
            if (line.DiscountSource == CartLineDiscountSource.Manual)
            {
                continue;
            }

            var clampedDiscount = Math.Clamp(
                decimal.Round(entry.Value, 2, MidpointRounding.AwayFromZero),
                0m,
                line.GrossAmount);
            if (clampedDiscount <= 0m)
            {
                continue;
            }

            line.SetPromotionDiscountAmount(clampedDiscount);
            changed = true;
        }

        if (changed)
        {
            OnCartChanged();
        }
    }

    public bool SetOrderDiscountAmount(decimal discountAmount)
    {
        if (_lines.Count == 0 || HasReturnLine || discountAmount < 0m || discountAmount > TotalAmount)
        {
            return false;
        }

        ApplyOrderDiscountAmount(discountAmount);
        RefreshDiscountsAndNotify();
        return true;
    }

    public bool SetOrderDiscountPercent(decimal discountPercent)
    {
        if (_lines.Count == 0 || HasReturnLine || discountPercent < 0m || discountPercent > 100m)
        {
            return false;
        }

        var discountAmount = decimal.Round(TotalAmount * discountPercent / 100m, 2, MidpointRounding.AwayFromZero);
        ApplyOrderDiscountAmount(discountAmount);
        RefreshDiscountsAndNotify();
        return true;
    }

    public void Clear()
    {
        if (_lines.Count == 0)
        {
            if (_returnPaymentCapacities.Count > 0 || _sharedHeldOrderClaimId is not null)
            {
                _returnPaymentCapacities.Clear();
                _sharedHeldOrderClaimId = null;
                _preserveSharedPromotionDiscounts = false;
                _preserveSharedSnapshotCatalogValues = false;
                OnCartChanged();
            }

            return;
        }

        _lines.Clear();
        _returnPaymentCapacities.Clear();
        _sharedHeldOrderClaimId = null;
        _preserveSharedPromotionDiscounts = false;
        _preserveSharedSnapshotCatalogValues = false;
        OnCartChanged();
    }

    public PosCartSnapshot CreateSnapshot()
    {
        return new PosCartSnapshot(_lines
            .Select(line => new PosCartLineSnapshot(
                line.StoreCode,
                line.ProductCode,
                line.ReferenceCode,
                line.DisplayName,
                line.LookupCode,
                line.ItemNumber,
                line.ProductImage,
                line.Quantity,
                line.UnitPrice,
                line.DiscountAmount,
                line.DiscountPercent,
                line.PriceSource,
                line.PriceSourceLabel,
                line.Kind,
                line.ReturnSourceKey,
                line.OriginalOrderGuid,
                line.OriginalOrderLineGuid,
                line.ReturnReason,
                MapSnapshotDiscountSource(line.DiscountSource),
                line.IsManualPrice))
            .ToArray(),
            _sharedHeldOrderClaimId);
    }

    public void RestoreSnapshot(PosCartSnapshot snapshot)
    {
        _lines.Clear();
        _returnPaymentCapacities.Clear();
        _sharedHeldOrderClaimId = null;
        _preserveSharedPromotionDiscounts = false;
        _preserveSharedSnapshotCatalogValues = false;
        foreach (var snapshotLine in snapshot.Lines)
        {
            if (!IsPositiveIntegerQuantity(snapshotLine.Quantity))
            {
                throw new InvalidOperationException("Cart line quantity must be a positive integer.");
            }

            CartLine line;
            if (snapshotLine.Kind == CartLineKind.Return)
            {
                line = new CartLine(new ReturnCartLineRequest(
                    snapshotLine.StoreCode,
                    snapshotLine.ProductCode,
                    snapshotLine.ReferenceCode,
                    snapshotLine.DisplayName,
                    snapshotLine.LookupCode,
                    snapshotLine.ItemNumber,
                    snapshotLine.ProductImage,
                    snapshotLine.Quantity,
                    snapshotLine.UnitPrice,
                    snapshotLine.PriceSource,
                    snapshotLine.PriceSourceLabel,
                    snapshotLine.ReturnSourceKey,
                    snapshotLine.OriginalOrderGuid,
                    snapshotLine.OriginalOrderLineGuid,
                    snapshotLine.ReturnReason));
            }
            else if (snapshotLine.Kind == CartLineKind.OpenItem)
            {
                var item = CreateSnapshotItem(snapshotLine);
                line = new CartLine(item, CartLineKind.OpenItem, snapshotLine.UnitPrice);
                line.SetQuantity(snapshotLine.Quantity);
                // 恢复快照时按来源还原折扣，避免促销折扣被误当手工折扣。
                RestoreSnapshotDiscount(line, snapshotLine);
            }
            else
            {
                var item = CreateSnapshotItem(snapshotLine);
                line = new CartLine(item);
                line.SetQuantity(snapshotLine.Quantity);
                line.SetUnitPrice(snapshotLine.UnitPrice);
                // 恢复快照时按来源还原折扣，避免促销折扣被误当手工折扣。
                RestoreSnapshotDiscount(line, snapshotLine);
            }

            // 恢复快照时按快照还原价格 provenance（手工改价/目录价格）。
            line.SetManualPrice(snapshotLine.IsManualPrice);
            _lines.Add(line);
        }

        _sharedHeldOrderClaimId = snapshot.SharedHeldOrderClaimId;
        // 快照用于挂单和支付恢复，必须保留当时金额；后续编辑或规则刷新再重新计算满减。
        OnCartChanged();
    }

    /// <summary>
    /// 共享 sale 快照恢复专用入口（跨 iPad→WPF 取单/离线 recall）：
    /// 只允许普通 sale 行与正有限小数数量；金额与折扣来源按快照精确恢复。
    /// 普通 RestoreSnapshot、Add/SetQuantity 与 UI 数量编辑仍严格正整数。
    /// </summary>
    public void RestoreSharedSaleSnapshot(PosCartSnapshot snapshot)
    {
        _lines.Clear();
        _returnPaymentCapacities.Clear();
        _sharedHeldOrderClaimId = null;
        _preserveSharedPromotionDiscounts = false;
        _preserveSharedSnapshotCatalogValues = false;
        foreach (var snapshotLine in snapshot.Lines)
        {
            if (snapshotLine.Kind != CartLineKind.Sale)
            {
                throw new InvalidOperationException("Shared sale restore only supports sale lines.");
            }

            if (!IsPositiveFiniteDecimalQuantity(snapshotLine.Quantity))
            {
                throw new InvalidOperationException("Shared sale quantity must be a positive finite number.");
            }

            var item = CreateSnapshotItem(snapshotLine);
            var line = new CartLine(item);
            line.SetSharedSaleQuantity(snapshotLine.Quantity);
            line.SetUnitPrice(snapshotLine.UnitPrice);
            // 恢复快照时按来源还原折扣，避免促销折扣被误当手工折扣。
            RestoreSnapshotDiscount(line, snapshotLine);
            line.SetManualPrice(snapshotLine.IsManualPrice);
            _lines.Add(line);
        }

        _sharedHeldOrderClaimId = snapshot.SharedHeldOrderClaimId;
        _preserveSharedPromotionDiscounts = _lines.Any(line => line.IsAutomaticPromotionDiscount);
        _preserveSharedSnapshotCatalogValues = _lines.Count > 0;
        OnCartChanged();
    }

    private static SellableItemDto CreateSnapshotItem(PosCartLineSnapshot snapshotLine)
    {
        return new SellableItemDto(
            snapshotLine.StoreCode,
            snapshotLine.ProductCode,
            snapshotLine.ReferenceCode,
            snapshotLine.DisplayName,
            snapshotLine.LookupCode,
            snapshotLine.ItemNumber,
            snapshotLine.LookupCode,
            snapshotLine.UnitPrice,
            snapshotLine.PriceSource,
            snapshotLine.PriceSourceLabel,
            1m,
            null,
            snapshotLine.ProductImage);
    }

    private void OnCartChanged()
    {
        CartChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearSharedHeldOrderBindingIfCartEmpty()
    {
        if (_lines.Count == 0)
        {
            _sharedHeldOrderClaimId = null;
            _preserveSharedPromotionDiscounts = false;
            _preserveSharedSnapshotCatalogValues = false;
        }
    }

    /// <summary>
    /// 主管强制释放后的本地 cart 清理：仅当购物车绑定到指定 claim（Active 取单购物车）
    /// 时整单清空并解绑；其他 claim 或普通购物车一律不动，Prepared 未恢复购物车时绝不误清。
    /// </summary>
    public bool ClearSharedHeldOrderClaim(Guid claimId)
    {
        if (_sharedHeldOrderClaimId != claimId)
        {
            return false;
        }

        Clear();
        return true;
    }

    public static bool IsPositiveIntegerQuantity(decimal quantity)
    {
        return quantity > 0m && decimal.Truncate(quantity) == quantity;
    }

    public static bool IsPositiveFiniteDecimalQuantity(decimal quantity)
    {
        return quantity > 0m
            && quantity <= SharedHeldOrderCanonicalConstants.MaxQuantity;
    }

    /// <summary>
    /// 挂单时深拷贝当前自动促销规则：后续目录刷新/重启不得影响已冻结金额。
    /// 无规则时返回 null（旧挂单/无促销挂单语义）。
    /// </summary>
    public IReadOnlyList<CatalogPromotionRuleDto>? CreateFrozenAutomaticPromotionRules()
    {
        if (_automaticPromotionRules.Count == 0)
        {
            return null;
        }

        return _automaticPromotionRules
            .Select(rule => rule with
            {
                Products = rule.Products
                    .Select(product => product with { })
                    .ToArray()
            })
            .ToArray();
    }

    private static void RestoreSnapshotDiscount(CartLine line, PosCartLineSnapshot snapshotLine)
    {
        // 恢复快照时必须保留折扣来源，后续促销重算才不会把旧促销当成手工折扣。
        if (snapshotLine.DiscountPercent is decimal discountPercent && discountPercent > 0m)
        {
            line.SetDiscountPercent(discountPercent);
            return;
        }

        if (snapshotLine.DiscountAmount <= 0m)
        {
            return;
        }

        if (snapshotLine.DiscountSource == PosCartLineDiscountSource.Promotion)
        {
            line.SetPromotionDiscountAmount(snapshotLine.DiscountAmount);
            return;
        }

        line.SetDiscountAmount(snapshotLine.DiscountAmount);
    }

    private static PosCartLineDiscountSource MapSnapshotDiscountSource(CartLineDiscountSource source)
    {
        return source switch
        {
            CartLineDiscountSource.Manual => PosCartLineDiscountSource.Manual,
            CartLineDiscountSource.Promotion => PosCartLineDiscountSource.Promotion,
            _ => PosCartLineDiscountSource.None
        };
    }

    private void RefreshDiscountsAndNotify(bool preserveFrozenSharedPromotion = false)
    {
        if (!preserveFrozenSharedPromotion)
        {
            // 任一成功的购物车编辑都会结束共享快照保护；其后的远端目录刷新可按正常路径更新。
            _preserveSharedSnapshotCatalogValues = false;
        }

        var releasedFrozenSharedPromotion =
            !preserveFrozenSharedPromotion && _preserveSharedPromotionDiscounts;
        if (releasedFrozenSharedPromotion)
        {
            // 冻结促销只保护刚恢复的共享快照不受当前目录刷新影响；任何成功的
            // 用户编辑都会使原计算失效，必须切回当前规则（无规则时清掉旧促销）。
            _preserveSharedPromotionDiscounts = false;
        }

        if (releasedFrozenSharedPromotion
            || (_automaticPromotionRules.Count > 0 && !_preserveSharedPromotionDiscounts))
        {
            RefreshAutomaticPromotionDiscounts();
        }

        OnCartChanged();
    }

    private void RefreshAutomaticPromotionDiscounts()
    {
        foreach (var line in _lines)
        {
            line.ClearAutomaticPromotionDiscount();
        }

        if (_automaticPromotionRules.Count == 0 || _lines.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var candidates = _automaticPromotionRules
            .Where(rule =>
                rule.ApplyQuantity > 0 &&
                rule.FixedPrice >= 0m &&
                rule.EffectiveStart <= now &&
                rule.EffectiveEnd >= now &&
                rule.Products.Count > 0)
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        // 后端促销评估：存在互斥促销时，只取优先级最高的一条，保持收银端和后台一致。
        if (candidates.Any(rule => rule.IsExclusive))
        {
            candidates = candidates
                .Where(rule => rule.IsExclusive)
                .OrderByDescending(rule => rule.Priority)
                .Take(1)
                .ToList();
        }

        var automaticDiscounts = new Dictionary<CartLine, decimal>();
        foreach (var rule in candidates)
        {
            ApplyAutomaticPromotionRule(rule, automaticDiscounts);
        }

        foreach (var (line, discountAmount) in automaticDiscounts)
        {
            var amount = Math.Clamp(
                decimal.Round(discountAmount, 2, MidpointRounding.AwayFromZero),
                0m,
                line.GrossAmount);
            if (amount > 0m)
            {
                line.SetAutomaticPromotionDiscountAmount(amount);
            }
        }
    }

    private void ApplyAutomaticPromotionRule(
        CatalogPromotionRuleDto rule,
        Dictionary<CartLine, decimal> automaticDiscounts)
    {
        var productWeights = rule.Products
            .GroupBy(product => NormalizeProductCode(product.ProductCode), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(
                group => group.Key,
                group => Math.Max(1, group.Last().UnitWeight),
                StringComparer.OrdinalIgnoreCase);
        if (productWeights.Count == 0)
        {
            return;
        }

        var cartUnits = new List<PromotionCartUnit>();
        foreach (var line in _lines)
        {
            if (!IsAutomaticPromotionLineEligible(line) ||
                !productWeights.TryGetValue(NormalizeProductCode(line.ProductCode), out var unitWeight))
            {
                continue;
            }

            var quantity = (int)line.Quantity;
            for (var i = 0; i < quantity * unitWeight; i++)
            {
                cartUnits.Add(new PromotionCartUnit(line, line.UnitPrice));
            }
        }

        var bundles = cartUnits.Count / rule.ApplyQuantity;
        if (rule.MaxApplicationsPerOrder is int maxApplications)
        {
            bundles = Math.Min(bundles, maxApplications);
        }

        if (bundles <= 0)
        {
            return;
        }

        var remainingUnits = bundles * rule.ApplyQuantity;
        var index = 0;
        while (remainingUnits > 0 && index < cartUnits.Count)
        {
            var take = Math.Min(remainingUnits, rule.ApplyQuantity);
            var group = cartUnits.Skip(index).Take(take).ToArray();
            ApplyPromotionBundle(rule, group, automaticDiscounts);
            index += take;
            remainingUnits -= take;
        }
    }

    private static void ApplyPromotionBundle(
        CatalogPromotionRuleDto rule,
        IReadOnlyList<PromotionCartUnit> group,
        Dictionary<CartLine, decimal> automaticDiscounts)
    {
        var grossAmount = group.Sum(unit => unit.UnitPrice);
        var bundleDiscount = decimal.Round(grossAmount - rule.FixedPrice, 2, MidpointRounding.AwayFromZero);
        if (bundleDiscount <= 0m)
        {
            return;
        }

        var remainingDiscount = bundleDiscount;
        for (var i = 0; i < group.Count; i++)
        {
            var unit = group[i];
            var unitDiscount = i == group.Count - 1
                ? remainingDiscount
                : decimal.Round(bundleDiscount * unit.UnitPrice / grossAmount, 2, MidpointRounding.AwayFromZero);
            unitDiscount = Math.Clamp(unitDiscount, 0m, remainingDiscount);
            if (unitDiscount > 0m)
            {
                automaticDiscounts[unit.Line] = automaticDiscounts.GetValueOrDefault(unit.Line) + unitDiscount;
                remainingDiscount -= unitDiscount;
            }
        }
    }

    private static bool IsAutomaticPromotionLineEligible(CartLine line)
    {
        return
            !line.IsReturnLine &&
            !line.IsOpenItem &&
            !line.HasManualDiscount &&
            line.GrossAmount > 0m &&
            IsPositiveIntegerQuantity(line.Quantity);
    }

    private static string NormalizeProductCode(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private void ApplyOrderDiscountAmount(decimal discountAmount)
    {
        var totalGrossAmount = TotalAmount;
        var remainingDiscount = Math.Clamp(
            decimal.Round(discountAmount, 2, MidpointRounding.AwayFromZero),
            0m,
            totalGrossAmount);
        var discountableLines = _lines.Where(line => line.GrossAmount > 0m).ToList();

        if (discountableLines.Count == 0)
        {
            return;
        }

        for (var i = 0; i < discountableLines.Count; i++)
        {
            var line = discountableLines[i];
            var lineDiscount = i == discountableLines.Count - 1
                ? remainingDiscount
                : decimal.Round(discountAmount * line.GrossAmount / totalGrossAmount, 2, MidpointRounding.AwayFromZero);

            lineDiscount = Math.Clamp(lineDiscount, 0m, Math.Min(line.GrossAmount, remainingDiscount));
            line.SetDiscountAmount(lineDiscount);
            remainingDiscount -= lineDiscount;
        }
    }

    private sealed record PromotionCartUnit(CartLine Line, decimal UnitPrice);
}

public sealed record PosCartSnapshot(
    IReadOnlyList<PosCartLineSnapshot> Lines,
    Guid? SharedHeldOrderClaimId = null);

public sealed record PosCartLineSnapshot(
    string StoreCode,
    string ProductCode,
    string? ReferenceCode,
    string DisplayName,
    string LookupCode,
    string? ItemNumber,
    string? ProductImage,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal? DiscountPercent,
    PriceSourceKind PriceSource,
    string PriceSourceLabel,
    CartLineKind Kind = CartLineKind.Sale,
    string ReturnSourceKey = "",
    Guid? OriginalOrderGuid = null,
    Guid? OriginalOrderLineGuid = null,
    string? ReturnReason = null,
    PosCartLineDiscountSource DiscountSource = PosCartLineDiscountSource.None,
    bool IsManualPrice = false);
