using BlazorApp.Shared.Helper;

namespace BlazorApp.Api.Services.React
{
    public static class StoreProductMaintenanceClearanceBarcodeHelper
    {
        public const int StoreCodeSegmentLength = 3;
        public const int DateSegmentLength = 6;
        public const int RandomSegmentLength = 3;
        public const int BarcodeBodyLength = 12;
        public const int MaxRandomAttempts = 1000;

        public static string NormalizeStoreCodeSegment(string storeCode)
        {
            if (string.IsNullOrWhiteSpace(storeCode))
            {
                return new string('0', StoreCodeSegmentLength);
            }

            var digits = new string(storeCode.Where(char.IsDigit).ToArray());
            if (digits.Length == 0)
            {
                return new string('0', StoreCodeSegmentLength);
            }

            return digits.Length >= StoreCodeSegmentLength
                ? digits[^StoreCodeSegmentLength..]
                : digits.PadLeft(StoreCodeSegmentLength, '0');
        }

        public static string FormatDateSegment(DateTime localNow)
        {
            return localNow.ToString("yyMMdd");
        }

        public static string GenerateBarcode(
            string storeCode,
            IReadOnlyCollection<string> existingBarcodes,
            DateTime? localNow = null,
            Func<int>? randomNumberProvider = null
        )
        {
            var storeSegment = NormalizeStoreCodeSegment(storeCode);
            var dateSegment = FormatDateSegment(localNow ?? DateTime.Now);
            var existingSet = new HashSet<string>(
                existingBarcodes.Where(code => !string.IsNullOrWhiteSpace(code)),
                StringComparer.Ordinal
            );
            var triedRandomSegments = new HashSet<string>(StringComparer.Ordinal);
            var nextRandom = randomNumberProvider ?? (() => Random.Shared.Next(0, 1000));

            for (var attempt = 1; attempt <= MaxRandomAttempts; attempt++)
            {
                var randomSegment = NormalizeRandomSegment(nextRandom());
                if (!triedRandomSegments.Add(randomSegment))
                {
                    continue;
                }

                var barcodeBody = $"{storeSegment}{dateSegment}{randomSegment}";
                if (barcodeBody.Length != BarcodeBodyLength)
                {
                    throw new InvalidOperationException(
                        $"清货条码主体长度无效: {barcodeBody.Length}"
                    );
                }

                var barcode = BarcodeHelper.GenerateCompleteEan13(barcodeBody);
                if (string.IsNullOrWhiteSpace(barcode))
                {
                    throw new InvalidOperationException(
                        $"无法根据主体码生成有效的 EAN13 清货条码: {barcodeBody}"
                    );
                }

                if (!existingSet.Contains(barcode))
                {
                    return barcode;
                }
            }

            throw new InvalidOperationException(
                $"清货条码生成失败: 分店 {storeCode} 在当天可用随机段已耗尽"
            );
        }

        public static string GenerateBarcodeForRandom(
            string storeCode,
            DateTime localNow,
            int randomValue
        )
        {
            var storeSegment = NormalizeStoreCodeSegment(storeCode);
            var dateSegment = FormatDateSegment(localNow);
            var randomSegment = NormalizeRandomSegment(randomValue);
            var barcodeBody = $"{storeSegment}{dateSegment}{randomSegment}";

            if (barcodeBody.Length != BarcodeBodyLength)
            {
                throw new InvalidOperationException(
                    $"清货条码主体长度无效: {barcodeBody.Length}"
                );
            }

            var barcode = BarcodeHelper.GenerateCompleteEan13(barcodeBody);
            if (string.IsNullOrWhiteSpace(barcode))
            {
                throw new InvalidOperationException(
                    $"无法根据主体码生成有效的 EAN13 清货条码: {barcodeBody}"
                );
            }

            return barcode;
        }

        private static string NormalizeRandomSegment(int value)
        {
            var normalized = Math.Abs(value % 1000);
            return normalized.ToString("D3");
        }
    }
}
