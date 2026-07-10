namespace BlazorApp.Api.Services
{
    public sealed record RequiredLocationValidationResult(
        bool Success,
        string Message,
        string ErrorCode
    );

    public static class RequiredLocationValidator
    {
        public const string ErrorCode = "LOCATION_REQUIRED";

        public static RequiredLocationValidationResult Validate(
            double? latitude,
            double? longitude,
            string? permissionStatus,
            DateTime? capturedAtUtc,
            string requiredMessage,
            TimeSpan maxAge
        )
        {
            if (!latitude.HasValue || !longitude.HasValue || !capturedAtUtc.HasValue)
            {
                return Error(requiredMessage);
            }

            if (!string.Equals(permissionStatus?.Trim(), "granted", StringComparison.OrdinalIgnoreCase))
            {
                return Error(requiredMessage);
            }

            // 服务端兜底验证坐标范围，避免绕过 App 直接写入无效审计位置。
            if (latitude.Value < -90 || latitude.Value > 90 || longitude.Value < -180 || longitude.Value > 180)
            {
                return Error("定位坐标无效");
            }

            var now = DateTime.UtcNow;
            var capturedAt = DateTime.SpecifyKind(capturedAtUtc.Value, DateTimeKind.Utc);
            if (capturedAt > now.AddMinutes(5) || capturedAt < now.Subtract(maxAge))
            {
                return Error("定位采集时间无效");
            }

            return new RequiredLocationValidationResult(true, string.Empty, string.Empty);
        }

        private static RequiredLocationValidationResult Error(string message)
        {
            return new RequiredLocationValidationResult(false, message, ErrorCode);
        }
    }
}
