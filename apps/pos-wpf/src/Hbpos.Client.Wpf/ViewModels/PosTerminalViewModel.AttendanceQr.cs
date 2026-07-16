namespace Hbpos.Client.Wpf.ViewModels;

public sealed partial class PosTerminalViewModel
{
    // 仅作为独立考勤二维码 VM 的视图宿主，不把密钥和计时逻辑带入收银主 VM。
    public AttendanceQrPanelViewModel? AttendanceQrPanel { get; set; }
}
