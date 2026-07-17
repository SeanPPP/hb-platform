using System.Net.Http;
using System.Net.Http.Json;
using Hbpos.Contracts.Attendance;
using Hbpos.Contracts.Common;

namespace Hbpos.Client.Wpf.Services;

public interface IAttendanceSigningKeyApiClient
{
    Task<AttendanceSigningKeyRegistrationResponse> RegisterAsync(
        AttendanceSigningKeyRegistrationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AttendanceSigningKeyApiClient(HttpClient httpClient) : IAttendanceSigningKeyApiClient
{
    public async Task<AttendanceSigningKeyRegistrationResponse> RegisterAsync(
        AttendanceSigningKeyRegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync(
            "api/v1/attendance/signing-key",
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ApiResult<AttendanceSigningKeyRegistrationResponse>>(
            cancellationToken);
        return result is { Success: true, Data: not null }
            ? result.Data
            : throw new HttpRequestException(result?.Message ?? "考勤二维码密钥登记失败");
    }
}
