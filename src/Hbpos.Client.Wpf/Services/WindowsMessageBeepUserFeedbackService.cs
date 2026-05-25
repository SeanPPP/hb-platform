using System.Runtime.InteropServices;

namespace Hbpos.Client.Wpf.Services;

public sealed class WindowsMessageBeepUserFeedbackService : IUserFeedbackService
{
    private const uint OkType = 0x00000000;
    private const uint IconExclamationType = 0x00000030;
    private const uint IconHandType = 0x00000010;

    public void Play(UserFeedbackCue cue)
    {
        MessageBeep(cue switch
        {
            UserFeedbackCue.ScanAdded => OkType,
            UserFeedbackCue.ScanMultipleMatches => IconExclamationType,
            UserFeedbackCue.ScanNoMatch => IconHandType,
            UserFeedbackCue.OperationError => IconHandType,
            _ => OkType
        });
    }

    [DllImport("user32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MessageBeep(uint type);
}
