namespace Hbpos.Client.Tests;

// 这些测试测量退出或 UI 空闲硬时限，必须独占运行以避免并行调度噪声。
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ShutdownTimingTestCollection
{
    public const string Name = "ShutdownTiming";
}
