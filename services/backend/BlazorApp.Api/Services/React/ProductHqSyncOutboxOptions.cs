namespace BlazorApp.Api.Services.React;

public sealed class ProductHqSyncOutboxOptions
{
    public const string SectionName = "ProductHqSyncOutbox";

    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 5;

    public int LeaseSeconds { get; set; } = 120;

    public int LeaseRenewalSeconds { get; set; } = 30;

    public int BaseRetryDelaySeconds { get; set; } = 5;

    public int MaxRetryDelaySeconds { get; set; } = 300;

    public int ClaimBatchSize { get; set; } = 20;
}
