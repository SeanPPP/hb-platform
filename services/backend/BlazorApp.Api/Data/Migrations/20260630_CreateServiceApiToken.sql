IF OBJECT_ID('ServiceApiToken', 'U') IS NULL
BEGIN
    CREATE TABLE [ServiceApiToken] (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_ServiceApiToken] PRIMARY KEY,
        [Name] nvarchar(120) NOT NULL,
        [TokenHash] nvarchar(64) NOT NULL,
        [TokenPrefix] nvarchar(32) NOT NULL,
        [Scopes] nvarchar(500) NOT NULL,
        [ExpiresAt] datetime2 NULL,
        [RevokedAt] datetime2 NULL,
        [RevokedBy] nvarchar(120) NULL,
        [LastUsedAt] datetime2 NULL,
        [LastUsedIp] nvarchar(64) NULL,
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
    WHERE name = 'IX_ServiceApiToken_TokenHash'
      AND object_id = OBJECT_ID('ServiceApiToken')
)
BEGIN
    CREATE UNIQUE INDEX [IX_ServiceApiToken_TokenHash]
    ON [ServiceApiToken]([TokenHash]);
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_ServiceApiToken_CreatedAt'
      AND object_id = OBJECT_ID('ServiceApiToken')
)
BEGIN
    CREATE INDEX [IX_ServiceApiToken_CreatedAt]
    ON [ServiceApiToken]([CreatedAt]);
END;
