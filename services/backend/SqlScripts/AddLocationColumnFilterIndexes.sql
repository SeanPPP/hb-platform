/*
  仓库标签管理列过滤性能索引。
  用法：由 DBA/部署流程按实际数据库类型选择对应段手动执行；脚本不由应用自动执行。
  注意：SQL Server 与 PostgreSQL 段语法互斥，不要把整文件直接原样执行到单一数据库。
  目标：支撑 Location 列筛选，以及 ProductLocation -> Product 的商品列 EXISTS 子查询。
*/

-- =====================================================
-- SQL Server 版本
-- =====================================================

IF OBJECT_ID(N'[dbo].[ProductLocation]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_LocationFilter_ProductLocation_LocationGuid_ProductCode'
         AND object_id = OBJECT_ID(N'[dbo].[ProductLocation]')
   )
BEGIN
    CREATE INDEX [IX_LocationFilter_ProductLocation_LocationGuid_ProductCode]
        ON [dbo].[ProductLocation] ([LocationGuid], [ProductCode])
        WHERE [IsDeleted] = 0;
END;

IF OBJECT_ID(N'[dbo].[ProductLocation]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_LocationFilter_ProductLocation_ProductCode_LocationGuid'
         AND object_id = OBJECT_ID(N'[dbo].[ProductLocation]')
   )
BEGIN
    CREATE INDEX [IX_LocationFilter_ProductLocation_ProductCode_LocationGuid]
        ON [dbo].[ProductLocation] ([ProductCode], [LocationGuid])
        WHERE [IsDeleted] = 0;
END;

IF OBJECT_ID(N'[dbo].[Product]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_LocationFilter_Product_ProductCode'
         AND object_id = OBJECT_ID(N'[dbo].[Product]')
   )
BEGIN
    CREATE INDEX [IX_LocationFilter_Product_ProductCode]
        ON [dbo].[Product] ([ProductCode])
        INCLUDE ([ItemNumber], [Barcode], [ProductName])
        WHERE [IsDeleted] = 0;
END;

IF OBJECT_ID(N'[dbo].[Product]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_LocationFilter_Product_ItemNumber'
         AND object_id = OBJECT_ID(N'[dbo].[Product]')
   )
BEGIN
    CREATE INDEX [IX_LocationFilter_Product_ItemNumber]
        ON [dbo].[Product] ([ItemNumber], [ProductCode])
        WHERE [IsDeleted] = 0 AND [ItemNumber] IS NOT NULL;
END;

IF OBJECT_ID(N'[dbo].[Product]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_LocationFilter_Product_Barcode'
         AND object_id = OBJECT_ID(N'[dbo].[Product]')
   )
BEGIN
    CREATE INDEX [IX_LocationFilter_Product_Barcode]
        ON [dbo].[Product] ([Barcode], [ProductCode])
        WHERE [IsDeleted] = 0 AND [Barcode] IS NOT NULL;
END;

IF OBJECT_ID(N'[dbo].[Product]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_LocationFilter_Product_ProductName'
         AND object_id = OBJECT_ID(N'[dbo].[Product]')
   )
BEGIN
    CREATE INDEX [IX_LocationFilter_Product_ProductName]
        ON [dbo].[Product] ([ProductName], [ProductCode])
        WHERE [IsDeleted] = 0 AND [ProductName] IS NOT NULL;
END;

IF OBJECT_ID(N'[dbo].[Location]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_LocationFilter_Location_List'
         AND object_id = OBJECT_ID(N'[dbo].[Location]')
   )
BEGIN
    CREATE INDEX [IX_LocationFilter_Location_List]
        ON [dbo].[Location] ([IsDeleted], [LocationCode], [LocationBarcode])
        INCLUDE ([LocationType], [Status], [UpdatedAt], [UpdatedBy]);
END;

IF OBJECT_ID(N'[dbo].[Location]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_LocationFilter_Location_Status'
         AND object_id = OBJECT_ID(N'[dbo].[Location]')
   )
BEGIN
    CREATE INDEX [IX_LocationFilter_Location_Status]
        ON [dbo].[Location] ([IsDeleted], [Status], [LocationCode]);
END;

IF OBJECT_ID(N'[dbo].[Location]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_LocationFilter_Location_LocationType'
         AND object_id = OBJECT_ID(N'[dbo].[Location]')
   )
BEGIN
    CREATE INDEX [IX_LocationFilter_Location_LocationType]
        ON [dbo].[Location] ([IsDeleted], [LocationType], [LocationCode]);
END;

IF OBJECT_ID(N'[dbo].[Location]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_LocationFilter_Location_UpdatedAt'
         AND object_id = OBJECT_ID(N'[dbo].[Location]')
   )
