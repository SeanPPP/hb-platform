namespace Hbpos.Client.Tests;

/// <summary>
/// 用例中等待异步完成的保险超时。
/// </summary>
/// <remarks>
/// 这类超时只用于防止用例挂死，本身不属于被测行为，因此取值必须能容忍共享 CI runner
/// 的负载抖动。此前散落各处的 3 秒与 5 秒在 Windows 分片上会间歇性误报失败：
/// DeviceRegistrationTests 的轮询恢复用例与 ClientLogOutboxWriterTests 的审计刷写用例
/// 都因此挡下过与它们无关的 PR。
/// <para>
/// 被测行为自身的时间参数——终端超时、业务等待预算、耗时断言、虚拟时钟推进——不在此列，
/// 保持各自原值，不要改用本常量。
/// </para>
/// </remarks>
internal static class TestWaitTimeouts
{
    /// <summary>等待某个异步操作完成的默认上限。</summary>
    internal static readonly TimeSpan Default = TimeSpan.FromSeconds(30);
}
