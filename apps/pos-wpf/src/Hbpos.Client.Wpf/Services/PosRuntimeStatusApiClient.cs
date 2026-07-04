using System.Net.Http;
using System.Net.Http.Json;

namespace Hbpos.Client.Wpf.Services;

public sealed record PosRuntimeStatusReport(
    bool IsOnline,
    string? CashierId,
    string? CashierName);

public interface IPosRuntimeStatusApiClient
{
    Task ReportAsync(
        PosRuntimeStatusReport report,
        CancellationToken cancellationToken = default);
}

public sealed class PosRuntimeStatusApiClient(HttpClient httpClient) : IPosRuntimeStatusApiClient
{
    public async Task ReportAsync(
        PosRuntimeStatusReport report,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/v1/devices/runtime-status",
            new
            {
                isOnline = report.IsOnline,
                currentCashierId = report.CashierId,
                currentCashierName = report.CashierName,
            },
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
