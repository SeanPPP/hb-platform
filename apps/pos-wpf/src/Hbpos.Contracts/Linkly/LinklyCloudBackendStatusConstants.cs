namespace Hbpos.Contracts.Linkly;

/// <summary>
/// Linkly Cloud Backend 会话状态与恢复动作常量
/// </summary>
public static class LinklyCloudBackendStatusConstants
{
    public const string StatusPending = "Pending";
    public const string StatusCompleted = "Completed";
    public const string StatusCancelled = "Cancelled";
    public const string StatusFailed = "Failed";
    public const string StatusNotSubmitted = "NotSubmitted";
    public const string StatusTokenRefreshRequired = "TokenRefreshRequired";

    public const string RecoveryRetry = "Retry";
    public const string RecoveryRefreshToken = "RefreshToken";

    public static bool IsSuccessfulSettlement(bool? operationSuccess, string? responseCode)
    {
        return operationSuccess == true &&
            string.Equals(responseCode?.Trim(), "00", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSettlementFailureStatus(string? status)
    {
        return string.Equals(status, StatusFailed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, StatusNotSubmitted, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, StatusCancelled, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase);
    }
}
