using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Data;

/// <summary>人脸考勤独立表使用幂等 DDL，事件和设备密钥均不与二维码表混用。</summary>
public static class FaceAttendanceSchemaMigrator
{
    public static Task EnsureAsync(ISqlSugarClient db, ILogger logger) => db.Ado.ExecuteCommandAsync("""
IF OBJECT_ID(N'[dbo].[FaceAttendanceEnrollment]', N'U') IS NULL
CREATE TABLE [dbo].[FaceAttendanceEnrollment]([Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,[UserGuid] nvarchar(50) NOT NULL,[StoreCode] nvarchar(50) NOT NULL,[Version] bigint NOT NULL,[Status] nvarchar(20) NOT NULL,[ProtectedTemplatesJson] nvarchar(max) NOT NULL,[CreatedAtUtc] datetime2 NOT NULL,[UpdatedAtUtc] datetime2 NOT NULL,[UpdatedBy] nvarchar(100) NULL);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_FaceAttendanceEnrollment_User_Store' AND object_id=OBJECT_ID(N'[dbo].[FaceAttendanceEnrollment]')) CREATE UNIQUE INDEX [UX_FaceAttendanceEnrollment_User_Store] ON [dbo].[FaceAttendanceEnrollment]([UserGuid],[StoreCode]);
IF OBJECT_ID(N'[dbo].[FaceAttendanceEvent]', N'U') IS NULL
CREATE TABLE [dbo].[FaceAttendanceEvent]([EventGuid] nvarchar(50) NOT NULL PRIMARY KEY,[ImmutablePayloadHash] nvarchar(64) NOT NULL,[UserGuid] nvarchar(50) NOT NULL,[StoreCode] nvarchar(50) NOT NULL,[DeviceCode] nvarchar(50) NOT NULL,[HardwareId] nvarchar(100) NOT NULL,[PunchType] nvarchar(20) NOT NULL,[OccurredAtUtc] datetime2 NOT NULL,[DeviceObservedAtUtc] datetime2 NOT NULL,[LocalSequence] bigint NOT NULL,[RosterVersion] bigint NOT NULL,[EnrollmentVersion] bigint NOT NULL,[TimeAnchorId] nvarchar(64) NOT NULL,[TimeTrusted] bit NOT NULL,[PhotoSha256] nvarchar(64) NOT NULL,[KeyId] nvarchar(64) NOT NULL,[ProtectedPhoto] nvarchar(max) NOT NULL,[Status] nvarchar(20) NOT NULL,[ReasonCode] nvarchar(80) NULL,[PunchGuid] nvarchar(50) NULL,[AttemptCount] int NOT NULL,[NextAttemptAtUtc] datetime2 NOT NULL,[ReceivedAtUtc] datetime2 NOT NULL,[UpdatedAtUtc] datetime2 NOT NULL,[RetainUntilUtc] datetime2 NULL);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_FaceAttendanceEvent_Work' AND object_id=OBJECT_ID(N'[dbo].[FaceAttendanceEvent]')) CREATE INDEX [IX_FaceAttendanceEvent_Work] ON [dbo].[FaceAttendanceEvent]([Status],[NextAttemptAtUtc]);
IF OBJECT_ID(N'[dbo].[FaceAttendanceDeviceKey]', N'U') IS NULL
CREATE TABLE [dbo].[FaceAttendanceDeviceKey]([KeyId] nvarchar(64) NOT NULL PRIMARY KEY,[StoreCode] nvarchar(50) NOT NULL,[DeviceCode] nvarchar(50) NOT NULL,[HardwareId] nvarchar(100) NOT NULL,[ProtectedSecret] nvarchar(max) NOT NULL,[Status] nvarchar(20) NOT NULL,[CreatedAtUtc] datetime2 NOT NULL,[RevokedAtUtc] datetime2 NULL);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_FaceAttendanceDeviceKey_ActiveDevice' AND object_id=OBJECT_ID(N'[dbo].[FaceAttendanceDeviceKey]')) CREATE UNIQUE INDEX [UX_FaceAttendanceDeviceKey_ActiveDevice] ON [dbo].[FaceAttendanceDeviceKey]([StoreCode],[DeviceCode],[HardwareId]) WHERE [Status]=N'active';
IF OBJECT_ID(N'[dbo].[FaceAttendanceTimeAnchor]', N'U') IS NULL
CREATE TABLE [dbo].[FaceAttendanceTimeAnchor]([TimeAnchorId] nvarchar(64) NOT NULL PRIMARY KEY,[KeyId] nvarchar(64) NOT NULL,[ServerObservedAtUtc] datetime2 NOT NULL,[DeviceObservedAtUtc] datetime2 NOT NULL,[ExpiresAtUtc] datetime2 NOT NULL);
IF OBJECT_ID(N'[dbo].[FaceAttendanceRosterSnapshot]', N'U') IS NULL
CREATE TABLE [dbo].[FaceAttendanceRosterSnapshot]([StoreCode] nvarchar(50) NOT NULL,[Version] bigint NOT NULL,[MemberFingerprintsJson] nvarchar(max) NOT NULL,[CreatedAtUtc] datetime2 NOT NULL, CONSTRAINT [PK_FaceAttendanceRosterSnapshot] PRIMARY KEY([StoreCode],[Version]));
IF COL_LENGTH(N'[dbo].[FaceAttendanceEnrollment]', N'LastRequestHash') IS NULL ALTER TABLE [dbo].[FaceAttendanceEnrollment] ADD [LastRequestHash] nvarchar(64) NULL;
IF COL_LENGTH(N'[dbo].[FaceAttendanceEvent]', N'Signature') IS NULL ALTER TABLE [dbo].[FaceAttendanceEvent] ADD [Signature] nvarchar(128) NOT NULL DEFAULT N'';
IF COL_LENGTH(N'[dbo].[FaceAttendanceEvent]', N'LeaseId') IS NULL ALTER TABLE [dbo].[FaceAttendanceEvent] ADD [LeaseId] nvarchar(50) NULL;
IF COL_LENGTH(N'[dbo].[FaceAttendanceEvent]', N'LeaseExpiresAtUtc') IS NULL ALTER TABLE [dbo].[FaceAttendanceEvent] ADD [LeaseExpiresAtUtc] datetime2 NULL;
IF COL_LENGTH(N'[dbo].[FaceAttendanceEvent]', N'FaceVerifiedAtUtc') IS NULL ALTER TABLE [dbo].[FaceAttendanceEvent] ADD [FaceVerifiedAtUtc] datetime2 NULL;
IF COL_LENGTH(N'[dbo].[FaceAttendanceEvent]', N'FaceSimilarity') IS NULL ALTER TABLE [dbo].[FaceAttendanceEvent] ADD [FaceSimilarity] float NULL;
IF COL_LENGTH(N'[dbo].[FaceAttendanceEvent]', N'ReviewedBy') IS NULL ALTER TABLE [dbo].[FaceAttendanceEvent] ADD [ReviewedBy] nvarchar(50) NULL;
IF COL_LENGTH(N'[dbo].[FaceAttendanceEvent]', N'ReviewedAtUtc') IS NULL ALTER TABLE [dbo].[FaceAttendanceEvent] ADD [ReviewedAtUtc] datetime2 NULL;
IF COL_LENGTH(N'[dbo].[FaceAttendanceEvent]', N'ReviewReason') IS NULL ALTER TABLE [dbo].[FaceAttendanceEvent] ADD [ReviewReason] nvarchar(500) NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_FaceAttendanceEvent_EmployeeTime' AND object_id=OBJECT_ID(N'[dbo].[FaceAttendanceEvent]')) CREATE INDEX [IX_FaceAttendanceEvent_EmployeeTime] ON [dbo].[FaceAttendanceEvent]([UserGuid],[OccurredAtUtc],[Status]);
IF COL_LENGTH(N'[dbo].[AttendancePunch]', N'FaceEventGuid') IS NULL ALTER TABLE [dbo].[AttendancePunch] ADD [FaceEventGuid] nvarchar(50) NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_AttendancePunch_FaceEventGuid' AND object_id=OBJECT_ID(N'[dbo].[AttendancePunch]')) EXEC(N'CREATE UNIQUE INDEX [UX_AttendancePunch_FaceEventGuid] ON [dbo].[AttendancePunch]([FaceEventGuid]) WHERE [FaceEventGuid] IS NOT NULL');
""");
}
