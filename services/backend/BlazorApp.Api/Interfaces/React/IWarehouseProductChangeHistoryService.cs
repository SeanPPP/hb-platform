using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React;

public interface IWarehouseProductChangeHistoryService
{
    Task<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>> CaptureSnapshotsAsync(
        IEnumerable<string> productCodes,
        CancellationToken cancellationToken = default
    );

    Task<int> RecordChangesAsync(
        IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto> beforeSnapshots,
        IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto> afterSnapshots,
        WarehouseProductChangeHistoryContextDto context,
        CancellationToken cancellationToken = default
    );

    Task<WarehouseProductChangeHistoryPageDto> GetChangeHistoryAsync(
        string productCode,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );
}
