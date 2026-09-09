using BlazorApp.Api.Services.Attendance;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class FaceAttendanceContractTests
{
    [Fact]
    public void EventCanonical_跨端固定毫秒UTC与数组字段顺序()
    {
        var command = new FaceAttendanceEventCommandDto
        {
            EventGuid = "0d5adc65-e3dd-4671-bdab-e20ad72bc234", UserGuid = "u\"1", StoreCode = "S1", DeviceCode = "D1", HardwareId = "H1",
            PunchType = FaceAttendanceStatuses.ClockIn, OccurredAtUtc = new DateTime(2026, 9, 9, 1, 2, 3, 456, DateTimeKind.Utc), DeviceObservedAtUtc = new DateTime(2026, 9, 9, 1, 2, 4, 5, DateTimeKind.Utc),
            LocalSequence = 7, RosterVersion = 8, EnrollmentVersion = 9, TimeAnchorId = "a1", TimeTrusted = true, PhotoSha256 = new string('A', 64), KeyId = "k1"
        };
        Assert.Equal("[\"v1\",\"0d5adc65-e3dd-4671-bdab-e20ad72bc234\",\"u\\\"1\",\"S1\",\"D1\",\"H1\",\"clockIn\",\"2026-09-09T01:02:03.456Z\",\"2026-09-09T01:02:04.005Z\",\"7\",\"8\",\"9\",\"a1\",\"true\",\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"k1\"]", FaceAttendanceService.EventCanonical(command));
    }

    [Fact]
    public async Task SubmitAsync_同Guid同内容幂等_不同内容冲突()
    {
        using var fixture = new FaceFixture();
        var session = await fixture.Service.CreateSessionAsync(fixture.SessionRequest(), fixture.Device, default);
        var command = fixture.Command(session.KeyId, session.DeviceKeySecret!);

        var first = await fixture.Service.SubmitAsync(command, fixture.Photo, fixture.Device, default);
        var second = await fixture.Service.SubmitAsync(command, fixture.Photo, fixture.Device, default);

        Assert.Equal(first.EventGuid, second.EventGuid);
        command.PunchType = FaceAttendanceStatuses.ClockOut;
        var error = await Assert.ThrowsAsync<FaceAttendanceException>(() => fixture.Service.SubmitAsync(command, fixture.Photo, fixture.Device, default));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal("EVENT_GUID_CONFLICT", error.Code);
    }

    [Fact]
    public async Task SubmitAsync_照片摘要签名和设备范围必须同时有效()
    {
        using var fixture = new FaceFixture();
        var session = await fixture.Service.CreateSessionAsync(fixture.SessionRequest(), fixture.Device, default);
        var command = fixture.Command(session.KeyId, session.DeviceKeySecret!);
        command.PhotoSha256 = new string('0', 64);
        var hashError = await Assert.ThrowsAsync<FaceAttendanceException>(() => fixture.Service.SubmitAsync(command, fixture.Photo, fixture.Device, default));
        Assert.Equal("PHOTO_HASH_MISMATCH", hashError.Code);

        command = fixture.Command(session.KeyId, session.DeviceKeySecret!);
        command.Signature = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var signatureError = await Assert.ThrowsAsync<FaceAttendanceException>(() => fixture.Service.SubmitAsync(command, fixture.Photo, fixture.Device, default));
        Assert.Equal("EVENT_SIGNATURE_INVALID", signatureError.Code);

        command = fixture.Command(session.KeyId, session.DeviceKeySecret!);
        var scopeError = await Assert.ThrowsAsync<FaceAttendanceException>(() => fixture.Service.SubmitAsync(command, fixture.Photo, new FaceDeviceContext("OTHER", "D1", "H1"), default));
        Assert.Equal("FACE_DEVICE_SCOPE_MISMATCH", scopeError.Code);
    }

    [Fact]
    public async Task SubmitAsync_不可信时钟直接待复核_模型未配置时保持queued()
    {
        using var fixture = new FaceFixture();
        var session = await fixture.Service.CreateSessionAsync(fixture.SessionRequest(), fixture.Device, default);
        var untrusted = fixture.Command(session.KeyId, session.DeviceKeySecret!);
        untrusted.TimeTrusted = false;
        untrusted.Signature = fixture.Sign(untrusted, session.DeviceKeySecret!);
        var needsReview = await fixture.Service.SubmitAsync(untrusted, fixture.Photo, fixture.Device, default);
        Assert.Equal(FaceAttendanceStatuses.EventNeedsReview, needsReview.Status);

        var queued = fixture.Command(session.KeyId, session.DeviceKeySecret!);
        var accepted = await fixture.Service.SubmitAsync(queued, fixture.Photo, fixture.Device, default);
        await fixture.Service.ProcessQueuedAsync(default);
        var afterWorker = await fixture.Service.GetAsync(accepted.EventGuid, fixture.Device, null, default);
        Assert.Equal(FaceAttendanceStatuses.EventQueued, afterWorker.Status);
    }


    [Theory]
    [InlineData("missing")]
    [InlineData("rollback")]
    [InlineData("expired")]
    [InlineData("future")]
    public async Task Submit_服务端独立核对时间锚_不可信事件保留照片待处理(string mode)
    {
        using var f = new FaceFixture();
        var session = await f.Service.CreateSessionAsync(f.SessionRequest(), f.Device, default);
        var c = f.Command(session.KeyId, session.DeviceKeySecret!);
        if (mode == "missing") c.TimeAnchorId = "forged-anchor";
        if (mode == "rollback") c.DeviceObservedAtUtc = c.DeviceObservedAtUtc.AddMinutes(-5);
        if (mode == "expired") { f.Clock.Now = f.Clock.Now.AddHours(25); c.OccurredAtUtc = c.DeviceObservedAtUtc = f.Clock.Now; }
        if (mode == "future") c.OccurredAtUtc = c.OccurredAtUtc.AddHours(1);
        c.Signature = f.Sign(c,session.DeviceKeySecret!);
        var result = await f.Service.SubmitAsync(c,f.Photo,f.Device,default);
        Assert.Equal("needsReview",result.Status);
        var saved = await f.Db.Queryable<FaceAttendanceEvent>().SingleAsync(x => x.EventGuid == c.EventGuid);
        Assert.Equal(c.Signature,saved.Signature);
        Assert.NotEmpty(saved.ProtectedPhoto);
        Assert.Equal(c.OccurredAtUtc,saved.OccurredAtUtc);
        f.Writer.Verify(x => x.CommitAsync(It.IsAny<FaceAttendanceEvent>(),It.IsAny<string>(),It.IsAny<CancellationToken>()),Times.Never);
    }

    [Fact]
    public async Task Worker_离线两条按原时间核验_丢失回执重放不会重复写入()
    {
        using var f = new FaceFixture(); f.Recognition();
        var session=await f.Service.CreateSessionAsync(f.SessionRequest(),f.Device,default);
        f.Clock.Now=f.Clock.Now.AddHours(1); var clockIn=f.Command(session.KeyId,session.DeviceKeySecret!);
        f.Clock.Now=f.Clock.Now.AddHours(8); var clockOut=f.Command(session.KeyId,session.DeviceKeySecret!);
        clockOut.PunchType="clockOut"; clockOut.LocalSequence=2; clockOut.Signature=f.Sign(clockOut,session.DeviceKeySecret!);
        f.Clock.Now=f.Clock.Now.AddHours(1);
        await f.Service.SubmitAsync(clockIn,f.Photo,f.Device,default);
        await f.Service.SubmitAsync(clockOut,f.Photo,f.Device,default);
        await f.Service.SubmitAsync(clockIn,f.Photo,f.Device,default);
        await f.Service.ProcessQueuedAsync(default);
        await f.Service.ProcessQueuedAsync(default);
        var events=await f.Db.Queryable<FaceAttendanceEvent>().OrderBy(x=>x.OccurredAtUtc).ToListAsync();
        Assert.Equal(2,events.Count); Assert.All(events,x=>Assert.Equal("verified",x.Status));
        Assert.Equal(new DateTime(2026,9,8,23,0,0),events[0].OccurredAtUtc);
        Assert.Equal(new DateTime(2026,9,9,7,0,0),events[1].OccurredAtUtc);
        Assert.Equal("clockIn",events[0].PunchType);Assert.Equal("clockOut",events[1].PunchType);
        f.Writer.Verify(x=>x.CommitAsync(It.IsAny<FaceAttendanceEvent>(),"FaceRecognitionWorker",It.IsAny<CancellationToken>()),Times.Exactly(2));
    }

    [Theory]
    [InlineData(503,"{}","queued","FACE_RECOGNITION_RETRY")]
    [InlineData(422,"{\"code\":\"no_face\"}","rejected","FACE_NO_FACE")]
    [InlineData(200,"{\"score\":0.1,\"modelVersion\":\"yunet-2023mar-sface-2021dec-v1\"}","rejected","FACE_MATCH_BELOW_THRESHOLD")]
    [InlineData(200,"{\"score\":2,\"modelVersion\":\"yunet-2023mar-sface-2021dec-v1\"}","queued","FACE_RECOGNITION_RETRY")]
    public async Task Worker_模型暂不可用重试_照片失败拒绝_非法分数不放行(int status,string body,string expected,string reason)
    {
        using var f=new FaceFixture();f.Recognition(body,status);
        var session=await f.Service.CreateSessionAsync(f.SessionRequest(),f.Device,default);
        var c=f.Command(session.KeyId,session.DeviceKeySecret!);
        await f.Service.SubmitAsync(c,f.Photo,f.Device,default);await f.Service.ProcessQueuedAsync(default);
        var result=await f.Service.GetAsync(c.EventGuid,f.Device,null,default);
        Assert.Equal(expected,result.Status);Assert.Equal(reason,result.ReasonCode);
        f.Writer.Verify(x=>x.CommitAsync(It.IsAny<FaceAttendanceEvent>(),It.IsAny<string>(),It.IsAny<CancellationToken>()),Times.Never);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("membership")]
    [InlineData("enrollment")]
    public async Task Worker_人员资料或模板改变时交人工_其他员工改变不影响本人(string change)
    {
        using var f=new FaceFixture();f.Recognition();
        var session=await f.Service.CreateSessionAsync(f.SessionRequest(),f.Device,default);
        var c=f.Command(session.KeyId,session.DeviceKeySecret!);await f.Service.SubmitAsync(c,f.Photo,f.Device,default);
        if(change=="user") await f.Db.Updateable<User>().SetColumns(x=>x.IsActive==false).Where(x=>x.UserGUID=="u1").ExecuteCommandAsync();
        if(change=="membership") await f.Db.Updateable<UserStore>().SetColumns(x=>x.AssignedAt==f.Clock.Now.AddHours(1)).Where(x=>x.UserGUID=="u1").ExecuteCommandAsync();
        if(change=="enrollment") await f.Db.Updateable<FaceAttendanceEnrollment>().SetColumns(x=>x.Status=="revoked").Where(x=>x.UserGuid=="u1").ExecuteCommandAsync();
        await f.Service.ProcessQueuedAsync(default);
        var result=await f.Service.GetAsync(c.EventGuid,f.Device,null,default);
        Assert.Equal("needsReview",result.Status);Assert.Equal("ROSTER_OR_ENROLLMENT_CHANGED",result.ReasonCode);
    }

    [Fact]
    public async Task Roster_班表变动版本不变_名单只返回本店最后打卡和员工编号()
    {
        using var f=new FaceFixture();
        await f.Db.Insertable(new AttendanceSchedule { ScheduleGuid="new",StoreCode="BRI",UserGuid="u1",WorkDate=f.Clock.Now.Date }).ExecuteCommandAsync();
        await f.Db.Insertable(new [] {
            new AttendancePunch { PunchGuid="local",UserGuid="u1",StoreCode="BRI",PunchType="ClockOut",PunchTimeUtc=f.Clock.Now,WorkDate=f.Clock.Now.Date },
            new AttendancePunch { PunchGuid="other",UserGuid="u1",StoreCode="OTHER",PunchType="ClockIn",PunchTimeUtc=f.Clock.Now.AddHours(1),WorkDate=f.Clock.Now.Date }
        }).ExecuteCommandAsync();
        var roster=await f.Service.GetRosterAsync("BRI",f.Device,null,default);
        Assert.Equal(f.RosterVersion,roster.RosterVersion);
        Assert.Equal("EMP01",roster.Employees.Single().EmployeeCode);
        Assert.Equal("clockOut",roster.Employees.Single().LastPunchType);
        Assert.Equal(f.Clock.Now,roster.Employees.Single().LastPunchTimeUtc);
    }

    [Fact]
    public async Task Worker_过期租约可恢复_照片到期删除但保留事件()
    {
        using var f=new FaceFixture();f.Recognition();
        var session=await f.Service.CreateSessionAsync(f.SessionRequest(),f.Device,default);
        var c=f.Command(session.KeyId,session.DeviceKeySecret!);await f.Service.SubmitAsync(c,f.Photo,f.Device,default);
        await f.Db.Updateable<FaceAttendanceEvent>().SetColumns(x=>new FaceAttendanceEvent { Status="verifying",LeaseId="crashed",LeaseExpiresAtUtc=f.Clock.Now.AddMinutes(-1) }).Where(x=>x.EventGuid==c.EventGuid).ExecuteCommandAsync();
        await f.Service.ProcessQueuedAsync(default);
        Assert.Equal("verified",(await f.Service.GetAsync(c.EventGuid,f.Device,null,default)).Status);
        f.Clock.Now=f.Clock.Now.AddDays(31);await f.Service.ProcessQueuedAsync(default);
        var row=await f.Db.Queryable<FaceAttendanceEvent>().SingleAsync(x=>x.EventGuid==c.EventGuid);
        Assert.Equal("",row.ProtectedPhoto);Assert.Equal("verified",row.Status);Assert.NotEmpty(row.Signature);
        var error=await Assert.ThrowsAsync<FaceAttendanceException>(()=>f.Service.GetPhotoAsync(c.EventGuid,f.Device,f.Manager(),default));
        Assert.Equal(410,error.StatusCode);
    }

    [Fact]
    public async Task Management_新鲜权限及实际人脸核验都不可绕过()
    {
        using var f=new FaceFixture();f.Recognition("{\"code\":\"no_face\"}",422);
        var session=await f.Service.CreateSessionAsync(f.SessionRequest(),f.Device,default);
        var c=f.Command(session.KeyId,session.DeviceKeySecret!);c.TimeTrusted=false;c.Signature=f.Sign(c,session.DeviceKeySecret!);
        await f.Service.SubmitAsync(c,f.Photo,f.Device,default);
        var request=new FaceAttendanceReviewRequestDto { Decision="approve",Reason="核对原始时间" };
        Assert.Equal(403,(await Assert.ThrowsAsync<FaceAttendanceException>(()=>f.Service.ReviewAsync(c.EventGuid,request,f.Device,null,default))).StatusCode);
        var manager=f.Manager();
        Assert.Equal(403,(await Assert.ThrowsAsync<FaceAttendanceException>(()=>f.Service.ReviewAsync(c.EventGuid,request,f.Device,manager with { AuthenticatedAtUtc=f.Clock.Now.AddMinutes(3) },default))).StatusCode);
        Assert.Equal(422,(await Assert.ThrowsAsync<FaceAttendanceException>(()=>f.Service.ReviewAsync(c.EventGuid,request,f.Device,manager,default))).StatusCode);
        f.Writer.Verify(x=>x.CommitAsync(It.IsAny<FaceAttendanceEvent>(),It.IsAny<string>(),It.IsAny<CancellationToken>()),Times.Never);
    }

    [Fact]
    public async Task Enrollment_同样三张照片重放及重复撤销不增加版本()
    {
        using var f=new FaceFixture();
        var templates=new [] { Convert.ToBase64String(new byte[512]),Convert.ToBase64String(new byte[512]),Convert.ToBase64String(new byte[512]) };
        f.Recognition(System.Text.Json.JsonSerializer.Serialize(new { templates,modelVersion=FaceAttendanceService.ModelVersion }));
        var actor=f.Manager();var photos=new [] { f.Photo,f.Photo,f.Photo };
        var first=await f.Service.EnrollAsync("u1","BRI",photos,f.Device,actor,default);
        var second=await f.Service.EnrollAsync("u1","BRI",photos,f.Device,actor,default);
        Assert.Equal(first.EnrollmentVersion,second.EnrollmentVersion);
        var request=new FaceEnrollmentRevokeRequestDto { StoreCode="BRI",Reason="撤销" };
        var a=await f.Service.RevokeAsync("u1",request,f.Device,actor,default);
        var b=await f.Service.RevokeAsync("u1",request,f.Device,actor,default);
        Assert.Equal(a.EnrollmentVersion,b.EnrollmentVersion);
    }


    [Fact]
    public async Task Retention_关闭识别后仍删除到期照片_不创建正式打卡()
    {
        using var f=new FaceFixture();
        var session=await f.Service.CreateSessionAsync(f.SessionRequest(),f.Device,default);
        var c=f.Command(session.KeyId,session.DeviceKeySecret!);await f.Service.SubmitAsync(c,f.Photo,f.Device,default);
        f.Configuration["FaceAttendance:Enabled"]="false";f.Clock.Now=f.Clock.Now.AddDays(31);
        await f.Service.CleanupExpiredPhotosAsync();
        var row=await f.Db.Queryable<FaceAttendanceEvent>().SingleAsync(x=>x.EventGuid==c.EventGuid);
        Assert.Equal("",row.ProtectedPhoto);Assert.Equal("needsReview",row.Status);Assert.Equal("PHOTO_EXPIRED",row.ReasonCode);
        f.Writer.Verify(x=>x.CommitAsync(It.IsAny<FaceAttendanceEvent>(),It.IsAny<string>(),It.IsAny<CancellationToken>()),Times.Never);
    }

    [Fact]
    public async Task Review_成功后响应丢失_相同操作重放不再写卡()
    {
        using var f=new FaceFixture();f.Recognition();
        var session=await f.Service.CreateSessionAsync(f.SessionRequest(),f.Device,default);
        var c=f.Command(session.KeyId,session.DeviceKeySecret!);c.TimeTrusted=false;c.Signature=f.Sign(c,session.DeviceKeySecret!);
        await f.Service.SubmitAsync(c,f.Photo,f.Device,default);
        var actor=f.Manager();var request=new FaceAttendanceReviewRequestDto { Decision="approve",Reason="核对原时间" };
        var first=await f.Service.ReviewAsync(c.EventGuid,request,f.Device,actor,default);
        var second=await f.Service.ReviewAsync(c.EventGuid,request,f.Device,actor,default);
        Assert.Equal("verified",second.Status);Assert.Equal(first.PunchGuid,second.PunchGuid);
        f.Writer.Verify(x=>x.CommitAsync(It.IsAny<FaceAttendanceEvent>(),actor.UserGuid,It.IsAny<CancellationToken>()),Times.Once);
    }

    [Theory]
    [InlineData("http://remote.example/worker/")]
    [InlineData("https://remote.example/worker/?secret=should-not-be-used")]
    [InlineData("https://remote.example/worker/#fragment")]
    public async Task Recognition_远程HTTP或混入query的地址禁止发送照片和凭据(string url)
    {
        using var f=new FaceFixture();f.Recognition();f.Configuration["FaceRecognition:BaseUrl"]=url;
        var session=await f.Service.CreateSessionAsync(f.SessionRequest(),f.Device,default);
        var c=f.Command(session.KeyId,session.DeviceKeySecret!);await f.Service.SubmitAsync(c,f.Photo,f.Device,default);
        await f.Service.ProcessQueuedAsync(default);
        Assert.Equal(0,f.Http.RequestCount);
        Assert.Equal("queued",(await f.Service.GetAsync(c.EventGuid,f.Device,null,default)).Status);
    }

    [Fact]
    public async Task Management_只有照片权限时只返回照片能力()
    {
        using var f=new FaceFixture();
        f.Roles.Setup(x=>x.UserHasPermissionAsync(It.IsAny<string>(),It.IsAny<string>())).ReturnsAsync(ApiResponse<bool>.OK(false));
        f.Roles.Setup(x=>x.UserHasPermissionAsync("u1",BlazorApp.Shared.Constants.Permissions.Attendance.Face.ViewPhotosManagedStore)).ReturnsAsync(ApiResponse<bool>.OK(true));
        var roster=await f.Service.GetRosterAsync("BRI",f.Device,new("u1",f.Clock.Now),default);
        Assert.True(roster.CanViewPhotos);Assert.False(roster.CanManage);Assert.False(roster.CanReview);
    }

    [Fact]
    public async Task Revoke_等待员工锁期间票据过期_取得锁后重新拒绝()
    {
        using var f=new FaceFixture();var actor=f.Manager();
        var permissionRead=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Roles.Setup(x=>x.UserHasPermissionAsync(It.IsAny<string>(),It.IsAny<string>())).Returns(() => { permissionRead.TrySetResult();return Task.FromResult(ApiResponse<bool>.OK(true)); });
        var held=await AttendanceDailyMutationLock.AcquireProcessAsync(AttendanceDailyMutationLock.BuildResource("u1","BRI",f.Clock.Now.Date));
        var pending=f.Service.RevokeAsync("u1",new FaceEnrollmentRevokeRequestDto { StoreCode="BRI",Reason="撤销" },f.Device,actor,default);
        await permissionRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Clock.Now=f.Clock.Now.AddMinutes(3);await held.DisposeAsync();
        var error=await Assert.ThrowsAsync<FaceAttendanceException>(()=>pending);
        Assert.Equal(403,error.StatusCode);
        Assert.Equal("active",(await f.Db.Queryable<FaceAttendanceEnrollment>().FirstAsync()).Status);
    }

    [Fact]
    public async Task Roster_新增另一员工不使原人员快照失效()
    {
        using var f=new FaceFixture();f.Recognition();
        var session=await f.Service.CreateSessionAsync(f.SessionRequest(),f.Device,default);
        var c=f.Command(session.KeyId,session.DeviceKeySecret!);await f.Service.SubmitAsync(c,f.Photo,f.Device,default);
        await f.Db.Insertable(new User { UserGUID="u2",Username="EMP02",FullName="员工",Email="other@example.invalid",PasswordHash="unused",IsActive=true }).ExecuteCommandAsync();
        await f.Db.Insertable(new UserStore { UserStoreGUID="us2",UserGUID="u2",StoreGUID="s" }).ExecuteCommandAsync();
        Assert.NotEqual(f.RosterVersion,(await f.Service.GetRosterAsync("BRI",f.Device,null,default)).RosterVersion);
        await f.Service.ProcessQueuedAsync(default);
        Assert.Equal("verified",(await f.Service.GetAsync(c.EventGuid,f.Device,null,default)).Status);
    }

    private sealed class FaceFixture : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"face-{Guid.NewGuid():N}.db");
        public readonly SqlSugarClient Db;
        public readonly MutableClock Clock = new();
        public readonly byte[] Photo = [0xff, 0xd8, 0xff, 0xe0, 1, 2, 3];
        public readonly FaceDeviceContext Device = new("BRI", "D1", "H1");
        public readonly Mock<IFaceAttendancePunchWriter> Writer = new();
        public readonly Mock<IRoleService> Roles = new();
        public readonly HttpClientFactoryStub Http = new();
        public readonly IConfigurationRoot Configuration;
        public readonly IDataProtector Protector;
        public FaceAttendanceService Service { get; }
        public string AnchorId = "";
        public long RosterVersion;

        public FaceFixture()
        {
            Db = new SqlSugarClient(new ConnectionConfig { ConnectionString = $"Data Source={_path}", DbType = DbType.Sqlite, IsAutoCloseConnection = false, InitKeyType = InitKeyType.Attribute });
            Db.CodeFirst.InitTables(typeof(User), typeof(Store), typeof(UserStore), typeof(AttendanceSchedule), typeof(AttendancePunch), typeof(FaceAttendanceEnrollment), typeof(FaceAttendanceEvent), typeof(FaceAttendanceDeviceKey), typeof(FaceAttendanceTimeAnchor), typeof(FaceAttendanceRosterSnapshot));
            Db.Insertable(new Store { StoreGUID="s", StoreCode="BRI", StoreName="Brisbane", IsActive=true, TimeZoneId="Australia/Brisbane" }).ExecuteCommand();
            Db.Insertable(new User { UserGUID="u1", Username="EMP01", FullName="员工", Email="test@example.invalid", PasswordHash="unused", IsActive=true }).ExecuteCommand();
            Db.Insertable(new UserStore { UserStoreGUID="us", UserGUID="u1", StoreGUID="s", IsPrimary=true }).ExecuteCommand();
            var protection = new EphemeralDataProtectionProvider(); Protector = protection.CreateProtector("HB.FaceAttendance.v1.private-payload");
            Db.Insertable(new FaceAttendanceEnrollment { UserGuid="u1", StoreCode="BRI", Version=1, Status="active", ProtectedTemplatesJson=Protector.Protect(System.Text.Json.JsonSerializer.Serialize(new { templates=new [] { new string('A',684), new string('A',684), new string('A',684) }, modelVersion=FaceAttendanceService.ModelVersion })), CreatedAtUtc=Clock.Now, UpdatedAtUtc=Clock.Now }).ExecuteCommand();
            var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
            typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, Db);
            var device = new Mock<IAttendancePosDeviceStatusProvider>(); device.Setup(x => x.IsActiveAsync("D1", "BRI", "H1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Roles.Setup(x => x.UserHasPermissionAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(ApiResponse<bool>.OK(false));
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>()).Build();
            Writer.Setup(x => x.CommitAsync(It.IsAny<FaceAttendanceEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("punch-1");
            Service = new FaceAttendanceService(context, device.Object, Roles.Object, protection, Configuration, Http, Clock, Writer.Object);
            RosterVersion = Service.GetRosterAsync("BRI", Device, null, default).GetAwaiter().GetResult().RosterVersion;
        }
        public void Recognition(string json = "{\"score\":0.9,\"modelVersion\":\"yunet-2023mar-sface-2021dec-v1\"}", int status=200)
        { Configuration["FaceRecognition:BaseUrl"]="https://recognition.test/"; Configuration["FaceRecognition:SharedSecret"]=new string('x',32); Http.Body=json; Http.Status=status; }
        public FaceManagementActor Manager() { Roles.Setup(x => x.UserHasPermissionAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(ApiResponse<bool>.OK(true)); return new("u1", Clock.Now); }
        public FaceDeviceSessionRequestDto SessionRequest() => new() { StoreCode="BRI",DeviceCode="D1",HardwareId="H1",DeviceObservedAtUtc=Clock.Now,Nonce=Guid.NewGuid().ToString("N") };
        public FaceAttendanceEventCommandDto Command(string keyId, string secret)
        {
            var anchor = Db.Queryable<FaceAttendanceTimeAnchor>().OrderByDescending(x=>x.ServerObservedAtUtc).First();
            var command = new FaceAttendanceEventCommandDto { EventGuid=Guid.NewGuid().ToString(), UserGuid="u1", StoreCode="BRI", DeviceCode="D1", HardwareId="H1", PunchType="clockIn", OccurredAtUtc=Clock.Now, DeviceObservedAtUtc=Clock.Now, LocalSequence=1, RosterVersion=RosterVersion, EnrollmentVersion=1, TimeAnchorId=anchor.TimeAnchorId, TimeTrusted=true, PhotoSha256=Convert.ToHexString(SHA256.HashData(Photo)).ToLowerInvariant(), KeyId=keyId };
            command.Signature=Sign(command,secret); return command;
        }
        public string Sign(FaceAttendanceEventCommandDto c,string secret) => Convert.ToBase64String(HMACSHA256.HashData(Convert.FromBase64String(secret), Encoding.UTF8.GetBytes(FaceAttendanceService.EventCanonical(c))));
        public void Dispose() { Db.Dispose(); SqliteConnection.ClearAllPools(); File.Delete(_path); }
    }
    private sealed class MutableClock : TimeProvider { public DateTime Now = new(2026,9,8,22,0,0,DateTimeKind.Utc); public override DateTimeOffset GetUtcNow() => new(Now); }
    private sealed class HttpClientFactoryStub : HttpMessageHandler, IHttpClientFactory
    {
        public int Status=200; public int RequestCount; public string Body="{}";
        public HttpClient CreateClient(string name) => new(this,false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c) { RequestCount++; return Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)Status) { Content=new StringContent(Body,Encoding.UTF8,"application/json") }); }
    }
}
