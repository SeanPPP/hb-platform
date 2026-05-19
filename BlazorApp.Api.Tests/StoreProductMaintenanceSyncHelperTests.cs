using BlazorApp.Api.Services.React;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public class StoreProductMaintenanceSyncHelperTests
    {
        [Fact]
        public void CalculateSetPurchasePrice_ReturnsRoundedRatio()
        {
            var result = StoreProductMaintenanceSyncHelper.CalculateSetPurchasePrice(12.5m, 25m, 18m);

            Assert.Equal(9m, result);
        }

        [Fact]
        public void CalculateSetPurchasePrice_ReturnsNullWhenMainRetailInvalid()
        {
            var result = StoreProductMaintenanceSyncHelper.CalculateSetPurchasePrice(12.5m, 0m, 18m);

            Assert.Null(result);
        }

        [Theory]
        [InlineData(0, "普通")]
        [InlineData(1, "套装")]
        [InlineData(2, "多码")]
        [InlineData(9, null)]
        public void NormalizeProductTypeLabel_UsesExpectedMapping(int input, string? expected)
        {
            var result = StoreProductMaintenanceSyncHelper.NormalizeProductTypeLabel(input);

            Assert.Equal(expected, result);
        }
    }
}
