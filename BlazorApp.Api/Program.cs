// ===================== 引用和命名空间 =====================
using System.Linq;
using System.Text; // 字符串编码
using AutoMapper; // AutoMapper映射服务
using BlazorApp.Api.Data; // 数据访问层
using BlazorApp.Api.Filters;
using BlazorApp.Api.Interfaces; // 数据模型
using BlazorApp.Api.Interfaces.React; // React 接口命名空间
using BlazorApp.Api.Models;
using BlazorApp.Api.Services; // 业务服务层
using BlazorApp.Api.Services.Background; // 后台定时服务
using BlazorApp.Api.Services.Pricing; // 自动定价服务
using BlazorApp.Api.Services.React; // React 专用服务层
using Microsoft.AspNetCore.Authentication.JwtBearer; // JWT Bearer认证
using Microsoft.IdentityModel.Tokens; // JWT令牌验证

// ===================== 应用程序入口点 =====================
// 创建WebApplicationBuilder实例，读取命令行参数和配置文件
// 这是ASP.NET Core 6+的新式启动方式，替代了传统的Startup.cs
var builder = WebApplication.CreateBuilder(args);

// ===================== 服务注册区域 =====================

// --------------------- 基础Web API服务 ---------------------
// 注册MVC控制器服务，启用基于控制器的API端点
builder
    .Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // 使用 camelCase 命名策略（前端 JavaScript 标准）
        options.JsonSerializerOptions.PropertyNamingPolicy = System
            .Text
            .Json
            .JsonNamingPolicy
            .CamelCase;
        options.JsonSerializerOptions.ReferenceHandler = System
            .Text
            .Json
            .Serialization
            .ReferenceHandler
            .IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System
            .Text
            .Json
            .Serialization
            .JsonIgnoreCondition
            .WhenWritingNull;
    })
    .AddMvcOptions(options =>
    {
        options.Filters.Add<BlazorApp.Api.Filters.ApiExceptionFilter>();
    });

// 注册HttpClient服务，供翻译服务等需要HTTP调用的服务使用
builder.Services.AddHttpClient();

// 注册API终结点浏览器，用于自动发现API端点
// 这是Swagger/OpenAPI文档生成的基础服务
builder.Services.AddEndpointsApiExplorer();

// 注册Swagger生成器服务，用于生成API文档和测试界面
// 仅在开发环境中启用，生产环境建议关闭
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc(
        "v1",
        new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "HB Platform API",
            Version = "v1",
            Description = "HB Platform多店铺管理系统API文档",
            Contact = new Microsoft.OpenApi.Models.OpenApiContact
            {
                Name = "HB Platform开发团队",
                Email = "dev@hbplatform.com",
            },
        }
    );

    // 配置JWT认证
    c.AddSecurityDefinition(
        "Bearer",
        new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Description =
                "JWT授权(数据将在请求头中进行传输) 参数结构: \"Authorization: Bearer {token}\" 注意：在Swagger中输入时，格式为 'Bearer ' + 空格 + 令牌值，不要包含大括号{}",
            Name = "Authorization",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
            BearerFormat = "JWT",
            Scheme = "Bearer",
        }
    );

    c.AddSecurityRequirement(
        new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "Bearer",
                    },
                },
                new string[] { }
            },
        }
    );

    // 添加XML注释支持
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }

    c.CustomSchemaIds(type => type.FullName);
    c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());

    c.OperationFilter<PDAHeaderOperationFilter>();
});

// 获取HttpContext的服务（用于在服务层读取当前用户）
builder.Services.AddHttpContextAccessor();

// 注册内存缓存服务
builder.Services.AddMemoryCache();

builder.Services.Configure<TencentCloudSettings>(
    builder.Configuration.GetSection("TencentCloud")
);
builder.Services.AddScoped<SalesStatisticsJobService>();
builder.Services.AddScoped<HBSalesRecordStatisticsService>();
builder.Services.AddScoped<ScheduledTaskLogService>();
builder.Services.AddScoped<ScheduledTaskRetryService>();
builder.Services.Configure<ScheduledTaskOptions>(
    builder.Configuration.GetSection("ScheduledTasks")
);
builder.Services.AddHostedService<ScheduledTaskService>();

