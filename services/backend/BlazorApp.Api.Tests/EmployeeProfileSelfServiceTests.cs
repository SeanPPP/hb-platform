using BlazorApp.Api.Services;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public sealed class EmployeeProfileSelfServiceTests
    {
        [Theory]
        [InlineData("avatar", "image/jpeg", 1, true)]
        [InlineData("identity", "image/png", 5 * 1024 * 1024, true)]
        [InlineData("portrait", "image/jpeg", 100, false)]
        [InlineData("avatar", "image/heic", 100, false)]
        [InlineData("avatar", "image/jpeg", 0, false)]
        [InlineData("avatar", "image/jpeg", 5 * 1024 * 1024 + 1, false)]
        public void ValidateImageRequest_EnforcesKindTypeAndProcessedSize(
            string kind,
            string contentType,
            long fileSize,
            bool expected
        )
        {
            Assert.Equal(expected, EmployeeProfileImageRules.IsValid(kind, contentType, fileSize));
        }

        [Fact]
        public void BuildObjectKey_UsesServerOwnedUserDirectoryAndNormalizedExtension()
        {
            var key = EmployeeProfileImageRules.BuildObjectKey(
                "user/../../other",
                "identity",
                "ignored.heic",
                "image/jpeg",
                "fixed-token"
            );

            Assert.Equal(
                "employee-profiles/userother/identity/fixed-token.jpg",
                key
            );
        }

        [Theory]
        [InlineData("290000000000", "2900000000001")]
        [InlineData("291234567890", "2912345678906")]
        public void AppendEan13CheckDigit_GeneratesExpectedBarcode(string body, string expected)
        {
            Assert.Equal(expected, EmployeeCashierBarcodeService.AppendEan13CheckDigit(body));
        }

        [Fact]
        public void OwnsObjectKey_RejectsCrossUserAndWrongKind()
        {
            Assert.True(
                EmployeeProfileImageRules.OwnsObjectKey(
                    "employee-profiles/user-a/avatar/token.jpg",
                    "user-a",
                    "avatar"
                )
            );
            Assert.False(
                EmployeeProfileImageRules.OwnsObjectKey(
                    "employee-profiles/user-b/avatar/token.jpg",
                    "user-a",
                    "avatar"
                )
            );
            Assert.False(
                EmployeeProfileImageRules.OwnsObjectKey(
                    "employee-profiles/user-a/identity/token.jpg",
                    "user-a",
                    "avatar"
                )
            );
        }

        [Theory]
        [InlineData(100, "image/jpeg", "user-a", "avatar", 100, "image/jpeg", true)]
        [InlineData(101, "image/jpeg", "user-a", "avatar", 100, "image/jpeg", false)]
        [InlineData(100, "image/png", "user-a", "avatar", 100, "image/jpeg", false)]
        [InlineData(100, "image/jpeg", "user-b", "avatar", 100, "image/jpeg", false)]
        public void MatchesMetadata_RejectsForgedSizeTypeAndOwner(
            long actualSize,
            string actualType,
            string owner,
            string kind,
            long declaredSize,
            string declaredType,
            bool expected
        )
        {
            Assert.Equal(
                expected,
                EmployeeProfileImageRules.MatchesMetadata(
                    "user-a",
                    "avatar",
                    actualSize,
                    actualType,
                    owner,
                    kind,
                    declaredSize,
                    declaredType
                )
            );
        }

        [Fact]
        public void MatchesImageContent_RejectsForgedJpegBytes()
        {
            Assert.False(
                EmployeeProfileImageRules.MatchesImageContent(
                    Encoding.UTF8.GetBytes("this is not a jpeg"),
                    "image/jpeg",
                    "avatar"
                )
            );
        }

        [Fact]
        public void MatchesImageContent_AcceptsDecodedPng()
        {
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="
            );
            Assert.True(EmployeeProfileImageRules.MatchesImageContent(png, "image/png", "avatar"));
        }

        [Fact]
        public void MatchesImageContent_RejectsAvatarOver1024PixelsOnEitherEdge()
        {
            Assert.False(
                EmployeeProfileImageRules.MatchesImageContent(
                    CreateImageBytes(1025, 1, new JpegEncoder()),
                    "image/jpeg",
                    "avatar"
                )
            );
        }

        [Fact]
        public void MatchesImageContent_RejectsIdentityOver2048PixelsOnEitherEdge()
        {
            Assert.False(
                EmployeeProfileImageRules.MatchesImageContent(
                    CreateImageBytes(1, 2049, new JpegEncoder()),
                    "image/jpeg",
                    "identity"
                )
            );
        }

        [Fact]
        public void MatchesImageContent_AcceptsSmallJpegAndWebpButRejectsTruncatedJpeg()
        {
            var jpeg = CreateImageBytes(8, 6, new JpegEncoder());
            var webp = CreateImageBytes(8, 6, new WebpEncoder());
            Assert.True(EmployeeProfileImageRules.MatchesImageContent(jpeg, "image/jpeg", "avatar"));
            Assert.True(EmployeeProfileImageRules.MatchesImageContent(webp, "image/webp", "identity"));
            Assert.False(
                EmployeeProfileImageRules.MatchesImageContent(
                    jpeg[..(jpeg.Length / 2)],
                    "image/jpeg",
                    "avatar"
                )
            );
        }

        [Theory]
        [InlineData("Violation of UNIQUE KEY constraint", true)]
        [InlineData("Cannot insert duplicate key row (2601)", true)]
        [InlineData("network timeout", false)]
        public void IsUniqueConstraintViolation_OnlyRetriesDuplicateBarcodeFailures(
            string message,
            bool expected
        )
        {
            Assert.Equal(
                expected,
                EmployeeCashierBarcodeService.IsUniqueConstraintViolation(
                    new InvalidOperationException(message)
                )
            );
        }

        private static byte[] CreateImageBytes(
            int width,
            int height,
            SixLabors.ImageSharp.Formats.IImageEncoder encoder
        )
        {
            using var image = new Image<Rgba32>(width, height);
            using var stream = new MemoryStream();
            image.Save(stream, encoder);
            return stream.ToArray();
        }
    }
}
