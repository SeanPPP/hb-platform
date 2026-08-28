using System.Collections.Concurrent;
using System.Data;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.DataSync.Common;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using SqlSugar;

namespace BlazorApp.Api.Features.DataSync.Queries;

/// <summary>
/// DataSyncConnectionQuery 保留旧同步步骤的顺序，仅负责本切片的持久化与读取。
/// </summary>
internal sealed class DataSyncConnectionQuery : DataSyncSliceBase
{
    public DataSyncConnectionQuery(DataSyncSliceContext context)
        : base(context)
    {
    }

        public async Task<SyncResult> TestPostgresConnectionAsync()
        {
            var result = new SyncResult();
            var connectionString =
                "Host=hotbargain.vip;Port=5432;Database=postgresdb;Username=postgres;Password=REDACTED;";

            try
            {
                Logger.LogInformation("🔗 开始测试PostgreSQL数据库连接...");
                Logger.LogInformation(
                    "连接字符串: Host=hotbargain.vip;Port=5432;Database=postgresdb;Username=postgres;Password=REDACTED;"
                );

                using var connection = new Npgsql.NpgsqlConnection(connectionString);

                // 测试连接
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                await connection.OpenAsync();
                stopwatch.Stop();

                // 测试简单查询
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT version(), current_database(), current_user, now()";
                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    var version = reader.GetString(0);
                    var database = reader.GetString(1);
                    var user = reader.GetString(2);
                    var serverTime = reader.GetDateTime(3);

                    result.IsSuccess = true;
                    result.Message =
                        $"✅ PostgreSQL连接成功！响应时间: {stopwatch.ElapsedMilliseconds}ms";
                    result.Details =
                        $"数据库版本: {version.Split(',')[0]}\n"
                        + $"数据库名称: {database}\n"
                        + $"连接用户: {user}\n"
                        + $"服务器时间: {serverTime:yyyy-MM-dd HH:mm:ss}";

                    Logger.LogInformation(
                        "✅ PostgreSQL数据库连接测试成功，响应时间: {ElapsedMs}ms",
                        stopwatch.ElapsedMilliseconds
                    );
                }
                else
                {
                    result.IsSuccess = false;
                    result.Message = "❌ 连接成功但无法执行查询";
                    result.Details = "数据库连接正常，但查询测试失败";
                }

                await connection.CloseAsync();
            }
            catch (Npgsql.NpgsqlException ex)
            {
                result.IsSuccess = false;
                result.Message = "❌ PostgreSQL连接失败";
                result.Details = $"错误类型: {ex.GetType().Name}\n错误信息: {ex.Message}";
                result.ErrorCount = 1;

                Logger.LogError(ex, "PostgreSQL数据库连接失败");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = "❌ 数据库连接测试异常";
                result.Details = $"异常类型: {ex.GetType().Name}\n异常信息: {ex.Message}";
                result.ErrorCount = 1;

                Logger.LogError(ex, "PostgreSQL数据库连接测试时发生异常");
            }

            return result;
        }

        public async Task<CPT_DIC_商品信息字典表?> GetProductWithStockInfoAsync(string productCode)
        {
            try
            {
                Logger.LogInformation($"🔍 使用导航查询获取商品 {productCode} 的完整信息...");

                // 🚀 从商品信息表出发，使用导航属性查询关联的库存信息
                var productWithStock = await HqContext
                    .CPT_DIC_商品信息字典表_HQDb.AsQueryable()
                    .Includes(x => x.库存信息) // 使用导航属性加载关联的库存信息
                    .Where(x => x.商品编码 == productCode)
                    .FirstAsync();

                if (productWithStock?.库存信息 != null)
                {
                    Logger.LogInformation($"✅ 商品 {productCode} 查询成功：");
                    Logger.LogInformation(
                        $"   - 商品名称: {productWithStock.中文名称 ?? productWithStock.英文名称}"
                    );
                    Logger.LogInformation($"   - 当前库存: {productWithStock.库存信息.H库存}");
                    Logger.LogInformation(
                        $"   - 最小订货量: {productWithStock.库存信息.H最小订货量}"
                    );
                    Logger.LogInformation(
                        $"   - 零售价: {productWithStock.库存信息.H贴牌价格:C2}"
                    );
                }
                else
                {
                    Logger.LogWarning($"⚠️ 商品 {productCode} 没有关联的库存信息");
                }

                return productWithStock;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"❌ 查询商品 {productCode} 信息时发生错误");
                return null;
            }
        }

        public async Task<List<CBP_DIC_商品库存表>> GetLowStockProductsAsync(
            decimal minStockThreshold = 100
        )
        {
            try
            {
                Logger.LogInformation(
                    $"🔍 使用导航查询获取库存低于 {minStockThreshold} 的商品..."
                );

                // 🚀 从库存表出发，使用导航属性查询关联的商品信息
                var lowStockProducts = await HqContext
                    .CBP_DIC_商品库存表Db.AsQueryable()
                    .Includes(x => x.商品信息) // 使用导航属性加载关联的商品信息
                    .Where(x => x.H库存 < minStockThreshold && x.H使用状态 == 1)
                    .OrderBy(x => x.H库存) // 按库存从低到高排序
                    .Take(50) // 限制返回50条
                    .ToListAsync();

                Logger.LogInformation($"✅ 找到 {lowStockProducts.Count} 个库存不足的商品");

                foreach (var product in lowStockProducts.Take(5)) // 只记录前5个
                {
                    var productName =
                        product.商品信息?.中文名称 ?? product.商品信息?.英文名称 ?? "未知商品";
                    Logger.LogInformation(
                        $"   - {product.H商品编码}: {productName}, 库存: {product.H库存}"
                    );
                }

                return lowStockProducts;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "❌ 查询库存不足商品时发生错误");
                return new List<CBP_DIC_商品库存表>();
            }
        }
}