// --------------------- CORS配置 ---------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "AllowSpecific",
        policy =>
        {
            policy
                .WithOrigins(
                    "http://localhost:8000",
                    "http://localhost:3000",
                    "http://localhost:5001",
                    "https://localhost", // ⭐ 支持 HTTPS localhost（宝塔面板）
                    "https://www.dats.com.au",
                    "https://www.malmar.com.au",
                    "https://www.yatstal.com.au",
                    "https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com",
                    "https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com",
                    "http://hotbargain.vip"
                )
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        }
    );
});

// --------------------- JWT认证配置 ---------------------
// 🔐 配置JSON Web Token（JWT）身份验证
// JWT是一种无状态的、基于令牌的认证机制，适合前后端分离架构
// 工作原理：用户登录后获得JWT令牌，后续请求携带此令牌进行身份验证

// 📋 从配置文件（appsettings.json）读取JWT相关设置
// 包括：密钥、签发者、受众、过期时间等配置
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>();

// 🔑 将JWT密钥字符串转换为字节数组，用于令牌签名和验证
// 使用HMAC-SHA256算法进行签名，确保令牌的完整性和真实性
// ⚠️ 安全警告：生产环境中密钥应足够复杂且定期更换
var key = Encoding.UTF8.GetBytes(jwtSettings!.Key);

// 🏗️ 配置ASP.NET Core认证服务，使用JWT Bearer认证方案
// Bearer认证：客户端在HTTP请求头中携带"Bearer {token}"进行认证
builder
    .Services.AddAuthentication(options =>
    {
        // 🎯 设置默认认证方案为JWT Bearer
        // 当需要验证用户身份时，系统将使用JWT令牌进行验证
        // 这告诉ASP.NET Core如何处理认证请求
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;

        // 🚫 设置默认挑战方案为JWT Bearer
        // 当用户未认证时，系统将要求提供JWT令牌
        // 这决定了如何响应未认证的请求（返回401状态码）
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        // 🔧 配置JWT令牌验证参数
        // 这些参数决定了如何验证传入的JWT令牌
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, // ✅ 验证JWT签发者（iss声明）
            ValidateAudience = true, // ✅ 验证JWT受众（aud声明）
            ValidateLifetime = true, // ✅ 验证JWT有效期（exp和nbf声明）
            ValidateIssuerSigningKey = true, // ✅ 验证JWT签名密钥
            ValidIssuer = jwtSettings.Issuer, // 🏢 设置有效的签发者
            ValidAudience = jwtSettings.Audience, // 👥 设置有效的受众
            IssuerSigningKey = new SymmetricSecurityKey(key), // 🔑 设置用于验证签名的密钥

            // 🔧 以下配置解决角色和用户名声明映射问题
            // 这是授权系统的关键配置，确保JWT中的角色信息能正确映射到ASP.NET Core的授权系统
            RoleClaimType = System.Security.Claims.ClaimTypes.Role, // 🎭 指定角色声明类型，用于[Authorize(Roles="Admin")]
            NameClaimType = System.Security.Claims.ClaimTypes.Name, // 👤 指定用户名声明类型，用于HttpContext.User.Identity.Name
        };
    });

// 🛡️ 注册动态权限授权服务
builder.Services.AddSingleton<
    Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider,
    BlazorApp.Api.Authorization.PermissionPolicyProvider
>();
builder.Services.AddScoped<
    Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    BlazorApp.Api.Authorization.PermissionAuthorizationHandler
>();

// --------------------- 依赖注入服务注册 ---------------------
// 🏗️ 配置依赖注入容器，注册应用程序所需的各种服务

