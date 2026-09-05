/* Remote maintenance schema. Run explicitly against the HB main database; startup is read-only. */
IF OBJECT_ID(N'dbo.HBweb_RemoteMaintenanceDevice', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[HBweb_RemoteMaintenanceDevice]
    (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_HBweb_RemoteMaintenanceDevice] PRIMARY KEY,
        [DeviceRegistrationId] int NOT NULL,
        [HardwareId] nvarchar(100) NOT NULL,
        [StoreCode] nvarchar(50) NOT NULL,
        [DeviceCode] nvarchar(100) NOT NULL,
        [ComputerName] nvarchar(120) NOT NULL,
        [RustdeskId] nvarchar(120) NULL,
        [ClientVersion] nvarchar(80) NULL,
        [AgentVersion] nvarchar(80) NULL,
        [ServiceStatus] nvarchar(20) NOT NULL CONSTRAINT [DF_HBweb_RemoteMaintenanceDevice_ServiceStatus] DEFAULT N'notInstalled',
        [LastSeenAtUtc] datetime2 NULL,
        [RegisteredAtUtc] datetime2 NOT NULL,
        [LastAcceptedSequence] bigint NOT NULL CONSTRAINT [DF_HBweb_RemoteMaintenanceDevice_LastAcceptedSequence] DEFAULT 0,
        [LastAcceptedAtUtc] datetime2 NULL,
        [LastOperationId] uniqueidentifier NULL,
        [MonitorTokenHash] nvarchar(128) NULL,
        [CredentialCiphertext] nvarchar(2048) NULL,
        [CommitResponseCiphertext] nvarchar(8192) NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_HBweb_RemoteMaintenanceDevice_IsDeleted] DEFAULT 0
    );
END;

IF OBJECT_ID(N'dbo.HBweb_RemoteMaintenanceDevice', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.HBweb_RemoteMaintenanceDevice', N'HardwareId') IS NULL
    ALTER TABLE [dbo].[HBweb_RemoteMaintenanceDevice] ADD [HardwareId] nvarchar(100) NOT NULL CONSTRAINT [DF_HBweb_RemoteMaintenanceDevice_HardwareId] DEFAULT N'';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_HBweb_RemoteMaintenanceDevice_DeviceRegistrationId' AND object_id = OBJECT_ID(N'dbo.HBweb_RemoteMaintenanceDevice'))
    CREATE UNIQUE INDEX [UX_HBweb_RemoteMaintenanceDevice_DeviceRegistrationId]
        ON [dbo].[HBweb_RemoteMaintenanceDevice]([DeviceRegistrationId])
        WHERE [IsDeleted] = 0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HBweb_RemoteMaintenanceDevice_StoreCode' AND object_id = OBJECT_ID(N'dbo.HBweb_RemoteMaintenanceDevice'))
    CREATE INDEX [IX_HBweb_RemoteMaintenanceDevice_StoreCode]
        ON [dbo].[HBweb_RemoteMaintenanceDevice]([StoreCode], [LastSeenAtUtc]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HBweb_RemoteMaintenanceDevice_LastSeenAtUtc' AND object_id = OBJECT_ID(N'dbo.HBweb_RemoteMaintenanceDevice'))
    CREATE INDEX [IX_HBweb_RemoteMaintenanceDevice_LastSeenAtUtc]
        ON [dbo].[HBweb_RemoteMaintenanceDevice]([LastSeenAtUtc]);
