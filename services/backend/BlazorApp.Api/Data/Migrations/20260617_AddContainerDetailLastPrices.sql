IF COL_LENGTH('ContainerDetail', 'TargetWarehouseCategoryGUID') IS NULL
BEGIN
    ALTER TABLE ContainerDetail ADD TargetWarehouseCategoryGUID NVARCHAR(50) NULL;
END
GO

IF COL_LENGTH('ContainerDetail', 'LastImportPrice') IS NULL
BEGIN
    ALTER TABLE ContainerDetail ADD LastImportPrice DECIMAL(18,2) NULL;
END
GO

IF COL_LENGTH('ContainerDetail', 'LastOEMPrice') IS NULL
BEGIN
    ALTER TABLE ContainerDetail ADD LastOEMPrice DECIMAL(18,2) NULL;
END
GO
