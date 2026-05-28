using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Data
{
    /// <summary>
    /// PostgreSQL数据库上下文
    /// 使用SqlSugar ORM操作PostgreSQL数据库
    /// </summary>
    public class PostgresSqlSugarContext
    {
        private readonly SqlSugarScope _db;
        private readonly ILogger<PostgresSqlSugarContext> _logger;

        /// <summary>
        /// 配置对象，用于并发连接创建
        /// </summary>
        public IConfiguration Configuration { get; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="connectionString">PostgreSQL连接字符串</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="configuration">配置对象</param>
        public PostgresSqlSugarContext(
            string connectionString,
            ILogger<PostgresSqlSugarContext> logger,
            IConfiguration configuration
        )
        {
            _logger = logger;
            Configuration = configuration;

            // 使用优化的连接字符串
            var optimizedConnectionString = BuildOptimizedConnectionString(connectionString);

            // 使用 Scoped 生命周期管理连接
            // IsAutoCloseConnection = true 自动管理连接的打开和关闭，避免连接泄露

            _db = new SqlSugarScope(
                new ConnectionConfig()
                {
                    ConnectionString = optimizedConnectionString,
                    DbType = DbType.PostgreSQL,
                    IsAutoCloseConnection = true,
                    InitKeyType = InitKeyType.Attribute,
                    MoreSettings = new ConnMoreSettings()
                    {
                        IsAutoRemoveDataCache = true,
                        IsWithNoLockQuery = false, // PostgreSQL不支持WITH(NOLOCK)
                    },
                }
            );

            // 设置命令超时时间（30分钟）
            _db.Ado.CommandTimeOut = 1800;

            // 配置日志
            _db.Aop.OnLogExecuting = (sql, pars) =>
            {
                _logger.LogDebug("PostgreSQL执行SQL: {Sql}", sql);
            };

            _db.Aop.OnError = (exp) =>
            {
                _logger.LogError(exp, "PostgreSQL操作异常: {Message}", exp.Message);
            };
        }

        /// <summary>
        /// 获取数据库实例
        /// </summary>
        public SqlSugarScope Db => _db;

        //// 用户相关实体
        //public SimpleClient<User> UserDb => new SimpleClient<User>(_db);
        //public SimpleClient<Role> RoleDb => new SimpleClient<Role>(_db);
        //public SimpleClient<UserRole> UserRoleDb => new SimpleClient<UserRole>(_db);
        //public SimpleClient<Store> StoreDb => new SimpleClient<Store>(_db);
        //public SimpleClient<UserStore> UserStoreDb => new SimpleClient<UserStore>(_db);
        //public SimpleClient<RefreshToken> RefreshTokenDb => new SimpleClient<RefreshToken>(_db);

        //// 商品相关实体
        //public SimpleClient<Product> ProductDb => new SimpleClient<Product>(_db);
        //public SimpleClient<ProductSetCode> ProductSetCodeDb =>
        //    new SimpleClient<ProductSetCode>(_db);
        //public SimpleClient<Cart> CartDb => new SimpleClient<Cart>(_db);
        //public SimpleClient<CartItem> CartItemDb => new SimpleClient<CartItem>(_db);

        //// 仓库相关实体
        //public SimpleClient<WarehouseCategory> WarehouseCategoryDb =>
        //    new SimpleClient<WarehouseCategory>(_db);
        //public SimpleClient<WarehouseProduct> WarehouseProductDb =>
        //    new SimpleClient<WarehouseProduct>(_db);
        //public SimpleClient<ChinaSupplier> ChinaSupplierDb => new SimpleClient<ChinaSupplier>(_db);

        //// 国内商品相关实体
        //public SimpleClient<DomesticProduct> DomesticProductDb =>
        //    new SimpleClient<DomesticProduct>(_db);
        //public SimpleClient<ProductPrefixCode> ProductPrefixCodeDb =>
        //    new SimpleClient<ProductPrefixCode>(_db);
        //public SimpleClient<DomesticSetProduct> DomesticSetProductDb =>
        //    new SimpleClient<DomesticSetProduct>(_db);

        //// 货位相关实体
        //public SimpleClient<Location> LocationDb => new SimpleClient<Location>(_db);
        //public SimpleClient<ProductLocation> ProductLocationDb =>
        //    new SimpleClient<ProductLocation>(_db);

        //// 义乌相关实体
        //public SimpleClient<YIWU_Order> YIWU_OrderDb => new SimpleClient<YIWU_Order>(_db);
        //public SimpleClient<YIWU_OrderDetail> YIWU_OrderDetailDb =>
        //    new SimpleClient<YIWU_OrderDetail>(_db);

        //// 货柜相关实体
        //public SimpleClient<Container> ContainerDb => new SimpleClient<Container>(_db);
        //public SimpleClient<ContainerDetail> ContainerDetailDb =>
        //    new SimpleClient<ContainerDetail>(_db);

        //// 分店相关实体
        //public SimpleClient<StoreRetailPrice> StoreRetailPriceDb =>
        //    new SimpleClient<StoreRetailPrice>(_db);
        //public SimpleClient<StoreClearancePrice> StoreClearancePriceDb =>
        //    new SimpleClient<StoreClearancePrice>(_db);
        //public SimpleClient<StoreMultiCodeProduct> StoreMultiCodeProductDb =>
        //    new SimpleClient<StoreMultiCodeProduct>(_db);

        /// <summary>
        /// 初始化数据库表（如果不存在）
        /// </summary>
        public void InitializeTablesIfNeeded()
        {
            try
            {
                _logger.LogInformation("开始检查PostgreSQL数据库表结构...");

                var tables = new[]
                {
                    typeof(User),
                    typeof(Role),
                    typeof(UserRole),
                    typeof(SysUserPermission),
                    typeof(Store),
                    typeof(UserStore),
                    typeof(RefreshToken),
                    typeof(Product),
                    typeof(ProductSetCode),
                    typeof(Cart),
                    typeof(CartItem),
                    typeof(WarehouseCategory),
                    typeof(WarehouseProduct),
                    typeof(ChinaSupplier),
                    typeof(DomesticProduct),
                    typeof(ProductPrefixCode),
                    typeof(SysPermission),
                    typeof(SysRolePermission),
                    typeof(DomesticSetProduct),
                    typeof(Location),
                    typeof(ProductLocation),
                    typeof(YIWU_Order),
                    typeof(YIWU_OrderDetail),
                    typeof(Container),
                    typeof(ContainerDetail),
                    typeof(StoreRetailPrice),
                    typeof(StoreClearancePrice),
                    typeof(StoreMultiCodeProduct),
                };

                _db.CodeFirst.InitTables(tables);
                _db.Ado.ExecuteCommand(
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_SysUserPermission_User_Permission_Unique\" ON \"HBwebSysUserPermissions\" (\"UserGuid\", \"PermissionCode\")"
                );
                _db.Ado.ExecuteCommand(
                    "CREATE INDEX IF NOT EXISTS \"IX_SysUserPermission_UserGuid\" ON \"HBwebSysUserPermissions\" (\"UserGuid\")"
                );
                _logger.LogInformation("PostgreSQL数据库表结构检查完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PostgreSQL数据库表初始化失败");
                throw;
            }
        }

        /// <summary>
        /// 重新创建所有表（危险操作，会删除所有数据）
        /// </summary>
        public void RecreateAllTables()
        {
            try
            {
                _logger.LogWarning("开始重新创建PostgreSQL所有数据库表，这将删除所有现有数据！");

                // 删除所有表（按依赖关系逆序删除）
                try
                {
                    _db.DbMaintenance.DropTable<StoreMultiCodeProduct>();
                    Console.WriteLine("✓ 删除StoreMultiCodeProduct表");
                }
                catch { }
                try
                {
                    _db.DbMaintenance.DropTable<StoreClearancePrice>();
                    Console.WriteLine("✓ 删除StoreClearancePrice表");
                }
                catch { }
                try
                {
                    _db.DbMaintenance.DropTable<StoreRetailPrice>();
                    Console.WriteLine("✓ 删除StoreRetailPrice表");
                }
                catch { }
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
                    _db.DbMaintenance.DropTable<ChinaSupplier>();
                    Console.WriteLine("✓ 删除ChinaSupplier表");
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

                // 重新创建所有表
                _db.CodeFirst.InitTables(typeof(User));
                Console.WriteLine("✓ User表创建成功");

                _db.CodeFirst.InitTables(typeof(Role));
                Console.WriteLine("✓ Role表创建成功");

                _db.CodeFirst.InitTables(typeof(UserRole));
                Console.WriteLine("✓ UserRole表创建成功");

                _db.CodeFirst.InitTables(typeof(Store));
                Console.WriteLine("✓ Store表创建成功");

                _db.CodeFirst.InitTables(typeof(UserStore));
                Console.WriteLine("✓ UserStore表创建成功");

                _db.CodeFirst.InitTables(typeof(RefreshToken));
                Console.WriteLine("✓ RefreshToken表创建成功");

                _db.CodeFirst.InitTables(typeof(Product));
                Console.WriteLine("✓ Product表创建成功");

                _db.CodeFirst.InitTables(typeof(ProductSetCode));
                Console.WriteLine("✓ ProductSetCode表创建成功");

                _db.CodeFirst.InitTables(typeof(Cart));
                Console.WriteLine("✓ Cart表创建成功");

                _db.CodeFirst.InitTables(typeof(CartItem));
                Console.WriteLine("✓ CartItem表创建成功");

                _db.CodeFirst.InitTables(typeof(WarehouseCategory));
                Console.WriteLine("✓ WarehouseCategory表创建成功");

                _db.CodeFirst.InitTables(typeof(WarehouseProduct));
                Console.WriteLine("✓ WarehouseProduct表创建成功");

                _db.CodeFirst.InitTables(typeof(ChinaSupplier));
                Console.WriteLine("✓ ChinaSupplier表创建成功");

                _db.CodeFirst.InitTables(typeof(ProductPrefixCode));
                Console.WriteLine("✓ ProductPrefixCode表创建成功");

                _db.CodeFirst.InitTables(typeof(DomesticProduct));
                Console.WriteLine("✓ DomesticProduct表创建成功");

                _db.CodeFirst.InitTables(typeof(DomesticSetProduct));
                Console.WriteLine("✓ DomesticSetProduct表创建成功");

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

                _db.CodeFirst.InitTables(typeof(StoreRetailPrice));
                Console.WriteLine("✓ StoreRetailPrice表创建成功");

                _db.CodeFirst.InitTables(typeof(StoreClearancePrice));
                Console.WriteLine("✓ StoreClearancePrice表创建成功");

                _db.CodeFirst.InitTables(typeof(StoreMultiCodeProduct));
                Console.WriteLine("✓ StoreMultiCodeProduct表创建成功");

                _logger.LogInformation("PostgreSQL所有数据库表重新创建完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PostgreSQL数据库表重新创建失败");
                throw;
            }
        }

        /// <summary>
        /// 检查和创建索引
        /// </summary>
        public void CheckIndexes()
        {
            try
            {
                _logger.LogInformation("开始检查PostgreSQL数据库索引...");

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
                    "Order",
                    "OrderItem",
                    "WarehouseCategory",
                    "WarehouseProduct",
                    "ChinaSupplier",
                    "DomesticProduct",
                    "ProductPrefixCode",
                    "ProductSetCode",
                    "DomesticSetProduct",
                    "Location",
                    "ProductLocation",
                    "YIWU_Order",
                    "YIWU_OrderDetail",
                };

                foreach (var table in tables)
                {
                    if (_db.DbMaintenance.IsAnyTable(table))
                    {
                        _logger.LogDebug($"PostgreSQL表 {table} 存在");
                    }
                    else
                    {
                        _logger.LogWarning($"PostgreSQL表 {table} 不存在");
                    }
                }

                _logger.LogInformation("PostgreSQL数据库索引检查完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PostgreSQL数据库索引检查失败");
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _db?.Dispose();
        }

        /// <summary>
        /// 创建并发连接（用于多线程并发操作）
        /// 🚀 优化PostgreSQL连接配置以支持高并发
        /// </summary>
        /// <param name="configuration">配置对象</param>
        /// <returns>独立的SqlSugar客户端实例</returns>
        public static ISqlSugarClient CreateConcurrentConnection(IConfiguration configuration)
        {
            var baseConnectionString = configuration.GetConnectionString("PostgresConnection");

            // 优化PostgreSQL连接字符串，添加超时和连接池设置
            var optimizedConnectionString = BuildOptimizedConnectionString(baseConnectionString);

            var client = new SqlSugarClient(
                new ConnectionConfig()
                {
                    DbType = DbType.PostgreSQL,
                    ConnectionString = optimizedConnectionString,
                    IsAutoCloseConnection = true,
                    InitKeyType = InitKeyType.Attribute,
                    MoreSettings = new ConnMoreSettings()
                    {
                        IsAutoRemoveDataCache = true,
                        IsWithNoLockQuery = false, // PostgreSQL不支持WITH(NOLOCK)
                    },
                    AopEvents = new AopEvents
                    {
                        OnLogExecuting = (sql, p) =>
                        {
                            // 只记录BulkCopy操作，减少日志噪音
                            if (sql.Contains("BulkCopy"))
                            {
                                Console.WriteLine($"[PostgreSQL Concurrent] {sql}");
                            }
                        },
                    },
                }
            );

            // 设置额外的连接参数
            client.Ado.CommandTimeOut = 300; // 5分钟SQL命令超时

            return client;
        }

        /// <summary>
        /// 构建优化的PostgreSQL连接字符串
        /// </summary>
        private static string BuildOptimizedConnectionString(string? baseConnectionString)
        {
            if (string.IsNullOrEmpty(baseConnectionString))
                throw new ArgumentException(
                    "Connection string cannot be null or empty",
                    nameof(baseConnectionString)
                );

            var builder = new Npgsql.NpgsqlConnectionStringBuilder(baseConnectionString);

            // 连接池优化
            builder.MinPoolSize = 5; // 最小连接池大小
            builder.MaxPoolSize = 50; // 最大连接池大小（支持更多并发）
            builder.ConnectionIdleLifetime = 300; // 连接空闲5分钟后回收
            builder.ConnectionPruningInterval = 10; // 每10秒清理空闲连接

            // 超时设置
            builder.Timeout = 180; // 3分钟连接超时
            builder.CommandTimeout = 300; // 5分钟命令超时
            builder.CancellationTimeout = 30; // 30秒取消超时

            // 性能优化
            builder.ReadBufferSize = 65536; // 64KB读缓冲区
            builder.WriteBufferSize = 65536; // 64KB写缓冲区
            builder.SocketReceiveBufferSize = 65536; // 64KB接收缓冲区
            builder.SocketSendBufferSize = 65536; // 64KB发送缓冲区

            // 并发和可靠性
            builder.Multiplexing = false; // 禁用多路复用，提高并发稳定性
            builder.KeepAlive = 30; // 30秒保活
            builder.TcpKeepAliveTime = 30; // TCP保活时间
            builder.TcpKeepAliveInterval = 5; // TCP保活间隔

            // 批量操作优化
            builder.WriteCoalescingBufferThresholdBytes = 1048576; // 1MB写合并缓冲区

            return builder.ToString();
        }
    }
}
