using BlazorApp.Api.Controllers;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>
/// 国内套装模板的持久化契约测试。
/// </summary>
public sealed class DomesticSetTemplateServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ISqlSugarClient _db;
    private readonly SqlSugarContext _context;

    public DomesticSetTemplateServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"domestic-set-template-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"DataSource={_dbPath}",
            })
            .Build();
        _context = new SqlSugarContext(
            configuration,
            NullLogger<SqlSugarContext>.Instance,
            Mock.Of<ICurrentUserService>()
        );
        _db = _context.Db;
    }

    [Fact]
    public async Task 国内套装模板_按供应商隔离_更新保留子项快照并支持停用恢复()
    {
        var sharedAssembly = typeof(ApiResponse<>).Assembly;
        var apiAssembly = typeof(SqlSugarContext).Assembly;
        var templateType = RequireType(sharedAssembly, "BlazorApp.Shared.Models.DomesticSetTemplate");
        var itemType = RequireType(sharedAssembly, "BlazorApp.Shared.Models.DomesticSetTemplateItem");
        var saveRequestType = RequireType(sharedAssembly, "BlazorApp.Shared.DTOs.SaveDomesticSetTemplateRequest");
        var itemRequestType = RequireType(sharedAssembly, "BlazorApp.Shared.DTOs.SaveDomesticSetTemplateItemRequest");
        var serviceType = RequireType(apiAssembly, "BlazorApp.Api.Services.DomesticSetTemplateService");
        _db.CodeFirst.InitTables(templateType, itemType);

        var service = Activator.CreateInstance(serviceType, _context);
        Assert.NotNull(service);

        var created = await InvokeAsync(
            service!,
            "CreateAsync",
            CreateSaveRequest(
                saveRequestType,
                itemRequestType,
                "CN-A",
                "冬季礼盒",
                "冬季套装",
                null,
                ("子项 B", 12.5m),
                ("子项 A", 0m)
            )
        );

        Assert.True((bool)GetProperty(created, "Success")!);
        var createdTemplate = GetProperty(created, "Data")!;
        var templateId = Assert.IsType<string>(GetProperty(createdTemplate, "TemplateId"));
        Assert.Equal(2, Convert.ToInt32(GetProperty(createdTemplate, "SetQuantity")));
        var createdItems = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            GetProperty(createdTemplate, "SubItems")
        ).Cast<object>().ToList();
        Assert.Equal(new[] { "子项 B", "子项 A" }, createdItems.Select(item => (string)GetProperty(item, "ProductName")!).ToArray());
        Assert.Equal(new[] { 0, 1 }, createdItems.Select(item => Convert.ToInt32(GetProperty(item, "SortOrder"))).ToArray());

        var sameNameDifferentSupplier = await InvokeAsync(
            service,
            "CreateAsync",
            CreateSaveRequest(
                saveRequestType,
                itemRequestType,
                "CN-B",
                "冬季礼盒",
                "另一套装",
                null,
                ("子项 C", 5m)
            )
        );
        Assert.True((bool)GetProperty(sameNameDifferentSupplier, "Success")!);

        var sameSupplierDuplicate = await InvokeAsync(
            service,
            "CreateAsync",
            CreateSaveRequest(
                saveRequestType,
                itemRequestType,
                "CN-A",
                "冬季礼盒",
                "重复套装",
                null,
                ("子项 D", 8m)
            )
        );
        Assert.False((bool)GetProperty(sameSupplierDuplicate, "Success")!);
        Assert.Equal("DOMESTIC_SET_TEMPLATE_NAME_EXISTS", GetProperty(sameSupplierDuplicate, "ErrorCode"));

        var updated = await InvokeAsync(
            service,
            "UpdateAsync",
            templateId,
            "CN-A",
            CreateSaveRequest(
                saveRequestType,
                itemRequestType,
                "CN-A",
                "冬季礼盒（更新）",
                "更新后的套装",
                null,
                ("更新子项", 23m)
            )
        );
        Assert.True((bool)GetProperty(updated, "Success")!);
        var updatedTemplate = GetProperty(updated, "Data")!;
        Assert.Equal(1, Convert.ToInt32(GetProperty(updatedTemplate, "SetQuantity")));

        var reloaded = await InvokeAsync(service, "GetTemplateAsync", templateId, "CN-A");
        var reloadedTemplate = GetProperty(reloaded, "Data")!;
        var reloadedItem = Assert.Single(
            Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                GetProperty(reloadedTemplate, "SubItems")
            ).Cast<object>()
        );
        Assert.Equal("更新子项", GetProperty(reloadedItem, "ProductName"));
        Assert.Equal(23m, GetProperty(reloadedItem, "PrivateLabelPrice"));
        Assert.Equal(0, Convert.ToInt32(GetProperty(reloadedItem, "SortOrder")));

        var itemRows = await _db.Ado.SqlQueryAsync<TemplateItemAuditRow>(
            "SELECT IsDeleted, SortOrder FROM DomesticSetTemplateItem WHERE TemplateId = @templateId ORDER BY SortOrder",
            new SugarParameter("@templateId", templateId)
        );
        Assert.Equal(3, itemRows.Count);
        Assert.Equal(2, itemRows.Count(item => item.IsDeleted));
        Assert.Single(itemRows, item => !item.IsDeleted && item.SortOrder == 0);

        var deactivated = await InvokeAsync(service, "DeactivateAsync", templateId, "CN-A");
        Assert.True((bool)GetProperty(deactivated, "Success")!);

        var selectableTemplates = await InvokeAsync(service, "GetTemplatesAsync", "CN-A", false);
        Assert.Empty(
            Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                GetProperty(selectableTemplates, "Data")
            ).Cast<object>()
        );
        var managementTemplates = await InvokeAsync(service, "GetTemplatesAsync", "CN-A", true);
        var managementTemplate = Assert.Single(
            Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                GetProperty(managementTemplates, "Data")
            ).Cast<object>()
        );
        Assert.False((bool)GetProperty(managementTemplate, "IsEnabled")!);

        var restored = await InvokeAsync(
            service,
            "UpdateAsync",
            templateId,
            "CN-A",
            CreateSaveRequest(
                saveRequestType,
                itemRequestType,
                "CN-A",
                "冬季礼盒（恢复）",
                "恢复后的套装",
                true,
                ("恢复子项", 6m)
            )
        );
        Assert.True((bool)GetProperty(restored, "Success")!);
        Assert.True((bool)GetProperty(GetProperty(restored, "Data")!, "IsEnabled")!);

        var templateRows = await _db.Ado.SqlQueryAsync<TemplateAuditRow>(
            "SELECT IsDeleted, IsEnabled FROM DomesticSetTemplate WHERE TemplateId = @templateId",
            new SugarParameter("@templateId", templateId)
        );
        var templateRow = Assert.Single(templateRows);
        Assert.False(templateRow.IsDeleted);
        Assert.True(templateRow.IsEnabled);
    }

    [Fact]
    public async Task 国内套装模板_拒绝空字段空子项和负价格()
    {
        var sharedAssembly = typeof(ApiResponse<>).Assembly;
        var apiAssembly = typeof(SqlSugarContext).Assembly;
        var templateType = RequireType(sharedAssembly, "BlazorApp.Shared.Models.DomesticSetTemplate");
        var itemType = RequireType(sharedAssembly, "BlazorApp.Shared.Models.DomesticSetTemplateItem");
        var saveRequestType = RequireType(sharedAssembly, "BlazorApp.Shared.DTOs.SaveDomesticSetTemplateRequest");
        var itemRequestType = RequireType(sharedAssembly, "BlazorApp.Shared.DTOs.SaveDomesticSetTemplateItemRequest");
        var serviceType = RequireType(apiAssembly, "BlazorApp.Api.Services.DomesticSetTemplateService");
        _db.CodeFirst.InitTables(templateType, itemType);
        var service = Activator.CreateInstance(serviceType, _context)!;

        var missingName = await InvokeAsync(
            service,
            "CreateAsync",
            CreateSaveRequest(saveRequestType, itemRequestType, "CN-A", " ", "套装", null, ("子项", 1m))
        );
        var emptyItems = await InvokeAsync(
            service,
            "CreateAsync",
            CreateSaveRequest(saveRequestType, itemRequestType, "CN-A", "模板", "套装", null)
        );
        var negativePrice = await InvokeAsync(
            service,
            "CreateAsync",
            CreateSaveRequest(saveRequestType, itemRequestType, "CN-A", "模板", "套装", null, ("子项", -0.01m))
        );
        var missingPriceRequest = CreateSaveRequest(
            saveRequestType,
            itemRequestType,
            "CN-A",
            "模板",
            "套装",
            null,
            ("子项", 0m)
        );
        var missingPriceItem = Assert.Single(
            Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                GetProperty(missingPriceRequest, "SubItems")
            ).Cast<object>()
        );
        SetProperty(missingPriceItem, "PrivateLabelPrice", null);
        var missingPrice = await InvokeAsync(service, "CreateAsync", missingPriceRequest);

        Assert.Equal("DOMESTIC_SET_TEMPLATE_TEMPLATE_NAME_REQUIRED", GetProperty(missingName, "ErrorCode"));
        Assert.Equal("DOMESTIC_SET_TEMPLATE_ITEMS_REQUIRED", GetProperty(emptyItems, "ErrorCode"));
        Assert.Equal("DOMESTIC_SET_TEMPLATE_ITEM_PRICE_REQUIRED", GetProperty(missingPrice, "ErrorCode"));
        Assert.Equal("DOMESTIC_SET_TEMPLATE_ITEM_PRICE_INVALID", GetProperty(negativePrice, "ErrorCode"));
    }

    [Fact]
    public void 国内套装模板价格字段_可区分缺失价格与有效零价格()
    {
        var itemRequestType = RequireType(
            typeof(ApiResponse<>).Assembly,
            "BlazorApp.Shared.DTOs.SaveDomesticSetTemplateItemRequest"
        );
        var price = Assert.IsAssignableFrom<PropertyInfo>(
            itemRequestType.GetProperty("PrivateLabelPrice")
        );

        Assert.Equal(typeof(decimal?), price.PropertyType);
        Assert.NotEmpty(price.GetCustomAttributes(typeof(RequiredAttribute), inherit: true));
        Assert.NotEmpty(price.GetCustomAttributes(typeof(RangeAttribute), inherit: true));
    }

    [Fact]
    public void 国内套装模板控制器端点_都要求国内商品管理权限()
    {
        AssertTemplatePolicy<HttpGetAttribute>("GetTemplates", "templates");
        AssertTemplatePolicy<HttpGetAttribute>("GetTemplate", "templates/{templateId}");
        AssertTemplatePolicy<HttpPostAttribute>("CreateTemplate", "templates");
        AssertTemplatePolicy<HttpPutAttribute>("UpdateTemplate", "templates/{templateId}");
        AssertTemplatePolicy<HttpPostAttribute>("DeactivateTemplate", "templates/{templateId}/deactivate");
    }

    [Fact]
    public void 国内套装模板控制器_通过单一DI构造函数接收服务并要求供应商查询参数()
    {
        var constructors = typeof(DomesticProductCreationController).GetConstructors();
        var constructor = Assert.Single(constructors);
        var templateServiceParameter = Assert.Single(
            constructor.GetParameters(),
            parameter => parameter.ParameterType == typeof(IDomesticSetTemplateService)
        );
        Assert.True(templateServiceParameter.HasDefaultValue);

        foreach (var methodName in new[] { "GetTemplate", "UpdateTemplate", "DeactivateTemplate" })
        {
            var method = Assert.IsAssignableFrom<MethodInfo>(
                typeof(DomesticProductCreationController).GetMethod(methodName)
            );
            var supplierCode = Assert.Single(
                method.GetParameters(),
                parameter => parameter.Name == "supplierCode"
            );
            Assert.NotNull(supplierCode.GetCustomAttribute<FromQueryAttribute>());
        }
    }

    [Fact]
    public async Task 国内套装模板_详情更新停用必须匹配请求供应商()
    {
        InitializeTemplateSchema(_context);
        var service = new DomesticSetTemplateService(_context);
        var created = await InvokeAsync(
            service,
            "CreateAsync",
            CreateRequest("CN-A", "供应商隔离模板", "A 套装", null, ("A 子项", 1m))
        );
        var templateId = Assert.IsType<string>(
            GetProperty(GetProperty(created, "Data")!, "TemplateId")
        );

        var otherSupplierDetail = await InvokeAsync(service, "GetTemplateAsync", templateId, "CN-B");
        var otherSupplierUpdate = await InvokeAsync(
            service,
            "UpdateAsync",
            templateId,
            "CN-B",
            CreateRequest("CN-B", "越权修改", "越权套装", null, ("越权子项", 2m))
        );
        var otherSupplierDeactivate = await InvokeAsync(
            service,
            "DeactivateAsync",
            templateId,
            "CN-B"
        );

        Assert.False((bool)GetProperty(otherSupplierDetail, "Success")!);
        Assert.False((bool)GetProperty(otherSupplierUpdate, "Success")!);
        Assert.False((bool)GetProperty(otherSupplierDeactivate, "Success")!);
        Assert.All(
            new[] { otherSupplierDetail, otherSupplierUpdate, otherSupplierDeactivate },
            result => Assert.Equal("DOMESTIC_SET_TEMPLATE_NOT_FOUND", GetProperty(result, "ErrorCode"))
        );

        var ownDetail = await InvokeAsync(service, "GetTemplateAsync", templateId, "CN-A");
        Assert.True((bool)GetProperty(ownDetail, "Success")!);
        Assert.Equal("供应商隔离模板", GetProperty(GetProperty(ownDetail, "Data")!, "TemplateName"));
        Assert.True((bool)GetProperty(GetProperty(ownDetail, "Data")!, "IsEnabled")!);
    }

    [Fact]
    public async Task 国内套装模板_数据库唯一索引阻止并发创建和恢复冲突()
    {
        InitializeTemplateSchema(_context);
        var indexes = await _db.Ado.SqlQueryAsync<SqliteIndexInfo>(
            "PRAGMA index_list('DomesticSetTemplate')"
        );
        var enabledNameIndex = Assert.Single(
            indexes,
            index => index.name == "IX_DomesticSetTemplate_EnabledSupplierTemplateName_Unique"
        );
        Assert.Equal(1, enabledNameIndex.unique);
        Assert.Equal(1, enabledNameIndex.partial);

        var secondContext = CreateContext(_dbPath);
        try
        {
            InitializeTemplateSchema(secondContext);
            var firstService = new DomesticSetTemplateService(_context);
            var secondService = new DomesticSetTemplateService(secondContext);

            var createResults = await Task.WhenAll(
                InvokeAsync(
                    firstService,
                    "CreateAsync",
                    CreateRequest("CN-A", "并发模板", "第一套装", true, ("子项", 1m))
                ),
                InvokeAsync(
                    secondService,
                    "CreateAsync",
                    CreateRequest("CN-A", "并发模板", "第二套装", true, ("子项", 1m))
                )
            );
            Assert.Single(createResults, result => (bool)GetProperty(result, "Success")!);
            Assert.Single(
                createResults,
                result => !(bool)GetProperty(result, "Success")!
                    && Equals("DOMESTIC_SET_TEMPLATE_NAME_EXISTS", GetProperty(result, "ErrorCode"))
            );

            var disabledFirst = await InvokeAsync(
                firstService,
                "CreateAsync",
                CreateRequest("CN-A", "恢复并发模板", "停用套装一", false, ("子项", 1m))
            );
            var disabledSecond = await InvokeAsync(
                secondService,
                "CreateAsync",
                CreateRequest("CN-A", "恢复并发模板", "停用套装二", false, ("子项", 1m))
            );
            var firstId = Assert.IsType<string>(
                GetProperty(GetProperty(disabledFirst, "Data")!, "TemplateId")
            );
            var secondId = Assert.IsType<string>(
                GetProperty(GetProperty(disabledSecond, "Data")!, "TemplateId")
            );

            var restoreResults = await Task.WhenAll(
                InvokeAsync(
                    firstService,
                    "UpdateAsync",
                    firstId,
                    "CN-A",
                    CreateRequest("CN-A", "恢复并发模板", "恢复套装一", true, ("子项", 1m))
                ),
                InvokeAsync(
                    secondService,
                    "UpdateAsync",
                    secondId,
                    "CN-A",
                    CreateRequest("CN-A", "恢复并发模板", "恢复套装二", true, ("子项", 1m))
                )
            );
            Assert.Single(restoreResults, result => (bool)GetProperty(result, "Success")!);
            Assert.Single(
                restoreResults,
                result => !(bool)GetProperty(result, "Success")!
                    && Equals("DOMESTIC_SET_TEMPLATE_NAME_EXISTS", GetProperty(result, "ErrorCode"))
            );
        }
        finally
        {
            secondContext.Db.Dispose();
        }
    }

    [Fact]
    public async Task 国内套装模板_真实JSON省略价格时返回稳定验证错误()
    {
        InitializeTemplateSchema(_context);
        var request = JsonSerializer.Deserialize<SaveDomesticSetTemplateRequest>(
            """
            {
              "supplierCode": "CN-A",
              "templateName": "JSON 价格校验",
              "setProductName": "JSON 套装",
              "subItems": [{ "productName": "未填价格子项" }]
            }
            """
        );
        Assert.NotNull(request);

        var result = await new DomesticSetTemplateService(_context).CreateAsync(request!);

        Assert.False(result.Success);
        Assert.Equal("DOMESTIC_SET_TEMPLATE_ITEM_PRICE_REQUIRED", result.ErrorCode);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void 国内套装模板列表_SQLServer查询不生成布尔OR条件(
        bool includeInactive,
        bool expectsEnabledFilter
    )
    {
        using var sqlServerDb = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString =
                "Server=127.0.0.1;Database=hb_platform_sql_generation;"
                + "User Id=test;Password=test;TrustServerCertificate=True;",
            DbType = DbType.SqlServer,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        });

        var sql = DomesticSetTemplateService.BuildTemplatesQuery(
                sqlServerDb,
                "CN-A",
                includeInactive
            )
            .ToSql()
            .Key;

        Assert.DoesNotContain(" OR ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            expectsEnabledFilter,
            sql.Contains("( [IsEnabled]=1 )", StringComparison.OrdinalIgnoreCase)
        );
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private static Type RequireType(Assembly assembly, string qualifiedName) =>
        Assert.IsAssignableFrom<Type>(assembly.GetType(qualifiedName));

    private static object CreateSaveRequest(
        Type requestType,
        Type itemRequestType,
        string supplierCode,
        string templateName,
        string setProductName,
        bool? isEnabled,
        params (string ProductName, decimal PrivateLabelPrice)[] subItems
    )
    {
        var request = Activator.CreateInstance(requestType)!;
        SetProperty(request, "SupplierCode", supplierCode);
        SetProperty(request, "TemplateName", templateName);
        SetProperty(request, "SetProductName", setProductName);
        SetProperty(request, "IsEnabled", isEnabled);

        var itemList = (System.Collections.IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(itemRequestType)
        )!;
        foreach (var (productName, privateLabelPrice) in subItems)
        {
            var item = Activator.CreateInstance(itemRequestType)!;
            SetProperty(item, "ProductName", productName);
            SetProperty(item, "PrivateLabelPrice", privateLabelPrice);
            itemList.Add(item);
        }
        SetProperty(request, "SubItems", itemList);
        return request;
    }

    private static async Task<object> InvokeAsync(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethods().SingleOrDefault(candidate =>
            candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length
        );
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(target, arguments));
        await task;
        var result = task.GetType().GetProperty("Result")?.GetValue(task);
        Assert.NotNull(result);
        return result;
    }

    private static object? GetProperty(object target, string name) =>
        target.GetType().GetProperty(name)?.GetValue(target);

    private static void SetProperty(object target, string name, object? value)
    {
        var property = target.GetType().GetProperty(name);
        Assert.NotNull(property);
        property!.SetValue(target, value);
    }

    private static void AssertTemplatePolicy<TAttribute>(string methodName, string template)
        where TAttribute : HttpMethodAttribute
    {
        var method = Assert.IsAssignableFrom<MethodInfo>(
            typeof(DomesticProductCreationController).GetMethod(methodName)
        );
        var route = Assert.IsAssignableFrom<TAttribute>(method.GetCustomAttribute<TAttribute>());
        var authorization = Assert.IsAssignableFrom<AuthorizeAttribute>(
            method.GetCustomAttribute<AuthorizeAttribute>()
        );

        Assert.Equal(template, route.Template);
        Assert.Equal(Permissions.DomesticPurchase.ManageProducts, authorization.Policy);
    }

    private static void InitializeTemplateSchema(SqlSugarContext context)
    {
        context.Db.CodeFirst.InitTables(typeof(DomesticSetTemplate), typeof(DomesticSetTemplateItem));
        context.CreateIndexes();
    }

    private static SqlSugarContext CreateContext(string dbPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"DataSource={dbPath}",
            })
            .Build();
        return new SqlSugarContext(
            configuration,
            NullLogger<SqlSugarContext>.Instance,
            Mock.Of<ICurrentUserService>()
        );
    }

    private static SaveDomesticSetTemplateRequest CreateRequest(
        string supplierCode,
        string templateName,
        string setProductName,
        bool? isEnabled,
        params (string ProductName, decimal? PrivateLabelPrice)[] subItems
    ) => new()
    {
        SupplierCode = supplierCode,
        TemplateName = templateName,
        SetProductName = setProductName,
        IsEnabled = isEnabled,
        SubItems = subItems
            .Select(item => new SaveDomesticSetTemplateItemRequest
            {
                ProductName = item.ProductName,
                PrivateLabelPrice = item.PrivateLabelPrice,
            })
            .ToList(),
    };

    private sealed class TemplateAuditRow
    {
        public bool IsDeleted { get; set; }
        public bool IsEnabled { get; set; }
    }

    private sealed class TemplateItemAuditRow
    {
        public bool IsDeleted { get; set; }
        public int SortOrder { get; set; }
    }

    private sealed class SqliteIndexInfo
    {
        public string name { get; set; } = string.Empty;
        public int unique { get; set; }
        public int partial { get; set; }
    }
}
