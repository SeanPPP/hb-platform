IF OBJECT_ID('MobileAppBuild', 'U') IS NULL
BEGIN
    CREATE TABLE [MobileAppBuild] (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_MobileAppBuild] PRIMARY KEY,
        [EasBuildId] nvarchar(120) NOT NULL,
        [AccountName] nvarchar(120) NOT NULL,
        [ProjectName] nvarchar(120) NOT NULL,
        [AppName] nvarchar(160) NULL,
        [Platform] nvarchar(30) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [BuildProfile] nvarchar(80) NOT NULL,
        [Distribution] nvarchar(80) NULL,
        [Channel] nvarchar(120) NULL,
        [RuntimeVersion] nvarchar(120) NULL,
        [AppVersion] nvarchar(80) NULL,
        [AppBuildVersion] nvarchar(80) NULL,
        [ArtifactUrl] nvarchar(1000) NOT NULL,
        [BuildDetailsPageUrl] nvarchar(1000) NULL,
        [GitCommitHash] nvarchar(120) NULL,
        [GitCommitMessage] nvarchar(1000) NULL,
        [CompletedAt] datetime2 NULL,
        [ExpirationDate] datetime2 NULL,
        [ReceivedAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(max) NULL,
        [IsDeleted] bit NULL
    );
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_MobileAppBuild_EasBuildId'
      AND object_id = OBJECT_ID('MobileAppBuild')
)
BEGIN
    CREATE UNIQUE INDEX [IX_MobileAppBuild_EasBuildId]
    ON [MobileAppBuild]([EasBuildId]);
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_MobileAppBuild_Profile_CompletedAt'
      AND object_id = OBJECT_ID('MobileAppBuild')
)
BEGIN
    CREATE INDEX [IX_MobileAppBuild_Profile_CompletedAt]
    ON [MobileAppBuild]([BuildProfile], [Platform], [Status], [CompletedAt]);
END;
