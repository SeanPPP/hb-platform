using Hbpos.Contracts.Linkly;

namespace Hbpos.Client.Wpf.Services;

public static class LinklyTimeoutPolicy
{
    public static readonly TimeSpan BusinessWait = LinklyTimeoutConstants.BusinessWait;
    public static readonly TimeSpan HttpTimeout = LinklyTimeoutConstants.HttpTimeout;
}
