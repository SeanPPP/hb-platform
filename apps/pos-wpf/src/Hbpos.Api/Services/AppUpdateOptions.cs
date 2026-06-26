namespace Hbpos.Api.Services;

public sealed class AppUpdateOptions
{
    public const string CenterApiKeyHeaderName = "X-HBPOS-App-Update-Key";

    public string? CenterBaseUrl { get; set; }

    public string Channel { get; set; } = "production";

    public string? CheckApiKey { get; set; }

    public string? CenterApiKey { get; set; }
}
