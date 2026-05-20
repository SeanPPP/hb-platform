using BlazorApp.Api.Services.React;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public class StoreProductMaintenanceClearanceBarcodeHelperTests
    {
        [Theory]
        [InlineData("7", "007")]
        [InlineData("200", "200")]
        [InlineData("S0200", "200")]
        [InlineData("STORE1234", "234")]
        [InlineData("DEFAULT", "000")]
        [InlineData("", "000")]
        public void NormalizeStoreCodeSegment_UsesExpectedThreeDigitSegment(
            string input,
            string expected
        )
        {
            var result = StoreProductMaintenanceClearanceBarcodeHelper.NormalizeStoreCodeSegment(input);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void FormatDateSegment_UsesYyMMdd()
        {
            var result = StoreProductMaintenanceClearanceBarcodeHelper.FormatDateSegment(
                new DateTime(2025, 9, 11, 10, 30, 0)
            );

            Assert.Equal("250911", result);
        }

        [Fact]
        public void GenerateBarcode_BuildsBodyFromStoreSegmentDateAndRandomSegment()
        {
            var result = StoreProductMaintenanceClearanceBarcodeHelper.GenerateBarcode(
                "S0200",
                Array.Empty<string>(),
                new DateTime(2025, 9, 11, 8, 0, 0),
                () => 7
            );

            Assert.Equal("2002509110073", result);
        }

        [Fact]
        public void GenerateBarcode_UsesZeroStoreSegmentWhenStoreCodeHasNoDigits()
        {
            var result = StoreProductMaintenanceClearanceBarcodeHelper.GenerateBarcode(
                "DEFAULT",
                Array.Empty<string>(),
                new DateTime(2025, 9, 11, 8, 0, 0),
                () => 7
            );

            Assert.Equal("0002509110075", result);
        }

        [Fact]
        public void GenerateBarcode_RetriesWhenBarcodeAlreadyExists()
        {
            var existing = new[]
            {
                StoreProductMaintenanceClearanceBarcodeHelper.GenerateBarcode(
                    "200",
                    Array.Empty<string>(),
                    new DateTime(2025, 9, 11, 8, 0, 0),
                    () => 7
                ),
            };
            var sequence = new Queue<int>(new[] { 7, 8 });

            var result = StoreProductMaintenanceClearanceBarcodeHelper.GenerateBarcode(
                "200",
                existing,
                new DateTime(2025, 9, 11, 8, 0, 0),
                () => sequence.Dequeue()
            );

            Assert.Equal("2002509110080", result);
        }

        [Fact]
        public void GenerateBarcodeForRandom_UsesExpectedCheckDigit()
        {
            var result = StoreProductMaintenanceClearanceBarcodeHelper.GenerateBarcodeForRandom(
                "1004",
                new DateTime(2025, 9, 11, 8, 0, 0),
                7
            );

            Assert.Equal("0042509110071", result);
        }

        [Fact]
        public void GenerateBarcode_ThrowsWhenAllRandomSegmentsAreExhausted()
        {
            var existing = Enumerable
                .Range(0, StoreProductMaintenanceClearanceBarcodeHelper.MaxRandomAttempts)
                .Select(value =>
                    StoreProductMaintenanceClearanceBarcodeHelper.GenerateBarcode(
                        "200",
                        Array.Empty<string>(),
                        new DateTime(2025, 9, 11, 8, 0, 0),
                        () => value
                    )
                )
                .ToArray();
            var sequence = new Queue<int>(
                Enumerable.Range(0, StoreProductMaintenanceClearanceBarcodeHelper.MaxRandomAttempts)
            );

            var ex = Assert.Throws<InvalidOperationException>(
                () =>
                    StoreProductMaintenanceClearanceBarcodeHelper.GenerateBarcode(
                        "200",
                        existing,
                        new DateTime(2025, 9, 11, 8, 0, 0),
                        () => sequence.Dequeue()
                    )
            );

            Assert.Contains("随机段已耗尽", ex.Message);
        }
    }
}
