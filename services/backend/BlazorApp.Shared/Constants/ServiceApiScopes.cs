namespace BlazorApp.Shared.Constants;

public static class ServiceApiScopes
{
    public const string ReadAppUpdateDecisions = "Service.ReadAppUpdateDecisions";
    public const string WritePerformanceMetrics = "Service.WritePerformanceMetrics";
    public const string WriteReleaseEvents = "Service.WriteReleaseEvents";
}

public static class ServiceApiTokenPurposes
{
    public const string MobileOtaPublisher = "mobile-ota-publisher";
    public const string PosIpadUpdateDecisionReader = "pos-ipad-update-decision-reader";
    public const string QualityCiReporter = "quality-ci-reporter";
    public const string DeploymentAcceptanceReporter = "deployment-acceptance-reporter";
}
