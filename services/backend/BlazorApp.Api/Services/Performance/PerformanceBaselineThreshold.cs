namespace BlazorApp.Api.Services.Performance;

public static class PerformanceBaselineThreshold
{
    public static double LatencyWarning(double baselineP95) => Math.Max(0, baselineP95) * 1.20;

    public static int BacklogWarning(double baselineP95) =>
        (int)Math.Max(Math.Ceiling(Math.Max(0, baselineP95) * 1.20), Math.Ceiling(Math.Max(0, baselineP95) + 1));

    public static double FailureRateWarning(double baselineFailureRate) =>
        Math.Min(1, Math.Max(0, baselineFailureRate) + Math.Max(Math.Max(0, baselineFailureRate) * 0.20, 0.01));

    public static double CrashRateWarning(double baselineCrashRate) =>
        Math.Min(1, Math.Max(0, baselineCrashRate) + Math.Max(Math.Max(0, baselineCrashRate) * 0.20, 0.001));
}
