using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.POSM;
using BlazorApp.Shared.Security;
using Hbpos.Api;
using Hbpos.Api.Auth;
using Hbpos.Api.Controllers;
using Hbpos.Api.Data;
using Hbpos.Api.Services;
using Hbpos.Contracts.Cashiers;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.OperationAudits;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace Hbpos.Api.Tests;

public sealed class OperationAuditReadControllerTests
{
    [Fact]
    public void Service_registration_adds_scoped_operation_audit_reader()
    {
        var services = new ServiceCollection();

        services.AddHbposApiServices();

        var registration = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IOperationAuditReadService));
        Assert.Equal(typeof(SqlSugarOperationAuditReadService), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
    }

    [Fact]
    public async Task List_requires_authenticated_device_claims()
    {
        var readService = new RecordingOperationAuditReadService();
        var controller = CreateController();

        var action = await controller.List(null, 100, readService, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(action.Result);
        Assert.Empty(readService.ListCalls);
    }

    [Fact]
    public async Task List_uses_claim_scope_and_clamps_requested_limit()
    {
        var readService = new RecordingOperationAuditReadService();
        var controller = CreateController("STORE-1", "POS-1");

        var action = await controller.List("sale", 500, readService, CancellationToken.None);

        Assert.IsType<OkObjectResult>(action.Result);
        var call = Assert.Single(readService.ListCalls);
        Assert.Equal("STORE-1", call.StoreCode);
        Assert.Equal("POS-1", call.DeviceCode);
        Assert.Equal("sale", call.Keyword);
        Assert.Equal(100, call.Limit);
    }

    [Fact]
    public async Task Detail_uses_claim_scope_and_returns_not_found_for_hidden_record()
    {
        var readService = new RecordingOperationAuditReadService();
        var controller = CreateController("STORE-1", "POS-1");
        var eventId = Guid.NewGuid();

        var action = await controller.Detail(eventId, readService, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(action.Result);
        var call = Assert.Single(readService.DetailCalls);
        Assert.Equal(("STORE-1", "POS-1", eventId), call);
    }

    [Fact]
    public void Read_endpoints_require_operation_audit_view_policy()
    {
        var type = typeof(OperationAuditsController);

        foreach (var methodName in new[]
                 {
                     nameof(OperationAuditsController.List),
                     nameof(OperationAuditsController.Detail)
                 })
        {
            var attribute = Assert.Single(type.GetMethod(methodName)!
                .GetCustomAttributes<AuthorizeAttribute>());
            Assert.Equal(CashierAuthorizationPolicies.OperationAuditView, attribute.Policy);
        }

        var options = new AuthorizationOptions();
        CashierAuthorizationPolicies.AddPolicies(options);
        var policy = options.GetPolicy(CashierAuthorizationPolicies.OperationAuditView);
        var requirement = Assert.Single(policy!.Requirements.OfType<CashierPermissionRequirement>());
        Assert.Equal([Permissions.PosTerminal.Audit.View], requirement.PermissionCodes);
    }

    [Fact]
    public async Task Operation_audit_view_policy_rejects_missing_cashier_ticket_in_enforce_mode()
    {
        var options = new AuthorizationOptions();
        CashierAuthorizationPolicies.AddPolicies(options);
        var policy = options.GetPolicy(CashierAuthorizationPolicies.OperationAuditView);
        Assert.NotNull(policy);

        var httpContext = new DefaultHttpContext();
        httpContext.User = DevicePrincipal("STORE-1", "POS-1");
        httpContext.RequestServices = new ServiceCollection()
            .AddSingleton<ICashierService>(new DeniedCashierService())
            .AddSingleton<IEmergencyGrantAuthorizationService>(new MissingEmergencyGrantService())
            .BuildServiceProvider();
        var authorizationContext = new AuthorizationHandlerContext(
            policy!.Requirements,
            httpContext.User,
            null);
        var handler = new CashierPermissionAuthorizationHandler(
            new HttpContextAccessor { HttpContext = httpContext },
            new MissingCashierTicketService());

        await handler.HandleAsync(authorizationContext);

        Assert.False(authorizationContext.HasSucceeded);
    }

    private static OperationAuditsController CreateController(
        string? storeCode = null,
        string? deviceCode = null)
    {
        var context = new DefaultHttpContext();
        if (storeCode is not null && deviceCode is not null)
        {
            context.User = DevicePrincipal(storeCode, deviceCode);
        }

        return new OperationAuditsController(new NoOpOperationAuditIngestService())
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static ClaimsPrincipal DevicePrincipal(string storeCode, string deviceCode) =>
        new(new ClaimsIdentity(
        [
            new Claim(DeviceAuthConstants.StoreCodeClaim, storeCode),
            new Claim(DeviceAuthConstants.DeviceCodeClaim, deviceCode)
        ], DeviceAuthConstants.Scheme));

    private sealed class RecordingOperationAuditReadService : IOperationAuditReadService
    {
        public List<(string StoreCode, string DeviceCode, string? Keyword, int Limit)> ListCalls { get; } = [];

        public List<(string StoreCode, string DeviceCode, Guid EventId)> DetailCalls { get; } = [];

        public Task<OperationAuditReadListDto> ListAsync(
            string storeCode,
            string deviceCode,
            string? keyword,
            int limit,
            CancellationToken cancellationToken)
        {
            ListCalls.Add((storeCode, deviceCode, keyword, limit));
            return Task.FromResult(new OperationAuditReadListDto());
        }

        public Task<OperationAuditReadRecordDto?> GetAsync(
            string storeCode,
            string deviceCode,
            Guid eventId,
            CancellationToken cancellationToken)
        {
            DetailCalls.Add((storeCode, deviceCode, eventId));
            return Task.FromResult<OperationAuditReadRecordDto?>(null);
        }
    }

    private sealed class NoOpOperationAuditIngestService : IOperationAuditIngestService
    {
        public Task<OperationAuditBatchResultDto> IngestAsync(
            OperationAuditBatchRequestDto request,
            string storeCode,
            string deviceCode,
            CancellationToken cancellationToken,
            string? deviceSystem = null) =>
            Task.FromResult(new OperationAuditBatchResultDto());
    }

    private sealed class MissingCashierTicketService : ICashierAuthorizationTicketService
    {
        public (string Token, DateTimeOffset ExpiresAtUtc) Issue(
            string cashierId,
            string userGuid,
            string storeCode,
            string deviceCode) => throw new NotSupportedException();

        public CashierAuthorizationTicket? Validate(string? token) => null;
    }

    private sealed class DeniedCashierService : ICashierService
    {
        public Task<CashierSessionDto?> BarcodeLoginAsync(
            CashierBarcodeLoginRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult<CashierSessionDto?>(null);

        public Task<bool> HasAnyPermissionAsync(
            string userGuid,
            string storeCode,
            IReadOnlyCollection<string> permissionCodes,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<CashierSessionDto?> RefreshSessionAsync(
            CashierAuthorizationTicket ticket,
            CancellationToken cancellationToken) =>
            Task.FromResult<CashierSessionDto?>(null);
    }

    private sealed class MissingEmergencyGrantService : IEmergencyGrantAuthorizationService
    {
        public Task<EmergencyLoginVerifiedClaims?> ValidateAsync(
            string? token,
            string deviceStoreCode,
            CancellationToken cancellationToken) =>
            Task.FromResult<EmergencyLoginVerifiedClaims?>(null);
    }
}

public sealed class OperationAuditReadServiceTests : IAsyncDisposable
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        $"hbpos-audit-read-{Guid.NewGuid():N}.db");
    private readonly ISqlSugarClient database;
    private readonly SqlSugarOperationAuditReadService service;

    public OperationAuditReadServiceTests()
    {
        database = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = $"Data Source={databasePath}",
            DbType = DbType.Sqlite,
            InitKeyType = InitKeyType.Attribute,
            IsAutoCloseConnection = true
        });
        database.CodeFirst.InitTables<PosOperationAudit, PosOperationAuditItem>();
        service = new SqlSugarOperationAuditReadService(CreateDbContext(database));
    }

    [Fact]
    public async Task List_is_exactly_terminal_scoped_and_capped_at_one_hundred()
    {
        var scoped = Enumerable.Range(0, 105)
            .Select(index => CreateAudit(
                storeCode: "STORE-1",
                deviceCode: "POS-1",
                occurredAtUtc: DateTime.UtcNow.AddSeconds(-index)))
            .ToArray();
        await database.Insertable(scoped).ExecuteCommandAsync();
        await database.Insertable(new[]
        {
            CreateAudit(storeCode: "STORE-1", deviceCode: "POS-2"),
            CreateAudit(storeCode: "STORE-2", deviceCode: "POS-1")
        }).ExecuteCommandAsync();

        var result = await service.ListAsync(
            "STORE-1",
            "POS-1",
            keyword: null,
            limit: 500,
            CancellationToken.None);

        Assert.Equal(100, result.Items.Count);
        Assert.All(result.Items, item =>
        {
            Assert.Equal("STORE-1", item.StoreCode);
            Assert.Equal("POS-1", item.DeviceCode);
        });
    }

    [Fact]
    public async Task List_keyword_searches_only_safe_whitelisted_fields()
    {
        var safeMessageMatch = CreateAudit(safeMessage: "needle approved");
        var primaryProductMatch = CreateAudit(primaryProduct: "needle product");
        var sensitiveOnly = CreateAudit(
            paymentMethod: "needle",
            propertiesJson: """{"token":"needle","paymentId":"pay-secret"}""");
        await database.Insertable(new[]
        {
            safeMessageMatch,
            primaryProductMatch,
            sensitiveOnly
        }).ExecuteCommandAsync();

        var result = await service.ListAsync(
            "STORE-1",
            "POS-1",
            "needle",
            100,
            CancellationToken.None);

        Assert.Equal(
            new[] { safeMessageMatch.EventId, primaryProductMatch.EventId }.Order().ToArray(),
            result.Items.Select(item => item.EventId).Order().ToArray());
        Assert.DoesNotContain(result.Items, item => item.EventId == sensitiveOnly.EventId);
    }

    [Fact]
    public async Task Detail_is_terminal_scoped_and_maps_only_sanitized_contract()
    {
        var visible = CreateAudit(
            paymentAmount: 12.34m,
            propertiesJson: """{"authorizationCode":"secret","paymentId":"pay-secret"}""");
        var hidden = CreateAudit(deviceCode: "POS-2");
        await database.Insertable(new[] { visible, hidden }).ExecuteCommandAsync();
        await database.Insertable(new[]
        {
            new PosOperationAuditItem
            {
                EventId = visible.EventId,
                LineIndex = 0,
                ProductCode = "P-1",
                DisplayName = "Product",
                QuantityDelta = 2.5m,
                ActualAmountDelta = 1.23m
            }
        }).ExecuteCommandAsync();

        var detail = await service.GetAsync(
            "STORE-1",
            "POS-1",
            visible.EventId,
            CancellationToken.None);
        var crossTerminal = await service.GetAsync(
            "STORE-1",
            "POS-1",
            hidden.EventId,
            CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Null(crossTerminal);
        Assert.Equal(1234, detail!.PaymentAmountCents);
        var item = Assert.Single(detail.Items);
        Assert.Equal(123, item.ActualAmountDeltaCents);
        Assert.Equal("2.5", item.QuantityDelta);
        Assert.Equal("uploaded", detail.UploadState);
        Assert.DoesNotContain(
            typeof(OperationAuditReadRecordDto).GetProperties(),
            property => property.Name is "PropertiesJson" or "PaymentMethod");
    }

    public ValueTask DisposeAsync()
    {
        database.Dispose();
        try
        {
            File.Delete(databasePath);
        }
        catch (IOException)
        {
            // SQLite 可能短暂占用测试库文件，不影响断言。
        }

        return ValueTask.CompletedTask;
    }

    private static PosOperationAudit CreateAudit(
        string storeCode = "STORE-1",
        string deviceCode = "POS-1",
        DateTime? occurredAtUtc = null,
        string? safeMessage = null,
        string? primaryProduct = null,
        string? paymentMethod = null,
        string? propertiesJson = null,
        decimal? paymentAmount = null) =>
        new()
        {
            EventId = Guid.NewGuid(),
            SchemaVersion = 1,
            OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            OperationType = "SALE_COMPLETE",
            Outcome = "Succeeded",
            StoreCode = storeCode,
            DeviceCode = deviceCode,
            SafeMessage = safeMessage,
            PrimaryProduct = primaryProduct,
            PaymentMethod = paymentMethod,
            PropertiesJson = propertiesJson,
            PaymentAmount = paymentAmount,
            CurrencyCode = "AUD"
        };

    private static HbposSqlSugarContext CreateDbContext(ISqlSugarClient posmDb)
    {
        var context = (HbposSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(HbposSqlSugarContext));
        SetAutoProperty(context, nameof(HbposSqlSugarContext.MainDb), posmDb);
        SetAutoProperty(context, nameof(HbposSqlSugarContext.PosmDb), posmDb);
        return context;
    }

    private static void SetAutoProperty(
        HbposSqlSugarContext context,
        string propertyName,
        ISqlSugarClient value)
    {
        var backingField = typeof(HbposSqlSugarContext).GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(backingField);
        backingField!.SetValue(context, value);
    }
}
