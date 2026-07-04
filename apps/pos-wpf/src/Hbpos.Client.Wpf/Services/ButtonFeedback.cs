using System.Windows;
using System.Windows.Controls.Primitives;

namespace Hbpos.Client.Wpf.Services;

public static class ButtonFeedback
{
    public static readonly DependencyProperty CueProperty = DependencyProperty.RegisterAttached(
        "Cue",
        typeof(UserFeedbackCue),
        typeof(ButtonFeedback),
        new PropertyMetadata(UserFeedbackCue.ButtonClick));

    public static void SetCue(ButtonBase element, UserFeedbackCue value) => element.SetValue(CueProperty, value);

    public static UserFeedbackCue GetCue(ButtonBase element) => (UserFeedbackCue)element.GetValue(CueProperty);
}

internal static class ButtonFeedbackRouter
{
    private static IUserFeedbackService? feedbackService;
    private static bool registered;

    public static void Register(IUserFeedbackService service)
    {
        feedbackService = service;
        if (registered)
        {
            return;
        }

        EventManager.RegisterClassHandler(
            typeof(ButtonBase),
            ButtonBase.ClickEvent,
            new RoutedEventHandler(OnButtonClick),
            handledEventsToo: true);
        registered = true;
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ButtonBase button)
        {
            return;
        }

        // 关键逻辑：所有未单独标记的按钮都走默认轻按键声，业务按钮只覆盖 Cue。
        feedbackService?.Play(ButtonFeedback.GetCue(button));
    }
}
