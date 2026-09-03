namespace BlazorApp.Api.Data.SchemaMigrations;

/// <summary>
/// 货柜明细查询的非过滤索引。不依赖运行时 ARITHABORT 会话选项，保留现有软删除语义。
/// </summary>
internal static class ContainerDetailQueryIndexSchema
{
    internal const string ApplySql = """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;
        SET ANSI_NULLS ON;
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_PADDING ON;
        SET ANSI_WARNINGS ON;
        SET CONCAT_NULL_YIELDS_NULL ON;
        SET ARITHABORT ON;
        SET NUMERIC_ROUNDABORT OFF;

        IF OBJECT_ID(N'dbo.ContainerDetail', N'U') IS NULL
        BEGIN
            THROW 51520, N'ContainerDetail table is missing.', 1;
        END;
        IF OBJECT_ID(N'dbo.Product', N'U') IS NULL
        BEGIN
            THROW 51521, N'Product table is missing.', 1;
        END;
        IF OBJECT_ID(N'dbo.DomesticProduct', N'U') IS NULL
        BEGIN
            THROW 51522, N'DomesticProduct table is missing.', 1;
        END;

        -- 生产数据已验证 ProductCode 全局非空唯一；迁移仍失败关闭，防止悄然改变 join 基数。
        IF EXISTS (SELECT 1 FROM dbo.Product WHERE ProductCode IS NULL)
        BEGIN
            THROW 51523, N'Product.ProductCode contains NULL values.', 1;
        END;
        IF EXISTS (
            SELECT ProductCode
            FROM dbo.Product
            GROUP BY ProductCode
            HAVING COUNT_BIG(*) > 1
        )
        BEGIN
            THROW 51524, N'Product.ProductCode contains duplicate values.', 1;
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.ContainerDetail')
              AND name = N'IX_ContainerDetail_ContainerCode_IsDeleted_ProductCode_All'
        )
        BEGIN
            CREATE NONCLUSTERED INDEX [IX_ContainerDetail_ContainerCode_IsDeleted_ProductCode_All]
                ON dbo.ContainerDetail ([ContainerCode], [IsDeleted], [ProductCode]);
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.Product')
              AND name = N'UX_Product_ProductCode_ContainerDetailLookup_All'
        )
        BEGIN
            CREATE UNIQUE NONCLUSTERED INDEX [UX_Product_ProductCode_ContainerDetailLookup_All]
                ON dbo.Product ([ProductCode])
                INCLUDE ([ItemNumber], [LocalSupplierCode], [ProductName], [WarehouseCategoryGUID]);
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.Product')
              AND name = N'IX_Product_LocalSupplierCode_ItemNumber_ProductCode_All'
        )
        BEGIN
            CREATE NONCLUSTERED INDEX [IX_Product_LocalSupplierCode_ItemNumber_ProductCode_All]
                ON dbo.Product ([LocalSupplierCode], [ItemNumber], [ProductCode]);
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.DomesticProduct')
              AND name = N'IX_DomesticProduct_SupplierCode_HBProductNo_IsDeleted_ProductCode_All'
        )
        BEGIN
            CREATE NONCLUSTERED INDEX [IX_DomesticProduct_SupplierCode_HBProductNo_IsDeleted_ProductCode_All]
                ON dbo.DomesticProduct ([SupplierCode], [HBProductNo], [IsDeleted], [ProductCode]);
        END;
        """;

    internal const string VerifySql = """
        SET NOCOUNT ON;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes AS i
            WHERE i.object_id = OBJECT_ID(N'dbo.ContainerDetail')
              AND i.name = N'IX_ContainerDetail_ContainerCode_IsDeleted_ProductCode_All'
              AND i.is_unique = 0
              AND i.has_filter = 0
              AND i.is_disabled = 0
              AND i.is_hypothetical = 0
              AND (SELECT COUNT(*) FROM sys.index_columns AS ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 3
              AND EXISTS (SELECT 1 FROM sys.index_columns AS ic JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND c.name = N'ContainerCode')
              AND EXISTS (SELECT 1 FROM sys.index_columns AS ic JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND c.name = N'IsDeleted')
              AND EXISTS (SELECT 1 FROM sys.index_columns AS ic JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 3 AND c.name = N'ProductCode')
              AND (SELECT COUNT(*) FROM sys.index_columns AS ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1) = 0
        )
        BEGIN
            THROW 51530, N'ContainerDetail query index signature is incompatible.', 1;
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes AS i
            WHERE i.object_id = OBJECT_ID(N'dbo.Product')
              AND i.name = N'UX_Product_ProductCode_ContainerDetailLookup_All'
              AND i.is_unique = 1
              AND i.has_filter = 0
              AND i.is_disabled = 0
              AND i.is_hypothetical = 0
              AND (SELECT COUNT(*) FROM sys.index_columns AS ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
              AND EXISTS (SELECT 1 FROM sys.index_columns AS ic JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND c.name = N'ProductCode')
              AND (SELECT COUNT(*) FROM sys.index_columns AS ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1) = 4
              AND NOT EXISTS (
                  SELECT required.name
                  FROM (VALUES (N'ItemNumber'), (N'LocalSupplierCode'), (N'ProductName'), (N'WarehouseCategoryGUID')) AS required(name)
                  WHERE NOT EXISTS (
                      SELECT 1
                      FROM sys.index_columns AS ic
                      JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                      WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1 AND c.name = required.name
                  )
              )
        )
        BEGIN
            THROW 51531, N'Product code lookup index signature is incompatible.', 1;
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes AS i
            WHERE i.object_id = OBJECT_ID(N'dbo.Product')
              AND i.name = N'IX_Product_LocalSupplierCode_ItemNumber_ProductCode_All'
              AND i.is_unique = 0
              AND i.has_filter = 0
              AND i.is_disabled = 0
              AND i.is_hypothetical = 0
              AND (SELECT COUNT(*) FROM sys.index_columns AS ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 3
              AND EXISTS (SELECT 1 FROM sys.index_columns AS ic JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND c.name = N'LocalSupplierCode')
              AND EXISTS (SELECT 1 FROM sys.index_columns AS ic JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND c.name = N'ItemNumber')
              AND EXISTS (SELECT 1 FROM sys.index_columns AS ic JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 3 AND c.name = N'ProductCode')
              AND (SELECT COUNT(*) FROM sys.index_columns AS ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1) = 0
        )
        BEGIN
            THROW 51532, N'Product supplier-item index signature is incompatible.', 1;
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes AS i
            WHERE i.object_id = OBJECT_ID(N'dbo.DomesticProduct')
              AND i.name = N'IX_DomesticProduct_SupplierCode_HBProductNo_IsDeleted_ProductCode_All'
              AND i.is_unique = 0
              AND i.has_filter = 0
              AND i.is_disabled = 0
              AND i.is_hypothetical = 0
              AND (SELECT COUNT(*) FROM sys.index_columns AS ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 4
              AND EXISTS (SELECT 1 FROM sys.index_columns AS ic JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND c.name = N'SupplierCode')
              AND EXISTS (SELECT 1 FROM sys.index_columns AS ic JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND c.name = N'HBProductNo')
              AND EXISTS (SELECT 1 FROM sys.index_columns AS ic JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 3 AND c.name = N'IsDeleted')
              AND EXISTS (SELECT 1 FROM sys.index_columns AS ic JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 4 AND c.name = N'ProductCode')
              AND (SELECT COUNT(*) FROM sys.index_columns AS ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1) = 0
        )
        BEGIN
            THROW 51533, N'DomesticProduct supplier-item index signature is incompatible.', 1;
        END;
        """;
}
