using System.Net;
using System.Net.Http;

namespace Hbpos.Client.Wpf.Services;

// 设置服务保留结构化错误码，界面按资源键呈现，不依赖 HTTP 客户端实现。
internal sealed class LinklyBackendHttpException(
    string message,
    HttpStatusCode httpStatus,
    string? errorCode = null) : HttpRequestException(message, inner: null, statusCode: httpStatus)
{
    public HttpStatusCode HttpStatus { get; } = httpStatus;

    public string? ErrorCode { get; } = errorCode;
}
