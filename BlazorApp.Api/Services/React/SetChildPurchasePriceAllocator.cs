namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 套装子项进货价分摊工具。
    /// </summary>
    internal static class SetChildPurchasePriceAllocator
    {
        public static Dictionary<string, decimal> AllocateByRetailRatio<T>(
            IEnumerable<T> items,
            decimal? mainPurchasePrice,
            Func<T, string?> codeSelector,
            Func<T, decimal?> retailPriceSelector
        )
        {
            var totalPurchase = mainPurchasePrice.GetValueOrDefault();
            if (totalPurchase <= 0)
            {
                return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            }

            var candidates = items
                .Select(item => new
                {
                    Code = codeSelector(item)?.Trim(),
                    RetailPrice = retailPriceSelector(item).GetValueOrDefault(),
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Code) && item.RetailPrice > 0)
                .GroupBy(item => item.Code!, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            var totalRetail = candidates.Sum(item => item.RetailPrice);
            if (totalRetail <= 0 || candidates.Count == 0)
            {
                return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            }

            var roundedTotalPurchase = Math.Round(totalPurchase, 2, MidpointRounding.AwayFromZero);
            var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var allocatedBeforeLast = 0m;

            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var allocated = index == candidates.Count - 1
                    ? roundedTotalPurchase - allocatedBeforeLast
                    : Math.Round(
                        roundedTotalPurchase * candidate.RetailPrice / totalRetail,
                        2,
                        MidpointRounding.AwayFromZero
                    );

                result[candidate.Code!] = allocated;
                allocatedBeforeLast += allocated;
            }

            return result;
        }
    }
}
