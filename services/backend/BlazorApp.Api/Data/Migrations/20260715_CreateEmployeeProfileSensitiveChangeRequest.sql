IF OBJECT_ID(N'[dbo].[EmployeeProfile]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.EmployeeProfile', 'SensitiveRevision') IS NULL
        ALTER TABLE [dbo].[EmployeeProfile] ADD [SensitiveRevision] int NOT NULL CONSTRAINT [DF_EmployeeProfile_SensitiveRevision] DEFAULT(0);
    IF COL_LENGTH('dbo.EmployeeProfile', 'IdentityType') IS NULL
        ALTER TABLE [dbo].[EmployeeProfile] ADD [IdentityType] nvarchar(50) NULL;
END;

IF OBJECT_ID(N'[dbo].[EmployeeImageUploadTickets]', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.EmployeeImageUploadTickets', 'SensitiveChangeRequestId') IS NULL
BEGIN
    ALTER TABLE [dbo].[EmployeeImageUploadTickets] ADD [SensitiveChangeRequestId] int NULL;
END;

IF OBJECT_ID(N'[dbo].[EmployeeProfileSensitiveChangeRequest]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EmployeeProfileSensitiveChangeRequest] (
        [RequestId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_EmployeeProfileSensitiveChangeRequest] PRIMARY KEY,
        [UserGUID] nvarchar(50) NOT NULL,
        [BankBsb] nvarchar(20) NULL,
        [BankAccountNumber] nvarchar(50) NULL,
        [SuperannuationCompanyName] nvarchar(200) NULL,
        [SuperannuationCompanyCode] nvarchar(100) NULL,
        [SuperannuationAccountNumber] nvarchar(100) NULL,
        [IdentityType] nvarchar(50) NULL,
        [IdentityId] nvarchar(100) NULL,
        [IdentityPhotoObjectKey] nvarchar(500) NULL,
        [RemoveIdentityPhoto] bit NOT NULL CONSTRAINT [DF_EmployeeProfileSensitiveChangeRequest_RemoveIdentityPhoto] DEFAULT(0),
        [ChangedFieldsJson] nvarchar(1000) NULL,
        [Status] int NOT NULL,
        [BaseSensitiveRevision] int NOT NULL,
        [SubmittedAt] datetime2 NOT NULL,
        [SubmittedBy] nvarchar(100) NULL,
        [ReviewedAt] datetime2 NULL,
        [ReviewedBy] nvarchar(100) NULL,
        [ReviewReason] nvarchar(1000) NULL,
        [SupersededAt] datetime2 NULL,
        [SupersededBy] nvarchar(100) NULL
    );
END;

IF OBJECT_ID(N'[dbo].[EmployeeProfileSensitiveChangeRequest]', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.EmployeeProfileSensitiveChangeRequest', 'RemoveIdentityPhoto') IS NULL
BEGIN
    ALTER TABLE [dbo].[EmployeeProfileSensitiveChangeRequest]
        ADD [RemoveIdentityPhoto] bit NOT NULL
            CONSTRAINT [DF_EmployeeProfileSensitiveChangeRequest_RemoveIdentityPhoto] DEFAULT(0) WITH VALUES;
END;

IF OBJECT_ID(N'[dbo].[EmployeeProfileSensitiveChangeRequest]', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.EmployeeProfileSensitiveChangeRequest', 'ChangedFieldsJson') IS NULL
BEGIN
    ALTER TABLE [dbo].[EmployeeProfileSensitiveChangeRequest]
        ADD [ChangedFieldsJson] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [name] = 'UX_EmployeeProfileSensitiveChangeRequest_User_Pending'
      AND [object_id] = OBJECT_ID(N'[dbo].[EmployeeProfileSensitiveChangeRequest]')
)
BEGIN
    CREATE UNIQUE INDEX [UX_EmployeeProfileSensitiveChangeRequest_User_Pending]
        ON [dbo].[EmployeeProfileSensitiveChangeRequest]([UserGUID])
        WHERE [Status] = 0;
END;
