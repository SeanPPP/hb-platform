using System;
using BlazorApp.Api.Services;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public class RequiredLocationValidatorTests
    {
        [Fact]
        public void Validate_WhenLocationCapturedAtIsTooOld_ReturnsLocationRequired()
        {
            var result = RequiredLocationValidator.Validate(
                -27.4698,
                153.0251,
                "granted",
                DateTime.UtcNow.AddMinutes(-6),
                "登录需要位置信息",
                TimeSpan.FromMinutes(5)
            );

            Assert.False(result.Success);
            Assert.Equal("LOCATION_REQUIRED", result.ErrorCode);
            Assert.Equal("定位采集时间无效", result.Message);
        }

        [Fact]
        public void Validate_WhenLocationIsFreshAndGranted_ReturnsSuccess()
        {
            var result = RequiredLocationValidator.Validate(
                -27.4698,
                153.0251,
                "granted",
                DateTime.UtcNow.AddMinutes(-1),
                "登录需要位置信息",
                TimeSpan.FromMinutes(5)
            );

            Assert.True(result.Success);
        }
    }
}
