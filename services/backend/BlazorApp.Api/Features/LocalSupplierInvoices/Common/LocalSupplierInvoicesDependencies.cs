using System.Data;
using System.Linq;
using System.Text.Json;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BlazorApp.Api.Features.LocalSupplierInvoices
{
    /// <summary>本地进货单各垂直切片共享的窄依赖端口。</summary>
    internal sealed class LocalSupplierInvoicesDependencies
    {
        public SqlSugarContext Context { get; }
        public HqSqlSugarContext HqContext { get; }
        public IMapper Mapper { get; }
        // Feature 只依赖通用日志端口，禁止通过日志泛型参数反向耦合 React façade。
        public ILogger Logger { get; }
        public IAutoPricingService AutoPricingService { get; }
        public IWarehouseProductChangeHistoryService ChangeHistoryService { get; }
        public ILocalSupplierInvoiceHqProductSyncService? HqProductSyncService { get; }

        public LocalSupplierInvoicesDependencies(
            SqlSugarContext context,
            HqSqlSugarContext hqContext,
            IMapper mapper,
            ILogger logger,
            IAutoPricingService autoPricingService,
            IWarehouseProductChangeHistoryService changeHistoryService,
            ILocalSupplierInvoiceHqProductSyncService? hqProductSyncService)
        {
            Context = context;
            HqContext = hqContext;
            Mapper = mapper;
            Logger = logger;
            AutoPricingService = autoPricingService;
            ChangeHistoryService = changeHistoryService ?? throw new ArgumentNullException(nameof(changeHistoryService));
            HqProductSyncService = hqProductSyncService;
        }
    }
}
