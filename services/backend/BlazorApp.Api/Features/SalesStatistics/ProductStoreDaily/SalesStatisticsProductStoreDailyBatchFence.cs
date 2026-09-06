using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services;

public sealed record ProductStoreDailyStateFence(
    string StatisticType,
    string Status,
    string? SourceProductVersion,
    Guid? JobId,
    DateTime? LastAggregatedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? LastSourceUploadTime);

public sealed record ProductStoreDailyBatchFence(
    DateTime Date,
    IReadOnlyList<ProductStoreDailyStateFence> States);

internal static class SalesStatisticsProductStoreDailyBatchFenceOperations
{
    internal static void Validate(
        IReadOnlyCollection<SalesStatisticRefreshState> states,
        ProductStoreDailyBatchFence expectedFence,
        string expectedStatus)
    {
        var expected = expectedFence.States.ToDictionary(state => state.StatisticType, StringComparer.Ordinal);
        var actual = states.ToDictionary(state => state.StatisticType, StringComparer.Ordinal);
        if (expected.Count != 4 || actual.Count != 4 || expected.Keys.Except(actual.Keys, StringComparer.Ordinal).Any())
            throw new InvalidOperationException($"2025 批次状态不完整，拒绝更新: {expectedFence.Date:yyyy-MM-dd}");

        foreach (var expectedState in expected.Values)
        {
            if (!actual.TryGetValue(expectedState.StatisticType, out var state)
                || state.Status != expectedStatus
                || state.SourceProductVersion != expectedState.SourceProductVersion
                || state.JobId != expectedState.JobId
                || state.LastAggregatedAtUtc != expectedState.LastAggregatedAtUtc
                || state.CompletedAtUtc != expectedState.CompletedAtUtc
                || state.LastSourceUploadTime != expectedState.LastSourceUploadTime)
            {
                throw new InvalidOperationException(
                    $"2025 批次状态已被其他刷新替换，拒绝更新: {expectedFence.Date:yyyy-MM-dd}");
            }
        }
    }
}
