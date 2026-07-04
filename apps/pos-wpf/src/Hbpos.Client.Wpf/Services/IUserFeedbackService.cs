namespace Hbpos.Client.Wpf.Services;

public enum UserFeedbackCue
{
    ButtonClick,
    ScanAdded,
    ScanMultipleMatches,
    ScanNoMatch,
    OperationError,
    Delete,
    Checkout,
    CashDrawer,
    Download,
    VersionNotice
}

public interface IUserFeedbackService
{
    void Play(UserFeedbackCue cue);
}

public sealed class NoopUserFeedbackService : IUserFeedbackService
{
    public static NoopUserFeedbackService Instance { get; } = new();

    private NoopUserFeedbackService()
    {
    }

    public void Play(UserFeedbackCue cue)
    {
    }
}
