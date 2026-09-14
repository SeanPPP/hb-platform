-- 批量货号销量：独立折扣聚合快照。前台无自动 DDL。
-- 执行前核验服务器及 HBweb 数据库，保留当前 API 版本作为回退。
-- 回退：部署上一版 API 并保留本表，不删除已排队或已完成结果。
SET XACT_ABORT ON;
IF DB_NAME() <> N'HBweb'
    THROW 51000, N'目标数据库必须为 HBweb，请先核对环境。', 1;

SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName,
       OBJECT_ID(N'dbo.BatchProductSalesDiscountSnapshot', N'U') AS ExistingTable;

BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.BatchProductSalesDiscountSnapshot', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BatchProductSalesDiscountSnapshot (
        Id nvarchar(64) NOT NULL PRIMARY KEY,
        SourceVersion nvarchar(64) NOT NULL,
        ProductCode nvarchar(50) NOT NULL,
        StartDate datetime2 NOT NULL,
        EndDate datetime2 NOT NULL,
        StoreCodesJson nvarchar(max) NOT NULL,
        Status nvarchar(20) NOT NULL,
        Attempts int NOT NULL,
        RequestedAtUtc datetime2 NOT NULL,
        NextAttemptAtUtc datetime2 NOT NULL,
        LeaseToken nvarchar(32) NULL,
        LeaseUntilUtc datetime2 NULL,
        CompletedAtUtc datetime2 NULL,
        PayloadJson nvarchar(max) NULL,
        CONSTRAINT CK_BatchSalesDiscount_Stores CHECK (ISJSON(StoreCodesJson) = 1),
        CONSTRAINT CK_BatchSalesDiscount_Payload CHECK (PayloadJson IS NULL OR ISJSON(PayloadJson) = 1),
        CONSTRAINT CK_BatchSalesDiscount_Fresh CHECK (Status <> N'Fresh' OR PayloadJson IS NOT NULL)
    );
    CREATE INDEX IX_BatchSalesDiscount_Queue ON dbo.BatchProductSalesDiscountSnapshot
        (Status, NextAttemptAtUtc, RequestedAtUtc) INCLUDE (Attempts, LeaseUntilUtc);
END;
COMMIT;

SELECT Status, COUNT_BIG(*) AS SnapshotCount FROM dbo.BatchProductSalesDiscountSnapshot GROUP BY Status;
-- 安装后：核验 worker 日志及同一商品 Queued -> Running -> Fresh；出现 OutOfSync 需对账，不能伪装完成。
