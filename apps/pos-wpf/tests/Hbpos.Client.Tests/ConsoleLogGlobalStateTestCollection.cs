namespace Hbpos.Client.Tests;

// 这些测试会修改 ConsoleLog 全局状态，必须独占运行以避免并行污染。
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleLogGlobalStateTestCollection
{
    public const string Name = "ConsoleLogGlobalState";
}
