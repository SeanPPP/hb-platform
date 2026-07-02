using Xunit;

namespace Hbpos.Client.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalLoggingTestCollection
{
    public const string Name = "GlobalLogging";
}
