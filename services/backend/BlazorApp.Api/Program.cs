// ===================== 引用和命名空间 =====================
using System.Linq;
using System.Security.Claims;
using System.Text; // 字符串编码
using AutoMapper; // AutoMapper映射服务
using BlazorApp.Api.Authentication;
using BlazorApp.Api.Data; // 数据访问层
using BlazorApp.Api.Filters;
using BlazorApp.Api.Interfaces; // 数据模型
using BlazorApp.Api.Interfaces.React; // React 接口命名空间
using BlazorApp.Api.Mappings; // AutoMapper 映射配置
using BlazorApp.Api.Middleware;
using BlazorApp.Api.Models;
using BlazorApp.Api.Repositories;
using BlazorApp.Api.Repositories.Interfaces;
using BlazorApp.Api.Services; // 业务服务层
using BlazorApp.Api.Services.Attendance;
using BlazorApp.Api.Services.Background; // 后台定时服务
using BlazorApp.Api.Services.Logging;
using BlazorApp.Api.Services.OperationAudits;
using BlazorApp.Api.Services.Pricing; // 自动定价服务
using BlazorApp.Api.Services.React; // React 专用服务层
using BlazorApp.Api.Utils; // Cookie 配置辅助类
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer; // JWT Bearer认证
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens; // JWT令牌验证

// ===================== 应用程序入口点 =====================
// 创建WebApplicationBuilder实例，读取命令行参数和配置文件
// 这是ASP.NET Core 6+的新式启动方式，替代了传统的Startup.cs
var builder = WebApplication.CreateBuilder(args);

MapTencentCloudEnvironmentVariables(builder.Configuration);

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

// 初始化 Cookie 配置辅助类
CookieOptionsHelper.Initialize(builder.Configuration, builder.Environment);

// 注册内存缓存服务
builder.Services.AddMemoryCache();
var dataProtectionKeysPath = builder.Configuration.GetValue<string>("DataProtection:KeysPath");
if (string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtectionKeysPath = Path.Combine("App_Data", "DataProtectionKeys");
}

if (!Path.IsPathRooted(dataProtectionKeysPath))
{
    dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, dataProtectionKeysPath);
}

Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services
    .AddDataProtection()
    .SetApplicationName(
        builder.Configuration.GetValue<string>("DataProtection:ApplicationName")
        ?? "HB.AdminPlatform"
    )
    // SMTP 密码需要跨重启/部署解密，key ring 必须落在稳定目录，目录本身不提交到 git。
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

var attendanceQrDataProtectionKeysPath = builder.Configuration.GetValue<string>(
    "AttendanceQrDataProtection:KeysPath");
if (string.IsNullOrWhiteSpace(attendanceQrDataProtectionKeysPath))
{
    // 关键逻辑：生产环境禁止回退到容器内部目录，避免考勤密钥在重建后丢失或与 POS 不一致。
    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException(
            "生产环境必须配置 AttendanceQrDataProtection:KeysPath。");
    }

    attendanceQrDataProtectionKeysPath = Path.Combine("App_Data", "AttendanceQrDataProtectionKeys");
}

if (!Path.IsPathRooted(attendanceQrDataProtectionKeysPath))
{
    attendanceQrDataProtectionKeysPath = Path.Combine(
        builder.Environment.ContentRootPath,
        attendanceQrDataProtectionKeysPath);
}

// 关键逻辑：考勤二维码使用专用 key ring，避免 POS 获得全局 Data Protection 密钥。
Directory.CreateDirectory(attendanceQrDataProtectionKeysPath);
var attendanceQrDataProtectionProvider =
    BlazorApp.Api.Security.AttendanceQrKeyDataProtection.CreateProvider(
            attendanceQrDataProtectionKeysPath);
builder.Services.AddSingleton(
    BlazorApp.Api.Security.AttendanceQrKeyDataProtection.CreateProtector(
        attendanceQrDataProtectionProvider));
