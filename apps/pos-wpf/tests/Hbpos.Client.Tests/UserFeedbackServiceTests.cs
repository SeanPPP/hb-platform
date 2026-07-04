using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class UserFeedbackServiceTests
{
    [Theory]
    [InlineData(UserFeedbackCue.ButtonClick, "iphone-keyboard-sound-typing.mp3")]
    [InlineData(UserFeedbackCue.ScanAdded, "AddCartItem.wav")]
    [InlineData(UserFeedbackCue.ScanMultipleMatches, "choice.wav")]
    [InlineData(UserFeedbackCue.ScanNoMatch, "searchFail.wav")]
    [InlineData(UserFeedbackCue.OperationError, "searchFail.wav")]
    [InlineData(UserFeedbackCue.Delete, "delete.wav")]
    [InlineData(UserFeedbackCue.Checkout, "checkOut.wav")]
    [InlineData(UserFeedbackCue.CashDrawer, "drawOpen.wav")]
    [InlineData(UserFeedbackCue.Download, "download.wav")]
    [InlineData(UserFeedbackCue.VersionNotice, "new version.mp3")]
    public void Wpf_audio_feedback_maps_cues_to_expected_sound_files(UserFeedbackCue cue, string expectedFileName)
    {
        Assert.Equal(expectedFileName, WpfAudioUserFeedbackService.GetSoundFileName(cue));
    }
}
