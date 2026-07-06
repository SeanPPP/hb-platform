namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 登录请求数据传输对象，用于用户登录时传输用户名和密码
    /// </summary>
    public class LoginRequest
    {
        /// <summary>
        /// 用户名
        /// </summary>
        public string Username { get; set; } = string.Empty;
        
        /// <summary>
        /// 密码
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// 密码格式：raw 表示 HTTPS 传输的原始密码；clientSha256 表示旧客户端传来的 SHA256。
        /// </summary>
        public string PasswordFormat { get; set; } = string.Empty;

        /// <summary>
        /// App 端稳定设备识别码，用于判断是否切换设备。
        /// </summary>
        public string? HardwareId { get; set; }

        /// <summary>
        /// 后台注册设备号，未注册设备为空。
        /// </summary>
        public string? SystemDeviceNumber { get; set; }

        /// <summary>
        /// 设备系统，例如 iOS 或 Android。
        /// </summary>
        public string? DeviceSystem { get; set; }

        /// <summary>
        /// App 登录时当前选择或绑定的分店代码。
        /// </summary>
        public string? StoreCode { get; set; }

        /// <summary>
        /// App 登录时采集的纬度。
        /// </summary>
        public double? LocationLatitude { get; set; }

        /// <summary>
        /// App 登录时采集的经度。
        /// </summary>
        public double? LocationLongitude { get; set; }

        /// <summary>
        /// App 登录时采集的位置精度，单位米。
        /// </summary>
        public double? LocationAccuracy { get; set; }

        /// <summary>
        /// App 登录时定位权限状态，服务端要求为 granted。
        /// </summary>
        public string? LocationPermissionStatus { get; set; }

        /// <summary>
        /// App 登录时定位采集时间。
        /// </summary>
        public DateTime? LocationCapturedAtUtc { get; set; }
    }
}
