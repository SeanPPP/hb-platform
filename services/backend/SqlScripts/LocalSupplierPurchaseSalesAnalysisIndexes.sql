/*
分店进货销量分析：性能索引

状态：索引 1 已于 2026-09-18 在生产库（HBweb）执行完毕，实测收益见下。
      索引 2 经实测判断不需要，保留说明供日后参考，默认不执行。

背景
----
页面主查询原先在生产库耗时：服务端 CPU 约 3.2~3.6 秒、占用时间 2.6~6.6 秒。
瓶颈是进货明细表被整表并行扫描：约 66,500 次逻辑读（≈520 MB），
而实际只需要某分店 180 天内的 94 张进货单、7,786 行明细。
根因是供应商信息不在明细表上，必须先扫描明细并 JOIN 商品/零售价才能按供应商过滤。

索引 1 实测收益（同一查询，StoreCode=1005 / SupplierCode=240 / 180 天）
------------------------------------------------------------------
                          加索引前              加索引后
  明细表逻辑读            66,986 次             1,065 次      ↓ 98.4%
  明细表访问方式          并行全表扫描(3)       94 次 seek
  服务端 CPU              3,625 毫秒            500~564 毫秒  ↓ 85%
  服务端占用时间          2,433~6,611 毫秒      286 毫秒
  索引占用                —                     265.3 MB

执行信息
--------
- 版本 Developer Edition 16.0（等同企业版），使用 ONLINE = ON 在线创建，未阻塞读写。
- 创建耗时约 25 秒，期间业务可正常读写。
- 执行时数据文件 47 GB、磁盘 E:\ 剩余 79.3 GB，新增 265 MB 占比极小。
- sqlcmd 执行需加 -I（或脚本内 SET QUOTED_IDENTIFIER ON），否则报 SET 选项错误。

回退
----
DROP INDEX IX_LSPSA_InvoiceDetails_Invoice_Covering ON [StoreLocalSupplierInvoiceDetails];
*/

-- ============================================================
-- 索引 1：已执行。进货明细按单据定位并覆盖查询所需列
-- ============================================================
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('[StoreLocalSupplierInvoiceDetails]')
      AND name = 'IX_LSPSA_InvoiceDetails_Invoice_Covering'
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_LSPSA_InvoiceDetails_Invoice_Covering
        ON [StoreLocalSupplierInvoiceDetails] (InvoiceGUID, IsDeleted)
        INCLUDE (StoreProductCode, ProductCode, ItemNumber, Barcode, ProductName, Quantity)
        WITH (ONLINE = ON, MAXDOP = 2);   -- ONLINE 需要企业版/开发版；标准版请去掉并安排低峰期
END
GO

/*
-- ============================================================
-- 索引 2：不执行。执行计划曾建议，但实测判断收益不成立
-- ============================================================
-- SQL Server 的 Missing Index 提示给出过下面这个索引（影响度 100%）：
--
--     CREATE NONCLUSTERED INDEX IX_LSPSA_StoreRetailPrice_NotDeleted_Covering
--         ON [StoreRetailPrice] (IsDeleted) INCLUDE (ProductCode, SupplierCode);
--
-- 不采纳的原因：
-- 1. IO 统计显示 StoreRetailPrice 的「扫描计数 = 0」，其 29,684 次逻辑读全部来自
--    按 UUID 的单行 seek（约 7,786 次 × B 树层级），并不是整表扫描，
--    换成 (IsDeleted) 这种低选择性键只能做覆盖扫描，估算页数反而更多。
-- 2. 该表已有 14 个索引、合计约 8.5 GB（数据本身仅 1.86 GB），
--    再加约 700~800 MB 会继续加重写入负担。
-- 3. 加完索引 1 后整体已降到 286 毫秒，没有继续优化的必要。
--
-- 若日后该表访问模式变化需要重新评估，可先用执行计划确认它是否真的在做整表扫描。
*/
