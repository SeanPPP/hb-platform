using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Media;

namespace Hbpos.Client.Wpf.Services;

public sealed class WpfAudioUserFeedbackService : IUserFeedbackService
{
    private const string SoundFolderName = "WAV";
    private const double PlaybackVolume = 0.75;
    private readonly List<MediaPlayer> _activePlayers = [];

    public void Play(UserFeedbackCue cue)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(new Action(() => Play(cue)));
            return;
        }

        PlayOnCurrentThread(cue);
    }

    internal static string GetSoundFileName(UserFeedbackCue cue)
    {
        return cue switch
        {
            UserFeedbackCue.ButtonClick => "iphone-keyboard-sound-typing.mp3",
            UserFeedbackCue.ScanAdded => "AddCartItem.wav",
            UserFeedbackCue.ScanMultipleMatches => "choice.wav",
            UserFeedbackCue.ScanNoMatch => "searchFail.wav",
            UserFeedbackCue.OperationError => "searchFail.wav",
            UserFeedbackCue.Delete => "delete.wav",
            UserFeedbackCue.Checkout => "checkOut.wav",
            UserFeedbackCue.CashDrawer => "drawOpen.wav",
            UserFeedbackCue.Download => "download.wav",
            UserFeedbackCue.VersionNotice => "new version.mp3",
            _ => "iphone-keyboard-sound-typing.mp3"
        };
    }

    private void PlayOnCurrentThread(UserFeedbackCue cue)
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, SoundFolderName, GetSoundFileName(cue));
        if (!File.Exists(filePath))
        {
            PlayFallback(cue);
            return;
        }

        var player = new MediaPlayer
        {
            Volume = PlaybackVolume
        };

        void ClosePlayer()
        {
            player.Close();
            _activePlayers.Remove(player);
        }

        player.MediaEnded += (_, _) => ClosePlayer();
        player.MediaFailed += (_, _) =>
        {
            ClosePlayer();
            PlayFallback(cue);
        };

        try
        {
            // 关键逻辑：MediaPlayer 需要在播放期间保留引用，否则短音效可能被提前回收。
            _activePlayers.Add(player);
            player.Open(new Uri(filePath));
            player.Play();
        }
        catch
        {
            ClosePlayer();
            PlayFallback(cue);
        }
    }

    private static void PlayFallback(UserFeedbackCue cue)
    {
        if (cue is UserFeedbackCue.ScanNoMatch or UserFeedbackCue.OperationError)
        {
            SystemSounds.Hand.Play();
            return;
        }

        SystemSounds.Beep.Play();
    }
}
