namespace Hbpos.Api.Services;

// Linkly Cloud 出站真实连接池上限（Token 与 REST 各一个 typed client，每个目标 origin 独立计数）。
// 原先两者各限 1 条连接，全进程所有门店的请求在同一条 HTTP/1.1 连接上排队：一台终端 11–17 秒的
// 登录/状态测试会挡住其他门店的收款与轮询，排队时间还计入 240 秒 HttpClient 超时，超时即被记为 408 进入恢复。
// Linkly 并未规定连接数（官方异步示例用默认 HttpClient），业务并发由按终端闸门控制；
// 这里的上限只是异常时防止 socket 无界增长的保险，放宽连接数不会增加请求量。
internal static class LinklyCloudHttpConnectionPolicy
{
    public const int MaxConnectionsPerOrigin = 16;

    // 监控服务按环境合计 Token 与 REST 两个来源的物理连接。
    public const int MaxTotalConnections = MaxConnectionsPerOrigin * 2;
}
