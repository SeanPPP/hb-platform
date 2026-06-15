using Hbpos.Contracts.Linkly;

namespace Hbpos.Api.Services;

public static class LinklyCloudBackendTimeoutPolicy
{
    public static readonly TimeSpan HttpTimeout = LinklyTimeoutConstants.HttpTimeout;
}