// 关键逻辑：短时打卡凭证复用考勤专用 key ring，但使用独立 purpose 与签码密钥隔离。
builder.Services.AddSingleton(
    BlazorApp.Api.Security.AttendancePunchAuthorizationDataProtection.CreateProtector(
        attendanceQrDataProtectionProvider));

builder.Services.Configure<TencentCloudSettings>(builder.Configuration.GetSection("TencentCloud"));
builder.Services.Configure<ApplicationLoggingOptions>(
    builder.Configuration.GetSection("ApplicationLogging")
);
builder.Services.AddSingleton<IApplicationLogQueue, ApplicationLogQueue>();
builder.Services.AddSingleton<ApplicationLogRateLimiter>();
builder.Services.AddSingleton<ILoggerProvider, ApplicationLogLoggerProvider>();
builder.Services.AddHostedService<ApplicationLogBackgroundService>();
builder.Services.AddHostedService<ApplicationLogCleanupService>();
builder.Services.AddHostedService<OperationAuditCleanupBackgroundService>();
builder.Services.AddScoped<SalesStatisticsJobService>();
builder.Services.AddScoped<HBSalesRecordStatisticsService>();
builder.Services.AddScoped<ScheduledTaskLogService>();
builder.Services.AddScoped<ScheduledTaskRetryService>();
builder.Services.AddScoped<ScheduledTaskRuntimeControlService>();
builder.Services.AddScoped<ScheduledTaskLeaseService>();
builder.Services.AddScoped<SalesStatisticsAlignmentService>();
builder.Services.Configure<ScheduledTaskOptions>(
    builder.Configuration.GetSection("ScheduledTasks")
);
builder.Services.AddHostedService<ProductStoreDailyStatisticRecoveryService>();
builder.Services.AddHostedService<ScheduledTaskService>();

