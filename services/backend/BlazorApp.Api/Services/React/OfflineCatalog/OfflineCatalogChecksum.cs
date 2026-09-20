using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services.React.OfflineCatalog
{
    /// <summary>
    /// 移动端离线目录的校验和与行版本（与 apps/mobile 的 offline-catalog-checksum.ts 逐字节一致）。
    ///
    /// canonical 编码：每个字段写入 {UTF-16 长度}:{UTF-8 内容}|；数值统一为 IEEE754 binary64 大端
    /// 16 位小写十六进制（decimal 先按 JSON 十进制文本转 double，与客户端 JSON.parse 观察到的值一致）；
    /// 可空数值为空串，布尔为 "1"/"0"，可空布尔为空串。时间戳字段直接使用 DTO 中的 UTC 毫秒 ISO 文本。
    /// </summary>
    public static class OfflineCatalogChecksum
    {
        public const string PageMarker = "HB-MOBILE-OFFLINE-CATALOG-PAGE-V1";
        public const string PagePrefix = "sha256-offline-catalog-page-v1:";
        public const string DeltaMarker = "HB-MOBILE-OFFLINE-CATALOG-DELTA-V1";
        public const string DeltaPrefix = "sha256-offline-catalog-delta-v1:";
        public const char LookupKeySeparator = '';
        private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

        // Unicode 空格分隔符（普通空格之外）；与商品维护 lookup 的 ItemNumberSpaceVariants 一致。
        private const string ItemNumberSpaceVariants =
            "               　";

        /// <summary>与移动端 normalizeOfflineLookupCode 一致：空格变体统一为半角空格 → Trim → 大写。</summary>
        public static string NormalizeLookupCode(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                builder.Append(ItemNumberSpaceVariants.Contains(character) ? ' ' : character);
            }

            return builder.ToString().Trim().ToUpperInvariant();
        }

        public static string BuildLookupKey(string lookupCodeNormalized, string matchSource, string productCode, string? codeId)
        {
            return string.Concat(
                lookupCodeNormalized,
                LookupKeySeparator,
                matchSource,
                LookupKeySeparator,
                productCode,
                LookupKeySeparator,
                codeId ?? string.Empty);
        }

        public static string FormatTimestamp(DateTimeOffset value)
        {
            return value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);
        }

        public static string? FormatTimestamp(DateTimeOffset? value)
        {
            return value.HasValue ? FormatTimestamp(value.Value) : null;
        }

        /// <summary>行版本：业务字段（不含 RowVersion 自身）的 SHA256，大写十六进制。</summary>
        public static string CreateRowVersion(OfflineCatalogItemDto item)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AppendItemFields(hash, item);
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        public static string CreatePageChecksum(IReadOnlyList<OfflineCatalogItemDto> items)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Append(hash, PageMarker);
            Append(hash, FormatBinary64(items.Count));
            foreach (var item in items)
            {
                AppendItemFields(hash, item);
            }

            return string.Concat(PagePrefix, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }

        /// <summary>delta 页校验和：操作按 LookupKey 的 Ordinal 顺序，U 行写全部字段，D 行写店码、键与删除时间。</summary>
        public static string CreateDeltaPageChecksum(
            string baseCatalogVersion,
            string targetCatalogVersion,
            IReadOnlyList<OfflineCatalogDeltaOperation> operations)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Append(hash, DeltaMarker);
            Append(hash, baseCatalogVersion);
            Append(hash, targetCatalogVersion);
            Append(hash, FormatBinary64(operations.Count));
            foreach (var operation in operations.OrderBy(o => o.LookupKey, StringComparer.Ordinal))
            {
                if (operation.Item is { } item)
                {
                    Append(hash, "U");
                    AppendItemFields(hash, item);
                    continue;
                }

                var deleted = operation.Deleted!;
                Append(hash, "D");
                Append(hash, deleted.StoreCode);
                Append(hash, deleted.LookupKey);
                Append(hash, deleted.DeletedAt ?? string.Empty);
            }

            return string.Concat(DeltaPrefix, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }

        private static void AppendItemFields(IncrementalHash hash, OfflineCatalogItemDto item)
        {
            Append(hash, item.StoreCode);
            Append(hash, item.LookupKey);
            Append(hash, item.LookupCode);
            Append(hash, item.LookupCodeNormalized);
            Append(hash, item.MatchSource);
            Append(hash, item.ProductCode);
            Append(hash, item.ProductName);
            Append(hash, item.ItemNumber ?? string.Empty);
            Append(hash, item.Barcode ?? string.Empty);
            Append(hash, item.ProductImage ?? string.Empty);
            Append(hash, FormatNullableBinary64(item.ProductType));
            Append(hash, item.Grade ?? string.Empty);
            Append(hash, item.LocalSupplierCode ?? string.Empty);
            Append(hash, item.LocalSupplierName ?? string.Empty);
            Append(hash, item.StoreName ?? string.Empty);
            Append(hash, item.StorePriceUuid ?? string.Empty);
            Append(hash, FormatNullableBinary64(item.PurchasePrice));
            Append(hash, FormatNullableBinary64(item.RetailPrice));
            Append(hash, FormatNullableBinary64(item.DiscountRate));
            Append(hash, item.IsAutoPricing ? "1" : "0");
            Append(hash, item.IsSpecialProduct ? "1" : "0");
            Append(hash, FormatNullableBinary64(item.Rate));
            Append(hash, item.StrategySourceLabel ?? string.Empty);
            Append(hash, item.StrategyRuleLabel ?? string.Empty);
            Append(hash, item.ClearanceUuid ?? string.Empty);
            Append(hash, item.ClearanceBarcode ?? string.Empty);
            Append(hash, FormatNullableBinary64(item.ClearancePrice));
            Append(hash, item.CodeId ?? string.Empty);
            Append(hash, item.CodeUuid ?? string.Empty);
            Append(hash, item.CodeProductCode ?? string.Empty);
            Append(hash, item.CodeItemNumber ?? string.Empty);
            Append(hash, FormatNullableBinary64(item.CodeRetailPrice));
            Append(hash, FormatNullableBinary64(item.CodePurchasePrice));
            Append(hash, FormatNullableBinary64(item.CodeQuantity));
            Append(hash, FormatNullableBinary64(item.CodeType));
            Append(hash, FormatNullableBinary64(item.CodeDiscountRate));
            Append(hash, FormatNullableBool(item.CodeIsAutoPricing));
            Append(hash, FormatNullableBool(item.CodeIsSpecialProduct));
            Append(hash, FormatNullableBool(item.CodeIsActive));
            Append(hash, item.UpdatedAt ?? string.Empty);
        }

        /// <summary>长度帧：{十进制 UTF-16 长度}:{UTF-8 内容}|。</summary>
        private static void Append(IncrementalHash hash, string value)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(value.Length.ToString(CultureInfo.InvariantCulture)));
            hash.AppendData(":"u8);
            hash.AppendData(Encoding.UTF8.GetBytes(value));
            hash.AppendData("|"u8);
        }

        private static string FormatNullableBool(bool? value)
        {
            return value.HasValue ? (value.Value ? "1" : "0") : string.Empty;
        }

        public static string FormatBinary64(decimal value)
        {
            // decimal 先走 JSON 会产生的十进制文本，再转换为客户端实际观察到的 double。
            var serialized = value.ToString("0.#############################", CultureInfo.InvariantCulture);
            return FormatBinary64(double.Parse(serialized, NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        public static string FormatBinary64(int value)
        {
            return FormatBinary64((double)value);
        }

        public static string FormatBinary64(double value)
        {
            if (value == 0d)
            {
                // 负零归一为正零，与客户端 Object.is(value, -0) 处理一致。
                value = 0d;
            }

            Span<byte> bytes = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64BigEndian(bytes, BitConverter.DoubleToInt64Bits(value));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string FormatNullableBinary64(decimal? value)
        {
            return value.HasValue ? FormatBinary64(value.Value) : string.Empty;
        }

        private static string FormatNullableBinary64(int? value)
        {
            return value.HasValue ? FormatBinary64(value.Value) : string.Empty;
        }
    }

    /// <summary>delta 归并操作：Item 为 upsert，Deleted 为删除。</summary>
    public sealed record OfflineCatalogDeltaOperation(
        string LookupKey,
        OfflineCatalogItemDto? Item,
        OfflineCatalogDeletedItemDto? Deleted)
    {
        public static OfflineCatalogDeltaOperation Upsert(OfflineCatalogItemDto item) =>
            new(item.LookupKey, item, null);

        public static OfflineCatalogDeltaOperation Delete(OfflineCatalogDeletedItemDto deleted) =>
            new(deleted.LookupKey, null, deleted);
    }
}
