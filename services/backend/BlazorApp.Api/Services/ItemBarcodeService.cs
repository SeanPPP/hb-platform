using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 货号条码生成服务
    /// 负责查询现有货号和条码,并生成新的货号和EAN-13条码
    /// </summary>
    public class ItemBarcodeService
    {
        private readonly ISqlSugarClient _db;
        private readonly ILogger<ItemBarcodeService> _logger;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="configuration">配置对象</param>
        public ItemBarcodeService(
            SqlSugarContext context,
            ILogger<ItemBarcodeService> logger,
            IConfiguration configuration
        )
        {
            _db = context.Db;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// 生成货号和条码
        /// 使用并发查询优化性能,独立连接避免并发冲突
        /// </summary>
        /// <param name="supplierCode">供应商编码,格式如 HB001</param>
        /// <param name="productType">商品类型(普通/组合/套装)</param>
        /// <param name="prefix">前缀代码(可选,用于带前缀货号)</param>
        /// <returns>元组: (货号, EAN-13条码)</returns>
        public async Task<(string itemNumber, string barcode)> GenerateItemNumberAndBarcodeAsync(
            string supplierCode,
            ProductTypeEnum productType,
            string? prefix = null
        )
        {
            var existingItemNumbersTask = Task.Run(async () =>
            {
                using var conn = SqlSugarContext.CreateConcurrentConnection(_configuration);
                return await conn.Queryable<DomesticProduct>()
                    .Where(dp =>
                        !dp.IsDeleted
                        && dp.HBProductNo != null
                        && dp.HBProductNo.StartsWith(supplierCode)
                    )
                    .Select(dp => dp.HBProductNo!)
                    .ToListAsync();
            });

            // 计算条码前缀: 9527(公司代码) + 9(普通)/8(套装) + 3位供应商号
            var supplierNumber = int.Parse(supplierCode.Replace("HB", ""));
            var typeCode = productType == ProductTypeEnum.Set ? "8" : "9";
            var barcodePrefix = $"9527{typeCode}{supplierNumber:D3}";

            var existingBarcodesTask = Task.Run(async () =>
            {
                using var conn = SqlSugarContext.CreateConcurrentConnection(_configuration);
                return await conn.Queryable<DomesticProduct>()
                    .Where(dp =>
                        dp.Barcode != null && !dp.IsDeleted && dp.Barcode.StartsWith(barcodePrefix)
                    )
                    .Select(dp => dp.Barcode!)
                    .ToListAsync();
            });

            // 等待两个查询完成
            await Task.WhenAll(existingItemNumbersTask, existingBarcodesTask);

            // 获取查询结果
            var existingItemNumbers = await existingItemNumbersTask;
            var existingBarcodes = await existingBarcodesTask;

            // 生成货号
            string itemNumber;
            if (string.IsNullOrWhiteSpace(prefix))
            {
                // 无前缀货号: 供应商代码-3位序号,如 HB001-001
                itemNumber = ItemNumberHelper.GenerateItemNumber(supplierCode, existingItemNumbers);
            }
            else
            {
                // 有前缀货号: 供应商代码-前缀代码-3位序号,如 HB001-YW-001
                itemNumber = ItemNumberHelper.GenerateItemNumberWithPrefix(
                    supplierCode,
                    prefix,
                    existingItemNumbers
                );
            }

            // 生成EAN-13条码
            var barcode = BarcodeHelper.GenerateEAN13Barcode(
                supplierCode,
                (int)productType,
                existingBarcodes,
                productType == ProductTypeEnum.Set
            );

            return (itemNumber, barcode);
        }

        /// <summary>
        /// 批量生成指定数量的货号和条码
        /// 使用并发查询优化性能,独立连接避免并发冲突
        /// </summary>
        /// <param name="supplierCode">供应商编码,格式如 HB001</param>
        /// <param name="productType">商品类型(普通/组合/套装)</param>
        /// <param name="count">需要生成的数量</param>
        /// <param name="prefix">前缀代码(可选,用于带前缀货号)</param>
        /// <returns>列表: 每个元素为元组 (货号, EAN-13条码)</returns>
        public async Task<
            List<(string itemNumber, string barcode)>
        > GenerateBatchItemNumbersAndBarcodesAsync(
            string supplierCode,
            ProductTypeEnum productType,
            int count,
            string? prefix = null
        )
        {
            if (count <= 0)
                throw new ArgumentException("生成数量必须大于0", nameof(count));

            if (count > 1000)
                throw new ArgumentException("单次批量生成数量不能超过1000", nameof(count));

            var existingItemNumbersTask = Task.Run(async () =>
            {
                using var conn = SqlSugarContext.CreateConcurrentConnection(_configuration);
                return await conn.Queryable<DomesticProduct>()
                    .Where(dp =>
                        !dp.IsDeleted
                        && dp.HBProductNo != null
                        && dp.HBProductNo.StartsWith(supplierCode)
                    )
                    .Select(dp => dp.HBProductNo!)
                    .ToListAsync();
            });

            var supplierNumber = int.Parse(supplierCode.Replace("HB", ""));
            var typeCode = productType == ProductTypeEnum.Set ? "8" : "9";
            var barcodePrefix = $"9527{typeCode}{supplierNumber:D3}";

            var existingBarcodesTask = Task.Run(async () =>
            {
                using var conn = SqlSugarContext.CreateConcurrentConnection(_configuration);
                return await conn.Queryable<DomesticProduct>()
                    .Where(dp =>
                        dp.Barcode != null && !dp.IsDeleted && dp.Barcode.StartsWith(barcodePrefix)
                    )
                    .Select(dp => dp.Barcode!)
                    .ToListAsync();
            });

            await Task.WhenAll(existingItemNumbersTask, existingBarcodesTask);

            var existingItemNumbers = await existingItemNumbersTask;
            var existingBarcodes = await existingBarcodesTask;

            List<string> itemNumbers;
            if (string.IsNullOrWhiteSpace(prefix))
            {
                itemNumbers = ItemNumberHelper.GenerateBatchItemNumbers(
                    supplierCode,
                    count,
                    existingItemNumbers
                );
            }
            else
            {
                itemNumbers = ItemNumberHelper.GenerateBatchItemNumbersWithPrefix(
                    supplierCode,
                    prefix,
                    count,
                    existingItemNumbers
                );
            }

            var barcodes = BarcodeHelper.GenerateBatchEAN13Barcodes(
                supplierCode,
                (int)productType,
                existingBarcodes,
                count,
                productType == ProductTypeEnum.Set
            );

            var result = new List<(string, string)>();
            for (int i = 0; i < Math.Min(itemNumbers.Count, barcodes.Count); i++)
            {
                result.Add((itemNumbers[i], barcodes[i]));
            }

            return result;
        }

        /// <summary>
        /// 生成套装商品货号和条码
        /// 套装货号格式: 基础商品货号-2位序号,如 HB001-001-01
        /// </summary>
        /// <param name="baseItemNumber">基础商品货号,如 HB001-001</param>
        /// <param name="productType">商品类型(必须为套装)</param>
        /// <returns>元组: (套装货号, EAN-13条码)</returns>
        public async Task<(string itemNumber, string barcode)> GenerateSetItemNumberAndBarcodeAsync(
            string baseItemNumber,
            ProductTypeEnum productType
        )
        {
            var existingSetItemNumbersTask = Task.Run(async () =>
            {
                using var conn = SqlSugarContext.CreateConcurrentConnection(_configuration);
                return await conn.Queryable<DomesticProduct>()
                    .Where(dp =>
                        !dp.IsDeleted && dp.HBProductNo != null && dp.HBProductNo.StartsWith(baseItemNumber)
                    )
                    .Select(dp => dp.HBProductNo!)
                    .ToListAsync();
            });

            var supplierCode = ExtractSupplierCodeFromItemNumber(baseItemNumber);
            var supplierNumber = int.Parse(supplierCode.Replace("HB", ""));
            var typeCode = productType == ProductTypeEnum.Set ? "8" : "9";
            var barcodePrefix = $"9527{typeCode}{supplierNumber:D3}";

            var existingBarcodesTask = Task.Run(async () =>
            {
                using var conn = SqlSugarContext.CreateConcurrentConnection(_configuration);
                return await conn.Queryable<DomesticProduct>()
                    .Where(dp =>
                        dp.Barcode != null && !dp.IsDeleted && dp.Barcode.StartsWith(barcodePrefix)
                    )
                    .Select(dp => dp.Barcode!)
                    .ToListAsync();
            });

            await Task.WhenAll(existingSetItemNumbersTask, existingBarcodesTask);

            var existingSetItemNumbers = await existingSetItemNumbersTask;
            var existingBarcodes = await existingBarcodesTask;

            var itemNumber = ItemNumberHelper.GenerateSetItemNumber(
                baseItemNumber,
                existingSetItemNumbers
            );

            var barcode = BarcodeHelper.GenerateEAN13Barcode(
                supplierCode,
                (int)productType,
                existingBarcodes,
                true
            );

            return (itemNumber, barcode);
        }

        /// <summary>
        /// 批量生成指定数量的套装商品货号和条码
        /// </summary>
        /// <param name="baseItemNumber">基础商品货号,如 HB001-001</param>
        /// <param name="productType">商品类型(必须为套装)</param>
        /// <param name="count">需要生成的数量</param>
        /// <returns>列表: 每个元素为元组 (套装货号, EAN-13条码)</returns>
        public async Task<
            List<(string itemNumber, string barcode)>
        > GenerateBatchSetItemNumbersAndBarcodesAsync(
            string baseItemNumber,
            ProductTypeEnum productType,
            int count
        )
        {
            if (count <= 0)
                throw new ArgumentException("生成数量必须大于0", nameof(count));

            if (count > 100)
                throw new ArgumentException("单次批量生成套装数量不能超过100", nameof(count));

            var existingSetItemNumbersTask = Task.Run(async () =>
            {
                using var conn = SqlSugarContext.CreateConcurrentConnection(_configuration);
                return await conn.Queryable<DomesticProduct>()
                    .Where(dp =>
                        !dp.IsDeleted && dp.HBProductNo != null && dp.HBProductNo.StartsWith(baseItemNumber)
                    )
                    .Select(dp => dp.HBProductNo!)
                    .ToListAsync();
            });

            var supplierCode = ExtractSupplierCodeFromItemNumber(baseItemNumber);
            var supplierNumber = int.Parse(supplierCode.Replace("HB", ""));
            var typeCode = productType == ProductTypeEnum.Set ? "8" : "9";
            var barcodePrefix = $"9527{typeCode}{supplierNumber:D3}";

            var existingBarcodesTask = Task.Run(async () =>
            {
                using var conn = SqlSugarContext.CreateConcurrentConnection(_configuration);
                return await conn.Queryable<DomesticProduct>()
                    .Where(dp =>
                        dp.Barcode != null && !dp.IsDeleted && dp.Barcode.StartsWith(barcodePrefix)
                    )
                    .Select(dp => dp.Barcode!)
                    .ToListAsync();
            });

            await Task.WhenAll(existingSetItemNumbersTask, existingBarcodesTask);

            var existingSetItemNumbers = await existingSetItemNumbersTask;
            var existingBarcodes = await existingBarcodesTask;

            var itemNumbers = ItemNumberHelper.GenerateBatchSetItemNumbers(
                baseItemNumber,
                count,
                existingSetItemNumbers
            );

            var barcodes = BarcodeHelper.GenerateBatchEAN13Barcodes(
                supplierCode,
                (int)productType,
                existingBarcodes,
                count,
                true
            );

            var result = new List<(string, string)>();
            for (int i = 0; i < Math.Min(itemNumbers.Count, barcodes.Count); i++)
            {
                result.Add((itemNumbers[i], barcodes[i]));
            }

            return result;
        }

        /// <summary>
        /// 从货号中提取供应商编码
        /// </summary>
        /// <param name="itemNumber">货号</param>
        /// <returns>供应商编码</returns>
        private static string ExtractSupplierCodeFromItemNumber(string itemNumber)
        {
            var parts = itemNumber.Split('-');
            if (parts.Length == 0)
                throw new ArgumentException("无效的货号格式", nameof(itemNumber));

            return parts[0];
        }
    }
}