BEGIN
    CREATE INDEX [IX_LocationFilter_Location_UpdatedAt]
        ON [dbo].[Location] ([IsDeleted], [UpdatedAt], [LocationCode]);
END;

IF OBJECT_ID(N'[dbo].[Location]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_LocationFilter_Location_UpdatedBy'
         AND object_id = OBJECT_ID(N'[dbo].[Location]')
   )
BEGIN
    CREATE INDEX [IX_LocationFilter_Location_UpdatedBy]
        ON [dbo].[Location] ([IsDeleted], [UpdatedBy], [LocationCode])
        WHERE [UpdatedBy] IS NOT NULL;
END;

IF OBJECT_ID(N'[dbo].[Location]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_Location_Picking_Code_Search'
         AND object_id = OBJECT_ID(N'[dbo].[Location]')
   )
BEGIN
    -- 支撑仓库商品表按配货位代码筛选/搜索，只覆盖配货位活跃数据。
    CREATE INDEX [IX_Location_Picking_Code_Search]
        ON [dbo].[Location] ([LocationCode])
        INCLUDE ([LocationGuid], [LocationBarcode], [Status])
        WHERE [IsDeleted] = 0
          AND [LocationType] = 1
          AND [LocationCode] IS NOT NULL;
END;

IF OBJECT_ID(N'[dbo].[Location]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_Location_Picking_Barcode_Search'
         AND object_id = OBJECT_ID(N'[dbo].[Location]')
   )
BEGIN
    -- 支撑仓库商品表按配货位条码筛选/搜索，只覆盖配货位活跃数据。
    CREATE INDEX [IX_Location_Picking_Barcode_Search]
        ON [dbo].[Location] ([LocationBarcode])
        INCLUDE ([LocationGuid], [LocationCode], [Status])
        WHERE [IsDeleted] = 0
          AND [LocationType] = 1
          AND [LocationBarcode] IS NOT NULL;
END;

-- =====================================================
-- PostgreSQL 版本
-- =====================================================

CREATE INDEX IF NOT EXISTS "IX_LocationFilter_ProductLocation_LocationGuid_ProductCode"
    ON "ProductLocation" ("LocationGuid", "ProductCode")
    WHERE "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_LocationFilter_ProductLocation_ProductCode_LocationGuid"
    ON "ProductLocation" ("ProductCode", "LocationGuid")
    WHERE "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_LocationFilter_Product_ProductCode"
    ON "Product" ("ProductCode")
    INCLUDE ("ItemNumber", "Barcode", "ProductName")
    WHERE "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_LocationFilter_Product_ItemNumber"
    ON "Product" ("ItemNumber", "ProductCode")
    WHERE "IsDeleted" = false AND "ItemNumber" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_LocationFilter_Product_Barcode"
    ON "Product" ("Barcode", "ProductCode")
    WHERE "IsDeleted" = false AND "Barcode" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_LocationFilter_Product_ProductName"
    ON "Product" ("ProductName", "ProductCode")
    WHERE "IsDeleted" = false AND "ProductName" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_LocationFilter_Location_List"
    ON "Location" ("IsDeleted", "LocationCode", "LocationBarcode")
    INCLUDE ("LocationType", "Status", "UpdatedAt", "UpdatedBy");

CREATE INDEX IF NOT EXISTS "IX_LocationFilter_Location_Status"
    ON "Location" ("IsDeleted", "Status", "LocationCode");

CREATE INDEX IF NOT EXISTS "IX_LocationFilter_Location_LocationType"
    ON "Location" ("IsDeleted", "LocationType", "LocationCode");

CREATE INDEX IF NOT EXISTS "IX_LocationFilter_Location_UpdatedAt"
    ON "Location" ("IsDeleted", "UpdatedAt", "LocationCode");

CREATE INDEX IF NOT EXISTS "IX_LocationFilter_Location_UpdatedBy"
    ON "Location" ("IsDeleted", "UpdatedBy", "LocationCode")
    WHERE "UpdatedBy" IS NOT NULL;

-- 支撑仓库商品表按配货位代码筛选/搜索，只覆盖配货位活跃数据。
CREATE INDEX IF NOT EXISTS "IX_Location_Picking_Code_Search"
    ON "Location" ("LocationCode")
    INCLUDE ("LocationGuid", "LocationBarcode", "Status")
    WHERE "IsDeleted" = false
      AND "LocationType" = 1
      AND "LocationCode" IS NOT NULL;

-- 支撑仓库商品表按配货位条码筛选/搜索，只覆盖配货位活跃数据。
CREATE INDEX IF NOT EXISTS "IX_Location_Picking_Barcode_Search"
    ON "Location" ("LocationBarcode")
    INCLUDE ("LocationGuid", "LocationCode", "Status")
    WHERE "IsDeleted" = false
      AND "LocationType" = 1
      AND "LocationBarcode" IS NOT NULL;
