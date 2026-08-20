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
        private const string ItemNumberIdentifierType = "ItemNumber";
        private const string BarcodeIdentifierType = "Barcode";
        private const int ReservationRetryCount = 8;
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
            var generated = await GenerateMainItemNumbersAndBarcodesAsync(
                supplierCode,
                productType,
                1,
                prefix
            );
            return generated[0];
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
            return await GenerateMainItemNumbersAndBarcodesAsync(
                supplierCode,
                productType,
                count,
                prefix
            );
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
            var generated = await GenerateSetItemNumbersAndBarcodesAsync(
                baseItemNumber,
                productType,
                1
            );
            return generated[0];
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
            return await GenerateSetItemNumbersAndBarcodesAsync(
                baseItemNumber,
                productType,
                count
            );
        }

        private Task<List<(string itemNumber, string barcode)>> GenerateMainItemNumbersAndBarcodesAsync(
            string supplierCode,
            ProductTypeEnum productType,
            int count,
            string? prefix
        ) =>
            GenerateAndReserveAsync(
                supplierCode,
                async db =>
                {
                    var existingItemNumbers = await LoadExistingItemNumbersAsync(db, supplierCode);
                    var existingBarcodes = await LoadExistingBarcodesAsync(
                        db,
                        BuildBarcodePrefix(supplierCode, productType)
                    );

                    var itemNumbers = string.IsNullOrWhiteSpace(prefix)
                        ? ItemNumberHelper.GenerateBatchItemNumbers(
                            supplierCode,
                            count,
                            existingItemNumbers
                        )
                        : ItemNumberHelper.GenerateBatchItemNumbersWithPrefix(
                            supplierCode,
                            prefix,
                            count,
                            existingItemNumbers
                        );
                    var barcodes = BarcodeHelper.GenerateBatchEAN13Barcodes(
                        supplierCode,
                        (int)productType,
                        existingBarcodes,
                        count,
                        productType == ProductTypeEnum.Set
                    );
                    return Pair(itemNumbers, barcodes);
                }
            );

        private Task<List<(string itemNumber, string barcode)>> GenerateSetItemNumbersAndBarcodesAsync(
            string baseItemNumber,
            ProductTypeEnum productType,
            int count
        )
        {
            var supplierCode = ExtractSupplierCodeFromItemNumber(baseItemNumber);
            return GenerateAndReserveAsync(
                supplierCode,
                async db =>
                {
                    var existingItemNumbers = await LoadExistingItemNumbersAsync(
                        db,
                        baseItemNumber
                    );
                    var existingBarcodes = await LoadExistingBarcodesAsync(
                        db,
                        BuildBarcodePrefix(supplierCode, productType)
                    );
                    var itemNumbers = ItemNumberHelper.GenerateBatchSetItemNumbers(
                        baseItemNumber,
                        count,
                        existingItemNumbers
                    );
                    var barcodes = BarcodeHelper.GenerateBatchEAN13Barcodes(
                        supplierCode,
                        (int)productType,
                        existingBarcodes,
                        count,
                        true
                    );
                    return Pair(itemNumbers, barcodes);
                }
            );
        }

        private async Task<List<(string itemNumber, string barcode)>> GenerateAndReserveAsync(
            string supplierCode,
            Func<ISqlSugarClient, Task<List<(string itemNumber, string barcode)>>> generate
        )
        {
            for (var attempt = 0; attempt < ReservationRetryCount; attempt++)
            {
                using var db = SqlSugarContext.CreateConcurrentConnection(_configuration);
                var transactionStarted = false;
                try
                {
                    await db.Ado.BeginTranAsync();
                    transactionStarted = true;
                    await AcquireReservationLockAsync(db, supplierCode);

                    var generated = await generate(db);
                    await ReserveAsync(db, generated);
                    await db.Ado.CommitTranAsync();
                    return generated;
                }
                catch (Exception ex)
                {
                    if (transactionStarted)
                    {
                        try
                        {
                            await db.Ado.RollbackTranAsync();
                        }
                        catch
                        {
                            // 原异常包含真实的冲突原因，回滚失败不覆盖它。
                        }
                    }

                    if (attempt >= ReservationRetryCount - 1 || !IsRetryableReservationConflict(ex))
                    {
                        throw;
                    }

                    _logger.LogWarning(
                        ex,
                        "货号条码预留冲突，正在重试。SupplierCode={SupplierCode}, Attempt={Attempt}",
                        supplierCode,
                        attempt + 1
                    );
                    await Task.Delay(20 * (attempt + 1));
                }
            }

            throw new InvalidOperationException("无法生成唯一货号和条码");
        }

        private static async Task<List<string>> LoadExistingItemNumbersAsync(
            ISqlSugarClient db,
            string itemNumberPrefix
        )
        {
            var productItemNumbers = await db.Queryable<DomesticProduct>()
                .Where(product =>
                    !product.IsDeleted
                    && product.HBProductNo != null
                    && product.HBProductNo.StartsWith(itemNumberPrefix)
                )
                .Select(product => product.HBProductNo!)
                .ToListAsync();
            var relationItemNumbers = await db.Queryable<DomesticSetProduct>()
                .Where(relation =>
                    !relation.IsDeleted && relation.SetProductNo.StartsWith(itemNumberPrefix)
                )
                .Select(relation => relation.SetProductNo)
                .ToListAsync();
            var reservedItemNumbers = await db.Queryable<ItemBarcodeReservation>()
                .Where(reservation =>
                    reservation.IdentifierType == ItemNumberIdentifierType
                    && reservation.IdentifierValue.StartsWith(itemNumberPrefix)
                )
                .Select(reservation => reservation.IdentifierValue)
                .ToListAsync();

            return productItemNumbers
                .Concat(relationItemNumbers)
                .Concat(reservedItemNumbers)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static async Task<List<string>> LoadExistingBarcodesAsync(
            ISqlSugarClient db,
            string barcodePrefix
        )
        {
            var productBarcodes = await db.Queryable<DomesticProduct>()
                .Where(product =>
                    !product.IsDeleted
                    && product.Barcode != null
                    && product.Barcode.StartsWith(barcodePrefix)
                )
                .Select(product => product.Barcode!)
                .ToListAsync();
            var relationBarcodes = await db.Queryable<DomesticSetProduct>()
                .Where(relation =>
                    !relation.IsDeleted
                    && relation.SetBarcode != null
                    && relation.SetBarcode.StartsWith(barcodePrefix)
                )
                .Select(relation => relation.SetBarcode!)
                .ToListAsync();
            var reservedBarcodes = await db.Queryable<ItemBarcodeReservation>()
                .Where(reservation =>
                    reservation.IdentifierType == BarcodeIdentifierType
                    && reservation.IdentifierValue.StartsWith(barcodePrefix)
                )
                .Select(reservation => reservation.IdentifierValue)
                .ToListAsync();

            return productBarcodes
                .Concat(relationBarcodes)
                .Concat(reservedBarcodes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static async Task ReserveAsync(
            ISqlSugarClient db,
            IReadOnlyCollection<(string itemNumber, string barcode)> generated
        )
        {
            var createdAt = DateTime.UtcNow;
            var reservations = generated
                .SelectMany(item =>
                    new[]
                    {
                        CreateReservation(ItemNumberIdentifierType, item.itemNumber, createdAt),
                        CreateReservation(BarcodeIdentifierType, item.barcode, createdAt),
                    }
                )
                .ToList();
            if (
                reservations.Select(item => item.ReservationKey).Distinct().Count()
                != reservations.Count
            )
            {
                throw new InvalidOperationException("本次生成结果包含重复货号或条码");
            }

            // 关键逻辑：先永久占位再返回给创建流程；创建失败只允许留下序号空洞，绝不回收复用。
            await db.Insertable(reservations).PageSize(200).ExecuteCommandAsync();
        }

        private static ItemBarcodeReservation CreateReservation(
            string identifierType,
            string identifierValue,
            DateTime createdAt
        )
        {
            var normalizedValue = identifierValue.Trim().ToUpperInvariant();
            return new ItemBarcodeReservation
            {
                ReservationKey = $"{identifierType.ToUpperInvariant()}:{normalizedValue}",
                IdentifierType = identifierType,
                IdentifierValue = normalizedValue,
                CreatedAt = createdAt,
            };
        }

        private static List<(string itemNumber, string barcode)> Pair(
            IReadOnlyList<string> itemNumbers,
            IReadOnlyList<string> barcodes
        )
        {
            if (itemNumbers.Count != barcodes.Count)
                throw new InvalidOperationException("货号和条码生成数量不一致");

            var result = new List<(string itemNumber, string barcode)>(itemNumbers.Count);
            for (var index = 0; index < itemNumbers.Count; index++)
            {
                result.Add((itemNumbers[index], barcodes[index]));
            }
            return result;
        }

        private static string BuildBarcodePrefix(
            string supplierCode,
            ProductTypeEnum productType
        )
        {
            var supplierNumber = int.Parse(supplierCode.Replace("HB", ""));
            var typeCode = productType == ProductTypeEnum.Set ? "8" : "9";
            return $"9527{typeCode}{supplierNumber:D3}";
        }

        private static async Task AcquireReservationLockAsync(
            ISqlSugarClient db,
            string supplierCode
        )
        {
            if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
                return;

            const string sql = """
DECLARE @lockResult int;
EXEC @lockResult = sys.sp_getapplock
    @Resource = @resource,
    @LockMode = 'Exclusive',
    @LockOwner = 'Transaction',
    @LockTimeout = 10000;
IF @lockResult < 0 THROW 51061, '获取货号条码预留锁失败', 1;
""";
            await db.Ado.ExecuteCommandAsync(
                sql,
                new SugarParameter(
                    "@resource",
                    $"DomesticItemBarcodeReservation:{supplierCode.Trim().ToUpperInvariant()}"
                )
            );
        }

        private static bool IsRetryableReservationConflict(Exception exception)
        {
            for (var current = exception; current is not null; current = current.InnerException)
            {
                var message = current.Message;
                if (
                    message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("2601", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("2627", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("database is locked", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("database is busy", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }
            }
            return false;
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
