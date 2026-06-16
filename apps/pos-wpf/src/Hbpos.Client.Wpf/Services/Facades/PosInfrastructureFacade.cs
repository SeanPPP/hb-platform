namespace Hbpos.Client.Wpf.Services.Facades;

public sealed class PosInfrastructureFacade : IPosInfrastructureFacade
{
    public IConnectivityApiClient ConnectivityApiClient { get; }
    public IRawScannerService RawScannerService { get; }
    public IUserFeedbackService? UserFeedbackService { get; }
    public IApplicationExitService? ApplicationExitService { get; }
    public IConfirmationDialogService? ConfirmationDialogService { get; }

    public PosInfrastructureFacade(
        IConnectivityApiClient connectivityApiClient,
        IRawScannerService rawScannerService,
        IUserFeedbackService? userFeedbackService,
        IApplicationExitService? applicationExitService,
        IConfirmationDialogService? confirmationDialogService)
    {
        ConnectivityApiClient = connectivityApiClient;
        RawScannerService = rawScannerService;
        UserFeedbackService = userFeedbackService;
        ApplicationExitService = applicationExitService;
        ConfirmationDialogService = confirmationDialogService;
    }
}
