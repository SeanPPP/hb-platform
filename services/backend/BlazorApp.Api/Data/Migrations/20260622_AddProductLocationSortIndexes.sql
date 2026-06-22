IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_ProductLocation_ProductCode_Active'
      AND object_id = OBJECT_ID('ProductLocation')
)
BEGIN
    -- 支撑按商品聚合最小货位：先定位活跃商品映射，再覆盖 LocationGuid 参与 Location Join。
    CREATE INDEX [IX_ProductLocation_ProductCode_Active]
    ON [ProductLocation]([ProductCode])
    INCLUDE([LocationGuid])
    WHERE [IsDeleted] = 0
      AND [ProductCode] IS NOT NULL
      AND [LocationGuid] IS NOT NULL;
END;

IF NOT EXISTS (
    SELECT *
    FROM sys.indexes
    WHERE name = 'IX_ProductLocation_LocationGuid_Active'
      AND object_id = OBJECT_ID('ProductLocation')
)
BEGIN
    -- 支撑从货位反查活跃商品映射，避免 LocationGuid Join 回表取 ProductCode。
    CREATE INDEX [IX_ProductLocation_LocationGuid_Active]
    ON [ProductLocation]([LocationGuid])
    INCLUDE([ProductCode])
    WHERE [IsDeleted] = 0
      AND [LocationGuid] IS NOT NULL
      AND [ProductCode] IS NOT NULL;
END;
