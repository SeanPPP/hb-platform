namespace BlazorApp.Shared.Constants;

public static class ServiceApiScopes
{
    public const string AttendanceFaceGateway = "Attendance.FaceGateway";
    public const string ReadAppUpdateDecisions = "Service.ReadAppUpdateDecisions";
    public const string WritePerformanceMetrics = "Service.WritePerformanceMetrics";
    public const string WriteReleaseEvents = "Service.WriteReleaseEvents";
    public const string RemoteMaintenance = "Service.RemoteMaintenance";
}

public static class ServiceApiTokenPurposes
{
    public const string AttendanceFaceGateway = "attendance-face-gateway";
    public const string MobileOtaPublisher = "mobile-ota-publisher";
    public const string PosIpadUpdateDecisionReader = "pos-ipad-update-decision-reader";
    public const string QualityCiReporter = "quality-ci-reporter";
    public const string DeploymentAcceptanceReporter = "deployment-acceptance-reporter";
    public const string RemoteMaintenanceGateway = "remote-maintenance-gateway";
}