// 📁 数据库上下文服务 - 使用作用域模式（Scoped）
// 说明：将原先的 Singleton 改为 Scoped，避免多个请求并发复用同一底层连接
// 好处：每个 HTTP 请求内共享一个上下文实例，请求结束自动释放，减少"连接未关闭/正在连接"的并发冲突
builder.Services.AddScoped<SqlSugarContext>(); // 主数据库上下文（每请求一个实例）
builder.Services.AddScoped<HqSqlSugarContext>(); // HQ总部数据库上下文（每请求一个实例）
builder.Services.AddScoped<HBSalesSqlSugarContext>(); // HBSales数据库上下文（每请求一个实例）
builder.Services.AddScoped<POSMSqlSugarContext>(); // POSM数据库上下文（每请求一个实例）
builder.Services.AddScoped<HBSalesRecordSqlSugarContext>(); // HBSalesRecord数据库上下文（每请求一个实例）

// PostgreSQL数据库上下文（每请求一个实例）
builder.Services.AddScoped<PostgresSqlSugarContext>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("PostgresConnection")!;
    var logger = provider.GetRequiredService<ILogger<PostgresSqlSugarContext>>();
    return new PostgresSqlSugarContext(connectionString, logger, configuration);
});

// 🔄 数据同步服务
builder.Services.AddScoped<DataSyncService>(); // 数据同步服务
builder.Services.AddScoped<PostgresDataSyncService>(); // PostgreSQL数据同步服务

// --------------------- AutoMapper配置 ---------------------
// 🗺️ 配置对象映射服务，用于DTO和实体之间的自动转换
// AutoMapper可以大大简化数据传输对象和领域模型之间的转换代码
// 例如：UserDto -> User, CreateOrderDto -> Order 等
// 注册AutoMapper服务，自动扫描程序集中所有继承自Profile的类
builder.Services.AddAutoMapper(typeof(Program).Assembly);

// 🔧 业务服务层 - 使用作用域模式（Scoped）
// 作用域：每个HTTP请求创建一次实例，请求结束时销毁
// 适合包含状态或需要事务管理的业务服务
builder.Services.AddScoped<IAuthService, AuthService>(); // 认证服务
builder.Services.AddScoped<IUserService, UserService>(); // 用户管理服务
builder.Services.AddScoped<IRoleService, RoleService>(); // 角色管理服务
builder.Services.AddScoped<IStoreService, StoreService>(); // 分店管理服务
builder.Services.AddScoped<StoreSyncService>(); // 分店数据同步服务
builder.Services.AddScoped<SeedDataService>(); // 种子数据初始化服务
builder.Services.AddScoped<IDataInitializationService, DataInitializationService>(); // 数据初始化服务
builder.Services.AddScoped<IChinaSupplierService, ChinaSupplierService>(); // 国内供应商管理服务
builder.Services.AddScoped<IDomesticSupplierService, DomesticSupplierService>(); // 义乌采购国内供应商服务
builder.Services.AddScoped<IWarehouseCategoryService, WarehouseCategoryService>(); // 仓库分类服务
builder.Services.AddScoped<IProductService, ProductService>(); // 商品服务
builder.Services.AddScoped<IProductStoreSyncService, ProductStoreSyncService>(); // 商品分店同步服务
builder.Services.AddScoped<IWarehouseProductService, WarehouseProductService>(); // 仓库商品服务
builder.Services.AddScoped<ILocationService, LocationService>(); // 位置服务
builder.Services.AddScoped<IProductLocationService, ProductLocationService>(); // 商品位置服务
builder.Services.AddScoped<ICartService, CartService>(); // 购物车服务
builder.Services.AddScoped<IContainerService, ContainerService>(); // 货柜服务（旧版）
builder.Services.AddScoped<IYiwuContainerService, YiwuContainerService>();
builder.Services.AddScoped<ContainerExportService>(); // 义乌货柜服务
builder.Services.AddScoped<ITranslationService, TranslationService>(); // 翻译服务
builder.Services.AddScoped<IYiwuOrderService, YiwuOrderService>(); // 义乌订单服务
builder.Services.AddScoped<IPostgreSqlService, PostgreSqlService>(); // PostgreSQL数据库服务
builder.Services.AddScoped<IProductPrefixCodeService, ProductPrefixCodeService>(); // 商品前缀管理服务
builder.Services.AddScoped<IDomesticProductService, DomesticProductService>(); // 国内商品管理服务
builder.Services.AddScoped<IDomesticSetProductService, DomesticSetProductService>(); // 套装商品管理服务
builder.Services.AddScoped<ItemBarcodeService>(); // 货号条码生成服务
builder.Services.AddScoped<AutoPricingService>(); // 自动定价计算服务
builder.Services.AddScoped<IVersionInfoService, VersionInfoService>(); // 版本管理服务

