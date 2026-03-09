using AutoMapper;
using BlazorApp.Shared.Models.POSM;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Mappings
{
    /// <summary>
    /// 设备相关的AutoMapper映射配置
    /// </summary>
    public class DeviceMappingProfile : Profile
    {
        public DeviceMappingProfile()
        {
            // POSM_设备注册信息表 到 DeviceData DTO 的映射
            CreateMap<POSM_设备注册信息表, DeviceDataDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.ID))
                .ForMember(dest => dest.HardwareId, opt => opt.MapFrom(src => src.设备硬件识别码))
                .ForMember(dest => dest.SystemDeviceNumber, opt => opt.MapFrom(src => src.系统设备编号))
                .ForMember(dest => dest.AuthCode, opt => opt.MapFrom(src => src.设备授权码))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.设备状态))
                .ForMember(dest => dest.DeviceType, opt => opt.MapFrom(src => src.设备类型))
                .ForMember(dest => dest.DeviceSystem, opt => opt.MapFrom(src => src.设备系统))
                .ForMember(dest => dest.StoreCode, opt => opt.MapFrom(src => src.分店代码))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.创建时间))
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => src.最后修改时间 ?? src.创建时间));

            // 设备注册响应数据映射
            CreateMap<POSM_设备注册信息表, DeviceRegistrationResponseDto>()
                .ForMember(dest => dest.DeviceId, opt => opt.MapFrom(src => src.ID))
                .ForMember(dest => dest.SystemDeviceNumber, opt => opt.MapFrom(src => src.系统设备编号))
                .ForMember(dest => dest.AuthCode, opt => opt.MapFrom(src => src.设备授权码))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.设备状态))
                .ForMember(dest => dest.StatusDescription, opt => opt.MapFrom(src => GetStatusDescription(src.设备状态)));

            // 设备列表数据映射（用于分页查询等）
            CreateMap<POSM_设备注册信息表, DeviceListItemDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.ID))
                .ForMember(dest => dest.HardwareId, opt => opt.MapFrom(src => src.设备硬件识别码))
                .ForMember(dest => dest.SystemDeviceNumber, opt => opt.MapFrom(src => src.系统设备编号))
                .ForMember(dest => dest.DeviceType, opt => opt.MapFrom(src => src.设备类型))
                .ForMember(dest => dest.DeviceSystem, opt => opt.MapFrom(src => src.设备系统))
                .ForMember(dest => dest.StoreCode, opt => opt.MapFrom(src => src.分店代码))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.设备状态))
                .ForMember(dest => dest.StatusDescription, opt => opt.MapFrom(src => GetStatusDescription(src.设备状态)))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.创建时间))
                .ForMember(dest => dest.LastModified, opt => opt.MapFrom(src => src.最后修改时间))
                .ForMember(dest => dest.CreatedBy, opt => opt.MapFrom(src => src.创建人))
                .ForMember(dest => dest.LastModifiedBy, opt => opt.MapFrom(src => src.最后修改人));
        }

        /// <summary>
        /// 获取设备状态描述
        /// </summary>
        private static string GetStatusDescription(int status)
        {
            return status switch
            {
                -1 => "待确认",
                0 => "禁用",
                1 => "启用",
                2 => "锁定",
                3 => "未注册",
                _ => "未知状态"
            };
        }
    }
}
