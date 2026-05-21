namespace Hbpos.Contracts.Common;

public sealed record ApiResult<T>(
    bool Success,
    T? Data,
    string? ErrorCode = null,
    string? Message = null)
{
    public static ApiResult<T> Ok(T data, string? message = null) => new(true, data, null, message);

    public static ApiResult<T> Fail(string errorCode, string message) => new(false, default, errorCode, message);
}
