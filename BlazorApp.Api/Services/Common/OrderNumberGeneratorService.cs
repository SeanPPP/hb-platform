using System;
using System.Threading;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services.Common
{
    public class OrderNumberGeneratorService : IOrderNumberGenerator
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<OrderNumberGeneratorService> _logger;
        private int _current; // 内部序号计数器
        private int _year; // 当前年份
        private readonly object _initLock = new object(); // 初始化锁，防止并发初始化
        private bool _isInitialized; // 标记是否已初始化

        public OrderNumberGeneratorService(
            IServiceProvider serviceProvider,
            ILogger<OrderNumberGeneratorService> logger
        )
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _isInitialized = false;
        }

        /// <summary>
        /// 初始化服务：读取当前年份的最大序号作为起始点
        /// </summary>
        private void Initialize()
        {
            if (_isInitialized)
                return;

            lock (_initLock)
            {
                if (_isInitialized)
                    return;

                var yearNow = DateTime.Now.Year;
                try
                {
                    //创建作用域以获取数据库上下文
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<SqlSugarContext>().Db;

                        // 查询当前年份的订单号，获取最大的四位序号
                        var sql =
                            "SELECT ISNULL(MAX(TRY_CAST(RIGHT(OrderNo, 4) AS INT)), 0) FROM WareHouseOrder WHERE OrderNo LIKE @p";
                        var like = $"{yearNow}-%";
                        var maxObj = db.Ado.GetScalar(sql, new SugarParameter("@p", like));
                        var maxSeq = Convert.ToInt32(maxObj);

                        _year = yearNow;
                        // 设置计数器：如果最大序号小于1000，则从999开始（下次调用会得到1000）
                        // 如果有更大的序号，则从该序号开始（下次调用会得到maxSeq+1）
                        _current = maxSeq < 1000 ? 999 : maxSeq;

                        _logger.LogInformation(
                            $"订单号生成器初始化完成，当前年份: {_year}, 起始序号: {_current + 1}"
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"订单号生成器初始化失败，使用默认值");
                    _year = yearNow;
                    _current = 999; // 出错时默认从1000开始
                }
                finally
                {
                    _isInitialized = true;
                }
            }
        }

        /// <summary>
        /// 获取下一个订单号
        /// </summary>
        /// <returns>格式为 {YYYY}-{NNNN} 的订单号</returns>
        public Task<string> GetNextOrderNoAsync()
        {
            // 确保已初始化
            if (!_isInitialized)
            {
                Initialize();
            }

            var yearNow = DateTime.Now.Year;

            // 检测年份变化，如果是新年重新初始化
            if (yearNow != _year)
            {
                lock (_initLock)
                {
                    if (yearNow != _year)
                    {
                        _logger.LogInformation(
                            $"检测到年份变化，从 {_year} 变为 {yearNow}，重新初始化订单号生成器"
                        );
                        _isInitialized = false; // 重置初始化标记
                        Initialize();
                    }
                }
            }

            // 使用 Interlocked 进行线程安全的原子递增
            var nextSeq = Interlocked.Increment(ref _current);
            var orderNo = $"{_year}-{nextSeq.ToString("D4")}";

            _logger.LogDebug($"生成订单号: {orderNo}");
            return Task.FromResult(orderNo);
        }
    }
}
