using System.Text.RegularExpressions;
using SixLabors.ImageSharp;

namespace BlazorApp.Api.Services
{
    public static class EmployeeProfileImageRules
    {
        public const long MaximumFileSize = 5 * 1024 * 1024;
        private static readonly HashSet<string> AllowedKinds = new(StringComparer.OrdinalIgnoreCase)
        {
            "avatar",
            "identity",
        };
        private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp",
        };

        public static bool IsValid(string? kind, string? contentType, long fileSize) =>
            !string.IsNullOrWhiteSpace(kind)
            && AllowedKinds.Contains(kind)
            && !string.IsNullOrWhiteSpace(contentType)
            && Extensions.ContainsKey(contentType)
            && fileSize > 0
            && fileSize <= MaximumFileSize;

        public static string BuildObjectKey(
            string userGuid,
            string kind,
            string fileName,
            string contentType,
            string? token = null
        )
        {
            _ = fileName;
            var safeUserGuid = Regex.Replace(userGuid, "[^A-Za-z0-9_-]", string.Empty);
            var safeKind = kind.ToLowerInvariant();
            var safeToken = token ?? Guid.NewGuid().ToString("N");
            return $"employee-profiles/{safeUserGuid}/{safeKind}/{safeToken}{Extensions[contentType]}";
        }

        public static string BuildPendingObjectKey(
            string userGuid,
            string kind,
            string contentType,
            string? token = null
        ) => "pending/" + BuildObjectKey(userGuid, kind, string.Empty, contentType, token);

        public static bool OwnsPendingObjectKey(string? objectKey, string userGuid, string kind) =>
            objectKey is not null
            && objectKey.StartsWith("pending/", StringComparison.Ordinal)
            && OwnsObjectKey(objectKey["pending/".Length..], userGuid, kind);

        public static bool OwnsObjectKey(string? objectKey, string userGuid, string kind)
        {
            if (string.IsNullOrWhiteSpace(objectKey))
            {
                return false;
            }
            var safeUserGuid = Regex.Replace(userGuid, "[^A-Za-z0-9_-]", string.Empty);
            var prefix = $"employee-profiles/{safeUserGuid}/{kind.ToLowerInvariant()}/";
            return objectKey.StartsWith(prefix, StringComparison.Ordinal)
                && !objectKey[prefix.Length..].Contains('/');
        }

        public static bool MatchesMetadata(
            string userGuid,
            string kind,
            long actualSize,
            string? actualContentType,
            string? owner,
            string? metadataKind,
            long? declaredSize,
            string? declaredContentType
        ) =>
            IsValid(kind, actualContentType, actualSize)
            && string.Equals(owner, userGuid, StringComparison.Ordinal)
            && string.Equals(metadataKind, kind, StringComparison.OrdinalIgnoreCase)
            && declaredSize == actualSize
            && string.Equals(
                declaredContentType,
                actualContentType,
                StringComparison.OrdinalIgnoreCase
            );

        public static bool MatchesImageContent(byte[] bytes, string contentType, string kind)
        {
            if (bytes.Length == 0)
            {
                return false;
            }
            try
            {
                var info = Image.Identify(bytes, out var identifiedFormat);
                var maximumEdge = string.Equals(kind, "avatar", StringComparison.OrdinalIgnoreCase)
                    ? 1024
                    : 2048;
                var maximumPixels = (long)maximumEdge * maximumEdge;
                if (
                    info is null
                    || info.Width <= 0
                    || info.Height <= 0
                    || info.Width > maximumEdge
                    || info.Height > maximumEdge
                    || (long)info.Width * info.Height > maximumPixels
                    || !string.Equals(
                        identifiedFormat.DefaultMimeType,
                        contentType,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return false;
                }

                // 关键逻辑：Identify 只看头部，仍需完整解码一次，拒绝伪造 magic 或截断图片。
                using var image = Image.Load(bytes, out var decodedFormat);
                return string.Equals(
                    decodedFormat.DefaultMimeType,
                    contentType,
                    StringComparison.OrdinalIgnoreCase
                );
            }
            catch (UnknownImageFormatException)
            {
                return false;
            }
            catch (InvalidImageContentException)
            {
                return false;
            }
        }
    }
}
