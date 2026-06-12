-- SQL Server: 创建 ProductCategory 表
-- 如果使用 SqlSugar CodeFirst 自动建表，则无需手动执行此脚本

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProductCategory')
BEGIN
    CREATE TABLE [ProductCategory] (
        [CategoryGUID]  NVARCHAR(200) NOT NULL,
        [ParentGUID]    NVARCHAR(200) NULL,
        [CategoryName]  NVARCHAR(100) NOT NULL,
        [IsActive]      BIT NOT NULL DEFAULT 1,
        [SortOrder]     INT NULL,
        [CreatedAt]     DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy]     NVARCHAR(200) NULL,
        [UpdatedAt]     DATETIME2 NULL,
        [UpdatedBy]     NVARCHAR(200) NULL,
        [IsDeleted]     BIT NULL DEFAULT 0,
        CONSTRAINT [PK_ProductCategory] PRIMARY KEY ([CategoryGUID])
    );

    CREATE NONCLUSTERED INDEX [IX_ProductCategory_ParentGUID]
        ON [ProductCategory] ([ParentGUID]);

    CREATE NONCLUSTERED INDEX [IX_ProductCategory_CategoryName]
        ON [ProductCategory] ([CategoryName]);

    PRINT 'ProductCategory 表创建成功';
END
ELSE
BEGIN
    PRINT 'ProductCategory 表已存在';
END
GO

-- PostgreSQL: 取消下方注释执行
/*
CREATE TABLE IF NOT EXISTS "ProductCategory" (
    "CategoryGUID"  VARCHAR(200) NOT NULL,
    "ParentGUID"    VARCHAR(200) NULL,
    "CategoryName"  VARCHAR(100) NOT NULL,
    "IsActive"      BOOLEAN NOT NULL DEFAULT TRUE,
    "SortOrder"     INTEGER NULL,
    "CreatedAt"     TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
    "CreatedBy"     VARCHAR(200) NULL,
    "UpdatedAt"     TIMESTAMP NULL,
    "UpdatedBy"     VARCHAR(200) NULL,
    "IsDeleted"     BOOLEAN NULL DEFAULT FALSE,
    CONSTRAINT "PK_ProductCategory" PRIMARY KEY ("CategoryGUID")
);

CREATE INDEX IF NOT EXISTS "IX_ProductCategory_ParentGUID" ON "ProductCategory" ("ParentGUID");
CREATE INDEX IF NOT EXISTS "IX_ProductCategory_CategoryName" ON "ProductCategory" ("CategoryName");
*/
