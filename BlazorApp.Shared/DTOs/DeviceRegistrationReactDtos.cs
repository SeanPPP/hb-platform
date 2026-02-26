using System;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 设备注册列表 DTO
    /// </summary>
    public class DeviceRegistrationListDto
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        public int ID { get; set; }

        /// <summary>
        /// 设备硬件识别码
        /// </summary>
        public string 设备硬件识别码 { get; set; } = string.Empty;

        /// <summary>
        /// 系统设备编号
        /// </summary>
        public string 系统设备编号 { get; set; } = string.Empty;

        /// <summary>
        /// 分店代码
        /// </summary>
        public string? 分店代码 { get; set; }

        /// <summary>
        /// 分店名称
        /// </summary>
        public string? 分店名称 { get; set; }

        /// <summary>
        /// 设备类型：PDA/Mobile/POS/Admin
        /// </summary>
        public string 设备类型 { get; set; } = string.Empty;

        /// <summary>
        /// 设备系统：Android/iOS/Mac/Windows
        /// </summary>
        public string 设备系统 { get; set; } = string.Empty;

        /// <summary>
        /// 设备状态：-1-待确认 0-禁用 1-启用 2-锁定 3-未注册
        /// </summary>
        public int 设备状态 { get; set; }

        /// <summary>
        /// 设备状态描述
        /// </summary>
        public string 设备状态描述 { get; set; } = string.Empty;

        /// <summary>
        /// 设备授权码
        /// </summary>
        public string 设备授权码 { get; set; } = string.Empty;

        /// <summary>
        /// 备注
        /// </summary>
        public string? 备注 { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime 创建时间 { get; set; }

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime? 最后修改时间 { get; set; }

        /// <summary>
        /// 最后修改人
        /// </summary>
        public string? 最后修改人 { get; set; }
    }

    /// <summary>
    /// 设备注册详情 DTO
    /// </summary>
    public class DeviceRegistrationDetailDto
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        public int ID { get; set; }

        /// <summary>
        /// 设备硬件识别码
        /// </summary>
        public string 设备硬件识别码 { get; set; } = string.Empty;

        /// <summary>
        /// 系统设备编号
        /// </summary>
        public string 系统设备编号 { get; set; } = string.Empty;

        /// <summary>
        /// 分店代码
        /// </summary>
        public string? 分店代码 { get; set; }

        /// <summary>
        /// 分店名称
        /// </summary>
        public string? 分店名称 { get; set; }

        /// <summary>
        /// 设备类型：PDA/Mobile/POS/Admin
        /// </summary>
        public string 设备类型 { get; set; } = string.Empty;

        /// <summary>
        /// 设备系统：Android/iOS/Mac/Windows
        /// </summary>
        public string 设备系统 { get; set; } = string.Empty;

        /// <summary>
        /// 设备状态：-1-待确认 0-禁用 1-启用 2-锁定 3-未注册
        /// </summary>
        public int 设备状态 { get; set; }

        /// <summary>
        /// 设备状态描述
        /// </summary>
        public string 设备状态描述 { get; set; } = string.Empty;

        /// <summary>
        /// 设备授权码
        /// </summary>
        public string 设备授权码 { get; set; } = string.Empty;

        /// <summary>
        /// 备注
        /// </summary>
        public string? 备注 { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime 创建时间 { get; set; }

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime? 最后修改时间 { get; set; }

        /// <summary>
        /// 最后修改人
        /// </summary>
        public string? 最后修改人 { get; set; }

        /// <summary>
        /// 创建人
        /// </summary>
        public string? 创建人 { get; set; }
    }

    /// <summary>
    /// 更新设备注册 DTO
    /// </summary>
    public class UpdateDeviceRegistrationDto
    {
        /// <summary>
        /// 设备状态：-1-待确认 0-禁用 1-启用 2-锁定 3-未注册
        /// </summary>
        public int 设备状态 { get; set; }

        /// <summary>
        /// 设备类型：PDA/Mobile/POS/Admin
        /// </summary>
        public string? 设备类型 { get; set; }

        /// <summary>
        /// 设备系统：Android/iOS/Mac/Windows
        /// </summary>
        public string? 设备系统 { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string? 备注 { get; set; }
    }
}
