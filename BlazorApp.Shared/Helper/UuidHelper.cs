using System;
using UUIDNext;

namespace BlazorApp.Shared.Helper
{
    /// <summary>
    /// UUID生成帮助类，提供UUID7格式的唯一标识符生成功能
    /// </summary>
    public static class UuidHelper
    {
        /// <summary>
        /// 生成UUID7格式的唯一标识符
        /// UUID7是基于时间的UUID，提供更好的排序性能和唯一性保证
        /// </summary>
        /// <returns>UUID7格式字符串（无连字符）</returns>
        public static string GenerateUuid7()
        {
            //返回大写字符串
            //无连字符
          
            return Uuid.NewDatabaseFriendly(Database.SqlServer)
                .ToString()
                .Replace("-", "")
                .ToUpper();
        }

        /// <summary>
        /// 生成带连字符的UUID7格式标识符
        /// </summary>
        /// <returns>UUID7格式字符串（带连字符）</returns>
        public static string GenerateUuid7WithHyphens()
        {
            return Uuid.NewDatabaseFriendly(Database.SqlServer).ToString();
        }

        /// <summary>
        /// 验证UUID7格式是否正确
        /// </summary>
        /// <param name="uuid">要验证的UUID字符串</param>
        /// <returns>是否为有效的UUID7格式</returns>
        public static bool IsValidUuid7(string uuid)
        {
            if (string.IsNullOrEmpty(uuid))
                return false;

            // 尝试解析为Guid
            if (!Guid.TryParse(uuid, out var guid))
                return false;

            var guidBytes = guid.ToByteArray();

            // 检查版本号是否为7
            var version = (guidBytes[6] & 0xf0) >> 4;
            return version == 7;
        }

        /// <summary>
        /// 从UUID7中提取时间戳
        /// </summary>
        /// <param name="uuid7">UUID7字符串</param>
        /// <returns>时间戳，如果无效则返回null</returns>
        public static DateTimeOffset? ExtractTimestampFromUuid7(string uuid7)
        {
            if (!IsValidUuid7(uuid7))
                return null;

            var guid = Guid.Parse(uuid7);
            var guidBytes = guid.ToByteArray();

            // 提取前6个字节的时间戳
            var timestampBytes = new byte[8];
            Array.Copy(guidBytes, 0, timestampBytes, 2, 6);

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(timestampBytes);
            }

            var timestamp = BitConverter.ToInt64(timestampBytes, 0);
            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
        }
    }
}
