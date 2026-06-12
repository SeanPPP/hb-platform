using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace BlazorApp.Shared.Helper
{
    /// <summary>
    /// 商品货号生成帮助类
    /// </summary>
    public static class ItemNumberHelper
    {
        /// <summary>
        /// 生成普通商品货号
        /// 格式：供应商编码+001 (如：HB001-001, HB002-001)
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <param name="existingItemNumbers">现有商品货号列表</param>
        /// <returns>生成的商品货号</returns>
        public static string GenerateItemNumber(
            string supplierCode,
            List<string> existingItemNumbers
        )
        {
            if (string.IsNullOrWhiteSpace(supplierCode))
                throw new ArgumentException("供应商编码不能为空", nameof(supplierCode));

            // 查找该供应商的所有商品货号
            var supplierItemNumbers = existingItemNumbers
                .Where(code => !string.IsNullOrEmpty(code) && code.StartsWith(supplierCode))
                .Where(code => IsValidItemNumber(code, supplierCode))
                .ToList();

            // 找到最大的序号
            int maxSequence = 0;
            var pattern = $@"^{Regex.Escape(supplierCode)}-(\d{{3}})$";

            foreach (var code in supplierItemNumbers)
            {
                var match = Regex.Match(code, pattern);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int sequence))
                {
                    maxSequence = Math.Max(maxSequence, sequence);
                }
            }

            // 生成下一个序号
            var nextSequence = maxSequence + 1;
            return $"{supplierCode}-{nextSequence:D3}";
        }

        /// <summary>
        /// 生成带前缀的商品货号
        /// 格式：供应商编码-前缀-001 (如：HB001-YW-001, HB002-GZ-001)
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <param name="prefix">前缀代码</param>
        /// <param name="existingItemNumbers">现有商品货号列表</param>
        /// <returns>生成的商品货号</returns>
        public static string GenerateItemNumberWithPrefix(
            string supplierCode,
            string prefix,
            List<string> existingItemNumbers
        )
        {
            if (string.IsNullOrWhiteSpace(supplierCode))
                throw new ArgumentException("供应商编码不能为空", nameof(supplierCode));

            if (string.IsNullOrWhiteSpace(prefix))
                throw new ArgumentException("前缀代码不能为空", nameof(prefix));

            var baseCode = $"{supplierCode}-{prefix}";

            // 查找该供应商该前缀的所有商品货号
            var prefixItemNumbers = existingItemNumbers
                .Where(code => !string.IsNullOrEmpty(code) && code.StartsWith(baseCode))
                .Where(code => IsValidItemNumberWithPrefix(code, supplierCode, prefix))
                .ToList();

            // 找到最大的序号
            int maxSequence = 0;
            var pattern = $@"^{Regex.Escape(baseCode)}-(\d{{3}})$";

            foreach (var code in prefixItemNumbers)
            {
                var match = Regex.Match(code, pattern);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int sequence))
                {
                    maxSequence = Math.Max(maxSequence, sequence);
                }
            }

            // 生成下一个序号
            var nextSequence = maxSequence + 1;
            return $"{baseCode}-{nextSequence:D3}";
        }

        /// <summary>
        /// 生成套装商品货号
        /// 格式：普通货号-01 (如：HB001-001-01, HB001-YW-001-01)
        /// </summary>
        /// <param name="baseItemNumber">基础商品货号</param>
        /// <param name="existingSetItemNumbers">现有套装商品货号列表</param>
        /// <returns>生成的套装商品货号</returns>
        public static string GenerateSetItemNumber(
            string baseItemNumber,
            List<string> existingSetItemNumbers
        )
        {
            if (string.IsNullOrWhiteSpace(baseItemNumber))
                throw new ArgumentException("基础商品货号不能为空", nameof(baseItemNumber));

            // 查找该基础商品的所有套装货号
            var baseSetCodes = existingSetItemNumbers
                .Where(code => !string.IsNullOrEmpty(code) && code.StartsWith(baseItemNumber + "-"))
                .Where(code => IsValidSetItemNumber(code, baseItemNumber))
                .ToList();

            // 找到最大的序号
            int maxSequence = 0;
            var pattern = $@"^{Regex.Escape(baseItemNumber)}-(\d{{2}})$";

            foreach (var code in baseSetCodes)
            {
                var match = Regex.Match(code, pattern);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int sequence))
                {
                    maxSequence = Math.Max(maxSequence, sequence);
                }
            }

            // 生成下一个序号
            var nextSequence = maxSequence + 1;
            return $"{baseItemNumber}-{nextSequence:D2}";
        }

        /// <summary>
        /// 验证普通商品货号格式是否正确
        /// </summary>
        /// <param name="itemNumber">商品货号</param>
        /// <param name="supplierCode">供应商编码</param>
        /// <returns>是否有效</returns>
        public static bool IsValidItemNumber(string itemNumber, string supplierCode)
        {
            if (string.IsNullOrWhiteSpace(itemNumber) || string.IsNullOrWhiteSpace(supplierCode))
                return false;

            var pattern = $@"^{Regex.Escape(supplierCode)}-\d{{3}}$";
            return Regex.IsMatch(itemNumber, pattern);
        }

        /// <summary>
        /// 验证带前缀商品货号格式是否正确
        /// </summary>
        /// <param name="itemNumber">商品货号</param>
        /// <param name="supplierCode">供应商编码</param>
        /// <param name="prefix">前缀代码</param>
        /// <returns>是否有效</returns>
        public static bool IsValidItemNumberWithPrefix(
            string itemNumber,
            string supplierCode,
            string prefix
        )
        {
            if (
                string.IsNullOrWhiteSpace(itemNumber)
                || string.IsNullOrWhiteSpace(supplierCode)
                || string.IsNullOrWhiteSpace(prefix)
            )
                return false;

            var pattern = $@"^{Regex.Escape(supplierCode)}-{Regex.Escape(prefix)}-\d{{3}}$";
            return Regex.IsMatch(itemNumber, pattern);
        }

        /// <summary>
        /// 验证套装商品货号格式是否正确
        /// </summary>
        /// <param name="setItemNumber">套装商品货号</param>
        /// <param name="baseItemNumber">基础商品货号</param>
        /// <returns>是否有效</returns>
        public static bool IsValidSetItemNumber(string setItemNumber, string baseItemNumber)
        {
            if (
                string.IsNullOrWhiteSpace(setItemNumber)
                || string.IsNullOrWhiteSpace(baseItemNumber)
            )
                return false;

            var pattern = $@"^{Regex.Escape(baseItemNumber)}-\d{{2}}$";
            return Regex.IsMatch(setItemNumber, pattern);
        }

        /// <summary>
        /// 从商品货号中提取供应商编码
        /// </summary>
        /// <param name="itemNumber">商品货号</param>
        /// <returns>供应商编码，如果无法提取则返回null</returns>
        public static string? ExtractSupplierCode(string itemNumber)
        {
            if (string.IsNullOrWhiteSpace(itemNumber))
                return null;

            // 尝试匹配普通格式：HB001-001
            var simpleMatch = Regex.Match(itemNumber, @"^([A-Z]+\d+)-\d{3}$");
            if (simpleMatch.Success)
                return simpleMatch.Groups[1].Value;

            // 尝试匹配带前缀格式：HB001-YW-001
            var prefixMatch = Regex.Match(itemNumber, @"^([A-Z]+\d+)-[A-Z]+-\d{3}$");
            if (prefixMatch.Success)
                return prefixMatch.Groups[1].Value;

            return null;
        }

        /// <summary>
        /// 从带前缀商品货号中提取前缀代码
        /// </summary>
        /// <param name="itemNumber">商品货号</param>
        /// <returns>前缀代码，如果无法提取则返回null</returns>
        public static string? ExtractPrefix(string itemNumber)
        {
            if (string.IsNullOrWhiteSpace(itemNumber))
                return null;

            // 匹配带前缀格式：HB001-YW-001
            var match = Regex.Match(itemNumber, @"^[A-Z]+\d+-([A-Z]+)-\d{3}$");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// 检查商品货号是否为套装货号
        /// </summary>
        /// <param name="itemNumber">商品货号</param>
        /// <returns>是否为套装货号</returns>
        public static bool IsSetItemNumber(string itemNumber)
        {
            if (string.IsNullOrWhiteSpace(itemNumber))
                return false;

            // 套装货号格式：基础货号-01
            // 普通基础货号：HB001-001-01
            // 带前缀基础货号：HB001-YW-001-01
            return Regex.IsMatch(itemNumber, @"^.+-\d{2}$")
                && (
                    Regex.IsMatch(itemNumber, @"^[A-Z]+\d+-\d{3}-\d{2}$")
                    || Regex.IsMatch(itemNumber, @"^[A-Z]+\d+-[A-Z]+-\d{3}-\d{2}$")
                );
        }

        /// <summary>
        /// 从套装货号中提取基础商品货号
        /// </summary>
        /// <param name="setItemNumber">套装商品货号</param>
        /// <returns>基础商品货号，如果无法提取则返回null</returns>
        public static string? ExtractBaseItemNumber(string setItemNumber)
        {
            if (string.IsNullOrWhiteSpace(setItemNumber) || !IsSetItemNumber(setItemNumber))
                return null;

            var lastDashIndex = setItemNumber.LastIndexOf('-');
            return lastDashIndex > 0 ? setItemNumber.Substring(0, lastDashIndex) : null;
        }

        /// <summary>
        /// 批量生成指定数量的普通商品货号
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <param name="count">需要生成的数量</param>
        /// <param name="existingItemNumbers">现有商品货号列表</param>
        /// <returns>生成的商品货号列表</returns>
        public static List<string> GenerateBatchItemNumbers(
            string supplierCode,
            int count,
            List<string> existingItemNumbers
        )
        {
            if (string.IsNullOrWhiteSpace(supplierCode))
                throw new ArgumentException("供应商编码不能为空", nameof(supplierCode));

            if (count <= 0)
                throw new ArgumentException("生成数量必须大于0", nameof(count));

            var result = new List<string>();

            // 查找该供应商的所有商品货号
            var supplierItemNumbers = existingItemNumbers
                .Where(code => !string.IsNullOrEmpty(code) && code.StartsWith(supplierCode))
                .Where(code => IsValidItemNumber(code, supplierCode))
                .ToList();

            // 找到最大的序号
            int maxSequence = 0;
            var pattern = $@"^{Regex.Escape(supplierCode)}-(\d{{3}})$";

            foreach (var code in supplierItemNumbers)
            {
                var match = Regex.Match(code, pattern);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int sequence))
                {
                    maxSequence = Math.Max(maxSequence, sequence);
                }
            }

            // 生成指定数量的货号
            for (int i = 1; i <= count; i++)
            {
                var nextSequence = maxSequence + i;
                result.Add($"{supplierCode}-{nextSequence:D3}");
            }

            return result;
        }

        /// <summary>
        /// 批量生成指定数量的带前缀商品货号
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <param name="prefix">前缀代码</param>
        /// <param name="count">需要生成的数量</param>
        /// <param name="existingItemNumbers">现有商品货号列表</param>
        /// <returns>生成的商品货号列表</returns>
        public static List<string> GenerateBatchItemNumbersWithPrefix(
            string supplierCode,
            string prefix,
            int count,
            List<string> existingItemNumbers
        )
        {
            if (string.IsNullOrWhiteSpace(supplierCode))
                throw new ArgumentException("供应商编码不能为空", nameof(supplierCode));

            if (string.IsNullOrWhiteSpace(prefix))
                throw new ArgumentException("前缀代码不能为空", nameof(prefix));

            if (count <= 0)
                throw new ArgumentException("生成数量必须大于0", nameof(count));

            var result = new List<string>();
            var baseCode = $"{supplierCode}-{prefix}";

            // 查找该供应商该前缀的所有商品货号
            var prefixItemNumbers = existingItemNumbers
                .Where(code => !string.IsNullOrEmpty(code) && code.StartsWith(baseCode))
                .Where(code => IsValidItemNumberWithPrefix(code, supplierCode, prefix))
                .ToList();

            // 找到最大的序号
            int maxSequence = 0;
            var pattern = $@"^{Regex.Escape(baseCode)}-(\d{{3}})$";

            foreach (var code in prefixItemNumbers)
            {
                var match = Regex.Match(code, pattern);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int sequence))
                {
                    maxSequence = Math.Max(maxSequence, sequence);
                }
            }

            // 生成指定数量的货号
            for (int i = 1; i <= count; i++)
            {
                var nextSequence = maxSequence + i;
                result.Add($"{baseCode}-{nextSequence:D3}");
            }

            return result;
        }

        /// <summary>
        /// 批量生成指定数量的套装商品货号
        /// </summary>
        /// <param name="baseItemNumber">基础商品货号</param>
        /// <param name="count">需要生成的数量</param>
        /// <param name="existingSetItemNumbers">现有套装商品货号列表</param>
        /// <returns>生成的套装商品货号列表</returns>
        public static List<string> GenerateBatchSetItemNumbers(
            string baseItemNumber,
            int count,
            List<string> existingSetItemNumbers
        )
        {
            if (string.IsNullOrWhiteSpace(baseItemNumber))
                throw new ArgumentException("基础商品货号不能为空", nameof(baseItemNumber));

            if (count <= 0)
                throw new ArgumentException("生成数量必须大于0", nameof(count));

            var result = new List<string>();

            // 查找该基础商品的所有套装货号
            var baseSetCodes = existingSetItemNumbers
                .Where(code => !string.IsNullOrEmpty(code) && code.StartsWith(baseItemNumber + "-"))
                .Where(code => IsValidSetItemNumber(code, baseItemNumber))
                .ToList();

            // 找到最大的序号
            int maxSequence = 0;
            var pattern = $@"^{Regex.Escape(baseItemNumber)}-(\d{{2}})$";

            foreach (var code in baseSetCodes)
            {
                var match = Regex.Match(code, pattern);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int sequence))
                {
                    maxSequence = Math.Max(maxSequence, sequence);
                }
            }

            // 生成指定数量的套装货号
            for (int i = 1; i <= count; i++)
            {
                var nextSequence = maxSequence + i;
                result.Add($"{baseItemNumber}-{nextSequence:D2}");
            }

            return result;
        }
    }
}
