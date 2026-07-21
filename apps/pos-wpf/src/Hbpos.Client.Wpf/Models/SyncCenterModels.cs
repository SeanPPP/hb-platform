using CommunityToolkit.Mvvm.ComponentModel;

namespace Hbpos.Client.Wpf.Models;

public sealed partial class RowSelectionState : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;
}

public sealed record SyncQueueOverview(
    int PendingCount,
    int FailedCount,
    int SyncingCount,
    string? LastError);

public sealed record SyncQueueListItem(
    Guid EntityId,
    string EntityType,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastTriedAt,
    string? ErrorMessage,
    decimal? Amount)
{
    public RowSelectionState Selection { get; } = new();

    public bool CanRetry => EntityType.Equals("Order", StringComparison.OrdinalIgnoreCase) &&
        (Status.Equals("Pending", StringComparison.OrdinalIgnoreCase) ||
         Status.Equals("Failed", StringComparison.OrdinalIgnoreCase));

    public string ShortEntityId => EntityId.ToString("N")[..10].ToUpperInvariant();

    public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string LastTriedAtDisplay => LastTriedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "-";

    public string AmountDisplay => Amount.HasValue ? Amount.Value.ToString("C2") : "-";
}

public sealed record OperationAuditQueueListItem(
    Guid EventId,
    string Status,
    DateTimeOffset OccurredAt,
    int AttemptCount,
    DateTimeOffset NextAttemptAt,
    string? ErrorCode,
    string? ErrorMessage)
{
    public RowSelectionState Selection { get; } = new();

    public string ShortEventId => EventId.ToString("N")[..10].ToUpperInvariant();

    public string OccurredAtDisplay => OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string NextAttemptAtDisplay => NextAttemptAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
