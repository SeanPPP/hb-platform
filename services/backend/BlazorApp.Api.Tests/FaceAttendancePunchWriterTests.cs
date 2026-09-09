using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Models;
using BlazorApp.Api.Security;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Attendance;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class FaceAttendancePunchWriterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"face-writer-{Guid.NewGuid():N}.db");
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;
    private readonly FaceAttendanceService _faceService;
    private readonly FaceAttendancePunchWriter _writer;
    private long _rosterVersion;

    public FaceAttendancePunchWriterTests()
    {
        _connection = new SqliteConnection($"Data Source={_path}"); _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig { ConnectionString = _connection.ConnectionString, DbType = DbType.Sqlite, IsAutoCloseConnection = false, InitKeyType = InitKeyType.Attribute });
        _db.CodeFirst.InitTables(typeof(User), typeof(Store), typeof(UserStore), typeof(AttendanceSchedule), typeof(AttendancePunch), typeof(AttendanceSettings), typeof(AttendanceApproval), typeof(Role), typeof(UserRole), typeof(FaceAttendanceEnrollment), typeof(FaceAttendanceRosterSnapshot));
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext)); typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, _db);
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() }; var current = new CurrentUserService(http);
        var scope = new CurrentUserManageableStoreScopeService(context, current, http);
        var dataProtection = new EphemeralDataProtectionProvider();
        var protector = AttendanceQrKeyDataProtection.CreateProtector(dataProtection);
        var auth = AttendancePunchAuthorizationDataProtection.CreateProtector(dataProtection);
        var upload = new TencentCloudUploadService(Options.Create(new TencentCloudSettings { SecretId="a", SecretKey="b", BucketName="test-bucket", Region="x" }), NullLogger<TencentCloudUploadService>.Instance, new HttpClient());
        var attendance = new AttendanceReactService(context, current, scope, http, NullLogger<AttendanceReactService>.Instance, upload, null, TimeProvider.System, protector, auth);
        _writer = new FaceAttendancePunchWriter(attendance);
        _faceService = new FaceAttendanceService(context, null!, null!, dataProtection, null!, null!);
    }

    [Fact]
    public async Task 同一event幂等_重复ClockIn拒绝()
    {
        await SeedAsync(); var first = Event("e1", "clockIn", new DateTime(2026,9,9,9,0,0,DateTimeKind.Utc));
        var id = await _writer.CommitAsync(first, "test", default);
        Assert.Equal(id, await _writer.CommitAsync(first, "test", default));
        var ex = await Assert.ThrowsAsync<FaceAttendanceException>(() => _writer.CommitAsync(Event("e2", "clockIn", new DateTime(2026,9,9,10,0,0,DateTimeKind.Utc)), "test", default));
        Assert.Equal("FACE_PUNCH_SEQUENCE_CONFLICT", ex.Code);
    }

    [Fact]
    public async Task 打卡使用原始0900与1700时间()
    {
        await SeedAsync(); await _writer.CommitAsync(Event("in", "clockIn", new DateTime(2026,9,8,23,0,0,DateTimeKind.Utc)), "test", default);
        await _writer.CommitAsync(Event("out", "clockOut", new DateTime(2026,9,9,7,0,0,DateTimeKind.Utc)), "test", default);
        var rows = await _db.Queryable<AttendancePunch>().OrderBy(x => x.PunchTimeUtc).ToListAsync();
        Assert.Collection(rows, x => Assert.Equal(new DateTime(2026,9,8,23,0,0,DateTimeKind.Utc), x.PunchTimeUtc), x => Assert.Equal(new DateTime(2026,9,9,7,0,0,DateTimeKind.Utc), x.PunchTimeUtc));
    }

    [Fact]
    public async Task Unspecified发生时间按存储UTC墙钟值写入()
    {
        await SeedAsync();
        var occurredAt = new DateTime(2026, 9, 8, 23, 0, 0, DateTimeKind.Unspecified);

        await _writer.CommitAsync(Event("unspecified", "clockIn", occurredAt), "test", default);

        var row = await _db.Queryable<AttendancePunch>().FirstAsync(x => x.FaceEventGuid == "unspecified");
        Assert.Equal(occurredAt.Ticks, row.PunchTimeUtc.Ticks);
    }

    [Fact]
    public async Task 模板撤销发生在写卡前时拒绝正式写入()
    {
        await SeedAsync();
        var enrollment = await _db.Queryable<FaceAttendanceEnrollment>().FirstAsync(x => x.UserGuid == "u" && x.StoreCode == "BRI");
        enrollment.Status = "revoked";
        enrollment.Version++;
        await _db.Updateable(enrollment).ExecuteCommandAsync();

        var ex = await Assert.ThrowsAsync<FaceAttendanceException>(() => _writer.CommitAsync(
            Event("revoked", "clockIn", new DateTime(2026, 9, 8, 23, 0, 0, DateTimeKind.Utc)), "test", default));
        Assert.Equal("ROSTER_OR_ENROLLMENT_CHANGED", ex.Code);
    }

    [Fact]
    public async Task 员工门店关系在写卡前变化时拒绝正式写入()
    {
        await SeedAsync();
        var membership = await _db.Queryable<UserStore>().FirstAsync(x => x.UserStoreGUID == "us");
        membership.IsDeleted = true;
        await _db.Updateable(membership).ExecuteCommandAsync();

        var ex = await Assert.ThrowsAsync<FaceAttendanceException>(() => _writer.CommitAsync(
            Event("membership-changed", "clockIn", new DateTime(2026, 9, 8, 23, 0, 0, DateTimeKind.Utc)), "test", default));
        Assert.Equal("ROSTER_OR_ENROLLMENT_CHANGED", ex.Code);
    }

    [Fact]
    public async Task 无排班被禁用时拒绝()
    {
        await SeedAsync(false);
        var ex = await Assert.ThrowsAsync<FaceAttendanceException>(() => _writer.CommitAsync(Event("x", "clockIn", new DateTime(2026,9,8,23,0,0,DateTimeKind.Utc)), "test", default));
        Assert.Equal("NO_SCHEDULE", ex.Code);
    }

    [Fact]
    public async Task 已有更晚记录时拒绝倒序补写()
    {
        await SeedAsync(); await _writer.CommitAsync(Event("later-in", "clockIn", new DateTime(2026,9,9,7,0,0,DateTimeKind.Utc)), "test", default);
        await _writer.CommitAsync(Event("later-out", "clockOut", new DateTime(2026,9,9,8,0,0,DateTimeKind.Utc)), "test", default);
        var ex = await Assert.ThrowsAsync<FaceAttendanceException>(() => _writer.CommitAsync(Event("old", "clockIn", new DateTime(2026,9,9,6,0,0,DateTimeKind.Utc)), "test", default));
        Assert.Equal("FACE_PUNCH_OUT_OF_ORDER", ex.Code);
    }

    [Fact]
    public async Task 另一eventGuid同一UTC时刻也拒绝零时长班段()
    {
        await SeedAsync();
        var occurredAt = new DateTime(2026, 9, 8, 23, 0, 0, DateTimeKind.Utc);
        await _writer.CommitAsync(Event("same-time-in", "clockIn", occurredAt), "test", default);

        var ex = await Assert.ThrowsAsync<FaceAttendanceException>(() => _writer.CommitAsync(
            Event("same-time-out", "clockOut", occurredAt), "test", default));

        Assert.Equal("FACE_PUNCH_OUT_OF_ORDER", ex.Code);
    }

    [Fact]
    public async Task 跨午夜业务日的更晚记录拒绝离线倒序补写()
    {
        await SeedAsync();
        await _db.Insertable(new AttendancePunch
        {
            PunchGuid = "later-midnight",
            StoreCode = "BRI",
            UserGuid = "u",
            // 这条记录属于前一日跨午夜班次；不能因 WorkDate 与事件本地日不同而漏检。
            WorkDate = new DateTime(2026, 9, 9),
            StoreTimeZone = "Australia/Sydney",
            PunchType = "ClockOut",
            PunchTimeUtc = new DateTime(2026, 9, 9, 15, 0, 0, DateTimeKind.Utc),
            PunchTimeLocal = new DateTime(2026, 9, 10, 1, 0, 0),
            DeviceId = "H-OTHER",
            PosDeviceCode = "D-OTHER",
            Source = "Test",
        }).ExecuteCommandAsync();

        var ex = await Assert.ThrowsAsync<FaceAttendanceException>(() => _writer.CommitAsync(
            Event("late-midnight", "clockIn", new DateTime(2026, 9, 9, 14, 0, 0, DateTimeKind.Utc)), "test", default));
        Assert.Equal("FACE_PUNCH_OUT_OF_ORDER", ex.Code);
    }

    [Fact]
    public async Task 其他门店有未结束上班时拒绝新的上班事件()
    {
        await SeedAsync();
        await _db.Insertable(new Store { StoreGUID = "s2", StoreCode = "OTH", StoreName = "Other", IsActive = true }).ExecuteCommandAsync();
        await _db.Insertable(new AttendancePunch
        {
            PunchGuid = "other-open",
            StoreCode = "OTH",
            UserGuid = "u",
            WorkDate = new DateTime(2026, 9, 9),
            StoreTimeZone = "Australia/Sydney",
            PunchType = "ClockIn",
            PunchTimeUtc = new DateTime(2026, 9, 8, 23, 0, 0, DateTimeKind.Utc),
            PunchTimeLocal = new DateTime(2026, 9, 9, 9, 0, 0),
            DeviceId = "H-OTHER",
            PosDeviceCode = "D-OTHER",
            Source = "Test",
        }).ExecuteCommandAsync();

        var ex = await Assert.ThrowsAsync<FaceAttendanceException>(() => _writer.CommitAsync(
            Event("other-store", "clockIn", new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc)), "test", default));
        Assert.Equal("CROSS_STORE_OPEN_SEGMENT", ex.Code);
    }

    [Fact]
    public async Task 同一事件并发提交只写入一次()
    {
        await SeedAsync();
        var row = Event("parallel", "clockIn", new DateTime(2026, 9, 8, 23, 0, 0, DateTimeKind.Utc));

        var ids = await Task.WhenAll(
            _writer.CommitAsync(row, "test", default),
            _writer.CommitAsync(row, "test", default));

        Assert.Single(ids.Distinct());
        Assert.Single(await _db.Queryable<AttendancePunch>().Where(x => x.FaceEventGuid == "parallel").ToListAsync());
    }

    [Fact]
    public async Task 跨店完成班段重叠时拒绝下班()
    {
        await SeedAsync();
        await _db.Insertable(new Store { StoreGUID="s2", StoreCode="OTH", StoreName="Other", IsActive=true }).ExecuteCommandAsync();
        await _writer.CommitAsync(Event("b-in", "clockIn", new DateTime(2026,9,9,1,0,0,DateTimeKind.Utc)), "test", default);
        await _db.Insertable(new[] { new AttendancePunch { PunchGuid="o-in", StoreCode="OTH", UserGuid="u", WorkDate=new DateTime(2026,9,9), StoreTimeZone="Australia/Sydney", PunchType="ClockIn", PunchTimeUtc=new DateTime(2026,9,9,0,0,0,DateTimeKind.Utc), PunchTimeLocal=new DateTime(2026,9,9,10,0,0), Source="Test" }, new AttendancePunch { PunchGuid="o-out", StoreCode="OTH", UserGuid="u", WorkDate=new DateTime(2026,9,9), StoreTimeZone="Australia/Sydney", PunchType="ClockOut", PunchTimeUtc=new DateTime(2026,9,9,8,0,0,DateTimeKind.Utc), PunchTimeLocal=new DateTime(2026,9,9,18,0,0), Source="Test" } }).ExecuteCommandAsync();
        var ex = await Assert.ThrowsAsync<FaceAttendanceException>(() => _writer.CommitAsync(Event("b-out", "clockOut", new DateTime(2026,9,9,9,0,0,DateTimeKind.Utc)), "test", default));
        Assert.Equal("CROSS_STORE_PUNCH_OVERLAP", ex.Code);
    }

    [Fact]
    public async Task 跨午夜排班在次日仍绑定原排班会话()
    {
        await SeedAsync();
        await _db.Insertable(new AttendanceSchedule
        {
            ScheduleGuid = "overnight",
            StoreCode = "BRI",
            UserGuid = "u",
            WorkDate = new DateTime(2026, 9, 9),
            StartTime = new TimeSpan(22, 0, 0),
            EndTime = new TimeSpan(6, 0, 0),
            Status = "Active",
        }).ExecuteCommandAsync();

        await _writer.CommitAsync(Event("night-in", "clockIn", new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc)), "test", default);
        await _writer.CommitAsync(Event("night-out", "clockOut", new DateTime(2026, 9, 9, 16, 0, 0, DateTimeKind.Utc)), "test", default);

        var rows = await _db.Queryable<AttendancePunch>().OrderBy(x => x.PunchTimeUtc).ToListAsync();
        Assert.All(rows, row => Assert.Equal("overnight", row.ScheduleGuid));
        Assert.All(rows, row => Assert.Equal(new DateTime(2026, 9, 9), row.WorkDate));
        Assert.Equal("Break", rows[1].Status);
    }

    [Fact]
    public async Task 排班达到两段上限后拒绝第三次上班()
    {
        await SeedAsync();
        await _db.Insertable(new AttendanceSchedule
        {
            ScheduleGuid = "two-segments",
            StoreCode = "BRI",
            UserGuid = "u",
            WorkDate = new DateTime(2026, 9, 9),
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(18, 0, 0),
            Status = "Active",
        }).ExecuteCommandAsync();

        await _writer.CommitAsync(Event("in-1", "clockIn", new DateTime(2026, 9, 8, 23, 0, 0, DateTimeKind.Utc)), "test", default);
        await _writer.CommitAsync(Event("out-1", "clockOut", new DateTime(2026, 9, 9, 2, 0, 0, DateTimeKind.Utc)), "test", default);
        await _writer.CommitAsync(Event("in-2", "clockIn", new DateTime(2026, 9, 9, 3, 0, 0, DateTimeKind.Utc)), "test", default);
        await _writer.CommitAsync(Event("out-2", "clockOut", new DateTime(2026, 9, 9, 4, 0, 0, DateTimeKind.Utc)), "test", default);

        var ex = await Assert.ThrowsAsync<FaceAttendanceException>(() => _writer.CommitAsync(Event("in-3", "clockIn", new DateTime(2026, 9, 9, 5, 0, 0, DateTimeKind.Utc)), "test", default));
        Assert.Equal("SEGMENT_LIMIT_REACHED", ex.Code);
    }

    private async Task SeedAsync(bool allowNoSchedule = true)
    {
        await _db.Insertable(new Store { StoreGUID="s", StoreCode="BRI", StoreName="Brisbane", IsActive=true }).ExecuteCommandAsync();
        await _db.Insertable(new User { UserGUID="u", Username="u", Email="u@x", PasswordHash="x", IsActive=true }).ExecuteCommandAsync();
        await _db.Insertable(new UserStore { UserStoreGUID="us", UserGUID="u", StoreGUID="s", IsPrimary=true }).ExecuteCommandAsync();
        await _db.Insertable(new FaceAttendanceEnrollment { UserGuid="u", StoreCode="BRI", Version=1, Status="active", ProtectedTemplatesJson="test", CreatedAtUtc=DateTime.UtcNow, UpdatedAtUtc=DateTime.UtcNow }).ExecuteCommandAsync();
        await _db.Insertable(new AttendanceSettings { AllowNoSchedulePunch = allowNoSchedule }).ExecuteCommandAsync();
        var roster = await _faceService.GetRosterAsync("BRI", new FaceDeviceContext("BRI", "D", "H"), null, default);
        _rosterVersion = roster.RosterVersion;
    }
    private FaceAttendanceEvent Event(string id, string type, DateTime at) => new() { EventGuid=id, UserGuid="u", StoreCode="BRI", DeviceCode="D", HardwareId="H", KeyId="K", PunchType=type, OccurredAtUtc=at, EnrollmentVersion=1, RosterVersion=_rosterVersion };
    public void Dispose() { _connection.Dispose(); File.Delete(_path); }
}
