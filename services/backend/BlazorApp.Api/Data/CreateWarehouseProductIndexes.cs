using BlazorApp.Api.Data;
using SqlSugar;
using System.Data;

namespace BlazorApp.Api.Data
{
    /// <summary>
    /// 仓库商品性能优化索引创建器
    /// 用于在应用启动时创建必要的数据库索引
    /// </summary>
    public static class WarehouseProductIndexCreator
    {
        /// <summary>
        /// 创建所有仓库商品相关的性能优化索引
        /// </summary>
        /// <param name="db">数据库连接</param>
        public static async Task CreateAllIndexesAsync(ISqlSugarClient db)
        {
            try
            {
                Console.WriteLine("开始创建仓库商品性能优化索引...");

                // 创建基础索引
                await CreateBasicIndexesAsync(db);

                // 创建复合索引
                await CreateCompositeIndexesAsync(db);

                // 创建外键表索引
                await CreateRelatedTableIndexesAsync(db);

                Console.WriteLine("仓库商品性能优化索引创建完成!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"创建索引时发生错误: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 创建基础索引（SQL Server兼容）
        /// </summary>
        private static async Task CreateBasicIndexesAsync(ISqlSugarClient db)
        {
            // 基础索引定义（SQL Server语法）
            var indexes = new Dictionary<string, string>
            {
                // 商品名称搜索索引
                ["IX_WarehouseProduct_ProductName"] = "CREATE NONCLUSTERED INDEX [IX_WarehouseProduct_ProductName] ON [WarehouseProduct] ([ProductName])",

                // 商品编码唯一索引
                ["IX_WarehouseProduct_ProductCode"] = "CREATE UNIQUE NONCLUSTERED INDEX [IX_WarehouseProduct_ProductCode] ON [WarehouseProduct] ([ProductCode])",

                // 条码索引
                ["IX_WarehouseProduct_Barcode"] = "CREATE NONCLUSTERED INDEX [IX_WarehouseProduct_Barcode] ON [WarehouseProduct] ([Barcode]) WHERE [Barcode] IS NOT NULL",

                // 品牌索引
                ["IX_WarehouseProduct_Brand"] = "CREATE NONCLUSTERED INDEX [IX_WarehouseProduct_Brand] ON [WarehouseProduct] ([Brand]) WHERE [Brand] IS NOT NULL",

                // 分类GUID索引
                ["IX_WarehouseProduct_CategoryGUID"] = "CREATE NONCLUSTERED INDEX [IX_WarehouseProduct_CategoryGUID] ON [WarehouseProduct] ([CategoryGUID]) WHERE [CategoryGUID] IS NOT NULL",

                // 状态索引
                ["IX_WarehouseProduct_IsActive"] = "CREATE NONCLUSTERED INDEX [IX_WarehouseProduct_IsActive] ON [WarehouseProduct] ([IsActive])",

                // 库存数量索引
                ["IX_WarehouseProduct_StockQuantity"] = "CREATE NONCLUSTERED INDEX [IX_WarehouseProduct_StockQuantity] ON [WarehouseProduct] ([StockQuantity]) WHERE [StockQuantity] IS NOT NULL",

                // 价格索引
                ["IX_WarehouseProduct_DomesticPrice"] = "CREATE NONCLUSTERED INDEX [IX_WarehouseProduct_DomesticPrice] ON [WarehouseProduct] ([DomesticPrice]) WHERE [DomesticPrice] IS NOT NULL",
                ["IX_WarehouseProduct_OEMPrice"] = "CREATE NONCLUSTERED INDEX [IX_WarehouseProduct_OEMPrice] ON [WarehouseProduct] ([OEMPrice]) WHERE [OEMPrice] IS NOT NULL",
                ["IX_WarehouseProduct_ImportPrice"] = "CREATE NONCLUSTERED INDEX [IX_WarehouseProduct_ImportPrice] ON [WarehouseProduct] ([ImportPrice]) WHERE [ImportPrice] IS NOT NULL",

                // 时间索引
                ["IX_WarehouseProduct_CreatedAt"] = "CREATE NONCLUSTERED INDEX [IX_WarehouseProduct_CreatedAt] ON [WarehouseProduct] ([CreatedAt])",
                ["IX_WarehouseProduct_UpdatedAt"] = "CREATE NONCLUSTERED INDEX [IX_WarehouseProduct_UpdatedAt] ON [WarehouseProduct] ([UpdatedAt])"
            };

            foreach (var index in indexes)
            {
                try
                {
                    // 先检查索引是否存在
                    var existsQuery = $@"
                        SELECT COUNT(*) 
                        FROM sys.indexes i
                        INNER JOIN sys.objects o ON i.object_id = o.object_id
                        WHERE i.name = '{index.Key}' AND o.name = 'WarehouseProduct'";

                    var indexExists = await db.Ado.SqlQuerySingleAsync<int>(existsQuery);

                    if (indexExists == 0)
                    {
                        await db.Ado.ExecuteCommandAsync(index.Value);
                        Console.WriteLine($"✓ 索引创建成功: {index.Key}");
                    }
                    else
                    {
                        Console.WriteLine($"◆ 索引已存在: {index.Key}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ 索引创建失败: {index.Key} - {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 创建复合索引（SQL Server兼容）
        /// </summary>
        private static async Task CreateCompositeIndexesAsync(ISqlSugarClient db)
        {
            var compositeIndexes = new Dictionary<string, string>
            {
                // 库存预警复合索引
                ["IX_WarehouseProduct_StockAlert"] = "CREATE NONCLUSTERED INDEX [IX_WarehouseProduct_StockAlert] ON [WarehouseProduct] ([IsActive], [StockQuantity], [StockAlertQuantity]) WHERE [StockQuantity] IS NOT NULL AND [StockAlertQuantity] IS NOT NULL",

                // 分类+状态复合索引
                ["IX_WarehouseProduct_Category_Active"] = "CREATE NONCLUSTERED INDEX [IX_WarehouseProduct_Category_Active] ON [WarehouseProduct] ([CategoryGUID], [IsActive]) WHERE [CategoryGUID] IS NOT NULL",

                // 品牌+状态复合索引
                ["IX_WarehouseProduct_Brand_Active"] = "CREATE NONCLUSTERED INDEX [IX_WarehouseProduct_Brand_Active] ON [WarehouseProduct] ([Brand], [IsActive]) WHERE [Brand] IS NOT NULL",

                // 关键字搜索复合索引
                ["IX_WarehouseProduct_Search"] = "CREATE NONCLUSTERED INDEX [IX_WarehouseProduct_Search] ON [WarehouseProduct] ([ProductName], [ProductCode], [Barcode], [Brand], [IsActive])"
            };

            foreach (var index in compositeIndexes)
            {
                try
                {
                    // 先检查索引是否存在
                    var existsQuery = $@"
                        SELECT COUNT(*) 
                        FROM sys.indexes i
                        INNER JOIN sys.objects o ON i.object_id = o.object_id
                        WHERE i.name = '{index.Key}' AND o.name = 'WarehouseProduct'";

                    var indexExists = await db.Ado.SqlQuerySingleAsync<int>(existsQuery);

                    if (indexExists == 0)
                    {
                        await db.Ado.ExecuteCommandAsync(index.Value);
                        Console.WriteLine($"✓ 复合索引创建成功: {index.Key}");
                    }
                    else
                    {
                        Console.WriteLine($"◆ 复合索引已存在: {index.Key}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ 复合索引创建失败: {index.Key} - {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 创建关联表索引（SQL Server兼容）
        /// </summary>
        private static async Task CreateRelatedTableIndexesAsync(ISqlSugarClient db)
        {
            var relatedIndexes = new Dictionary<string, (string tableName, string sql)>
            {
                // ProductLocation表索引
                ["IX_ProductLocation_ProductCode"] = ("ProductLocation", "CREATE NONCLUSTERED INDEX [IX_ProductLocation_ProductCode] ON [ProductLocation] ([ProductCode])"),
                ["IX_ProductLocation_LocationGuid"] = ("ProductLocation", "CREATE NONCLUSTERED INDEX [IX_ProductLocation_LocationGuid] ON [ProductLocation] ([LocationGuid]) WHERE [LocationGuid] IS NOT NULL"),
                ["IX_ProductLocation_Product_Location"] = ("ProductLocation", "CREATE NONCLUSTERED INDEX [IX_ProductLocation_Product_Location] ON [ProductLocation] ([ProductCode], [LocationGuid]) WHERE [LocationGuid] IS NOT NULL"),
                ["IX_ProductLocation_ProductCode_Active"] = ("ProductLocation", "CREATE NONCLUSTERED INDEX [IX_ProductLocation_ProductCode_Active] ON [ProductLocation] ([ProductCode]) INCLUDE ([LocationGuid]) WHERE [IsDeleted] = 0 AND [ProductCode] IS NOT NULL AND [LocationGuid] IS NOT NULL"),
                ["IX_ProductLocation_LocationGuid_Active"] = ("ProductLocation", "CREATE NONCLUSTERED INDEX [IX_ProductLocation_LocationGuid_Active] ON [ProductLocation] ([LocationGuid]) INCLUDE ([ProductCode]) WHERE [IsDeleted] = 0 AND [LocationGuid] IS NOT NULL AND [ProductCode] IS NOT NULL"),

                // WarehouseCategory表索引
                ["IX_WarehouseCategory_ParentGUID"] = ("WarehouseCategory", "CREATE NONCLUSTERED INDEX [IX_WarehouseCategory_ParentGUID] ON [WarehouseCategory] ([ParentGUID]) WHERE [ParentGUID] IS NOT NULL"),
                ["IX_WarehouseCategory_Parent_Active"] = ("WarehouseCategory", "CREATE NONCLUSTERED INDEX [IX_WarehouseCategory_Parent_Active] ON [WarehouseCategory] ([ParentGUID], [IsActive])"),
                ["IX_WarehouseCategory_SortOrder"] = ("WarehouseCategory", "CREATE NONCLUSTERED INDEX [IX_WarehouseCategory_SortOrder] ON [WarehouseCategory] ([SortOrder]) WHERE [SortOrder] IS NOT NULL"),
                ["IX_WarehouseCategory_CategoryName"] = ("WarehouseCategory", "CREATE NONCLUSTERED INDEX [IX_WarehouseCategory_CategoryName] ON [WarehouseCategory] ([CategoryName])"),

                // Product表索引（支持与WarehouseProduct的关联查询）
                ["IX_Product_ProductCode"] = ("Product", "CREATE UNIQUE NONCLUSTERED INDEX [IX_Product_ProductCode] ON [Product] ([ProductCode])"),
                ["IX_Product_ProductName"] = ("Product", "CREATE NONCLUSTERED INDEX [IX_Product_ProductName] ON [Product] ([ProductName])"),
                ["IX_Product_Barcode"] = ("Product", "CREATE NONCLUSTERED INDEX [IX_Product_Barcode] ON [Product] ([Barcode]) WHERE [Barcode] IS NOT NULL"),
                ["IX_Product_ItemNumber"] = ("Product", "CREATE NONCLUSTERED INDEX [IX_Product_ItemNumber] ON [Product] ([ItemNumber]) WHERE [ItemNumber] IS NOT NULL"),
                ["IX_Product_LocalSupplierCode"] = ("Product", "CREATE NONCLUSTERED INDEX [IX_Product_LocalSupplierCode] ON [Product] ([LocalSupplierCode]) WHERE [LocalSupplierCode] IS NOT NULL"),
                ["IX_Product_Active_ItemNumber_Lookup"] = ("Product", "CREATE NONCLUSTERED INDEX [IX_Product_Active_ItemNumber_Lookup] ON [Product] ([IsDeleted], [IsActive], [ItemNumber]) INCLUDE ([ProductCode], [Barcode], [LocalSupplierCode]) WHERE [ItemNumber] IS NOT NULL"),
                ["IX_Product_Active_Barcode_Lookup"] = ("Product", "CREATE NONCLUSTERED INDEX [IX_Product_Active_Barcode_Lookup] ON [Product] ([IsDeleted], [IsActive], [Barcode]) INCLUDE ([ProductCode], [ItemNumber], [LocalSupplierCode]) WHERE [Barcode] IS NOT NULL"),
                ["IX_Product_Active_LocalSupplier_ItemNumber"] = ("Product", "CREATE NONCLUSTERED INDEX [IX_Product_Active_LocalSupplier_ItemNumber] ON [Product] ([IsDeleted], [IsActive], [LocalSupplierCode], [ItemNumber]) INCLUDE ([ProductCode], [Barcode]) WHERE [LocalSupplierCode] IS NOT NULL"),
                ["IX_WarehouseProduct_ProductCode_NotDeleted"] = ("WarehouseProduct", "CREATE NONCLUSTERED INDEX [IX_WarehouseProduct_ProductCode_NotDeleted] ON [WarehouseProduct] ([ProductCode]) WHERE [IsDeleted] = 0"),
                ["IX_Product_ProductCategoryGUID"] = ("Product", "CREATE NONCLUSTERED INDEX [IX_Product_ProductCategoryGUID] ON [Product] ([ProductCategoryGUID]) WHERE [ProductCategoryGUID] IS NOT NULL"),
                ["IX_Product_IsActive"] = ("Product", "CREATE NONCLUSTERED INDEX [IX_Product_IsActive] ON [Product] ([IsActive])"),
                ["IX_Product_Search"] = ("Product", "CREATE NONCLUSTERED INDEX [IX_Product_Search] ON [Product] ([ProductName], [ProductCode], [Barcode], [ItemNumber], [LocalSupplierCode], [IsActive])"),

                // 购物车重载按门店、订单明细和商品编码回查，补齐关键连接过滤索引；生产大表需在低峰受控执行。
                ["IX_WareHouseOrder_StoreCode_FlowStatus_IsDeleted"] = ("WareHouseOrder", "CREATE NONCLUSTERED INDEX [IX_WareHouseOrder_StoreCode_FlowStatus_IsDeleted] ON [WareHouseOrder] ([StoreCode], [FlowStatus], [IsDeleted])"),
                ["IX_WareHouseOrderDetails_OrderGUID_IsDeleted_ProductCode"] = ("WareHouseOrderDetails", "CREATE NONCLUSTERED INDEX [IX_WareHouseOrderDetails_OrderGUID_IsDeleted_ProductCode] ON [WareHouseOrderDetails] ([OrderGUID], [IsDeleted], [ProductCode])"),
                ["IX_ProductGrade_ProductCode_IsDeleted"] = ("ProductGrade", "CREATE NONCLUSTERED INDEX [IX_ProductGrade_ProductCode_IsDeleted] ON [ProductGrade] ([ProductCode], [IsDeleted])"),
                ["IX_DomesticProduct_ProductCode"] = ("DomesticProduct", "CREATE NONCLUSTERED INDEX [IX_DomesticProduct_ProductCode] ON [DomesticProduct] ([ProductCode])")
            };

            foreach (var index in relatedIndexes)
            {
                try
                {
                    // 先检查索引是否存在
                    var existsQuery = $@"
                        SELECT COUNT(*) 
                        FROM sys.indexes i
                        INNER JOIN sys.objects o ON i.object_id = o.object_id
                        WHERE i.name = '{index.Key}' AND o.name = '{index.Value.tableName}'";

                    var indexExists = await db.Ado.SqlQuerySingleAsync<int>(existsQuery);

                    if (indexExists == 0)
                    {
                        await db.Ado.ExecuteCommandAsync(index.Value.sql);
                        Console.WriteLine($"✓ 关联表索引创建成功: {index.Key} ({index.Value.tableName})");
                    }
                    else
                    {
                        Console.WriteLine($"◆ 关联表索引已存在: {index.Key} ({index.Value.tableName})");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ 关联表索引创建失败: {index.Key} ({index.Value.tableName}) - {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 创建统计视图（SQL Server兼容）
        /// </summary>
        public static async Task CreateStatisticsViewsAsync(ISqlSugarClient db)
        {
            var views = new Dictionary<string, string>
            {
                // 商品统计视图
                ["V_WarehouseProductStats"] = @"
                CREATE VIEW [V_WarehouseProductStats] AS
                SELECT 
                    COUNT(*) as TotalProducts,
                    COUNT(CASE WHEN IsActive = 1 THEN 1 END) as ActiveProducts,
                    COUNT(CASE WHEN IsActive = 0 THEN 1 END) as InactiveProducts,
                    COALESCE(SUM(StockQuantity), 0) as TotalStock,
                    COALESCE(SUM(StockValue), 0) as TotalStockValue,
                    COUNT(CASE WHEN StockQuantity <= StockAlertQuantity 
                               AND StockQuantity IS NOT NULL 
                               AND StockAlertQuantity IS NOT NULL 
                               THEN 1 END) as StockAlertCount,
                    COUNT(CASE WHEN StockQuantity <= 0 
                               AND StockQuantity IS NOT NULL 
                               THEN 1 END) as OutOfStockCount
                FROM WarehouseProduct",

                // 按分类统计视图
                ["V_WarehouseProductStatsByCategory"] = @"
                CREATE VIEW [V_WarehouseProductStatsByCategory] AS
                SELECT 
                    wc.CategoryGUID,
                    wc.CategoryName,
                    COUNT(wp.ProductCode) as ProductCount,
                    COUNT(CASE WHEN wp.IsActive = 1 THEN 1 END) as ActiveProductCount,
                    COALESCE(SUM(wp.StockQuantity), 0) as TotalStock,
                    COALESCE(SUM(wp.StockValue), 0) as TotalStockValue
                FROM WarehouseCategory wc
                LEFT JOIN WarehouseProduct wp ON wc.CategoryGUID = wp.CategoryGUID
                WHERE wc.IsActive = 1
                GROUP BY wc.CategoryGUID, wc.CategoryName"
            };

            foreach (var view in views)
            {
                try
                {
                    // 先检查视图是否存在
                    var existsQuery = $@"
                        SELECT COUNT(*) 
                        FROM INFORMATION_SCHEMA.VIEWS 
                        WHERE TABLE_NAME = '{view.Key}'";

                    var viewExists = await db.Ado.SqlQuerySingleAsync<int>(existsQuery);

                    if (viewExists == 0)
                    {
                        await db.Ado.ExecuteCommandAsync(view.Value);
                        Console.WriteLine($"✓ 统计视图创建成功: {view.Key}");
                    }
                    else
                    {
                        Console.WriteLine($"◆ 统计视图已存在: {view.Key}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ 统计视图创建失败: {view.Key} - {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 分析数据库性能（SQL Server兼容）
        /// </summary>
        public static async Task AnalyzeDatabasePerformanceAsync(ISqlSugarClient db)
        {
            try
            {
                Console.WriteLine("开始分析数据库性能...");

                // SQL Server使用UPDATE STATISTICS更新统计信息
                var tables = new[] { "WarehouseProduct", "ProductLocation", "WarehouseCategory", "Product" };

                foreach (var table in tables)
                {
                    try
                    {
                        await db.Ado.ExecuteCommandAsync($"UPDATE STATISTICS [{table}]");
                        Console.WriteLine($"✓ 已更新表 {table} 的统计信息");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"✗ 更新表 {table} 统计信息失败: {ex.Message}");
                    }
                }

                Console.WriteLine("✓ 数据库性能分析完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ 数据库性能分析失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从SQL语句中提取索引名称
        /// </summary>
        private static string GetIndexNameFromSql(string sql)
        {
            try
            {
                var parts = sql.Split(' ');
                var indexKeyword = Array.FindIndex(parts, p => p.Equals("INDEX", StringComparison.OrdinalIgnoreCase));
                if (indexKeyword >= 0 && indexKeyword + 3 < parts.Length)
                {
                    return parts[indexKeyword + 3];
                }
            }
            catch
            {
                // 忽略解析错误
            }

            return "Unknown";
        }

        /// <summary>
        /// 检查索引是否存在（SQL Server兼容）
        /// </summary>
        public static async Task<bool> IndexExistsAsync(ISqlSugarClient db, string indexName, string tableName = "WarehouseProduct")
        {
            try
            {
                var existsQuery = $@"
                    SELECT COUNT(*) 
                    FROM sys.indexes i
                    INNER JOIN sys.objects o ON i.object_id = o.object_id
                    WHERE i.name = @indexName AND o.name = @tableName";

                var count = await db.Ado.SqlQuerySingleAsync<int>(existsQuery, new { indexName, tableName });
                return count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取所有索引信息（SQL Server兼容）
        /// </summary>
        public static async Task<List<string>> GetAllIndexesAsync(ISqlSugarClient db)
        {
            try
            {
                var query = @"
                    SELECT DISTINCT i.name
                    FROM sys.indexes i
                    INNER JOIN sys.objects o ON i.object_id = o.object_id
                    WHERE i.name IS NOT NULL
                      AND i.name NOT LIKE 'PK_%'
                      AND i.name NOT LIKE 'FK_%'
                      AND i.name LIKE 'IX_%'
                    ORDER BY i.name";

                var result = await db.Ado.GetDataTableAsync(query);

                return result.AsEnumerable()
                    .Select(row => row["name"].ToString() ?? "")
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取索引信息失败: {ex.Message}");
                return new List<string>();
            }
        }
    }
}
