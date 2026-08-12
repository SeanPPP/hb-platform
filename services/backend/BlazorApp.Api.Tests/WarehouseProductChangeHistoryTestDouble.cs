using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Moq;

namespace BlazorApp.Api.Tests;

internal static class WarehouseProductChangeHistoryTestDouble
{
    public static IWarehouseProductChangeHistoryService CreateNoop()
    {
        var service = new Mock<IWarehouseProductChangeHistoryService>();
        service
            .Setup(x =>
                x.CaptureSnapshotsAsync(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new Dictionary<string, WarehouseProductChangeSnapshotDto>());
        service
            .Setup(x =>
                x.RecordChangesAsync(
                    It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                    It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                    It.IsAny<WarehouseProductChangeHistoryContextDto>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(0);
        service
            .Setup(x =>
                x.GetChangeHistoryAsync(
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new WarehouseProductChangeHistoryPageDto());
        return service.Object;
    }
}
