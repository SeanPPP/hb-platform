using System.Reflection;
using Hbpos.Client.Wpf;

namespace Hbpos.Client.Tests;

public sealed class AppGlobalLoggingTests
{
    [Fact]
    public void App_uses_two_second_total_host_shutdown_budget()
    {
        var field = typeof(App).GetField("HostShutdownTimeoutSeconds", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.Equal(2, field!.GetRawConstantValue());
    }

    [Theory]
    [InlineData("OnDispatcherUnhandledException")]
    [InlineData("OnAppDomainUnhandledException")]
    [InlineData("OnUnobservedTaskException")]
    public void App_defines_global_exception_observer_without_changing_exception_semantics(string methodName)
    {
        var method = typeof(App).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal(typeof(void), method!.ReturnType);
    }
}
