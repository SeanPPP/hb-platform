using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Services.React
{
    public class HqContainerReactService : IHqContainerReactService
    {
        private readonly HqSqlSugarContext _hq;
        private readonly ILogger<HqContainerReactService> _logger;

        public HqContainerReactService(
            HqSqlSugarContext hq,
            ILogger<HqContainerReactService> logger
        )
        {
            _hq = hq;
            _logger = logger;
        }

        public async Task<ContainerListResponse> GetContainersAsync(ContainerQueryRequest request)
        {
            var query = _hq.Db.Queryable<CPT_RED_货柜单主表Store>();

            if (request.StartDate.HasValue && request.EndDate.HasValue)
            {
                if (
                    string.Equals(
                        request.DateType,
                        "实际到货日期",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    query = query.Where(x =>
                        x.实际到货日期 >= request.StartDate && x.实际到货日期 <= request.EndDate
                    );
                }
                else
                {
                    query = query.Where(x =>
                        x.预计到岸日期 >= request.StartDate && x.预计到岸日期 <= request.EndDate
                    );
                }
            }

            var total = await query.CountAsync();

            // 默认按装柜日期或预计到岸日期倒序
            query = string.Equals(
                request.DateType,
                "实际到货日期",
                StringComparison.OrdinalIgnoreCase
            )
                ? query.OrderByDescending(x => x.实际到货日期)
                : query.OrderByDescending(x => x.预计到岸日期);

            var rows = await query
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToListAsync();

            var list = rows.Select(x => new ContainerMainDto
            {
                ID = x.ID,
                HGUID = x.HGUID,
                货柜编号 = x.货柜编号,
                装柜日期 = x.装柜日期,
                预计到岸日期 = x.预计到岸日期,
                实际到货日期 = x.实际到货日期,
                合计件数 = x.合计件数,
                合计数量 = x.合计数量,
                合计金额 = x.合计金额,
                总体积 = x.总体积,
                成本浮率 = x.成本浮率,
                汇率 = x.汇率,
                运费 = x.运费,
                备注 = x.备注,
                状态 = x.状态,
            })
                .ToList();

            return new ContainerListResponse
            {
                Containers = list,
                TotalCount = total,
                Page = request.Page,
                PageSize = request.PageSize,
            };
        }

        public async Task<ContainerMainDto?> GetContainerDetailAsync(string containerGuid)
        {
            if (string.IsNullOrWhiteSpace(containerGuid))
                return null;

            var main = await _hq
                .Db.Queryable<CPT_RED_货柜单主表Store>()
                .Where(x => x.HGUID == containerGuid)
                .FirstAsync();
            if (main == null)
                return null;

            var details = await _hq
                .Db.Queryable<CPT_RED_货柜单详情表Store>()
                .Where(d => d.主表GUID == containerGuid)
                .LeftJoin<CPT_DIC_商品信息字典表>((d, p) => d.商品编码 == p.商品编码)
                .Select(
                    (d, p) =>
                        new ContainerDetailDto
                        {
                            ID = d.ID,
                            HGUID = d.HGUID,
                            主表GUID = d.主表GUID,
                            商品编码 = d.商品编码,
                            装柜类型 = d.装柜类型,
                            商品类型 = d.商品类型,
                            套装数量 = d.套装数量,
                            装柜件数 = d.装柜件数,
                            装柜数量 = d.装柜数量,
                            国内价格 = d.国内价格,
                            调整浮率 = d.调整浮率,
                            进口价格 = d.进口价格,
                            贴牌价格 = d.贴牌价格,
                            单件装箱数 = d.单件装箱数,
                            单件体积 = d.单件体积,
                            合计装柜金额 = d.合计装柜金额,
                            合计装柜体积 = d.合计装柜体积,
                            运输成本 = d.运输成本,
                            备注 = d.备注,
                            商品信息 = new ContainerProductInfoDto
                            {
                                商品编码 = p.商品编码,
                                商品名称 = p.中文名称,
                                英文名称 = p.英文名称,
                                单件装箱数 = p.单件装箱数,
                                单件体积 = p.单件体积,
                                商品类型 = p.商品类型 == null ? null : p.商品类型.ToString(),
                                套装数量 = p.套装数量,
                            },
                        }
                )
                .ToListAsync();

            return new ContainerMainDto
            {
                ID = main.ID,
                HGUID = main.HGUID,
                货柜编号 = main.货柜编号,
                装柜日期 = main.装柜日期,
                预计到岸日期 = main.预计到岸日期,
                实际到货日期 = main.实际到货日期,
                合计件数 = main.合计件数,
                合计数量 = main.合计数量,
                合计金额 = main.合计金额,
                总体积 = main.总体积,
                成本浮率 = main.成本浮率,
                汇率 = main.汇率,
                运费 = main.运费,
                备注 = main.备注,
                状态 = main.状态,
                Details = details,
            };
        }
    }
}