// --------------------- CORS配置 ---------------------
// 🌐 配置跨域资源共享策略，支持 Cookie 认证方案
// ⚠️ 重要：AllowCredentials() 必须与 WithOrigins() 一起使用，不能与 AllowAnyOrigin() 一起使用
// 原因：当使用凭据时，必须明确指定允许的源，不能使用通配符 "*"
var configuredCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
var corsOrigins =
    configuredCorsOrigins?.Length > 0
        ? configuredCorsOrigins
        : new[]
        {
            // 🔧 开发环境默认域名
            "http://localhost:8000", // 前端开发服务器
            "http://localhost:3000", // 备用前端端口
            "http://localhost:5002", // 后端 API 端口
            "https://localhost", // 支持 HTTPS localhost（宝塔面板）
            // 🌐 生产环境域名
            "https://www.dats.com.au",
            "https://www.malmar.com.au",
            "https://www.yatstal.com.au",
            "https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com",
            "https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com",
            "http://hotbargain.vip",
        };

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "AllowSpecific",
        policy =>
        {
            // ✅ 配置 CORS 策略
            policy
                .WithOrigins(corsOrigins) // 📍 指定允许的源（域名列表）
                .AllowAnyMethod() // 🔓 允许所有 HTTP 方法（GET, POST, PUT, DELETE 等）
                .AllowAnyHeader() // 🔓 允许所有请求头
                .AllowCredentials(); // 🍪 允许发送凭据（Cookie、Authorization 头等）
            // ⚠️ 这是 Cookie 认证的关键配置
            // ✅ 前端请求时必须设置 withCredentials: true 或 credentials: 'include'
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
var authenticationBuilder = builder.Services.AddAuthentication(options =>
{
    // 默认认证保持 JWT，service token 只能在显式声明的自动化端点使用，避免越权访问普通 [Authorize]。
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
});

authenticationBuilder.AddPolicyScheme(
    ServiceApiTokenAuthenticationDefaults.PolicyScheme,
    "Bearer JWT or Service API Token",
    options =>
    {
        options.ForwardDefaultSelector = context =>
            ServiceApiTokenAuthenticationDefaults.RequestHasServiceApiToken(context.Request)
                ? ServiceApiTokenAuthenticationDefaults.AuthenticationScheme
                : JwtBearerDefaults.AuthenticationScheme;
    }
);
authenticationBuilder.AddScheme<AuthenticationSchemeOptions, ServiceApiTokenAuthenticationHandler>(
    ServiceApiTokenAuthenticationDefaults.AuthenticationScheme,
    ConfigureServiceApiTokenAuthentication
);
authenticationBuilder.AddJwtBearer(options =>
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

        // 🍪 从 Cookie 中读取 token（支持 Cookie 认证方案）
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // 优先从 Authorization header 读取 token
                var accessToken = context
                    .Request.Headers["Authorization"]
                    .FirstOrDefault()
                    ?.Split(" ")
                    .Last();

                // 如果 header 中没有 token，尝试从 Cookie 读取
                if (string.IsNullOrEmpty(accessToken))
                {
                    context.Request.Cookies.TryGetValue("access_token", out accessToken);
                }

                // 如果找到 token，设置到 Token 属性
                if (!string.IsNullOrEmpty(accessToken))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                var identity = principal?.Identities.FirstOrDefault(identity => identity.IsAuthenticated);
                if (principal == null || identity == null)
                {
                    context.Fail("无效的认证主体");
                    return;
                }

                var userGuid =
                    principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? principal.FindFirst("userId")?.Value
                    ?? principal.FindFirst("userGuid")?.Value
                    ?? principal.FindFirst("uid")?.Value
                    ?? principal.FindFirst(ClaimTypes.Name)?.Value
                    ?? principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

                if (string.IsNullOrWhiteSpace(userGuid))
                {
                    context.Fail("令牌缺少用户标识");
                    return;
                }

                var dbContext = context.HttpContext.RequestServices.GetRequiredService<SqlSugarContext>();
                var user = await dbContext.Db.Queryable<User>()
                    .FirstAsync(item => item.UserGUID == userGuid && item.IsActive && !item.IsDeleted);

                if (user == null)
                {
                    context.Fail("用户已失效");
                    return;
                }

                var authSessionValidator = context.HttpContext.RequestServices
                    .GetRequiredService<IAuthSessionValidator>();
                if (!await authSessionValidator.IsAccessSessionActiveAsync(userGuid, principal))
                {
                    context.Fail("登录会话已失效");
                    return;
                }

                var activeRoleNames = await dbContext.Db.Queryable<UserRole>()
                    .InnerJoin<Role>((userRole, role) => userRole.RoleGUID == role.RoleGUID)
                    .Where((userRole, role) =>
                        userRole.UserGUID == userGuid
                        && !userRole.IsDeleted
                        && role.IsActive
                        && !role.IsDeleted
                    )
                    .Select((userRole, role) => role.RoleName)
                    .Distinct()
                    .ToListAsync();

                var staleClaims = identity.Claims
                    .Where(claim =>
                        claim.Type == ClaimTypes.Role
                        || claim.Type == "permission"
                    )
                    .ToList();

                foreach (var staleClaim in staleClaims)
                {
                    identity.RemoveClaim(staleClaim);
                }

                foreach (var roleName in activeRoleNames)
                {
                    identity.AddClaim(new System.Security.Claims.Claim(ClaimTypes.Role, roleName));
                }
            },
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

// 🔑 当前用户服务 - 用于在数据库操作中自动填充审计字段（CreatedBy/UpdatedBy）
builder.Services.AddScoped<
    BlazorApp.Api.Services.ICurrentUserService,
    BlazorApp.Api.Services.CurrentUserService
>();
builder.Services.AddScoped<
    BlazorApp.Api.Interfaces.ICurrentUserManageableStoreScopeService,
    BlazorApp.Api.Services.CurrentUserManageableStoreScopeService
>();

// 📁 数据库上下文服务 - 使用作用域模式（Scoped）
// 说明：将原先的 Singleton 改为 Scoped，避免多个请求并发复用同一底层连接
// 好处：每个 HTTP 请求内共享一个上下文实例，请求结束自动释放，减少"连接未关闭/正在连接"的并发冲突
builder.Services.AddScoped<SqlSugarContext>(); // 主数据库上下文（每请求一个实例）
builder.Services.AddScoped<HqSqlSugarContext>(); // HQ总部数据库上下文（每请求一个实例）
builder.Services.AddScoped<HBSalesSqlSugarContext>(); // HBSales数据库上下文（每请求一个实例）
builder.Services.AddScoped<POSMSqlSugarContext>(); // POSM数据库上下文（每请求一个实例）
builder.Services.AddScoped<HBSalesRecordSqlSugarContext>(); // HBSalesRecord数据库上下文（每请求一个实例）
builder.Services.AddScoped<OperationAuditQueryService>(sp =>
    new OperationAuditQueryService(
        sp.GetRequiredService<POSMSqlSugarContext>().Db,
        sp.GetRequiredService<ICurrentUserManageableStoreScopeService>(),
        sp.GetRequiredService<IHttpContextAccessor>()
    )
);
builder.Services.AddScoped<OperationAuditRetentionService>(sp =>
    new OperationAuditRetentionService(sp.GetRequiredService<POSMSqlSugarContext>().Db)
);
builder.Services.AddScoped<ApplicationLogService>(sp =>
{
    var context = sp.GetRequiredService<SqlSugarContext>();
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApplicationLoggingOptions>>();
    var logger = sp.GetRequiredService<ILogger<ApplicationLogService>>();
    var queue = sp.GetRequiredService<IApplicationLogQueue>();
    return new ApplicationLogService(context.Db, options, logger, queue);
});
builder.Services.AddScoped(typeof(IRepository<>), typeof(SqlSugarRepository<>));
builder.Services.AddScoped<IStoreRetailPriceRepository, StoreRetailPriceRepository>();

builder.Services.AddScoped<MigrationScripts>(sp =>
{
    var context = sp.GetRequiredService<SqlSugarContext>();
    var logger = sp.GetRequiredService<ILogger<MigrationScripts>>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var currentUser = sp.GetRequiredService<ICurrentUserService>();
    return new MigrationScripts(context.Db, logger, loggerFactory, currentUser);
});

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
// 注册AutoMapper服务，自动扫描程序集中所有继承自Profile的类
// AutoMapper可以大大简化数据传输对象和领域模型之间的转换代码
// 例如：UserDto -> User, CreateOrderDto -> Order 等
// 注册AutoMapper服务，自动扫描程序集中所有继承自Profile的类
builder.Services.AddAutoMapper(cfg => { }, typeof(MappingProfile).Assembly);

// 🔧 业务服务层 - 使用作用域模式（Scoped）
// 作用域：每个HTTP请求创建一次实例，请求结束时销毁
// 适合包含状态或需要事务管理的业务服务
builder.Services.AddScoped<IAuthService, AuthService>(); // 认证服务
builder.Services.AddScoped<IAuthSessionValidator, AuthSessionValidator>(); // access token 会话有效性校验
builder.Services.AddSingleton<IClientIpResolver, ClientIpResolver>(); // 登录公网 IP 解析
builder.Services.AddScoped<IServiceApiTokenService, ServiceApiTokenService>(); // 后台自动化 service API token
builder.Services.AddScoped<IUserService, UserService>(); // 用户管理服务
builder.Services.AddScoped<
    IUserStorePosTerminalPermissionService,
    UserStorePosTerminalPermissionService
>();
builder.Services.AddScoped<IEmployeeProfileService, EmployeeProfileService>(); // 员工个人信息服务
builder.Services.AddScoped<EmployeeProfileSensitiveChangeService>();
builder.Services.AddScoped<EmployeeProfileMediaService>();
builder.Services.AddScoped<EmployeeCashierBarcodeService>();
builder.Services.AddHostedService<EmployeeImageUploadCleanupBackgroundService>();
builder.Services.AddScoped<IRoleService, RoleService>(); // 角色管理服务
builder.Services.AddScoped<IStoreService, StoreService>(); // 分店管理服务
builder.Services.AddScoped<StoreSyncService>(); // 分店数据同步服务
builder.Services.AddScoped<SeedDataService>(); // 种子数据初始化服务
builder.Services.AddScoped<IDataInitializationService, DataInitializationService>(); // 数据初始化服务
builder.Services.Configure<InvoiceEmailOptions>(builder.Configuration.GetSection("InvoiceEmail"));
builder.Services.Configure<EasWebhookOptions>(builder.Configuration.GetSection("EasWebhook"));
builder.Services.AddScoped<IInvoiceEmailSettingsService, InvoiceEmailSettingsService>();
builder.Services.AddScoped<IInvoiceEmailService, InvoiceEmailService>();
builder.Services.AddScoped<PaymentTerminalSettingsService>();
builder.Services.AddScoped<EmergencyLoginGrantService>();
builder.Services.AddScoped<EmergencyLoginKeyManagementService>();
builder.Services.AddHttpClient<TencentCosMobileAppBuildArtifactMirror>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<IMobileAppBuildArtifactMirror>(sp =>
    sp.GetRequiredService<TencentCosMobileAppBuildArtifactMirror>()
);
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
builder.Services.AddScoped<IDomesticProductCreationService, DomesticProductCreationService>(); // 国内商品货号条码批量创建服务
builder.Services.AddScoped<IAutoPricingService, AutoPricingService>(); // 自动定价计算服务
builder.Services.AddScoped<IVersionInfoService, VersionInfoService>(); // 版本管理服务
builder.Services.AddScoped<MobileAppBuildService>(sp =>
{
    var context = sp.GetRequiredService<SqlSugarContext>();
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EasWebhookOptions>>();
    var logger = sp.GetRequiredService<ILogger<MobileAppBuildService>>();
    return new MobileAppBuildService(context.Db, options, logger);
}); // Expo EAS APK 构建记录服务
builder.Services.AddScoped<IMobileAppBuildService>(sp =>
    sp.GetRequiredService<MobileAppBuildService>()
);
builder.Services.AddScoped<IMobileAppBuildMirrorQueue>(sp =>
    sp.GetRequiredService<MobileAppBuildService>()
);
builder.Services.AddHostedService<MobileAppBuildMirrorBackgroundService>();
builder.Services.AddScoped<WpfAppReleaseService>(sp =>
{
    var context = sp.GetRequiredService<SqlSugarContext>();
    var uploadService = sp.GetRequiredService<TencentCloudUploadService>();
    var logger = sp.GetRequiredService<ILogger<WpfAppReleaseService>>();
    return new WpfAppReleaseService(context.Db, uploadService, logger);
}); // WPF 客户端安装包发布与更新策略服务
builder.Services.AddScoped<IWpfAppReleaseService>(sp =>
    sp.GetRequiredService<WpfAppReleaseService>()
);
builder.Services.AddScoped<INavigationService, NavigationService>(); // 动态导航菜单服务

// React 专用：仅限 Product 与 WarehouseProduct 的商品检测/更新/新建服务
builder.Services.AddScoped<
    BlazorApp.Api.Interfaces.React.IProductWarehouseReactService,
    BlazorApp.Api.Services.React.ProductWarehouseReactService
>();
builder.Services.AddSingleton<IWarehouseProductHqSyncJobService, WarehouseProductHqSyncJobService>();
builder.Services.AddScoped<IDeviceRegistrationService, DeviceRegistrationService>(); // POSM设备注册管理服务
builder.Services.AddScoped<MobileAppDeviceStatusService>(sp =>
{
    var context = sp.GetRequiredService<SqlSugarContext>();
    var deviceRegistrationService = sp.GetRequiredService<IDeviceRegistrationService>();
    var logger = sp.GetRequiredService<ILogger<MobileAppDeviceStatusService>>();
    return new MobileAppDeviceStatusService(context.Db, deviceRegistrationService, logger);
}); // Expo App 设备版本与在线快照服务
builder.Services.AddScoped<UserLoginDeviceAuditService>(); // App 登录设备与定位审计
builder.Services.AddScoped<IProductSyncService, ProductSyncService>(); // 货柜商品同步服务（检测、批量创建、批量更新）
builder.Services.AddScoped<IProductIntegrityService, ProductIntegrityService>(); // 商品数据一致性校验与修复服务
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
builder.Services.AddScoped<IContainerAllocationSalesReportService, ContainerAllocationSalesReportService>();
builder.Services.AddScoped<
    IContainerProductCreationExecutorService,
    ContainerProductCreationExecutorService
>();
builder.Services.AddSingleton<IContainerProductCreationJobService, ContainerProductCreationJobService>();
builder.Services.Configure<ContainerHqSyncOptions>(
    builder.Configuration.GetSection("ContainerHqSync")
);
builder.Services.AddScoped<ContainerHqSyncService>();
builder.Services.AddScoped<IContainerHqSyncService, ContainerHqSyncService>();
builder.Services.AddScoped<IDomesticProductReactService, DomesticProductReactService>();
builder.Services.AddScoped<IProductPrefixCodeReactService, ProductPrefixCodeReactService>();
builder.Services.AddScoped<IProductGradeReactService, ProductGradeReactService>();
builder.Services.AddScoped<IDomesticSupplierReactService, DomesticSupplierReactService>();
builder.Services.AddScoped<ILocalSuppliersReactService, LocalSupplierReactService>();
builder.Services.AddScoped<IWarehouseCategoryReactService, WarehouseCategoryReactService>();
builder.Services.AddScoped<IProductCategoryReactService, ProductCategoryReactService>();
builder.Services.AddScoped<IProductReactService, ProductReactService>(); // Product CRUD和批量操作服务
builder.Services.AddSingleton<
    IProductSupplierImageBatchUpdateJobService,
    ProductSupplierImageBatchUpdateJobService
>();
builder.Services.AddSingleton<IProductStoreSyncJobService, ProductStoreSyncJobService>();
builder.Services.AddSingleton<IProductPushToHqJobService, ProductPushToHqJobService>();
builder.Services.AddSingleton<IStorePriceTransferJobService, StorePriceTransferJobService>();
builder.Services.AddScoped<IProductHqSyncService, ProductHqSyncService>(); // 商品HQ解耦同步服务
builder.Services.AddScoped<IProductSetCodeReactService, ProductSetCodeReactService>();
builder.Services.Configure<StoreRetailPriceHqSyncOptions>(
    builder.Configuration.GetSection("StoreRetailPriceHqSync")
);
builder.Services.AddScoped<IStoreRetailPriceHqSyncService, StoreRetailPriceHqSyncService>();
builder.Services.AddScoped<IStoreRetailPriceReactService, StoreRetailPriceReactService>();
builder.Services.AddScoped<IStoreProductPriceReactService, StoreProductPriceReactService>();
builder.Services.AddScoped<IStoreMultiCodePricesReactService, StoreMultiCodePricesReactService>();
builder.Services.AddScoped<IStorePriceTransferService, StorePriceTransferService>();
builder.Services.AddScoped<ICashRegisterUserReactService, CashRegisterUserReactService>();
builder.Services.AddScoped<IStoreUserReactService, StoreUserReactService>();
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
builder.Services.AddScoped<ILocalSupplierInvoiceHqSyncService, LocalSupplierInvoiceHqSyncService>();
builder.Services.AddScoped<
    ILocalSupplierInvoiceHqProductSyncService,
    LocalSupplierInvoiceHqProductSyncService
>();
builder.Services.AddScoped<ILocalSupplierInvoiceOcrService, NoopLocalSupplierInvoiceOcrService>();
builder.Services.AddScoped<ILocalSupplierInvoiceImportService, LocalSupplierInvoiceImportService>();
builder.Services.AddSingleton<
    ILocalSupplierInvoiceBatchUpdateJobService,
    LocalSupplierInvoiceBatchUpdateJobService
>();
builder.Services.AddScoped<ILocalSupplierInvoicesReactService, LocalSupplierInvoicesReactService>();
builder.Services.AddScoped<IPricingStrategyReactService, PricingStrategyReactService>();
builder.Services.AddScoped<IPromotionReactService, PromotionReactService>();
builder.Services.AddScoped<IAdvertisementReactService, AdvertisementReactService>();
builder.Services.AddSingleton<IStoreOrderSyncJobService, StoreOrderSyncJobService>();
builder.Services.AddSingleton<IStoreOrderInvoiceEmailJobService, StoreOrderInvoiceEmailJobService>();
builder.Services.AddSingleton<IStoreOrderPasteReplaceJobService, StoreOrderPasteReplaceJobService>();
builder.Services.AddScoped<IStoreOrderInvoiceAttachmentService, StoreOrderInvoiceAttachmentService>();
builder.Services.AddScoped<
    IStoreOrderInvoiceEmailTextTranslationService,
    StoreOrderInvoiceEmailTextTranslationService
>();
builder.Services.AddScoped<IStoreOrderHqSyncService, StoreOrderHqSyncService>();
builder.Services.AddScoped<IStoreOrderReactService, StoreOrderReactService>();
builder.Services.AddScoped<PreorderReactService>();
builder.Services.AddScoped<IPreorderReactService>(provider =>
    provider.GetRequiredService<PreorderReactService>()
);
builder.Services.AddScoped<IPreorderGateService>(provider =>
    provider.GetRequiredService<PreorderReactService>()
);
builder.Services.AddScoped<IStoreProductMaintenanceReactService, StoreProductMaintenanceReactService>();
builder.Services.AddScoped<IAustralianPublicHolidayProvider, AustralianPublicHolidayProvider>();
builder.Services.AddScoped<IAttendancePublicHolidaySyncService, AttendancePublicHolidaySyncService>();
builder.Services.AddScoped<IAttendanceReactService, AttendanceReactService>();
builder.Services.AddScoped<ISeasonalCardRemainingReactService, SeasonalCardRemainingReactService>();
builder.Services.AddScoped<IPDACartToOrderService, PDACartToOrderService>();
builder.Services.AddScoped<IPDAWarehouseOrderService, PDAWarehouseOrderService>();
builder.Services.AddScoped<IPosmSalesOrderReactService, PosmSalesOrderReactService>();
builder.Services.AddScoped<IInstallmentOrderReactService, InstallmentOrderReactService>();
builder.Services.AddScoped<IStoreVoucherReactService, StoreVoucherReactService>();
builder.Services.AddScoped<IDeviceRegistrationReactService, DeviceRegistrationReactService>();
builder.Services.AddScoped<IAttendancePosDeviceStatusProvider, AttendancePosDeviceStatusProvider>();
builder.Services.AddScoped<ITaxInvoiceService, TaxInvoiceService>();
builder.Services.AddScoped<ISalesDashboardReactService, SalesDashboardReactService>();
builder.Services.AddScoped<ISalesDashboardCacheWarmer, SalesDashboardCacheWarmer>();
builder.Services.AddScoped<IProductMovementReportService, ProductMovementReportService>();
builder.Services.AddScoped<
    ILocalSupplierInvoiceSalesAnalysisService,
    LocalSupplierInvoiceSalesAnalysisService
>();
builder.Services.AddScoped<ILocalPurchaseDashboardService, LocalPurchaseDashboardService>();
builder.Services.AddScoped<IHolidayProductReactService, HolidayProductReactService>();
builder.Services.AddScoped<IStoreManagerProductReactService, StoreManagerProductReactService>();
builder.Services.AddScoped<ILocationReactService, LocationReactService>();
builder.Services.AddScoped<
    BlazorApp.Api.Interfaces.IStoreOrderCacheWarmer,
    BlazorApp.Api.Cache.StoreOrderCacheWarmer
>();

builder.Services.AddScoped<SalesStatisticsJobService>();
builder.Services.AddScoped<SalesStatisticsAlignmentBackgroundRecalculateService>();

builder.Services.AddSingleton<
    BlazorApp.Api.Interfaces.React.IOrderNumberGenerator,
    BlazorApp.Api.Services.Common.OrderNumberGeneratorService
>();

// ===================== 应用构建与中间件配置 =====================

// 🏗️ 构建WebApplication实例
// 此时所有服务配置完成，开始构建实际的Web应用程序
var app = builder.Build();

// 📋 在应用构建完成后记录实际启用的 CORS 域名，避免在服务注册阶段提前创建容器。
app.Logger.LogInformation("CORS 允许的域名: {Origins}", string.Join(", ", corsOrigins));

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
    // 🔗 访问地址：http://localhost:5002/swagger
}

