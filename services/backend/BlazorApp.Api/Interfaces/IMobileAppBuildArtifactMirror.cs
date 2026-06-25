using BlazorApp.Shared.Models.HBweb;

namespace BlazorApp.Api.Interfaces
{
    public sealed class MobileAppBuildArtifactMirrorResult
    {
        public string ArtifactUrl { get; set; } = string.Empty;

        public string ObjectKey { get; set; } = string.Empty;

        public DateTime MirroredAt { get; set; } = DateTime.UtcNow;
    }

    public sealed class MobileAppBuildArtifactMirrorException : InvalidOperationException
    {
        public MobileAppBuildArtifactMirrorException(string message, bool isDownloadUnsafe = false)
            : base(message)
        {
            IsDownloadUnsafe = isDownloadUnsafe;
        }

        public bool IsDownloadUnsafe { get; }
    }

    public interface IMobileAppBuildArtifactMirror
    {
        Task<MobileAppBuildArtifactMirrorResult> MirrorAsync(
            MobileAppBuild build,
            CancellationToken cancellationToken = default
        );
    }
}