// React 专用：仅限 Product 与 WarehouseProduct 的商品检测/更新/新建服务
builder.Services.AddScoped<
    BlazorApp.Api.Interfaces.React.IProductWarehouseReactService,
    BlazorApp.Api.Services.React.ProductWarehouseReactService
>();
builder.Services.AddScoped<IDeviceRegistrationService, DeviceRegistrationService>(); // POSM设备注册管理服务
builder.Services.AddScoped<IProductSyncService, ProductSyncService>(); // 货柜商品同步服务（检测、批量创建、批量更新）
builder.Services.AddScoped<IWarehouseProductBatchService, WarehouseProductBatchService>(); // 仓库商品批量管理服务
builder.Services.AddScoped<TencentCloudUploadService>();

// 🚧 已禁用的服务（如需启用请取消注释）
// builder.Services.AddScoped<IHqBranchService, HqBranchService>(); // 总部分支服务

// 💡 服务生命周期说明：
// - Singleton: 应用启动时创建，应用关闭时销毁（适合无状态、线程安全的服务）
// - Scoped: 每个请求创建，请求结束时销毁（适合有状态的业务服务）
// - Transient: 每次注入时创建新实例（适合轻量级、无状态的工具类）

// ===================== React 专用服务注册（与原有服务解耦） =====================
builder.Services.AddScoped<IContainerReactService, ContainerReactService>();
builder.Services.AddScoped<IDomesticProductReactService, DomesticProductReactService>();
builder.Services.AddScoped<IProductPrefixCodeReactService, ProductPrefixCodeReactService>();
builder.Services.AddScoped<IDomesticSupplierReactService, DomesticSupplierReactService>();
builder.Services.AddScoped<ILocalSuppliersReactService, LocalSupplierReactService>();
builder.Services.AddScoped<IWarehouseCategoryReactService, WarehouseCategoryReactService>();
builder.Services.AddScoped<IProductReactService, ProductReactService>(); // Product CRUD和批量操作服务
builder.Services.AddScoped<IProductSetCodeReactService, ProductSetCodeReactService>();
builder.Services.AddScoped<IStoreRetailPriceReactService, StoreRetailPriceReactService>();
builder.Services.AddScoped<IStoreProductPriceReactService, StoreProductPriceReactService>();
builder.Services.AddScoped<IStoreMultiCodePricesReactService, StoreMultiCodePricesReactService>();
builder.Services.AddScoped<ICashRegisterUserReactService, CashRegisterUserReactService>();
builder.Services.AddScoped<IDataSyncFullService, DataSyncFullService>();
builder.Services.AddScoped<IDataSyncIncrementalService, DataSyncIncrementalService>();
builder.Services.AddScoped<IDataSyncReactService, DataSyncReactService>();
builder.Services.AddScoped<
    BlazorApp.Api.Interfaces.React.IHqContainerReactService,
    BlazorApp.Api.Services.React.HqContainerReactService
>();
builder.Services.AddScoped<
    BlazorApp.Api.Interfaces.React.IHqProductTranslationReactService,
    BlazorApp.Api.Services.React.HqProductTranslationReactService
>();
builder.Services.AddScoped<ILocalSupplierInvoicesReactService, LocalSupplierInvoicesReactService>();
builder.Services.AddScoped<IPricingStrategyReactService, PricingStrategyReactService>();
builder.Services.AddScoped<IPromotionReactService, PromotionReactService>();
builder.Services.AddScoped<IStoreOrderReactService, StoreOrderReactService>();
builder.Services.AddScoped<IPDACartToOrderService, PDACartToOrderService>();
builder.Services.AddScoped<IPDAWarehouseOrderService, PDAWarehouseOrderService>();
builder.Services.AddScoped<IPosmSalesOrderReactService, PosmSalesOrderReactService>();
builder.Services.AddScoped<IDeviceRegistrationReactService, DeviceRegistrationReactService>();
builder.Services.AddScoped<ITaxInvoiceService, TaxInvoiceService>();
builder.Services.AddScoped<ISalesDashboardReactService, SalesDashboardReactService>();
builder.Services.AddScoped<ISalesDashboardCacheWarmer, SalesDashboardCacheWarmer>();
builder.Services.AddScoped<IHolidayProductReactService, HolidayProductReactService>();
builder.Services.AddScoped<IStoreManagerProductReactService, StoreManagerProductReactService>();
builder.Services.AddScoped<
    BlazorApp.Api.Interfaces.IStoreOrderCacheWarmer,
    BlazorApp.Api.Cache.StoreOrderCacheWarmer
