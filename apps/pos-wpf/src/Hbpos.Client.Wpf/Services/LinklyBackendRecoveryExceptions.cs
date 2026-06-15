namespace Hbpos.Client.Wpf.Services;

internal sealed class LinklyBackendLocalCancelException : Exception;

internal sealed class LinklyBackendResultUnknownException(string message) : Exception(message);
