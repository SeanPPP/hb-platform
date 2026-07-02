using Xunit;

namespace Hbpos.Client.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CultureSensitiveTestCollection
{
    public const string Name = "CultureSensitive";
}