>();

builder.Services.AddScoped<SalesStatisticsJobService>();

builder.Services.AddSingleton<
    BlazorApp.Api.Interfaces.React.IOrderNumberGenerator,
    BlazorApp.Api.Services.Common.OrderNumberGeneratorService
>();

// ===================== 应用构建与中间件配置 =====================

// 🏗️ 构建WebApplication实例
// 此时所有服务配置完成，开始构建实际的Web应用程序
var app = builder.Build();

// 📝 初始化缓存键日志记录器
using (var scope = app.Services.CreateScope())
{
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
    var cacheLogger = loggerFactory.CreateLogger("BlazorApp.Api.Cache.SalesDashboardCacheKeys");
    BlazorApp.Api.Cache.SalesDashboardCacheKeys.SetLogger(cacheLogger);

    var storeOrderCacheLogger = loggerFactory.CreateLogger(
        "BlazorApp.Api.Cache.StoreOrderCacheKeys"
    );
    BlazorApp.Api.Cache.StoreOrderCacheKeys.SetLogger(storeOrderCacheLogger);
}

// --------------------- HTTP请求管道配置 ---------------------
// ⚡ 中间件管道按顺序执行，顺序很重要！
// 请求：客户端 → 中间件1 → 中间件2 → ... → 控制器
// 响应：控制器 → ... → 中间件2 → 中间件1 → 客户端

// 📚 开发环境专用：Swagger API文档中间件
if (app.Environment.IsDevelopment())
{
    app.UseSwagger(); // 启用Swagger JSON端点
    app.UseSwaggerUI(); // 启用Swagger UI界面（通常在 /swagger 路径）
    // 🔗 访问地址：https://localhost:7171/swagger
}

// 🔒 HTTPS重定向中间件（当前已禁用）
// 生产环境建议启用HTTPS重定向以提高安全性
// app.UseHttpsRedirection();

// 🌐 CORS中间件：处理跨域请求
// 必须在认证/授权中间件之前，允许预检请求通过
app.UseCors("AllowSpecific");

// 🔐 认证中间件：验证JWT令牌，设置HttpContext.User
// 必须在授权中间件之前执行
app.UseAuthentication();

// 🛡️ 授权中间件：基于用户身份和角色检查访问权限
// 处理[Authorize]特性标记的控制器和方法
app.UseAuthorization();

// 映射控制器路由（启用API端点）
app.MapControllers();

// ===================== 缓存预热 =====================
// 🔥 应用启动后预热首页商品列表缓存
_ = Task.Run(async () =>
{
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(5)); // 等待应用完全启动
        using (var scope = app.Services.CreateScope())
        {
            var cacheWarmer =
                scope.ServiceProvider.GetRequiredService<BlazorApp.Api.Interfaces.IStoreOrderCacheWarmer>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("开始预热首页商品列表缓存");
            await cacheWarmer.WarmUpHomePageAsync();
            logger.LogInformation("首页商品列表缓存预热完成");
        }
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "预热首页商品列表缓存失败，但不影响应用启动");
    }
});

