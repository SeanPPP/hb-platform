IF OBJECT_ID('MobileAppBuild', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('MobileAppBuild', 'CosArtifactUrl') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosArtifactUrl] nvarchar(1000) NULL;
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosObjectKey') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosObjectKey] nvarchar(500) NULL;
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosMirroredAt') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosMirroredAt] datetime2 NULL;
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosMirrorError') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosMirrorError] nvarchar(1000) NULL;
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosMirrorStatus') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosMirrorStatus] nvarchar(32) NOT NULL
            CONSTRAINT [DF_MobileAppBuild_CosMirrorStatus] DEFAULT('pending');
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosMirrorAttempts') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosMirrorAttempts] int NOT NULL
            CONSTRAINT [DF_MobileAppBuild_CosMirrorAttempts] DEFAULT(0);
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosMirrorLastAttemptAtUtc') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosMirrorLastAttemptAtUtc] datetime2 NULL;
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosArtifactUrl') IS NOT NULL
       AND COL_LENGTH('MobileAppBuild', 'CosMirrorStatus') IS NOT NULL
    BEGIN
        EXEC(N'
            UPDATE [MobileAppBuild]
            SET [CosMirrorStatus] = ''succeeded''
            WHERE [CosArtifactUrl] IS NOT NULL
              AND LTRIM(RTRIM([CosArtifactUrl])) <> ''''
              AND ([CosMirrorStatus] IS NULL OR [CosMirrorStatus] = ''pending'');
        ');
    END;
END;
