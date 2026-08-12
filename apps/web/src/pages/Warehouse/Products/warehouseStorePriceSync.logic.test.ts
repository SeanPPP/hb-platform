import {
  WAREHOUSE_STORE_PRICE_SYNC_FIELD_MAPPINGS,
  buildWarehouseStorePriceSyncPayload,
  getWarehouseStorePriceSyncScopeSummary,
  isWarehouseStorePriceSyncTerminalStatus,
  normalizeWarehouseStorePriceSyncErrors,
  summarizeWarehouseStorePriceSyncResult,
  validateWarehouseStorePriceSyncInput,
} from './warehouseStorePriceSync.logic'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${message}。Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

async function main() {
  assertDeepEqual(
    buildWarehouseStorePriceSyncPayload({
      productCodes: [' P001 ', 'P001', 'P002'],
      targetStoreCodes: ['S01', 'S02'],
    }),
    {
      productCodes: ['P001', 'P002'],
      applyToAllProducts: false,
      targetStoreCodes: ['S01', 'S02'],
      syncToHq: false,
    },
    '选中模式 payload 应只提交去重后的 ProductCode，且 HQ 默认关闭',
  )

  assertDeepEqual(
    buildWarehouseStorePriceSyncPayload({ productCodes: [], targetStoreCodes: ['S01'], syncToHq: true }),
    {
      productCodes: [],
      applyToAllProducts: true,
      targetStoreCodes: ['S01'],
      syncToHq: true,
    },
    '无选择时 payload 必须切换为全量模式并允许明确开启 HQ',
  )

  assertEqual(
    validateWarehouseStorePriceSyncInput({ productCodes: ['P001'], targetStoreCodes: [] }),
    'targetStoreRequired',
    '没有目标分店时必须阻止提交',
  )
  assertEqual(
    validateWarehouseStorePriceSyncInput({ productCodes: [], targetStoreCodes: ['S01', 'S02'] }),
    null,
    '全量模式选择多个目标分店时应通过校验',
  )

  assertDeepEqual(
    getWarehouseStorePriceSyncScopeSummary({
      productCodes: ['P001', 'P002'],
      allProductCount: 999,
      targetStoreCount: 3,
    }),
    { isFullScope: false, productCount: 2, maxWriteCount: 6 },
    '选中模式最大写入量应为选中商品数乘目标分店数',
  )
  assertDeepEqual(
    getWarehouseStorePriceSyncScopeSummary({
      productCodes: [],
      allProductCount: 120,
      targetStoreCount: 2,
    }),
    { isFullScope: true, productCount: 120, maxWriteCount: 240 },
    '全量模式应使用无筛选商品总数计算最大写入量',
  )

  assertDeepEqual(
    WAREHOUSE_STORE_PRICE_SYNC_FIELD_MAPPINGS.map(({ source, target, fixedValue }) => ({ source, target, fixedValue })),
    [
      { source: 'importPrice', target: 'purchasePrice', fixedValue: undefined },
      { source: 'retailPrice', target: 'storeRetailPrice', fixedValue: undefined },
      { source: 'discountRate', target: 'discountRate', fixedValue: 0 },
      { source: 'autoPricing', target: 'autoPricing', fixedValue: false },
    ],
    '界面固定映射必须保持进口价/零售价/折扣率/自动定价四项语义',
  )

  for (const status of ['Succeeded', 'PartiallySucceeded', 'Failed'] as const) {
    assert(isWarehouseStorePriceSyncTerminalStatus(status), `${status} 必须是终止状态`)
  }
  for (const status of ['Pending', 'Running'] as const) {
    assert(!isWarehouseStorePriceSyncTerminalStatus(status), `${status} 必须继续轮询`)
  }

  assertDeepEqual(
    normalizeWarehouseStorePriceSyncErrors([
      'legacy error text',
      {
        stage: 'HQ',
        productCode: 'P009',
        storeCode: 'S02',
        code: 'HQ_SYNC_FAILED',
        message: 'HQ 价格表更新失败',
      },
    ]),
    [
      { message: 'legacy error text' },
      {
        stage: 'HQ',
        productCode: 'P009',
        storeCode: 'S02',
        code: 'HQ_SYNC_FAILED',
        message: 'HQ 价格表更新失败',
      },
    ],
    'errors 归一化应兼容历史字符串和后端结构化对象',
  )

  assertDeepEqual(
    summarizeWarehouseStorePriceSyncResult({
      status: 'PartiallySucceeded',
      result: {
        requestedProductCount: 10,
        eligibleProductCount: 9,
        skippedProductCount: 1,
        targetStoreCount: 2,
        localCreatedCount: 3,
        localUpdatedCount: 4,
        hqCreatedCount: 5,
        hqUpdatedCount: 6,
        hqProvisionedProductCount: 7,
        errors: [
          'legacy error text',
          { productCode: 'P009', storeCode: 'S02', message: '价格更新失败' },
          { productCode: 'P010', code: 'MISSING_PRICE', message: '缺少 ImportPrice' },
        ],
      },
    }),
    {
      status: 'PartiallySucceeded',
      requestedProductCount: 10,
      eligibleProductCount: 9,
      skippedProductCount: 1,
      targetStoreCount: 2,
      localCreatedCount: 3,
      localUpdatedCount: 4,
      hqCreatedCount: 5,
      hqUpdatedCount: 6,
      hqProvisionedProductCount: 7,
      failedCount: 2,
      errors: [
        { message: 'legacy error text' },
        { productCode: 'P009', storeCode: 'S02', message: '价格更新失败' },
        { productCode: 'P010', code: 'MISSING_PRICE', message: '缺少 ImportPrice' },
      ],
    },
    '结果摘要应完整呈现本地/HQ新增更新、跳过、失败数量和 errors',
  )

  console.log('warehouseStorePriceSync.logic.test: ok')
}

await main()
