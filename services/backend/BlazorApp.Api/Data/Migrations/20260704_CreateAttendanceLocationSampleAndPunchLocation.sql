IF OBJECT_ID('UserLoginDeviceRecord', 'U') IS NULL
BEGIN
    CREATE TABLE [UserLoginDeviceRecord] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_UserLoginDeviceRecord] PRIMARY KEY,
        [RecordGuid] nvarchar(50) NOT NULL,
        [UserGuid] nvarchar(50) NULL,
        [Username] nvarchar(100) NULL,
        [HardwareId] nvarchar(100) NULL,
        [SystemDeviceNumber] nvarchar(100) NULL,
        [DeviceSystem] nvarchar(30) NULL,
        [StoreCode] nvarchar(50) NULL,
        [LoginSource] nvarchar(30) NOT NULL,
        [LoginAtUtc] datetime2 NOT NULL,
        [LoginIp] nvarchar(50) NULL,
        [UserAgent] nvarchar(500) NULL,
        [LocationLatitude] float NULL,
        [LocationLongitude] float NULL,
        [LocationAccuracyMeters] float NULL,
        [LocationCapturedAtUtc] datetime2 NULL,
        [IsDeviceSwitched] bit NOT NULL CONSTRAINT [DF_UserLoginDeviceRecord_IsDeviceSwitched] DEFAULT(0),
        [IsCommonDevice] bit NOT NULL CONSTRAINT [DF_UserLoginDeviceRecord_IsCommonDevice] DEFAULT(0),
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(max) NULL,
        [IsDeleted] bit NULL
    );
END;

IF OBJECT_ID('AttendanceLocationSample', 'U') IS NULL
BEGIN
    CREATE TABLE [AttendanceLocationSample] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AttendanceLocationSample] PRIMARY KEY,
        [SampleGuid] nvarchar(50) NOT NULL,
        [UserGuid] nvarchar(50) NOT NULL,
        [StoreCode] nvarchar(50) NULL,
        [HardwareId] nvarchar(100) NULL,
        [SystemDeviceNumber] nvarchar(100) NULL,
        [DeviceSystem] nvarchar(30) NULL,
        [EventType] nvarchar(30) NOT NULL,
        [LocationLatitude] float NOT NULL,
        [LocationLongitude] float NOT NULL,
        [LocationAccuracyMeters] float NULL,
        [LocationPermissionStatus] nvarchar(30) NULL,
        [LocationCapturedAtUtc] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(max) NULL,
        [IsDeleted] bit NULL
    );
END;

IF OBJECT_ID('AttendancePunch', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('AttendancePunch', 'LocationLatitude') IS NULL
        ALTER TABLE [AttendancePunch] ADD [LocationLatitude] float NULL;
    IF COL_LENGTH('AttendancePunch', 'LocationLongitude') IS NULL
        ALTER TABLE [AttendancePunch] ADD [LocationLongitude] float NULL;
    IF COL_LENGTH('AttendancePunch', 'LocationAccuracyMeters') IS NULL
        ALTER TABLE [AttendancePunch] ADD [LocationAccuracyMeters] float NULL;
    IF COL_LENGTH('AttendancePunch', 'LocationPermissionStatus') IS NULL
        ALTER TABLE [AttendancePunch] ADD [LocationPermissionStatus] nvarchar(30) NULL;
    IF COL_LENGTH('AttendancePunch', 'LocationCapturedAtUtc') IS NULL
        ALTER TABLE [AttendancePunch] ADD [LocationCapturedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_UserLoginDeviceRecord_User_LoginAt'
      AND object_id = OBJECT_ID('UserLoginDeviceRecord')
)
BEGIN
    CREATE INDEX [IX_UserLoginDeviceRecord_User_LoginAt]
    ON [UserLoginDeviceRecord]([UserGuid], [LoginAtUtc]);
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_UserLoginDeviceRecord_Hardware_LoginAt'
      AND object_id = OBJECT_ID('UserLoginDeviceRecord')
)
BEGIN
    CREATE INDEX [IX_UserLoginDeviceRecord_Hardware_LoginAt]
    ON [UserLoginDeviceRecord]([HardwareId], [LoginAtUtc]);
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_AttendanceLocationSample_Store_User_Captured'
      AND object_id = OBJECT_ID('AttendanceLocationSample')
)
BEGIN
    CREATE INDEX [IX_AttendanceLocationSample_Store_User_Captured]
    ON [AttendanceLocationSample]([StoreCode], [UserGuid], [LocationCapturedAtUtc]);
END;
