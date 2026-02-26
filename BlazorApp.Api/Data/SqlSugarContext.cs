using System.Threading.Tasks;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Data
{
    public class SqlSugarContext
    {
        private readonly ISqlSugarClient _db;
        private readonly ILogger<SqlSugarContext> _logger;

        public SqlSugarContext(IConfiguration configuration, ILogger<SqlSugarContext> logger)
        {
            _logger = logger;

            var connectionString =
                configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection 连接字符串未配置");

            // 自动检测数据库类型
            var dbType = GetDatabaseType(connectionString);

            // 仅SQL Server需要MARS支持
            if (
                dbType == DbType.SqlServer
                && !connectionString.Contains(
                    "MultipleActiveResultSets",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                connectionString += ";MultipleActiveResultSets=True";
            }

            // 使用 Scoped 生命周期管理连接
            // IsAutoCloseConnection = false 由 ASP.NET Core 的 Scoped 生命周期管理
            // ASP.NET Core 会在请求结束时自动释放 Scoped 服务的资源
            // 避免并发查询时连接被自动关闭导致"连接被关闭"错误

            _db = new SqlSugarClient(
                new ConnectionConfig()
                {
                    ConnectionString = connectionString,
                    DbType = dbType,
                    IsAutoCloseConnection = true,
                    InitKeyType = InitKeyType.Attribute,
                    MoreSettings = new ConnMoreSettings()
                    {
                        IsAutoRemoveDataCache = true,
                        IsWithNoLockQuery = dbType == DbType.SqlServer, // ⭐ PostgreSQL不支持WITH(NOLOCK)
                        // 🚀 优化并发性能配置
                        SqlServerCodeFirstNvarchar = dbType == DbType.SqlServer,
                        DefaultCacheDurationInSeconds = 600,
                    },
                    // 🚀 连接池配置优化 - 支持高并发
                    ConfigureExternalServices = new ConfigureExternalServices()
                    {
                        EntityService = (c, p) =>
                        {
                            // PostgreSQL 兼容性处理
                            if (dbType == DbType.PostgreSQL && !string.IsNullOrEmpty(p.DataType))
                            {
                                // 1. 将 nvarchar 映射为 varchar
                                if (
                                    p.DataType.Contains(
                                        "nvarchar",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                )
                                {
                                    p.DataType = p.DataType.Replace(
                                        "nvarchar",
                                        "varchar",
                                        StringComparison.OrdinalIgnoreCase
                                    );
                                }

                                // 2. 将 (max) 映射为 text (PG不支持varchar(max))
                                if (
                                    p.DataType.Contains("(max)", StringComparison.OrdinalIgnoreCase)
                                )
                                {
                                    p.DataType = "text";
                                }
                            }
                        },
                    },
                }
            );

            // 设置命令超时时间（从配置读取，默认60秒）
            _db.Ado.CommandTimeOut = configuration.GetValue<int>(
                "Database:CommandTimeoutSeconds",
                60
            );

            // 调试模式 - 基于配置开关记录SQL日志
            _db.Aop.OnLogExecuting = (sql, pars) =>
            {
                if (configuration.GetValue<bool>("Database:EnableSqlLogging", false))
                {
                    _logger.LogDebug("SQL执行: {Sql}", sql);
                }
            };

            // 数据库初始化 - 基于配置开关决定是否在启动时执行
            var initOnStartup = configuration.GetValue<bool>("Database:InitializeOnStartup", false);
            if (initOnStartup)
            {
                Task.Run(() =>
                {
                    try
                    {
                        // 确保数据库存在（仅PostgreSQL需要手动创建）
                        if (dbType == DbType.PostgreSQL)
                        {
                            _db.DbMaintenance.CreateDatabase();
                        }

                        // 执行表结构检查和索引创建
                        CreateTable();
                        CreateIndexes();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "后台数据库初始化失败");
                    }
                });
            }
        }

        public ISqlSugarClient Db => _db;

        // 简化的数据访问接口
        public SimpleClient<User> UserDb => new SimpleClient<User>(_db);
        public SimpleClient<Role> RoleDb => new SimpleClient<Role>(_db);
        public SimpleClient<Store> StoreDb => new SimpleClient<Store>(_db);
        public SimpleClient<UserRole> UserRoleDb => new SimpleClient<UserRole>(_db);
        public SimpleClient<UserStore> UserStoreDb => new SimpleClient<UserStore>(_db);
        public SimpleClient<RefreshToken> RefreshTokenDb => new SimpleClient<RefreshToken>(_db);
        public SimpleClient<Product> ProductDb => new SimpleClient<Product>(_db);

        public SimpleClient<WarehouseCategory> WarehouseCategoryDb =>
            new SimpleClient<WarehouseCategory>(_db);
        public SimpleClient<WarehouseProduct> WarehouseProductDb =>
            new SimpleClient<WarehouseProduct>(_db);

        // 购物车相关实体
        public SimpleClient<Cart> CartDb => new SimpleClient<Cart>(_db);
        public SimpleClient<CartItem> CartItemDb => new SimpleClient<CartItem>(_db);

        // 新添加的实体
        public SimpleClient<ChinaSupplier> ChinaSupplierDb => new SimpleClient<ChinaSupplier>(_db);
        public SimpleClient<Location> LocationDb => new SimpleClient<Location>(_db);
        public SimpleClient<ProductLocation> ProductLocationDb =>
            new SimpleClient<ProductLocation>(_db);

        public SimpleClient<HBLocalSupplier> HBLocalSupplierDb =>
            new SimpleClient<HBLocalSupplier>(_db);

        // 义乌订单相关实体
        public SimpleClient<YIWU_Order> YiwuOrderDb => new SimpleClient<YIWU_Order>(_db);
        public SimpleClient<YIWU_OrderDetail> YiwuOrderDetailDb =>
            new SimpleClient<YIWU_OrderDetail>(_db);

        // 国内商品相关实体
        public SimpleClient<DomesticProduct> DomesticProductDb =>
            new SimpleClient<DomesticProduct>(_db);
        public SimpleClient<ProductPrefixCode> ProductPrefixCodeDb =>
            new SimpleClient<ProductPrefixCode>(_db);
        public SimpleClient<ProductSetCode> ProductSetCodeDb =>
            new SimpleClient<ProductSetCode>(_db);
        public SimpleClient<DomesticSetProduct> DomesticSetProductDb =>
            new SimpleClient<DomesticSetProduct>(_db);
        public SimpleClient<DomesticProductCreationLog> DomesticProductCreationLogDb =>
            new SimpleClient<DomesticProductCreationLog>(_db);

        // 货柜相关实体
        public SimpleClient<Container> ContainerDb => new SimpleClient<Container>(_db);
        public SimpleClient<ContainerDetail> ContainerDetailDb =>
            new SimpleClient<ContainerDetail>(_db);

        // 分店价格相关实体
        public SimpleClient<StoreRetailPrice> StoreRetailPriceDb =>
            new SimpleClient<StoreRetailPrice>(_db);
        public SimpleClient<StoreClearancePrice> StoreClearancePriceDb =>
            new SimpleClient<StoreClearancePrice>(_db);
        public SimpleClient<StoreMultiCodeProduct> StoreMultiCodeProductDb =>
            new SimpleClient<StoreMultiCodeProduct>(_db);

        public SimpleClient<StoreLocalSupplierInvoice> StoreLocalSupplierInvoiceDb =>
            new SimpleClient<StoreLocalSupplierInvoice>(_db);
        public SimpleClient<StoreLocalSupplierInvoiceDetails> StoreLocalSupplierInvoiceDetailsDb =>
            new SimpleClient<StoreLocalSupplierInvoiceDetails>(_db);
        public SimpleClient<WareHouseOrder> WareHouseOrderDb =>
            new SimpleClient<WareHouseOrder>(_db);
        public SimpleClient<WareHouseOrderDetails> WareHouseOrderDetailsDb =>
            new SimpleClient<WareHouseOrderDetails>(_db);

        // 自动定价策略相关实体
        public SimpleClient<PricingStrategy> PricingStrategyDb =>
            new SimpleClient<PricingStrategy>(_db);
        public SimpleClient<PricingStrategyDetail> PricingStrategyDetailDb =>
            new SimpleClient<PricingStrategyDetail>(_db);
        public SimpleClient<PricingStrategyTarget> PricingStrategyTargetDb =>
            new SimpleClient<PricingStrategyTarget>(_db);

        // 促销相关实体
        public SimpleClient<Promotion> PromotionDb => new SimpleClient<Promotion>(_db);
        public SimpleClient<PromotionProduct> PromotionProductDb =>
            new SimpleClient<PromotionProduct>(_db);
        public SimpleClient<PromotionStore> PromotionStoreDb =>
            new SimpleClient<PromotionStore>(_db);

        // 权限管理实体
        public SimpleClient<SysPermission> SysPermissionDb => new SimpleClient<SysPermission>(_db);
        public SimpleClient<SysRolePermission> SysRolePermissionDb =>
            new SimpleClient<SysRolePermission>(_db);

        // 收银用户相关实体
        public SimpleClient<CashRegisterUser> CashRegisterUserDb =>
            new SimpleClient<CashRegisterUser>(_db);

        // 销售统计相关实体
        public SimpleClient<DailySalesStatistic> DailySalesStatisticDb =>
            new SimpleClient<DailySalesStatistic>(_db);
        public SimpleClient<HourlySalesStatistic> HourlySalesStatisticDb =>
            new SimpleClient<HourlySalesStatistic>(_db);
        public SimpleClient<StoreSalesStatistic> StoreSalesStatisticDb =>
            new SimpleClient<StoreSalesStatistic>(_db);
        public SimpleClient<SupplierSalesStatistic> SupplierSalesStatisticDb =>
            new SimpleClient<SupplierSalesStatistic>(_db);
        public SimpleClient<StoreSupplierSalesDetail> StoreSupplierSalesDetailDb =>
            new SimpleClient<StoreSupplierSalesDetail>(_db);
        public SimpleClient<AustralianSupplierStoreSalesDetail> AustralianSupplierStoreSalesDetailDb =>
            new SimpleClient<AustralianSupplierStoreSalesDetail>(_db);
        public SimpleClient<ChinaSupplierStoreSalesDetail> ChinaSupplierStoreSalesDetailDb =>
            new SimpleClient<ChinaSupplierStoreSalesDetail>(_db);

        // 定时任务日志实体
        public SimpleClient<ScheduledTaskLog> ScheduledTaskLogDb =>
            new SimpleClient<ScheduledTaskLog>(_db);

        // 节日商品相关实体
        public SimpleClient<HolidayProduct> HolidayProductDb =>
            new SimpleClient<HolidayProduct>(_db);

        // 义乌订单相关
        public SimpleClient<YIWU_Order> YIWU_OrderDb => new SimpleClient<YIWU_Order>(_db);
        public SimpleClient<YIWU_OrderDetail> YIWU_OrderDetailDb =>
            new SimpleClient<YIWU_OrderDetail>(_db);

        private void ConfigureIndexes()
        {
            // 配置GUID字段的索引
            _db.CodeFirst.SetStringDefaultLength(200); // 设置默认字符串长度
        }

        public void CreateTable()
        {
            try
            {
                Console.WriteLine("开始检查和初始化数据库表...");

                // 智能初始化：只在需要时创建或更新表
                //  InitializeTablesIfNeeded();
                CreateNormalIndexes();

                Console.WriteLine("数据库表检查完成！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"初始化表时出现错误: {ex.Message}");
                Console.WriteLine($"错误详情: {ex.StackTrace}");
                throw;
            }
        }

        public void ForceRecreateAllTables()
        {
            try
            {
                Console.WriteLine("强制重新创建所有表（GUID主键）...");
                // 创建索引
                CreateIndexes();

                Console.WriteLine("所有表重新创建完成（GUID主键）！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"重新创建表时出现错误: {ex.Message}");
                throw;
            }
        }

        private void InitializeTablesIfNeeded()
        {
            // 定义表的创建顺序（考虑外键依赖）
            var tableTypes = new Type[]
            {
                typeof(User),
                typeof(Role),
                typeof(Store),
                typeof(UserRole),
                typeof(UserStore),
                typeof(RefreshToken),
                typeof(WarehouseCategory),
                typeof(WarehouseProduct),
                typeof(Product),
                typeof(Cart),
                typeof(CartItem),
                typeof(ChinaSupplier),
                typeof(HBLocalSupplier),
                typeof(DomesticProduct),
                typeof(SysPermission),
                typeof(SysRolePermission),
                typeof(ProductPrefixCode),
                typeof(ProductSetCode),
                typeof(DomesticSetProduct),
                typeof(DomesticProductCreationLog), // ⭐ 新增：商品创建记录表
                typeof(Location),
                typeof(ProductLocation),
                typeof(Container),
                typeof(ContainerDetail),
                typeof(YIWU_Order),
                typeof(YIWU_OrderDetail),
                typeof(StoreRetailPrice),
                typeof(StoreClearancePrice),
                typeof(StoreMultiCodeProduct),
                typeof(StoreLocalSupplierInvoice),
                typeof(StoreLocalSupplierInvoiceDetails),
                typeof(WareHouseOrder),
                typeof(WareHouseOrderDetails),
                typeof(PricingStrategy),
                typeof(PricingStrategyDetail),
                typeof(PricingStrategyTarget),
                typeof(Promotion),
                typeof(PromotionProduct),
                typeof(PromotionStore),
                typeof(SysPermission),
                typeof(SysRolePermission),
                typeof(CashRegisterUser),
                typeof(DailySalesStatistic),
                typeof(HourlySalesStatistic),
                typeof(StoreSalesStatistic),
                typeof(SupplierSalesStatistic),
                typeof(StoreSupplierSalesDetail),
                typeof(AustralianSupplierStoreSalesDetail),
                typeof(ChinaSupplierStoreSalesDetail),
                typeof(ScheduledTaskLog),
                typeof(HolidayProduct),
            };

            // 检查并创建不存在的表
            foreach (var tableType in tableTypes)
            {
                var tableName = _db.EntityMaintenance.GetTableName(tableType);

                if (!_db.DbMaintenance.IsAnyTable(tableName))
                {
                    Console.WriteLine($"表 {tableName} 不存在，正在创建...");
                    _db.CodeFirst.InitTables(tableType);
                    Console.WriteLine($"✓ {tableName} 表创建成功");
                }
                else
                {
                    Console.WriteLine($"✓ {tableName} 表已存在");

                    // 检查是否需要更新表结构
                    try
                    {
                        // 使用CodeFirst的非删除模式更新表结构
                        _db.CodeFirst.InitTables(tableType);
                        Console.WriteLine($"✓ {tableName} 表结构检查完成");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ {tableName} 表结构更新时出现警告: {ex.Message}");
                    }
                }
            }

            // 检查并创建索引
            try
            {
                Console.WriteLine("检查索引状态...");
                CreateIndexesIfNotExists();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"创建索引时出现警告: {ex.Message}");
            }
        }

        private void RecreateAllTables()
        {
            Console.WriteLine("开始删除旧表...");

            // 按相反顺序删除表（避免外键约束问题）
            try
            {
                _db.DbMaintenance.DropTable<ContainerDetail>();
                Console.WriteLine("✓ 删除ContainerDetail表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<Container>();
                Console.WriteLine("✓ 删除Container表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<CashRegisterUser>();
                Console.WriteLine("✓ 删除CashRegisterUser表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<YIWU_OrderDetail>();
                Console.WriteLine("✓ 删除YIWU_OrderDetail表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<YIWU_Order>();
                Console.WriteLine("✓ 删除YIWU_Order表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<ProductLocation>();
                Console.WriteLine("✓ 删除ProductLocation表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<Location>();
                Console.WriteLine("✓ 删除Location表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<ChinaSupplier>();
                Console.WriteLine("✓ 删除Supplier表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<DomesticProductCreationLog>();
                Console.WriteLine("✓ 删除DomesticProductCreationLog表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<DomesticSetProduct>();
                Console.WriteLine("✓ 删除DomesticSetProduct表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<DomesticProduct>();
                Console.WriteLine("✓ 删除DomesticProduct表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<ProductSetCode>();
                Console.WriteLine("✓ 删除ProductSetCode表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<ProductPrefixCode>();
                Console.WriteLine("✓ 删除ProductPrefixCode表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<CartItem>();
                Console.WriteLine("✓ 删除CartItem表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<Cart>();
                Console.WriteLine("✓ 删除Cart表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<Product>();
                Console.WriteLine("✓ 删除Product表");
            }
            catch { }

            try
            {
                _db.DbMaintenance.DropTable<WarehouseProduct>();
                Console.WriteLine("✓ 删除WarehouseProduct表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<WarehouseCategory>();
                Console.WriteLine("✓ 删除WarehouseCategory表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<RefreshToken>();
                Console.WriteLine("✓ 删除RefreshToken表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<UserStore>();
                Console.WriteLine("✓ 删除UserStore表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<UserRole>();
                Console.WriteLine("✓ 删除UserRole表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<Store>();
                Console.WriteLine("✓ 删除Store表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<Role>();
                Console.WriteLine("✓ 删除Role表");
            }
            catch { }
            try
            {
                _db.DbMaintenance.DropTable<User>();
                Console.WriteLine("✓ 删除User表");
            }
            catch { }

            Console.WriteLine("开始创建新表...");

            // 按依赖顺序创建表
            _db.CodeFirst.InitTables(typeof(User));
            Console.WriteLine("✓ User表创建成功");

            _db.CodeFirst.InitTables(typeof(Role));
            Console.WriteLine("✓ Role表创建成功");

            _db.CodeFirst.InitTables(typeof(Store));
            Console.WriteLine("✓ Store表创建成功");

            _db.CodeFirst.InitTables(typeof(UserRole));
            Console.WriteLine("✓ UserRole表创建成功");

            _db.CodeFirst.InitTables(typeof(UserStore));
            Console.WriteLine("✓ UserStore表创建成功");

            _db.CodeFirst.InitTables(typeof(RefreshToken));
            Console.WriteLine("✓ RefreshToken表创建成功");

            _db.CodeFirst.InitTables(typeof(WarehouseCategory));
            Console.WriteLine("✓ WarehouseCategory表创建成功");

            _db.CodeFirst.InitTables(typeof(WarehouseProduct));
            Console.WriteLine("✓ WarehouseProduct表创建成功");

            _db.CodeFirst.InitTables(typeof(Product));
            Console.WriteLine("✓ Product表创建成功");

            _db.CodeFirst.InitTables(typeof(Cart));
            Console.WriteLine("✓ Cart表创建成功");

            _db.CodeFirst.InitTables(typeof(CartItem));
            Console.WriteLine("✓ CartItem表创建成功");

            _db.CodeFirst.InitTables(typeof(ChinaSupplier));
            Console.WriteLine("✓ Supplier表创建成功");

            _db.CodeFirst.InitTables(typeof(DomesticProduct));
            Console.WriteLine("✓ DomesticProduct表创建成功");

            _db.CodeFirst.InitTables(typeof(ProductPrefixCode));
            Console.WriteLine("✓ ProductPrefixCode表创建成功");

            _db.CodeFirst.InitTables(typeof(ProductSetCode));
            Console.WriteLine("✓ ProductSetCode表创建成功");

            _db.CodeFirst.InitTables(typeof(DomesticSetProduct));
            Console.WriteLine("✓ DomesticSetProduct表创建成功");

            _db.CodeFirst.InitTables(typeof(DomesticProductCreationLog));
            Console.WriteLine("✓ DomesticProductCreationLog表创建成功");

            _db.CodeFirst.InitTables(typeof(Location));
            Console.WriteLine("✓ Location表创建成功");

            _db.CodeFirst.InitTables(typeof(ProductLocation));
            Console.WriteLine("✓ ProductLocation表创建成功");

            _db.CodeFirst.InitTables(typeof(YIWU_Order));
            Console.WriteLine("✓ YIWU_Order表创建成功");

            _db.CodeFirst.InitTables(typeof(YIWU_OrderDetail));
            Console.WriteLine("✓ YIWU_OrderDetail表创建成功");

            _db.CodeFirst.InitTables(typeof(Container));
            Console.WriteLine("✓ Container表创建成功");

            _db.CodeFirst.InitTables(typeof(ContainerDetail));
            Console.WriteLine("✓ ContainerDetail表创建成功");

            _db.CodeFirst.InitTables(typeof(CashRegisterUser));
            Console.WriteLine("✓ CashRegisterUser表创建成功");

            // 创建索引
            CreateIndexes();
        }

        private void CreateIndexesIfNotExists()
        {
            try
            {
                Console.WriteLine("检查并创建缺失的索引...");

                if (_db.CurrentConnectionConfig.DbType == DbType.SqlServer)
                {
                    // 创建唯一索引（如果不存在）
                    CreateUniqueIndexesIfNotExists();

                    // 创建普通索引（如果不存在）
                    CreateNormalIndexesIfNotExists();
                }
                else if (_db.CurrentConnectionConfig.DbType == DbType.PostgreSQL)
                {
                    CreatePostgreSQLIndexesIfNotExists();
                }

                Console.WriteLine("✓ 索引检查完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查索引时出现错误: {ex.Message}");
                // 索引创建失败不影响主要功能，仅记录日志
            }
        }

        private void CreatePostgreSQLIndexesIfNotExists()
        {
            Console.WriteLine("检查 PostgreSQL 索引...");

            var indexStatements = new Dictionary<string, string>
            {
                // 唯一索引
                ["IX_User_Username_Unique"] =
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_User_Username_Unique\" ON \"User\" (\"Username\")",
                ["IX_User_Email_Unique"] =
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_User_Email_Unique\" ON \"User\" (\"Email\")",
                ["IX_Role_RoleName_Unique"] =
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Role_RoleName_Unique\" ON \"Role\" (\"RoleName\")",
                ["IX_Store_StoreCode_Unique"] =
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Store_StoreCode_Unique\" ON \"Store\" (\"StoreCode\")",
                ["IX_UserRole_UserRole_Unique"] =
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_UserRole_UserRole_Unique\" ON \"UserRole\" (\"UserGUID\", \"RoleGUID\")",
                ["IX_UserStore_UserStore_Unique"] =
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_UserStore_UserStore_Unique\" ON \"UserStore\" (\"UserGUID\", \"StoreGUID\")",
                ["IX_RefreshToken_Token_Unique"] =
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_RefreshToken_Token_Unique\" ON \"RefreshToken\" (\"Token\")",
                ["IX_Cart_UserGUID_Unique"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_Cart_UserGUID_Unique\" ON \"Cart\" (\"UserGUID\")", // 原代码中此索引非唯一，保持一致
                ["IX_CartItem_CartGUID_ProductCode_Unique"] =
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_CartItem_CartGUID_ProductCode_Unique\" ON \"CartItem\" (\"CartGUID\", \"ProductCode\")",
                ["IX_LocalSupplier_Code_Unique"] =
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_LocalSupplier_Code_Unique\" ON \"LocalSupplier\" (\"LocalSupplierCode\")",

                // 普通索引
                ["IX_User_IsActive"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_User_IsActive\" ON \"User\" (\"IsActive\")",
                ["IX_User_CreatedAt"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_User_CreatedAt\" ON \"User\" (\"CreatedAt\")",
                ["IX_User_LastLoginAt"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_User_LastLoginAt\" ON \"User\" (\"LastLoginAt\")",
                ["IX_Role_IsActive"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_Role_IsActive\" ON \"Role\" (\"IsActive\")",
                ["IX_Role_CreatedAt"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_Role_CreatedAt\" ON \"Role\" (\"CreatedAt\")",
                ["IX_Store_IsActive"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_Store_IsActive\" ON \"Store\" (\"IsActive\")",
                ["IX_Store_CreatedAt"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_Store_CreatedAt\" ON \"Store\" (\"CreatedAt\")",
                ["IX_UserRole_UserGUID"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_UserRole_UserGUID\" ON \"UserRole\" (\"UserGUID\")",
                ["IX_UserRole_RoleGUID"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_UserRole_RoleGUID\" ON \"UserRole\" (\"RoleGUID\")",
                ["IX_UserStore_UserGUID"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_UserStore_UserGUID\" ON \"UserStore\" (\"UserGUID\")",
                ["IX_UserStore_StoreGUID"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_UserStore_StoreGUID\" ON \"UserStore\" (\"StoreGUID\")",
                ["IX_RefreshToken_UserGUID"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_RefreshToken_UserGUID\" ON \"RefreshToken\" (\"UserGUID\")",
                ["IX_RefreshToken_ExpiresAt"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_RefreshToken_ExpiresAt\" ON \"RefreshToken\" (\"ExpiresAt\")",
                ["IX_Cart_CartStatus"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_Cart_CartStatus\" ON \"Cart\" (\"CartStatus\")",
                ["IX_Cart_LastModified"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_Cart_LastModified\" ON \"Cart\" (\"LastModified\")",
                ["IX_Cart_ExpiresAt"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_Cart_ExpiresAt\" ON \"Cart\" (\"ExpiresAt\")",
                ["IX_CartItem_CartGUID"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_CartItem_CartGUID\" ON \"CartItem\" (\"CartGUID\")",
                ["IX_CartItem_ProductCode"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_CartItem_ProductCode\" ON \"CartItem\" (\"ProductCode\")",
                ["IX_CartItem_AddedAt"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_CartItem_AddedAt\" ON \"CartItem\" (\"AddedAt\")",
                ["IX_LocalSupplier_Status"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_LocalSupplier_Status\" ON \"LocalSupplier\" (\"Status\")",
                ["IX_LocalSupplier_Name"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_LocalSupplier_Name\" ON \"LocalSupplier\" (\"Name\")",
                ["IX_DailySalesStatistic_Date"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_DailySalesStatistic_Date\" ON \"DailySalesStatistic\" (\"Date\")",
                ["IX_HourlySalesStatistic_Date_BranchCode_Hour"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_HourlySalesStatistic_Date_BranchCode_Hour\" ON \"HourlySalesStatistic\" (\"Date\", \"BranchCode\", \"Hour\")",
                ["IX_StoreSalesStatistic_Date_TotalAmount"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_StoreSalesStatistic_Date_TotalAmount\" ON \"StoreSalesStatistic\" (\"Date\", \"TotalAmount\")",
                ["IX_SupplierSalesStatistic_Date_IsDomestic_TotalAmount"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_SupplierSalesStatistic_Date_IsDomestic_TotalAmount\" ON \"SupplierSalesStatistic\" (\"Date\", \"IsDomestic\", \"TotalAmount\")",
                ["IX_StoreSupplierSalesDetail_Date_BranchCode_SupplierCode"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_StoreSupplierSalesDetail_Date_BranchCode_SupplierCode\" ON \"StoreSupplierSalesDetail\" (\"Date\", \"BranchCode\", \"SupplierCode\")",
                ["IX_ScheduledTaskLog_TaskType"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_ScheduledTaskLog_TaskType\" ON \"ScheduledTaskLog\" (\"TaskType\")",
                ["IX_ScheduledTaskLog_Status"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_ScheduledTaskLog_Status\" ON \"ScheduledTaskLog\" (\"Status\")",
                ["IX_ScheduledTaskLog_StartedAt"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_ScheduledTaskLog_StartedAt\" ON \"ScheduledTaskLog\" (\"StartedAt\")",
                ["IX_ScheduledTaskLog_TaskType_Status"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_ScheduledTaskLog_TaskType_Status\" ON \"ScheduledTaskLog\" (\"TaskType\", \"Status\")",
                ["IX_ScheduledTaskLog_ScheduledTime"] =
                    "CREATE INDEX IF NOT EXISTS \"IX_ScheduledTaskLog_ScheduledTime\" ON \"ScheduledTaskLog\" (\"ScheduledTime\")",
            };

            foreach (var indexCheck in indexStatements)
            {
                try
                {
                    _db.Ado.ExecuteCommand(indexCheck.Value);
                    Console.WriteLine($"✓ PostgreSQL 索引 {indexCheck.Key} 检查/创建完成");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"⚠️ 创建 PostgreSQL 索引 {indexCheck.Key} 时出现警告: {ex.Message}"
                    );
                }
            }
        }

        private void CreateUniqueIndexesIfNotExists()
        {
            Console.WriteLine("检查唯一索引...");

            // 检查并创建唯一索引的SQL语句
            var uniqueIndexChecks = new Dictionary<string, string>
            {
                // 注意：主键GUID字段现在由SqlSugar自动处理唯一性，不需要额外的唯一索引
                ["IX_User_Username_Unique"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_User_Username_Unique' AND object_id = OBJECT_ID('User')) CREATE UNIQUE INDEX IX_User_Username_Unique ON [User](Username)",
                ["IX_User_Email_Unique"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_User_Email_Unique' AND object_id = OBJECT_ID('User')) CREATE UNIQUE INDEX IX_User_Email_Unique ON [User](Email)",
                ["IX_Role_RoleName_Unique"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Role_RoleName_Unique' AND object_id = OBJECT_ID('Role')) CREATE UNIQUE INDEX IX_Role_RoleName_Unique ON [Role](RoleName)",
                ["IX_Store_StoreCode_Unique"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Store_StoreCode_Unique' AND object_id = OBJECT_ID('Store')) CREATE UNIQUE INDEX IX_Store_StoreCode_Unique ON [Store](StoreCode)",
                ["IX_UserRole_UserRole_Unique"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserRole_UserRole_Unique' AND object_id = OBJECT_ID('UserRole')) CREATE UNIQUE INDEX IX_UserRole_UserRole_Unique ON [UserRole](UserGUID, RoleGUID)",
                ["IX_UserStore_UserStore_Unique"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserStore_UserStore_Unique' AND object_id = OBJECT_ID('UserStore')) CREATE UNIQUE INDEX IX_UserStore_UserStore_Unique ON [UserStore](UserGUID, StoreGUID)",
                ["IX_RefreshToken_Token_Unique"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_RefreshToken_Token_Unique' AND object_id = OBJECT_ID('RefreshToken')) CREATE UNIQUE INDEX IX_RefreshToken_Token_Unique ON [RefreshToken](Token)",
                ["IX_Cart_UserGUID_Unique"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Cart_UserGUID_Unique' AND object_id = OBJECT_ID('Cart')) CREATE INDEX IX_Cart_UserGUID_Unique ON [Cart](UserGUID)",
                ["IX_CartItem_CartGUID_ProductCode_Unique"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CartItem_CartGUID_ProductCode_Unique' AND object_id = OBJECT_ID('CartItem')) CREATE UNIQUE INDEX IX_CartItem_CartGUID_ProductCode_Unique ON [CartItem](CartGUID, ProductCode)",
                ["IX_LocalSupplier_Code_Unique"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_LocalSupplier_Code_Unique' AND object_id = OBJECT_ID('LocalSupplier')) CREATE UNIQUE INDEX IX_LocalSupplier_Code_Unique ON [LocalSupplier](LocalSupplierCode)",
            };

            foreach (var indexCheck in uniqueIndexChecks)
            {
                try
                {
                    _db.Ado.ExecuteCommand(indexCheck.Value);
                    Console.WriteLine($"✓ 唯一索引 {indexCheck.Key} 检查完成");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ 创建唯一索引 {indexCheck.Key} 时出现警告: {ex.Message}");
                }
            }
        }

        private void CreateNormalIndexesIfNotExists()
        {
            Console.WriteLine("检查普通索引...");

            // 检查并创建普通索引的SQL语句
            var normalIndexChecks = new Dictionary<string, string>
            {
                ["IX_User_IsActive"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_User_IsActive' AND object_id = OBJECT_ID('User')) CREATE INDEX IX_User_IsActive ON [User](IsActive)",
                ["IX_User_CreatedAt"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_User_CreatedAt' AND object_id = OBJECT_ID('User')) CREATE INDEX IX_User_CreatedAt ON [User](CreatedAt)",
                ["IX_User_LastLoginAt"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_User_LastLoginAt' AND object_id = OBJECT_ID('User')) CREATE INDEX IX_User_LastLoginAt ON [User](LastLoginAt)",
                ["IX_Role_IsActive"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Role_IsActive' AND object_id = OBJECT_ID('Role')) CREATE INDEX IX_Role_IsActive ON [Role](IsActive)",
                ["IX_Role_CreatedAt"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Role_CreatedAt' AND object_id = OBJECT_ID('Role')) CREATE INDEX IX_Role_CreatedAt ON [Role](CreatedAt)",
                ["IX_Store_IsActive"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Store_IsActive' AND object_id = OBJECT_ID('Store')) CREATE INDEX IX_Store_IsActive ON [Store](IsActive)",
                ["IX_Store_CreatedAt"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Store_CreatedAt' AND object_id = OBJECT_ID('Store')) CREATE INDEX IX_Store_CreatedAt ON [Store](CreatedAt)",
                ["IX_UserRole_UserGUID"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserRole_UserGUID' AND object_id = OBJECT_ID('UserRole')) CREATE INDEX IX_UserRole_UserGUID ON [UserRole](UserGUID)",
                ["IX_UserRole_RoleGUID"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserRole_RoleGUID' AND object_id = OBJECT_ID('UserRole')) CREATE INDEX IX_UserRole_RoleGUID ON [UserRole](RoleGUID)",
                ["IX_UserStore_UserGUID"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserStore_UserGUID' AND object_id = OBJECT_ID('UserStore')) CREATE INDEX IX_UserStore_UserGUID ON [UserStore](UserGUID)",
                ["IX_UserStore_StoreGUID"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserStore_StoreGUID' AND object_id = OBJECT_ID('UserStore')) CREATE INDEX IX_UserStore_StoreGUID ON [UserStore](StoreGUID)",
                ["IX_RefreshToken_UserGUID"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_RefreshToken_UserGUID' AND object_id = OBJECT_ID('RefreshToken')) CREATE INDEX IX_RefreshToken_UserGUID ON [RefreshToken](UserGUID)",
                ["IX_RefreshToken_ExpiresAt"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_RefreshToken_ExpiresAt' AND object_id = OBJECT_ID('RefreshToken')) CREATE INDEX IX_RefreshToken_ExpiresAt ON [RefreshToken](ExpiresAt)",
                ["IX_Cart_CartStatus"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Cart_CartStatus' AND object_id = OBJECT_ID('Cart')) CREATE INDEX IX_Cart_CartStatus ON [Cart](CartStatus)",
                ["IX_Cart_LastModified"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Cart_LastModified' AND object_id = OBJECT_ID('Cart')) CREATE INDEX IX_Cart_LastModified ON [Cart](LastModified)",
                ["IX_Cart_ExpiresAt"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Cart_ExpiresAt' AND object_id = OBJECT_ID('Cart')) CREATE INDEX IX_Cart_ExpiresAt ON [Cart](ExpiresAt)",
                ["IX_CartItem_CartGUID"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CartItem_CartGUID' AND object_id = OBJECT_ID('CartItem')) CREATE INDEX IX_CartItem_CartGUID ON [CartItem](CartGUID)",
                ["IX_CartItem_ProductCode"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CartItem_ProductCode' AND object_id = OBJECT_ID('CartItem')) CREATE INDEX IX_CartItem_ProductCode ON [CartItem](ProductCode)",
                ["IX_CartItem_AddedAt"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CartItem_AddedAt' AND object_id = OBJECT_ID('CartItem')) CREATE INDEX IX_CartItem_AddedAt ON [CartItem](AddedAt)",
                ["IX_LocalSupplier_Status"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_LocalSupplier_Status' AND object_id = OBJECT_ID('LocalSupplier')) CREATE INDEX IX_LocalSupplier_Status ON [LocalSupplier](Status)",
                ["IX_LocalSupplier_Name"] =
                    "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_LocalSupplier_Name' AND object_id = OBJECT_ID('LocalSupplier')) CREATE INDEX IX_LocalSupplier_Name ON [LocalSupplier](Name)",
            };

            foreach (var indexCheck in normalIndexChecks)
            {
                try
                {
                    _db.Ado.ExecuteCommand(indexCheck.Value);
                    Console.WriteLine($"✓ 普通索引 {indexCheck.Key} 检查完成");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ 创建普通索引 {indexCheck.Key} 时出现警告: {ex.Message}");
                }
            }
        }

        public void CreateIndexes()
        {
            try
            {
                Console.WriteLine("开始创建GUID字段的索引...");

                if (_db.CurrentConnectionConfig.DbType == DbType.SqlServer)
                {
                    // 创建唯一索引
                    // CreateUniqueIndexes();

                    // 创建普通索引
                    CreateNormalIndexes();
                }
                else if (_db.CurrentConnectionConfig.DbType == DbType.PostgreSQL)
                {
                    CreatePostgreSQLIndexesIfNotExists();
                }

                Console.WriteLine("✓ 所有索引创建成功");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"创建索引时出现错误: {ex.Message}");
                // 索引创建失败不影响主要功能，仅记录日志
            }
        }

        private void CreateUniqueIndexes()
        {
            Console.WriteLine("创建唯一索引...");

            // 使用SQL语句直接创建唯一索引 (SQL Server语法)
            var uniqueIndexStatements = new[]
            {
                // User表的唯一索引（主键GUID字段自动由SqlSugar处理，无需手动创建索引）
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_User_Username' AND object_id = OBJECT_ID('User')) CREATE UNIQUE INDEX IX_User_Username ON [User](Username)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_User_Email' AND object_id = OBJECT_ID('User')) CREATE UNIQUE INDEX IX_User_Email ON [User](Email)",
                // Role表的唯一索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Role_RoleName' AND object_id = OBJECT_ID('Role')) CREATE UNIQUE INDEX IX_Role_RoleName ON [Role](RoleName)",
                // Store表的唯一索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Store_StoreCode' AND object_id = OBJECT_ID('Store')) CREATE UNIQUE INDEX IX_Store_StoreCode ON [Store](StoreCode)",
                // UserRole表的唯一索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserRole_UserRole_Unique' AND object_id = OBJECT_ID('UserRole')) CREATE UNIQUE INDEX IX_UserRole_UserRole_Unique ON [UserRole](UserGUID, RoleGUID)",
                // UserStore表的唯一索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserStore_UserStore_Unique' AND object_id = OBJECT_ID('UserStore')) CREATE UNIQUE INDEX IX_UserStore_UserStore_Unique ON [UserStore](UserGUID, StoreGUID)",
                // RefreshToken表的唯一索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_RefreshToken_Token' AND object_id = OBJECT_ID('RefreshToken')) CREATE UNIQUE INDEX IX_RefreshToken_Token ON [RefreshToken](Token)",
                // Cart表的索引（用户GUID非唯一，一个用户可能有多个购物车）
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Cart_UserGUID' AND object_id = OBJECT_ID('Cart')) CREATE INDEX IX_Cart_UserGUID ON [Cart](UserGUID)",
                // CartItem表的复合唯一索引（一个购物车中同一商品不能重复）
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CartItem_CartGUID_ProductCode' AND object_id = OBJECT_ID('CartItem')) CREATE UNIQUE INDEX IX_CartItem_CartGUID_ProductCode ON [CartItem](CartGUID, ProductCode)",
            };

            foreach (var sql in uniqueIndexStatements)
            {
                try
                {
                    _db.Ado.ExecuteCommand(sql);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"创建唯一索引失败: {ex.Message}");
                }
            }

            Console.WriteLine("✓ 唯一索引创建完成");
        }

        private void CreateNormalIndexes()
        {
            Console.WriteLine("创建普通索引...");

            // 使用SQL语句创建普通索引 (SQL Server语法)
            var normalIndexStatements = new[]
            {
                // User表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_User_IsActive' AND object_id = OBJECT_ID('User')) CREATE INDEX IX_User_IsActive ON [User](IsActive)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_User_CreatedAt' AND object_id = OBJECT_ID('User')) CREATE INDEX IX_User_CreatedAt ON [User](CreatedAt)",
                // Role表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Role_IsActive' AND object_id = OBJECT_ID('Role')) CREATE INDEX IX_Role_IsActive ON [Role](IsActive)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Role_CreatedAt' AND object_id = OBJECT_ID('Role')) CREATE INDEX IX_Role_CreatedAt ON [Role](CreatedAt)",
                // Store表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Store_StoreName' AND object_id = OBJECT_ID('Store')) CREATE INDEX IX_Store_StoreName ON [Store](StoreName)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Store_IsActive' AND object_id = OBJECT_ID('Store')) CREATE INDEX IX_Store_IsActive ON [Store](IsActive)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Store_CreatedAt' AND object_id = OBJECT_ID('Store')) CREATE INDEX IX_Store_CreatedAt ON [Store](CreatedAt)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Store_UpdatedAt' AND object_id = OBJECT_ID('Store')) CREATE INDEX IX_Store_UpdatedAt ON [Store](UpdatedAt)",
                // UserRole表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserRole_UserGUID' AND object_id = OBJECT_ID('UserRole')) CREATE INDEX IX_UserRole_UserGUID ON [UserRole](UserGUID)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserRole_RoleGUID' AND object_id = OBJECT_ID('UserRole')) CREATE INDEX IX_UserRole_RoleGUID ON [UserRole](RoleGUID)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserRole_AssignedAt' AND object_id = OBJECT_ID('UserRole')) CREATE INDEX IX_UserRole_AssignedAt ON [UserRole](AssignedAt)",
                // UserStore表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserStore_UserGUID' AND object_id = OBJECT_ID('UserStore')) CREATE INDEX IX_UserStore_UserGUID ON [UserStore](UserGUID)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserStore_StoreGUID' AND object_id = OBJECT_ID('UserStore')) CREATE INDEX IX_UserStore_StoreGUID ON [UserStore](StoreGUID)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserStore_AssignedAt' AND object_id = OBJECT_ID('UserStore')) CREATE INDEX IX_UserStore_AssignedAt ON [UserStore](AssignedAt)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserStore_AssignedByGUID' AND object_id = OBJECT_ID('UserStore')) CREATE INDEX IX_UserStore_AssignedByGUID ON [UserStore](AssignedByGUID)",
                // RefreshToken表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_RefreshToken_UserGUID' AND object_id = OBJECT_ID('RefreshToken')) CREATE INDEX IX_RefreshToken_UserGUID ON [RefreshToken](UserGUID)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_RefreshToken_ExpiresAt' AND object_id = OBJECT_ID('RefreshToken')) CREATE INDEX IX_RefreshToken_ExpiresAt ON [RefreshToken](ExpiresAt)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_RefreshToken_CreatedAt' AND object_id = OBJECT_ID('RefreshToken')) CREATE INDEX IX_RefreshToken_CreatedAt ON [RefreshToken](CreatedAt)",
                // Cart表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Cart_CartStatus' AND object_id = OBJECT_ID('Cart')) CREATE INDEX IX_Cart_CartStatus ON [Cart](CartStatus)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Cart_LastModified' AND object_id = OBJECT_ID('Cart')) CREATE INDEX IX_Cart_LastModified ON [Cart](LastModified)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Cart_ExpiresAt' AND object_id = OBJECT_ID('Cart')) CREATE INDEX IX_Cart_ExpiresAt ON [Cart](ExpiresAt)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Cart_CreatedAt' AND object_id = OBJECT_ID('Cart')) CREATE INDEX IX_Cart_CreatedAt ON [Cart](CreatedAt)",
                // CartItem表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CartItem_CartGUID' AND object_id = OBJECT_ID('CartItem')) CREATE INDEX IX_CartItem_CartGUID ON [CartItem](CartGUID)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CartItem_ProductCode' AND object_id = OBJECT_ID('CartItem')) CREATE INDEX IX_CartItem_ProductCode ON [CartItem](ProductCode)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CartItem_AddedAt' AND object_id = OBJECT_ID('CartItem')) CREATE INDEX IX_CartItem_AddedAt ON [CartItem](AddedAt)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CartItem_LastUpdated' AND object_id = OBJECT_ID('CartItem')) CREATE INDEX IX_CartItem_LastUpdated ON [CartItem](LastUpdated)",
                // LocalSupplier表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_LocalSupplier_Status' AND object_id = OBJECT_ID('LocalSupplier')) CREATE INDEX IX_LocalSupplier_Status ON [LocalSupplier](Status)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_LocalSupplier_Name' AND object_id = OBJECT_ID('LocalSupplier')) CREATE INDEX IX_LocalSupplier_Name ON [LocalSupplier](Name)",
                // DailySalesStatistic表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DailySalesStatistic_Date' AND object_id = OBJECT_ID('DailySalesStatistic')) CREATE INDEX IX_DailySalesStatistic_Date ON [DailySalesStatistic](Date)",
                // HourlySalesStatistic表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_HourlySalesStatistic_Date_BranchCode_Hour' AND object_id = OBJECT_ID('HourlySalesStatistic')) CREATE INDEX IX_HourlySalesStatistic_Date_BranchCode_Hour ON [HourlySalesStatistic](Date, BranchCode, Hour)",
                // StoreSalesStatistic表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StoreSalesStatistic_Date_TotalAmount' AND object_id = OBJECT_ID('StoreSalesStatistic')) CREATE INDEX IX_StoreSalesStatistic_Date_TotalAmount ON [StoreSalesStatistic](Date, TotalAmount)",
                // SupplierSalesStatistic表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SupplierSalesStatistic_Date_IsDomestic_TotalAmount' AND object_id = OBJECT_ID('SupplierSalesStatistic')) CREATE INDEX IX_SupplierSalesStatistic_Date_IsDomestic_TotalAmount ON [SupplierSalesStatistic](Date, IsDomestic, TotalAmount)",
                // StoreSupplierSalesDetail表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StoreSupplierSalesDetail_Date_BranchCode_SupplierCode' AND object_id = OBJECT_ID('StoreSupplierSalesDetail')) CREATE INDEX IX_StoreSupplierSalesDetail_Date_BranchCode_SupplierCode ON [StoreSupplierSalesDetail](Date, BranchCode, SupplierCode)",
                // StoreRetailPrice表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StoreRetailPrice_ProductCode_StoreCode' AND object_id = OBJECT_ID('StoreRetailPrice')) CREATE INDEX IX_StoreRetailPrice_ProductCode_StoreCode ON [StoreRetailPrice](ProductCode, StoreCode)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StoreRetailPrice_StoreCode' AND object_id = OBJECT_ID('StoreRetailPrice')) CREATE INDEX IX_StoreRetailPrice_StoreCode ON [StoreRetailPrice](StoreCode)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StoreRetailPrice_SupplierCode' AND object_id = OBJECT_ID('StoreRetailPrice')) CREATE INDEX IX_StoreRetailPrice_SupplierCode ON [StoreRetailPrice](SupplierCode)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StoreRetailPrice_PurchasePrice' AND object_id = OBJECT_ID('StoreRetailPrice')) CREATE INDEX IX_StoreRetailPrice_PurchasePrice ON [StoreRetailPrice](PurchasePrice)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StoreRetailPrice_StoreRetailPriceValue' AND object_id = OBJECT_ID('StoreRetailPrice')) CREATE INDEX IX_StoreRetailPrice_StoreRetailPriceValue ON [StoreRetailPrice](StoreRetailPriceValue)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StoreRetailPrice_DiscountRate' AND object_id = OBJECT_ID('StoreRetailPrice')) CREATE INDEX IX_StoreRetailPrice_DiscountRate ON [StoreRetailPrice](DiscountRate)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StoreRetailPrice_IsActive' AND object_id = OBJECT_ID('StoreRetailPrice')) CREATE INDEX IX_StoreRetailPrice_IsActive ON [StoreRetailPrice](IsActive)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StoreRetailPrice_IsAutoPricing' AND object_id = OBJECT_ID('StoreRetailPrice')) CREATE INDEX IX_StoreRetailPrice_IsAutoPricing ON [StoreRetailPrice](IsAutoPricing)",
                // Product表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Product_ProductName' AND object_id = OBJECT_ID('Product')) CREATE INDEX IX_Product_ProductName ON [Product](ProductName)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Product_IsActive' AND object_id = OBJECT_ID('Product')) CREATE INDEX IX_Product_IsActive ON [Product](IsActive)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Product_IsSpecialProduct' AND object_id = OBJECT_ID('Product')) CREATE INDEX IX_Product_IsSpecialProduct ON [Product](IsSpecialProduct)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Product_WarehouseCategoryGUID' AND object_id = OBJECT_ID('Product')) CREATE INDEX IX_Product_WarehouseCategoryGUID ON [Product](WarehouseCategoryGUID)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Product_ProductType' AND object_id = OBJECT_ID('Product')) CREATE INDEX IX_Product_ProductType ON [Product](ProductType)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Product_UpdatedAt' AND object_id = OBJECT_ID('Product')) CREATE INDEX IX_Product_UpdatedAt ON [Product](UpdatedAt DESC)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Product_LocalSupplierCode' AND object_id = OBJECT_ID('Product')) CREATE INDEX IX_Product_LocalSupplierCode ON [Product](LocalSupplierCode)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Product_Search' AND object_id = OBJECT_ID('Product')) CREATE INDEX IX_Product_Search ON [Product](ProductName, ProductCode, ItemNumber, Barcode)",
                // WarehouseProduct表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WarehouseProduct_Barcode' AND object_id = OBJECT_ID('WarehouseProduct')) CREATE INDEX IX_WarehouseProduct_Barcode ON [WarehouseProduct]([Barcode]) WHERE [Barcode] IS NOT NULL",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WarehouseProduct_ProductCode' AND object_id = OBJECT_ID('WarehouseProduct')) CREATE UNIQUE INDEX IX_WarehouseProduct_ProductCode ON [WarehouseProduct]([ProductCode])",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WarehouseProduct_IsActive' AND object_id = OBJECT_ID('WarehouseProduct')) CREATE INDEX IX_WarehouseProduct_IsActive ON [WarehouseProduct]([IsActive])",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WarehouseProduct_Barcode_Search' AND object_id = OBJECT_ID('WarehouseProduct')) CREATE INDEX IX_WarehouseProduct_Barcode_Search ON [WarehouseProduct]([IsActive], [Barcode]) WHERE [Barcode] IS NOT NULL",
                // ProductSetCode表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ProductSetCode_SetBarcode' AND object_id = OBJECT_ID('ProductSetCode')) CREATE INDEX IX_ProductSetCode_SetBarcode ON [ProductSetCode]([SetBarcode]) WHERE [SetBarcode] IS NOT NULL",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ProductSetCode_ProductCode' AND object_id = OBJECT_ID('ProductSetCode')) CREATE INDEX IX_ProductSetCode_ProductCode ON [ProductSetCode]([ProductCode])",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ProductSetCode_Barcode_ProductCode' AND object_id = OBJECT_ID('ProductSetCode')) CREATE INDEX IX_ProductSetCode_Barcode_ProductCode ON [ProductSetCode]([SetBarcode], [ProductCode]) WHERE [SetBarcode] IS NOT NULL",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ProductSetCode_SetItemNumber' AND object_id = OBJECT_ID('ProductSetCode')) CREATE INDEX IX_ProductSetCode_SetItemNumber ON [ProductSetCode]([SetItemNumber]) WHERE [SetItemNumber] IS NOT NULL",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ProductSetCode_IsActive' AND object_id = OBJECT_ID('ProductSetCode')) CREATE INDEX IX_ProductSetCode_IsActive ON [ProductSetCode]([IsActive])",
                // ScheduledTaskLog表的普通索引
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ScheduledTaskLog_TaskType' AND object_id = OBJECT_ID('ScheduledTaskLog')) CREATE INDEX IX_ScheduledTaskLog_TaskType ON [ScheduledTaskLog](TaskType)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ScheduledTaskLog_Status' AND object_id = OBJECT_ID('ScheduledTaskLog')) CREATE INDEX IX_ScheduledTaskLog_Status ON [ScheduledTaskLog](Status)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ScheduledTaskLog_StartedAt' AND object_id = OBJECT_ID('ScheduledTaskLog')) CREATE INDEX IX_ScheduledTaskLog_StartedAt ON [ScheduledTaskLog](StartedAt)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ScheduledTaskLog_TaskType_Status' AND object_id = OBJECT_ID('ScheduledTaskLog')) CREATE INDEX IX_ScheduledTaskLog_TaskType_Status ON [ScheduledTaskLog](TaskType, Status)",
                "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ScheduledTaskLog_ScheduledTime' AND object_id = OBJECT_ID('ScheduledTaskLog')) CREATE INDEX IX_ScheduledTaskLog_ScheduledTime ON [ScheduledTaskLog](ScheduledTime)",
            };

            foreach (var sql in normalIndexStatements)
            {
                try
                {
                    _db.Ado.ExecuteCommand(sql);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"创建普通索引失败: {ex.Message}");
                }
            }

            Console.WriteLine("✓ 普通索引创建完成");
        }

        /// <summary>
        /// 检查索引是否存在
        /// </summary>
        public void CheckIndexes()
        {
            try
            {
                if (_db.CurrentConnectionConfig.DbType == DbType.SqlServer)
                {
                    CheckSqlServerIndexes();
                }
                else if (_db.CurrentConnectionConfig.DbType == DbType.PostgreSQL)
                {
                    CheckPostgreSQLIndexes();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查索引时出现错误: {ex.Message}");
            }
        }

        private void CheckSqlServerIndexes()
        {
            Console.WriteLine("检查数据库索引状态...");

            var tables = new[]
            {
                "User",
                "Role",
                "Store",
                "UserRole",
                "UserStore",
                "RefreshToken",
                "Product",
                "Cart",
                "CartItem",
                "WarehouseCategory",
                "WarehouseProduct",
                "ChinaSupplier",
                "DomesticProduct",
                "ProductPrefixCode",
                "ProductSetCode",
                "DomesticSetProduct",
                "DomesticProductCreationLog",
                "Location",
                "ProductLocation",
                "YIWU_Order",
                "YIWU_OrderDetail",
                "Container",
                "ContainerDetail",
            };

            foreach (var table in tables)
            {
                var sql =
                    $@"
                        SELECT 
                            i.name AS IndexName,
                            i.type_desc AS IndexType,
                            i.is_unique AS IsUnique,
                            STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS Columns
                        FROM sys.indexes i
                        INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                        INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                        WHERE i.object_id = OBJECT_ID('{table}')
                        GROUP BY i.name, i.type_desc, i.is_unique
                        ORDER BY i.name";

                var indexes = _db.Ado.SqlQuery<dynamic>(sql);

                Console.WriteLine($"\n{table}表索引:");
                foreach (var index in indexes)
                {
                    var unique = index.IsUnique ? "唯一" : "普通";
                    Console.WriteLine($"  - {index.IndexName} ({unique}): {index.Columns}");
                }
            }

            Console.WriteLine("\n✓ 索引检查完成");
        }

        private void CheckPostgreSQLIndexes()
        {
            Console.WriteLine("检查 PostgreSQL 数据库索引状态...");

            var tables = new[]
            {
                "User",
                "Role",
                "Store",
                "UserRole",
                "UserStore",
                "RefreshToken",
                "Product",
                "Cart",
                "CartItem",
                "WarehouseCategory",
                "WarehouseProduct",
                "ChinaSupplier",
                "DomesticProduct",
                "ProductPrefixCode",
                "ProductSetCode",
                "DomesticSetProduct",
                "DomesticProductCreationLog",
                "Location",
                "ProductLocation",
                "YIWU_Order",
                "YIWU_OrderDetail",
                "Container",
                "ContainerDetail",
            };

            foreach (var table in tables)
            {
                // 注意：SqlSugar在PG中可能使用带引号的表名，查询pg_indexes时tablename通常不带引号
                // 这里假设表名在数据库中是区分大小写的（因为创建时加了引号）
                // 如果数据库中实际表名是小写，这里可能查不到，需要根据实际情况调整
                var sql =
                    $@"
                    SELECT
                        indexname as IndexName,
                        indexdef as IndexDef
                    FROM pg_indexes
                    WHERE tablename = '{table}' OR tablename = '""{table}""' OR tablename = lower('{table}')
                    ORDER BY indexname";

                var indexes = _db.Ado.SqlQuery<dynamic>(sql);

                Console.WriteLine($"\n{table}表索引:");
                foreach (var index in indexes)
                {
                    string def = index.IndexDef;
                    bool isUnique = def.Contains(
                        "UNIQUE INDEX",
                        StringComparison.OrdinalIgnoreCase
                    );
                    string uniqueStr = isUnique ? "唯一" : "普通";

                    // 简单的解析列名，仅用于显示
                    string columns = "";
                    int startIndex = def.IndexOf('(');
                    int endIndex = def.LastIndexOf(')');
                    if (startIndex > 0 && endIndex > startIndex)
                    {
                        columns = def.Substring(startIndex + 1, endIndex - startIndex - 1);
                    }

                    Console.WriteLine($"  - {index.IndexName} ({uniqueStr}): {columns}");
                }
            }

            Console.WriteLine("\n✓ PostgreSQL 索引检查完成");
        }

        private static DbType GetDatabaseType(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return DbType.SqlServer;

            if (
                connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase)
                || connectionString.Contains("Port=", StringComparison.OrdinalIgnoreCase)
            )
            {
                return DbType.PostgreSQL;
            }
            return DbType.SqlServer;
        }

        /// <summary>
        /// 🚀 在现有事务上下文中创建独立的查询连接，用于并发查询
        /// 避免多个并发任务共享同一个连接导致连接关闭错误
        /// </summary>
        /// <returns>独立的查询连接实例</returns>
        public ISqlSugarClient CreateConcurrentQueryConnection()
        {
            var config = _db.CurrentConnectionConfig;

            var concurrentDb = new SqlSugarClient(
                new ConnectionConfig()
                {
                    ConnectionString = config.ConnectionString,
                    DbType = config.DbType,
                    IsAutoCloseConnection = false,
                    InitKeyType = config.InitKeyType,
                    MoreSettings = new ConnMoreSettings()
                    {
                        IsAutoRemoveDataCache = config.MoreSettings.IsAutoRemoveDataCache,
                        IsWithNoLockQuery = config.MoreSettings.IsWithNoLockQuery,
                        SqlServerCodeFirstNvarchar = config.MoreSettings.SqlServerCodeFirstNvarchar,
                        DefaultCacheDurationInSeconds = 0,
                    },
                    ConfigureExternalServices = config.ConfigureExternalServices,
                }
            );

            concurrentDb.Ado.CommandTimeOut = _db.Ado.CommandTimeOut;

            return concurrentDb;
        }

        /// <summary>
        /// 🚀 创建独立的数据库连接实例用于并发操作
        /// 每个并发任务使用独立的连接，避免连接冲突
        /// </summary>
        /// <param name="configuration">配置对象</param>
        /// <returns>独立的SqlSugar客户端实例</returns>
        public static ISqlSugarClient CreateConcurrentConnection(IConfiguration configuration)
        {
            // 确保连接字符串包含并发支持配置
            var connectionString =
                configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection 连接字符串未配置");

            var dbType = GetDatabaseType(connectionString);

            if (dbType == DbType.SqlServer)
            {
                // 添加高并发优化参数（从配置读取）
                if (
                    !connectionString.Contains(
                        "MultipleActiveResultSets",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    connectionString += ";MultipleActiveResultSets=True";
                }
                if (!connectionString.Contains("Max Pool Size", StringComparison.OrdinalIgnoreCase))
                {
                    var maxPoolSize = configuration.GetValue<int?>("Database:MaxPoolSize") ?? 100;
                    connectionString += $";Max Pool Size={maxPoolSize}";
                }
                if (!connectionString.Contains("Min Pool Size", StringComparison.OrdinalIgnoreCase))
                {
                    var minPoolSize = configuration.GetValue<int?>("Database:MinPoolSize") ?? 5;
                    connectionString += $";Min Pool Size={minPoolSize}";
                }
                if (
                    !connectionString.Contains(
                        "Connection Timeout",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    var connectionTimeout =
                        configuration.GetValue<int?>("Database:ConnectionTimeoutSeconds") ?? 60;
                    connectionString += $";Connection Timeout={connectionTimeout}";
                }
                if (
                    !connectionString.Contains(
                        "Command Timeout",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    var commandTimeout =
                        configuration.GetValue<int?>("Database:ConcurrentCommandTimeoutSeconds")
                        ?? 30;
                    connectionString += $";Command Timeout={commandTimeout}";
                }
                if (
                    !connectionString.Contains(
                        "Connect Retry Count",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    var connectRetryCount =
                        configuration.GetValue<int?>("Database:ConnectRetryCount") ?? 2;
                    connectionString += $";Connect Retry Count={connectRetryCount}";
                }
                if (
                    !connectionString.Contains(
                        "Connect Retry Interval",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    var connectRetryInterval =
                        configuration.GetValue<int?>("Database:ConnectRetryIntervalSeconds") ?? 5;
                    connectionString += $";Connect Retry Interval={connectRetryInterval}";
                }
            }

            var client = new SqlSugarClient(
                new ConnectionConfig()
                {
                    ConnectionString = connectionString,
                    DbType = dbType,
                    IsAutoCloseConnection = false, // 重要：自动关闭连接
                    InitKeyType = InitKeyType.Attribute,
                    MoreSettings = new ConnMoreSettings()
                    {
                        IsAutoRemoveDataCache = false,
                        IsWithNoLockQuery = dbType == DbType.SqlServer,
                        // 🚀 并发优化配置
                        SqlServerCodeFirstNvarchar = dbType == DbType.SqlServer,
                        DefaultCacheDurationInSeconds = 0, // 禁用缓存以避免并发问题
                        DisableNvarchar = false,
                    },
                    ConfigureExternalServices = new ConfigureExternalServices()
                    {
                        EntityService = (c, p) =>
                        {
                            // PostgreSQL 兼容性处理
                            if (dbType == DbType.PostgreSQL && !string.IsNullOrEmpty(p.DataType))
                            {
                                // 1. 将 nvarchar 映射为 varchar
                                if (
                                    p.DataType.Contains(
                                        "nvarchar",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                )
                                {
                                    p.DataType = p.DataType.Replace(
                                        "nvarchar",
                                        "varchar",
                                        StringComparison.OrdinalIgnoreCase
                                    );
                                }

                                // 2. 将 (max) 映射为 text (PG不支持varchar(max))
                                if (
                                    p.DataType.Contains("(max)", StringComparison.OrdinalIgnoreCase)
                                )
                                {
                                    p.DataType = "text";
                                }
                            }
                        },
                    },
                }
            );

            // 设置较短的超时时间，避免长时间占用连接（从配置读取）
            var concurrentCommandTimeout = configuration.GetValue<int>(
                "Database:ConcurrentCommandTimeoutSeconds",
                30
            );
            client.Ado.CommandTimeOut = concurrentCommandTimeout;

            return client;
        }
    }
}
