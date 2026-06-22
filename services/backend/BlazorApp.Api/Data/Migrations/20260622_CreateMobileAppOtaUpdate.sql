IF OBJECT_ID('MobileAppOtaUpdate', 'U') IS NULL
BEGIN
    CREATE TABLE [MobileAppOtaUpdate] (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_MobileAppOtaUpdate] PRIMARY KEY,
        [UpdateGroupId] nvarchar(120) NOT NULL,
        [AndroidUpdateId] nvarchar(120) NULL,
        [Channel] nvarchar(120) NOT NULL,
        [Branch] nvarchar(120) NULL,
        [Platform] nvarchar(30) NOT NULL,
        [RuntimeVersion] nvarchar(120) NULL,
        [Message] nvarchar(1000) NULL,
        [GitCommitHash] nvarchar(120) NULL,
        [DashboardUrl] nvarchar(1000) NULL,
        [PublishedAt] datetime2 NOT NULL,
        [IsRollback] bit NOT NULL,
        [RollbackOfGroupId] nvarchar(120) NULL,
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
    WHERE name = 'IX_MobileAppOtaUpdate_Group_Platform'
      AND object_id = OBJECT_ID('MobileAppOtaUpdate')
)
BEGIN
    CREATE UNIQUE INDEX [IX_MobileAppOtaUpdate_Group_Platform]
    ON [MobileAppOtaUpdate]([UpdateGroupId], [Platform]);
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_MobileAppOtaUpdate_Channel_Runtime_PublishedAt'
      AND object_id = OBJECT_ID('MobileAppOtaUpdate')
)
BEGIN
    CREATE INDEX [IX_MobileAppOtaUpdate_Channel_Runtime_PublishedAt]
    ON [MobileAppOtaUpdate]([Channel], [RuntimeVersion], [PublishedAt]);
END;