// 🔒 HTTPS重定向中间件（当前已禁用）
// 生产环境建议启用HTTPS重定向以提高安全性
// app.UseHttpsRedirection();

// 🧾 全局请求管道异常日志，覆盖未进入 MVC Filter 的异常。
app.UseMiddleware<ApplicationExceptionLoggingMiddleware>();

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
        dbContext.EnsureLoginSessionSchema();
        // 关键逻辑：空库先由 CodeFirst 创建 AttendancePunch 等基础表，再执行补列与索引迁移。
        dbContext.CreateTable();
        await StartupSchemaMigrator.EnsureAsync(dbContext.Db, app.Logger);
        await StartupSchemaMigrator.EnsurePosmAsync(posmDbContext.Db, app.Logger);
        await PaymentTerminalSettingsSchemaMigrator.EnsureAsync(posmDbContext.Db, app.Logger);
        await DeviceRuntimeStatusSchemaMigrator.EnsureAsync(posmDbContext.Db, app.Logger);
        await EmergencyLoginGrantSchemaMigrator.EnsureAsync(posmDbContext.Db, app.Logger);
        await EmergencyLoginKeySchemaMigrator.EnsureAsync(posmDbContext.Db, app.Logger);
        // 默认关闭已有表自动同步，中心日志新增列和过滤唯一索引在这里显式升级。
        await ApplicationLogSchemaMigrator.EnsureAsync(dbContext.Db, app.Logger);
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
        hqDbContext.CheckTables();          // 检查必要的表是否存在
        Console.WriteLine("✅ HQ数据库连接检查完成");

        // 🌱 初始化种子数据
        // 创建默认管理员账号、基础角色、系统配置等
        Console.WriteLine("🌱 开始初始化种子数据...");
        var seedDataService = services.GetRequiredService<SeedDataService>();
        await seedDataService.InitializePermissionSeedsAsync();
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
    Console.WriteLine($"❌ 数据库初始化失败: {ex.Message}");
    Console.WriteLine($"🔍 详细错误信息: {ex}");
    Console.WriteLine("💡 请检查数据库连接字符串和权限设置");
    throw;
}

