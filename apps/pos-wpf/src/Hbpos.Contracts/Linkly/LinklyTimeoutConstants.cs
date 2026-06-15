namespace Hbpos.Contracts.Linkly;

public static class LinklyTimeoutConstants
{
    public static readonly TimeSpan BusinessWait = TimeSpan.FromSeconds(180);
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(240);
}