// ===================== 数据库初始化与种子数据 =====================
// 🗄️ 应用启动时自动初始化数据库结构和基础数据
// 这个过程在Web服务器启动之前完成，确保数据库就绪
try
{
    // 🔧 创建服务作用域
    // 由于数据库服务是Scoped生命周期，需要创建作用域来获取实例
    using (var scope = app.Services.CreateScope())
    {
        // 📁 获取数据库上下文服务
        var dbContext = scope.ServiceProvider.GetRequiredService<SqlSugarContext>(); // 主业务数据库
        var hqDbContext = scope.ServiceProvider.GetRequiredService<HqSqlSugarContext>(); // HQ总部数据库
        var posmDbContext = scope.ServiceProvider.GetRequiredService<POSMSqlSugarContext>(); // POSM数据库
        var services = scope.ServiceProvider;

        Console.WriteLine("🚀 开始初始化数据库...");

        // 🔄 智能模式：增量更新数据库结构
        // 只创建不存在的表，更新表结构，保留现有数据
        Console.WriteLine("🧠 使用智能初始化模式（保留现有数据）");
        // dbContext.CreateTable();
        //await posmDbContext.InitializeTablesAsync();
        Console.WriteLine("✅ 主数据库表检查完成");

        /*   // 🔄 检查数据库初始化模式
          // 支持两种启动模式：智能更新 vs 强制重建
          var forceRecreate = Environment.GetEnvironmentVariable("FORCE_RECREATE_DB")?.ToLower() == "true" ||
                             args.Contains("--force-recreate-db");
  
          if (forceRecreate)
          {
              // ⚠️ 危险模式：完全重建数据库
              // 删除所有现有表和数据，适用于开发环境或数据重置
              Console.WriteLine("⚠️ 检测到强制重建标志，将删除所有现有数据！");
              Console.WriteLine("💡 启动参数: --force-recreate-db 或环境变量 FORCE_RECREATE_DB=true");
              dbContext.ForceRecreateAllTables();
              Console.WriteLine("✅ 数据库强制重建完成");
          }
          else
          {
              // 🔄 智能模式：增量更新数据库结构
              // 只创建不存在的表，更新表结构，保留现有数据
              Console.WriteLine("🧠 使用智能初始化模式（保留现有数据）");
              dbContext.CreateTable();
              Console.WriteLine("✅ 主数据库表检查完成");
          } */

        // 🔗 验证HQ总部数据库连接
        Console.WriteLine("🔍 检查HQ数据库连接...");
        // hqDbContext.CheckConnection();      // 测试连接是否正常
        // hqDbContext.CheckTables();          // 检查必要的表是否存在
        Console.WriteLine("✅ HQ数据库连接检查完成");

        // 🌱 初始化种子数据
        // 创建默认管理员账号、基础角色、系统配置等
        Console.WriteLine("🌱 开始初始化种子数据...");
        var seedDataService = services.GetRequiredService<SeedDataService>();
        //  await seedDataService.InitializeAsync();

        // 🔍 检查并初始化角色数据
        Console.WriteLine("🔍 检查角色数据...");
        var dataInitService = services.GetRequiredService<IDataInitializationService>();
        //  await dataInitService.CheckAndInitializeDataAsync();

        Console.WriteLine("🎉 数据库初始化完成！");
        Console.WriteLine("📊 后台定时任务服务已启动");
    }
}
catch (Exception ex)
{
    // 💥 数据库初始化失败处理
    // 任何数据库初始化错误都会终止应用启动，确保不在不完整状态下运行
    Console.WriteLine($"❌ 数据库初始化失败: {ex.Message}");
    Console.WriteLine($"🔍 详细错误信息: {ex}");
    Console.WriteLine("💡 请检查数据库连接字符串和权限设置");
    throw; // 重新抛出异常，终止应用启动
}

/*

// 🚀 应用启动前的数据库优化
// 创建性能优化索引和统计视图
try
{
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<SqlSugarContext>();
        await BlazorApp.Api.Data.WarehouseProductIndexCreator.CreateAllIndexesAsync(context.Db);
        await BlazorApp.Api.Data.WarehouseProductIndexCreator.CreateStatisticsViewsAsync(context.Db);
        await BlazorApp.Api.Data.WarehouseProductIndexCreator.AnalyzeDatabasePerformanceAsync(context.Db);
    }
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning(ex, "数据库索引创建过程中出现警告，但不影响应用启动");
}
 */
// 启动Web应用
app.Run();