// ===================== 缓存预热 =====================
var enableStoreOrderWarmUp = builder.Configuration.GetValue<bool>(
    "Cache:EnableStoreOrderWarmUp",
    false
);
if (enableStoreOrderWarmUp)
{
    _ = Task.Run(async () =>
    {
        try
        {
            var cacheWarmUpDelaySeconds = builder.Configuration.GetValue<int>(
                "Cache:StoreOrderWarmUpDelaySeconds",
                30
            );
            await Task.Delay(TimeSpan.FromSeconds(cacheWarmUpDelaySeconds));
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

static void ConfigureServiceApiTokenAuthentication(AuthenticationSchemeOptions options)
{
    // service token handler 不需要额外 options，保留显式方法避免链式注册里的 lambda 解析歧义。
}

static void MapTencentCloudEnvironmentVariables(ConfigurationManager configuration)
{
    var envMappings = new Dictionary<string, string>
    {
        ["TENCENT_SECRET_ID"] = "TencentCloud:SecretId",
        ["TENCENT_SECRET_KEY"] = "TencentCloud:SecretKey",
        ["TENCENT_BUCKET_NAME"] = "TencentCloud:BucketName",
        ["TENCENT_REGION"] = "TencentCloud:Region",
        ["TENCENT_IMAGE_BUCKET_NAME"] = "TencentCloud:ImageBucketName",
        ["TENCENT_IMAGE_REGION"] = "TencentCloud:ImageRegion",
    };

    foreach (var (envKey, configKey) in envMappings)
    {
        var value = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrWhiteSpace(value))
        {
            configuration[configKey] = value;
        }
    }
}
