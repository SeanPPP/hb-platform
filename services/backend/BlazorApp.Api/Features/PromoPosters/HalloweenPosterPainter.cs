namespace BlazorApp.Api.Features.PromoPosters;

internal static class HalloweenPosterPainter
{
    internal static SeasonalPosterLayout.Tokens For(PromoPosterSize size) => SeasonalPosterLayout.For(size);
    internal static void Paint(PosterCanvas canvas, PromoPosterSpec spec) => SeasonalPosterLayout.Paint(canvas, spec, halloween: true);
}
