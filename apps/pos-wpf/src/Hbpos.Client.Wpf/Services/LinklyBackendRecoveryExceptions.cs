namespace Hbpos.Client.Wpf.Services;

internal sealed class LinklyBackendLocalCancelException : Exception;

internal sealed class LinklyBackendResultUnknownException(string message) : Exception(message);

/// <summary>
/// 表示调用方取消发生在新卡交易提交到终端之前。workflow 依靠该窄信号区分
/// “退款 claim 已落库”与“金融请求已经提交”，避免制造假的未知交易。
/// </summary>
internal sealed class CardTerminalNotSubmittedException(
    OperationCanceledException innerException,
    CancellationToken cancellationToken)
    : OperationCanceledException(
        "Card terminal request was canceled before submission.",
        innerException,
        cancellationToken);
