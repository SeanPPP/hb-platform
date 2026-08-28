using System.Text.Json;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.LocalSupplierInvoices
{
    /// <summary>跨查询与商品审核复用的条码 DTO 映射规则。</summary>
    internal static class LocalSupplierInvoicesBarcodeRules
    {
        public static List<string> DeserializeAdditionalBarcodes(string? additionalBarcodesJson)
        {
            if (string.IsNullOrWhiteSpace(additionalBarcodesJson))
                return new List<string>();

            try
            {
                var values = JsonSerializer.Deserialize<List<string>>(additionalBarcodesJson);
                return NormalizeAdditionalBarcodeValues(null, values);
            }
            catch (JsonException)
            {
                return NormalizeAdditionalBarcodeValues(
                    null,
                    SplitPastedBarcodeCandidates(additionalBarcodesJson)
                );
            }
        }

        public static void PopulateAdditionalBarcodes(IEnumerable<LocalSupplierInvoiceItemDto> items)
        {
            foreach (var item in items)
            {
                item.AdditionalBarcodes = DeserializeAdditionalBarcodes(item.AdditionalBarcodesJson);
            }
        }

        public static bool IsLikelyBarcode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = value.Trim();
            return normalized.All(char.IsDigit)
                && normalized.Length >= 8
                && normalized.Length <= 13;
        }

        public static void AddBarcodeProductCode(
            Dictionary<string, HashSet<string>> barcodeProductCodes,
            string? barcode,
            string? productCode
        )
        {
            if (string.IsNullOrWhiteSpace(barcode) || string.IsNullOrWhiteSpace(productCode))
                return;

            var normalizedBarcode = barcode.Trim();
            if (!barcodeProductCodes.TryGetValue(normalizedBarcode, out var productCodes))
            {
                productCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                barcodeProductCodes[normalizedBarcode] = productCodes;
            }

            productCodes.Add(productCode.Trim());
        }

        public static bool IsBarcodeOwnedByProduct(
            Dictionary<string, HashSet<string>> barcodeProductCodes,
            string? barcode,
            string? productCode
        )
        {
            return !string.IsNullOrWhiteSpace(barcode)
                && !string.IsNullOrWhiteSpace(productCode)
                && barcodeProductCodes.TryGetValue(barcode.Trim(), out var productCodes)
                && productCodes.Contains(productCode.Trim());
        }

        private static IEnumerable<string> SplitPastedBarcodeCandidates(string? value)
        {
            var normalized = NormalizePastedBarcodeSource(value);
            if (string.IsNullOrWhiteSpace(normalized))
                yield break;

            foreach (var barcode in normalized
                .Split(new[] { ',', '，', ';', '；', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Select(x => NormalizePastedTextField(x, 50))
                .Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                yield return barcode!;
            }
        }

        private static string? NormalizePastedBarcodeSource(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = value
                .Trim()
                .TrimStart('\'')
                .Replace("条码", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("barcode", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("bar code", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("ean", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("upc", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(":", string.Empty)
                .Replace("：", string.Empty);

            return string.Concat(normalized.Where(ch => !char.IsWhiteSpace(ch)));
        }

        private static List<string> NormalizeAdditionalBarcodeValues(string? primaryBarcode, IEnumerable<string>? values)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(primaryBarcode))
                seen.Add(primaryBarcode.Trim());

            foreach (var barcode in values ?? Enumerable.Empty<string>())
            {
                var normalized = NormalizePastedTextField(barcode, 50);
                if (!string.IsNullOrWhiteSpace(normalized) && seen.Add(normalized))
                    result.Add(normalized);
            }

            return result;
        }

        private static string? NormalizePastedTextField(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = string.Join(" ", value.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
            return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
        }
    }

    /// <summary>商品审核批量查询的投影 DTO，避免审核切片依赖定价切片。</summary>
    internal sealed class ProductDetectProjection
    {
        public string ItemNumber { get; set; } = string.Empty;
        public string? ProductCode { get; set; }
        public string? ProductName { get; set; }
        public string? ProductImage { get; set; }
    }

    internal sealed class PriceDetectProjection
    {
        public string ProductCode { get; set; } = string.Empty;
        public decimal? PurchasePrice { get; set; }
        public decimal? Retail { get; set; }
        public string? StoreProductCode { get; set; }
    }
}
