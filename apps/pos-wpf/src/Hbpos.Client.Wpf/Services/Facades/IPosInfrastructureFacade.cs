namespace Hbpos.Client.Wpf.Services.Facades;

public interface IPosInfrastructureFacade
{
    IConnectivityApiClient ConnectivityApiClient { get; }
    IRawScannerService RawScannerService { get; }
    IUserFeedbackService? UserFeedbackService { get; }
    IApplicationExitService? ApplicationExitService { get; }
    IConfirmationDialogService? ConfirmationDialogService { get; }
}
