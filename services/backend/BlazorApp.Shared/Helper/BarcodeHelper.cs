using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace BlazorApp.Shared.Helper
{
    /// <summary>
    /// 条码生成帮助类
    /// 格式：9527 + 9（普通）/8（套装） + 222（供应商代码去掉HB） + 1001（4位顺序号） + 校验码
    /// </summary>
    public static class BarcodeHelper
    {
        private const string COMPANY_PREFIX = "9527"; // 公司前缀

        /// <summary>
        /// 生成12位商品条码
        /// 格式：9527 + 9（普通）/8（套装） + 222（供应商代码去掉HB） + 1001（4位顺序号）
        /// </summary>
        /// <param name="supplierCode">供应商编码（如：HB001）</param>
        /// <param name="productType">商品类型（0=普通，1=组合，2=套装）</param>
        /// <param name="existingBarcodes">现有条码列表</param>
        /// <param name="isSetProduct">是否为套装商品</param>
        /// <returns>12位条码（不含校验位）</returns>
        public static string GenerateProductBarcode(
            string supplierCode,
            int productType,
            List<string> existingBarcodes,
            bool isSetProduct = false
        )
        {
            if (string.IsNullOrWhiteSpace(supplierCode))
                throw new ArgumentException("供应商编码不能为空", nameof(supplierCode));

            // 提取供应商数字部分（去掉HB前缀）
            var supplierNumber = ExtractSupplierNumber(supplierCode);
            if (supplierNumber == null)
                throw new ArgumentException(
                    $"无效的供应商编码格式: {supplierCode}",
                    nameof(supplierCode)
                );

            // 确定商品类型码：普通商品用9，套装商品用8
            var typeCode = isSetProduct ? "8" : "9";

            // 生成基础码：9527 + 类型码 + 供应商号（3位）
            var baseCode = $"{COMPANY_PREFIX}{typeCode}{supplierNumber:D3}";

            // 查找该供应商该类型的现有条码，获取最大序号
            var maxSequence = GetMaxSequenceForSupplier(existingBarcodes, baseCode);

            // 生成下一个序号（4位）
            var nextSequence = maxSequence + 1;
            if (nextSequence > 9999)
                throw new InvalidOperationException(
                    $"供应商 {supplierCode} 的{(isSetProduct ? "套装" : "普通")}商品条码序号已达上限"
                );

            // 生成12位条码
            return $"{baseCode}{nextSequence:D4}";
        }

        /// <summary>
        /// 生成完整的13位EAN条码（包含校验位）
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <param name="productType">商品类型</param>
        /// <param name="existingBarcodes">现有条码列表</param>
        /// <param name="isSetProduct">是否为套装商品</param>
        /// <returns>13位EAN条码</returns>
        public static string GenerateEAN13Barcode(
            string supplierCode,
            int productType,
            List<string> existingBarcodes,
            bool isSetProduct = false
        )
        {
            var barcode12 = GenerateProductBarcode(
                supplierCode,
                productType,
                existingBarcodes,
                isSetProduct
            );
            return GenerateCompleteEan13(barcode12);
        }

        /// <summary>
        /// 批量生成指定数量的EAN-13条码
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <param name="productType">商品类型</param>
        /// <param name="existingBarcodes">现有条码列表</param>
        /// <param name="count">需要生成的条码数量</param>
        /// <param name="isSetProduct">是否为套装商品</param>
        /// <returns>生成的条码列表</returns>
        /// <exception cref="ArgumentException">供应商编码无效时抛出</exception>
        /// <exception cref="ArgumentOutOfRangeException">数量超出范围时抛出</exception>
        /// <exception cref="InvalidOperationException">条码序号达到上限时抛出</exception>
        public static List<string> GenerateBatchEAN13Barcodes(
            string supplierCode,
            int productType,
            List<string> existingBarcodes,
            int count,
            bool isSetProduct = false
        )
        {
            if (string.IsNullOrWhiteSpace(supplierCode))
                throw new ArgumentException("供应商编码不能为空", nameof(supplierCode));

            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "数量必须大于0");

            if (count > 1000)
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    "单次批量生成数量不能超过1000"
                );

            // 提取供应商数字部分
            var supplierNumber = ExtractSupplierNumber(supplierCode);
            if (supplierNumber == null)
                throw new ArgumentException(
                    $"无效的供应商编码格式: {supplierCode}",
                    nameof(supplierCode)
                );

            // 确定商品类型码
            var typeCode = isSetProduct ? "8" : "9";

            // 生成基础码
            var baseCode = $"{COMPANY_PREFIX}{typeCode}{supplierNumber:D3}";

            // 获取当前最大序号
            var maxSequence = GetMaxSequenceForSupplier(existingBarcodes, baseCode);

            // 批量生成条码
            var barcodes = new List<string>(count);

            for (int i = 0; i < count; i++)
            {
                var nextSequence = maxSequence + i + 1;

                if (nextSequence > 9999)
                    throw new InvalidOperationException(
                        $"供应商 {supplierCode} 的{(isSetProduct ? "套装" : "普通")}商品条码序号已达上限。"
                            + $"当前序号: {maxSequence}, 请求生成: {count}, 超出位置: {i + 1}"
                    );

                // 生成12位条码
                var barcode12 = $"{baseCode}{nextSequence:D4}";

                // 生成完整的13位条码（包含校验位）
                var barcode13 = GenerateCompleteEan13(barcode12);

                barcodes.Add(barcode13);
            }

            return barcodes;
        }

        /// <summary>
        /// 生成EAN-13条码的校验位（最后一位）
        /// EAN-13条码总共13位，前12位为数据位，第13位为校验位
        /// </summary>
        /// <param name="first12Digits">前12位数字字符串</param>
        /// <returns>校验位（0-9），如果输入无效则返回-1</returns>
        public static int GenerateEan13CheckDigit(string first12Digits)
        {
            // 验证输入
            if (string.IsNullOrEmpty(first12Digits))
                return -1;

            if (first12Digits.Length != 12)
                return -1;

            if (!first12Digits.All(char.IsDigit))
                return -1;

            try
            {
                int sum = 0;

                // EAN-13校验位计算规则：
                // 1. 从右到左，奇数位乘以1，偶数位乘以3
                // 2. 所有结果相加
                // 3. 用10减去和的个位数，如果结果是10则校验位为0
                for (int i = 0; i < 12; i++)
                {
                    int digit = int.Parse(first12Digits[i].ToString());

                    // 位置从1开始计算，奇数位（1,3,5...）乘以1，偶数位（2,4,6...）乘以3
                    if ((i + 1) % 2 == 1) // 奇数位
                    {
                        sum += digit * 1;
                    }
                    else // 偶数位
                    {
                        sum += digit * 3;
                    }
                }

                // 计算校验位
                int checkDigit = (10 - (sum % 10)) % 10;
                return checkDigit;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// 生成完整的EAN-13条码（包含校验位）
        /// </summary>
        /// <param name="first12Digits">前12位数字字符串</param>
        /// <returns>完整的13位EAN-13条码，如果输入无效则返回空字符串</returns>
        public static string GenerateCompleteEan13(string first12Digits)
        {
            int checkDigit = GenerateEan13CheckDigit(first12Digits);

            if (checkDigit == -1)
                return string.Empty;

            return first12Digits + checkDigit.ToString();
        }

        /// <summary>
        /// 验证EAN-13条码是否有效
        /// </summary>
        /// <param name="ean13Code">13位EAN-13条码</param>
        /// <returns>是否为有效的EAN-13条码</returns>
        public static bool ValidateEan13(string ean13Code)
        {
            if (string.IsNullOrEmpty(ean13Code))
                return false;

            if (ean13Code.Length != 13)
                return false;

            if (!ean13Code.All(char.IsDigit))
                return false;

            // 提取前12位和校验位
            string first12Digits = ean13Code.Substring(0, 12);
            int providedCheckDigit = int.Parse(ean13Code[12].ToString());

            // 计算正确的校验位
            int calculatedCheckDigit = GenerateEan13CheckDigit(first12Digits);

            return calculatedCheckDigit == providedCheckDigit;
        }

        /// <summary>
        /// 验证条码是否为公司生成的条码
        /// </summary>
        /// <param name="barcode">条码</param>
        /// <returns>是否为公司条码</returns>
        public static bool IsCompanyBarcode(string barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode))
                return false;

            // 去掉校验位，检查前8位
            var code = barcode.Length == 13 ? barcode.Substring(0, 12) : barcode;
            if (code.Length < 8)
                return false;

            return code.StartsWith(COMPANY_PREFIX) && (code[4] == '9' || code[4] == '8');
        }

        /// <summary>
        /// 从条码中提取供应商编码
        /// </summary>
        /// <param name="barcode">条码</param>
        /// <returns>供应商编码，如果无法提取则返回null</returns>
        public static string? ExtractSupplierCodeFromBarcode(string barcode)
        {
            if (!IsCompanyBarcode(barcode))
                return null;

            var code = barcode.Length == 13 ? barcode.Substring(0, 12) : barcode;
            if (code.Length < 8)
                return null;

            // 提取供应商号（第6-8位）
            var supplierNumberStr = code.Substring(5, 3);
            if (int.TryParse(supplierNumberStr, out int supplierNumber))
            {
                return $"HB{supplierNumber:D3}";
            }

            return null;
        }

        /// <summary>
        /// 检查条码是否为套装商品条码
        /// </summary>
        /// <param name="barcode">条码</param>
        /// <returns>是否为套装商品条码</returns>
        public static bool IsSetProductBarcode(string barcode)
        {
            if (!IsCompanyBarcode(barcode))
                return false;

            var code = barcode.Length == 13 ? barcode.Substring(0, 12) : barcode;
            return code.Length >= 5 && code[4] == '8';
        }

        /// <summary>
        /// 从供应商编码中提取数字部分
        /// </summary>
        /// <param name="supplierCode">供应商编码（如：HB001）</param>
        /// <returns>供应商数字，如果无法提取则返回null</returns>
        private static int? ExtractSupplierNumber(string supplierCode)
        {
            if (string.IsNullOrWhiteSpace(supplierCode))
                return null;

            // 匹配HB开头的编码
            var match = Regex.Match(supplierCode, @"^HB(\d+)$");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int number))
            {
                return number;
            }

            return null;
        }

        /// <summary>
        /// 获取指定基础码的最大序号
        /// </summary>
        /// <param name="existingBarcodes">现有条码列表</param>
        /// <param name="baseCode">基础码（8位）</param>
        /// <returns>最大序号</returns>
        private static int GetMaxSequenceForSupplier(List<string> existingBarcodes, string baseCode)
        {
            int maxSequence = 0;
            var pattern = $@"^{Regex.Escape(baseCode)}(\d{{4}})";

            foreach (var barcode in existingBarcodes.Where(b => !string.IsNullOrEmpty(b)))
            {
                // 处理12位或13位条码
                var code = barcode.Length == 13 ? barcode.Substring(0, 12) : barcode;
                var match = Regex.Match(code, pattern);

                if (match.Success && int.TryParse(match.Groups[1].Value, out int sequence))
                {
                    maxSequence = Math.Max(maxSequence, sequence);
                }
            }

            return maxSequence;
        }

        /// <summary>
        /// 生成套装商品的条码
        /// </summary>
        /// <param name="baseProductBarcode">基础商品条码</param>
        /// <param name="existingSetBarcodes">现有套装条码列表</param>
        /// <returns>套装商品条码</returns>
        public static string GenerateSetProductBarcode(
            string baseProductBarcode,
            List<string> existingSetBarcodes
        )
        {
            var supplierCode = ExtractSupplierCodeFromBarcode(baseProductBarcode);
            if (supplierCode == null)
                throw new ArgumentException(
                    "无法从基础商品条码中提取供应商编码",
                    nameof(baseProductBarcode)
                );

            return GenerateEAN13Barcode(supplierCode, 2, existingSetBarcodes, true);
        }

        /// <summary>
        /// 批量验证条码列表
        /// </summary>
        /// <param name="barcodes">条码列表</param>
        /// <returns>验证结果字典</returns>
        public static Dictionary<string, bool> ValidateBarcodes(List<string> barcodes)
        {
            var results = new Dictionary<string, bool>();
            foreach (var barcode in barcodes)
            {
                if (!string.IsNullOrWhiteSpace(barcode))
                {
                    results[barcode] = ValidateEan13(barcode);
                }
            }
            return results;
        }

        /// <summary>
        /// 格式化EAN-13条码显示（添加分隔符）
        /// 格式: X-XXXXXX-XXXXXX-X
        /// </summary>
        /// <param name="ean13Code">13位EAN-13条码</param>
        /// <returns>格式化后的条码字符串</returns>
        public static string FormatEan13Display(string ean13Code)
        {
            if (!ValidateEan13(ean13Code))
                return ean13Code;

            // 格式: X-XXXXXX-XXXXXX-X
            return $"{ean13Code[0]}-{ean13Code.Substring(1, 6)}-{ean13Code.Substring(7, 5)}-{ean13Code[12]}";
        }

        /// <summary>
        /// 获取条码前缀
        /// 格式：9527 + 9（普通）/8（套装） + 3位供应商号
        /// </summary>
        /// <param name="supplierCode">供应商编码（如：HB001）</param>
        /// <param name="isSetProduct">是否为套装商品</param>
        /// <returns>条码前缀（8位）</returns>
        public static string GetBarcodePrefix(string supplierCode, bool isSetProduct = false)
        {
            if (string.IsNullOrWhiteSpace(supplierCode))
                throw new ArgumentException("供应商编码不能为空", nameof(supplierCode));

            var supplierNumber = ExtractSupplierNumber(supplierCode);
            if (supplierNumber == null)
                throw new ArgumentException(
                    $"无效的供应商编码格式: {supplierCode}",
                    nameof(supplierCode)
                );

            var typeCode = isSetProduct ? "8" : "9";
            return $"{COMPANY_PREFIX}{typeCode}{supplierNumber:D3}";
        }

        /// <summary>
        /// 常见国家代码常量
        /// </summary>
        public static class CountryCodes
        {
            public const string USA_CANADA = "0"; // 美国和加拿大
            public const string CHINA = "690"; // 中国
            public const string AUSTRALIA = "93"; // 澳大利亚
            public const string JAPAN = "45"; // 日本
            public const string GERMANY = "40"; // 德国
            public const string FRANCE = "30"; // 法国
            public const string UK = "50"; // 英国
        }
    }
}
