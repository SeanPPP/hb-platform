using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services.Attendance;

/// <summary>人脸核验通过后的正式写卡只经此适配器进入既有考勤核心。</summary>
public interface IFaceAttendancePunchWriter
{
    Task<string> CommitAsync(FaceAttendanceEvent row, string actor, CancellationToken ct);
}

public sealed class FaceAttendancePunchWriter(AttendanceReactService attendance) : IFaceAttendancePunchWriter
{
    public Task<string> CommitAsync(FaceAttendanceEvent row, string actor, CancellationToken ct) =>
        attendance.CommitFacePunchAsync(row, actor, ct);
}
