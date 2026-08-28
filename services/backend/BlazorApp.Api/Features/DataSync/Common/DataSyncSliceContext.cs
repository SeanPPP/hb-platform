using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Features.DataSync.Common;

/// <summary>
/// 保持旧构造函数兼容所需的同步依赖，仅由 facade 在进程内组装。
/// </summary>
internal sealed class DataSyncSliceContext
{
    public DataSyncSliceContext(SqlSugarContext localContext, HqSqlSugarContext hqContext, HBSalesSqlSugarContext hbSalesContext, ILogger logger, IMapper mapper, ITranslationService translationService, IConfiguration configuration, IWarehouseProductChangeHistoryService changeHistoryService, ICurrentUserService currentUserService)
    {
        LocalContext = localContext;
        HqContext = hqContext;
        HbSalesContext = hbSalesContext;
        Logger = logger;
        Mapper = mapper;
        TranslationService = translationService;
        Configuration = configuration;
        ChangeHistoryService = changeHistoryService;
        CurrentUserService = currentUserService;
    }

    public SqlSugarContext LocalContext { get; }
    public HqSqlSugarContext HqContext { get; }
    public HBSalesSqlSugarContext HbSalesContext { get; }
    public ILogger Logger { get; }
    public IMapper Mapper { get; }
    public ITranslationService TranslationService { get; }
    public IConfiguration Configuration { get; }
    public IWarehouseProductChangeHistoryService ChangeHistoryService { get; }
    public ICurrentUserService CurrentUserService { get; }
}

internal abstract class DataSyncSliceBase
{
    protected DataSyncSliceBase(DataSyncSliceContext context)
    {
        LocalContext = context.LocalContext;
        HqContext = context.HqContext;
        HbSalesContext = context.HbSalesContext;
        Logger = context.Logger;
        Mapper = context.Mapper;
        TranslationService = context.TranslationService;
        Configuration = context.Configuration;
        ChangeHistoryService = context.ChangeHistoryService;
        CurrentUserService = context.CurrentUserService;
        HistoryContextFactory = new DataSyncHistoryContextFactory(CurrentUserService);
    }

    protected SqlSugarContext LocalContext { get; }
    protected HqSqlSugarContext HqContext { get; }
    protected HBSalesSqlSugarContext HbSalesContext { get; }
    protected ILogger Logger { get; }
    protected IMapper Mapper { get; }
    protected ITranslationService TranslationService { get; }
    protected IConfiguration Configuration { get; }
    protected IWarehouseProductChangeHistoryService ChangeHistoryService { get; }
    protected ICurrentUserService CurrentUserService { get; }
    protected DataSyncHistoryContextFactory HistoryContextFactory { get; }
}
