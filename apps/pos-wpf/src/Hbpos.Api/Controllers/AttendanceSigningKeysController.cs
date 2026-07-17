using System.Security.Claims;
using Hbpos.Api.Auth;
using Hbpos.Api.Services;
using Hbpos.Contracts.Attendance;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/v1/attendance/signing-key")]
[Authorize(AuthenticationSchemes = DeviceAuthConstants.Scheme)]
public sealed class AttendanceSigningKeysController(
    IAttendanceSigningKeyRegistrationService service) : ControllerBase
{
    [HttpPut]
    public async Task<ActionResult<ApiResult<AttendanceSigningKeyRegistrationResponse>>> Register(
        [FromBody] AttendanceSigningKeyRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var deviceCode = User.FindFirstValue(DeviceAuthConstants.DeviceCodeClaim);
        var storeCode = User.FindFirstValue(DeviceAuthConstants.StoreCodeClaim);
        var hardwareId = User.FindFirstValue(DeviceAuthConstants.HardwareIdClaim);
        if (string.IsNullOrWhiteSpace(deviceCode)
            || string.IsNullOrWhiteSpace(storeCode)
            || string.IsNullOrWhiteSpace(hardwareId))
        {
            return Unauthorized(ApiResult<AttendanceSigningKeyRegistrationResponse>.Fail(
                "DEVICE_AUTH_REQUIRED",
                "Device authorization is required."));
        }

        try
        {
            var response = await service.RegisterAsync(
                new AttendanceSigningKeyDeviceIdentity(deviceCode, storeCode, hardwareId),
                request,
                cancellationToken);
            return Ok(ApiResult<AttendanceSigningKeyRegistrationResponse>.Ok(response));
        }
        catch (AttendanceSigningKeyValidationException)
        {
            return BadRequest(ApiResult<AttendanceSigningKeyRegistrationResponse>.Fail(
                "ATTENDANCE_QR_KEY_INVALID",
                "Attendance QR key request is invalid."));
        }
        catch (AttendanceSigningKeyConflictException)
        {
            return Conflict(ApiResult<AttendanceSigningKeyRegistrationResponse>.Fail(
                "ATTENDANCE_QR_KEY_KID_CONFLICT",
                "The QR key id is already assigned; generate a new kid."));
        }
    }
}
