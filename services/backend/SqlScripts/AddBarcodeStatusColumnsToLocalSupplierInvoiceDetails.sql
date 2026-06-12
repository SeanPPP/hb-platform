-- 添加条码状态和条码匹配数字段到 StoreLocalSupplierInvoiceDetails 表
-- 创建日期：2026-03-21
-- 用途：保存条码检测结果，避免条码状态被覆盖

-- 添加条码状态字段：0=未检测，1=正常，2=异常
ALTER TABLE StoreLocalSupplierInvoiceDetails
ADD COLUMN BarcodeStatus INT NULL;

-- 添加条码匹配数量字段
ALTER TABLE StoreLocalSupplierInvoiceDetails
ADD COLUMN BarcodeMatchCount INT NULL;

-- 为现有数据设置默认值（可选）
-- UPDATE StoreLocalSupplierInvoiceDetails
-- SET BarcodeStatus = 0, BarcodeMatchCount = 0
-- WHERE BarcodeStatus IS NULL;
