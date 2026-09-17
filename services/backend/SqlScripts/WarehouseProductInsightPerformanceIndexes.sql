/*
  仓库商品进销查询性能索引（SQL Server）。

  适用接口：GET /api/react/v1/warehouse-product-insights（移动端仓库商品进销查询）。
  该接口按单个 ProductCode + 最长 400 天区间读取货柜进货、分店订货、分店发货与 POS 日销售四段事实，
  首屏预算为 3 秒。下列三个索引把 ProductCode 提到前导列；缺失时这三段查询只能走扫描。

  安全约束：
  1. 本脚本仅供人工在已确认的业务数据库中执行，不接入应用启动迁移。
  2. 执行前必须核对数据库、表行数、等价索引、最近完整备份、磁盘空间、活动请求与阻塞。
  3. 建议按下方三个步骤逐段在低峰执行，每步完成后复查执行计划、锁等待与接口耗时。
  4. 脚本不会删除或重建既有索引；仅按本功能精确名称判断已创建，重复执行安全。
  5. 非支持 ONLINE CREATE INDEX 的版本会直接停止，禁止无意中退化为离线建索引。

  既有索引（本脚本只依赖、不创建）：
  - IX_ContainerDetail_ContainerCode_IsDeleted_ProductCode：货柜前导，服务于按货柜查明细，无法服务按商品查货柜；
  - IX_ProductStoreDailySalesStatistic_Branch_Product_Date：分店前导，授权分店范围下可命中；
  - IX_WareHouseOrderDetails_OrderGUID_IsDeleted_ProductCode：订单前导，无法服务按商品查订单。
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET DEADLOCK_PRIORITY LOW;
SET LOCK_TIMEOUT 5000;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @EngineEdition int = CONVERT(int, SERVERPROPERTY(N'EngineEdition'));
IF @EngineEdition NOT IN (3, 5, 8)
BEGIN
    THROW 51040, N'当前 SQL Server 版本未确认支持 ONLINE CREATE INDEX；请在维护窗口单独审阅，禁止自动改为离线创建。', 1;
END;

IF OBJECT_ID(N'dbo.ContainerDetail', N'U') IS NULL
    THROW 51041, N'缺少 dbo.ContainerDetail，禁止在非目标数据库执行本脚本。', 1;
IF OBJECT_ID(N'dbo.WareHouseOrderDetails', N'U') IS NULL
    THROW 51042, N'缺少 dbo.WareHouseOrderDetails，禁止在非目标数据库执行本脚本。', 1;
IF OBJECT_ID(N'dbo.ProductStoreDailySalesStatistic', N'U') IS NULL
    THROW 51043, N'缺少 dbo.ProductStoreDailySalesStatistic，禁止在非目标数据库执行本脚本。', 1;
GO

/* 步骤 1：货柜明细按商品前导。服务「进货合计 + 货柜明细」段。 */
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ContainerDetail')
      AND name = N'IX_ContainerDetail_Product_NotDeleted'
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_ContainerDetail_Product_NotDeleted
        ON dbo.ContainerDetail (ProductCode)
        INCLUDE (ContainerCode, LoadingQuantity, LoadingPieces, Status)
        WHERE IsDeleted = 0
        WITH
        (
            ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)),
            SORT_IN_TEMPDB = ON,
            MAXDOP = 2
        );
END;
GO

/* 步骤 2：仓库订单明细按商品前导。同时服务「订货」与「发货」两段，两段只在订单表侧的日期列不同。 */
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.WareHouseOrderDetails')
      AND name = N'IX_WareHouseOrderDetails_Product_NotDeleted'
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_WareHouseOrderDetails_Product_NotDeleted
        ON dbo.WareHouseOrderDetails (ProductCode)
        INCLUDE (OrderGUID, Quantity, AllocQuantity)
        WHERE IsDeleted = 0
        WITH
        (
            ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)),
            SORT_IN_TEMPDB = ON,
            MAXDOP = 2
        );
END;
GO

/* 步骤 3：POS 日销售统计按商品 + 业务日前导。服务全分店范围（仓库管理员）下的「分店销售」段；
   授权分店范围的用户仍可命中既有的 Branch_Product_Date 索引。 */
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ProductStoreDailySalesStatistic')
      AND name = N'IX_ProductStoreDailySalesStatistic_Product_Date'
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_ProductStoreDailySalesStatistic_Product_Date
        ON dbo.ProductStoreDailySalesStatistic (ProductCode, Date)
        INCLUDE (BranchCode, TotalQuantity, TotalAmount, UpdateTime)
        WITH
        (
            ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)),
            SORT_IN_TEMPDB = ON,
            MAXDOP = 2
        );
END;
GO

/* 校验：三个索引必须以精确定义存在，否则说明存在同名但定义不同的索引，需要人工处置。 */
DECLARE @missing nvarchar(max) = N'';

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ContainerDetail')
      AND name = N'IX_ContainerDetail_Product_NotDeleted'
      AND is_disabled = 0
      AND has_filter = 1
)
    SET @missing = @missing + N'IX_ContainerDetail_Product_NotDeleted;';

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.WareHouseOrderDetails')
      AND name = N'IX_WareHouseOrderDetails_Product_NotDeleted'
      AND is_disabled = 0
      AND has_filter = 1
)
    SET @missing = @missing + N'IX_WareHouseOrderDetails_Product_NotDeleted;';

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ProductStoreDailySalesStatistic')
      AND name = N'IX_ProductStoreDailySalesStatistic_Product_Date'
      AND is_disabled = 0
      AND has_filter = 0
)
    SET @missing = @missing + N'IX_ProductStoreDailySalesStatistic_Product_Date;';

IF LEN(@missing) > 0
BEGIN
    DECLARE @message nvarchar(max) = N'仓库商品进销查询索引校验失败，缺失或定义不符：' + @missing;
    THROW 51044, @message, 1;
END;
GO
