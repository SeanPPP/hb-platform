import {
  buildAssignContainerItems,
  findInvalidAssignContainerItems,
  stripAssignContainerItemsForRequest,
  summarizeAssignProductsResult,
} from './utils'
import type { ProductImportItem } from './types'

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

function createProduct(overrides: Partial<ProductImportItem>): ProductImportItem {
  return {
    id: 'row-1',
    selected: true,
    imageUrl: '',
    customImage: false,
    imageLoadStatus: 'success',
    newProduct: {
      quantity: 1,
      productCode: 'HB001',
      productName: '测试商品',
    },
    status: 'unchanged',
    isDuplicate: false,
    calculated: { totalProducts: 1, totalVolume: 0 },
    ...overrides,
  }
}

assertDeepEqual(
  buildAssignContainerItems([
    createProduct({
      newProduct: {
        quantity: 17,
        productCode: 'JM-018',
        productName: '1pc Baby Muffin',
        domesticPrice: 11.6,
        oemPrice: 6.99,
        casePackQuantity: 48,
        volume: 0.118,
      },
      matchedProduct: { productCode: 'P-JM-018' },
    }),
  ], '补价格'),
  [
    {
      hbProductNo: 'JM-018',
      productCode: 'P-JM-018',
      quantity: 17,
      packingQuantity: 48,
      unitVolume: 0.118,
      domesticPrice: 11.6,
      oemPrice: 6.99,
      notes: '补价格',
    },
  ],
  '发送货柜本地校验对象应保留导入行国内价格和贴牌价格',
)

assertDeepEqual(
  stripAssignContainerItemsForRequest(buildAssignContainerItems([
    createProduct({
      newProduct: {
        quantity: 17,
        productCode: 'JM-018',
        productName: '1pc Baby Muffin',
        domesticPrice: 11.6,
        oemPrice: 6.99,
        casePackQuantity: 48,
        volume: 0.118,
      },
      matchedProduct: { productCode: 'P-JM-018' },
    }),
  ], '补价格')),
  [
    {
      hbProductNo: 'JM-018',
      productCode: 'P-JM-018',
      quantity: 17,
      packingQuantity: 48,
      unitVolume: 0.118,
      notes: '补价格',
    },
  ],
  '发送 assign-products 前应移除仅用于本地校验的价格字段，保持接口请求结构不变',
)

assertDeepEqual(
  buildAssignContainerItems([
    createProduct({
      newProduct: {
        quantity: 2,
        productCode: 'HB002',
        productName: '旧价兜底商品',
      },
      matchedProduct: {
        productCode: 'P-HB002',
        domesticPrice: 3.2,
        oemPrice: 1.8,
        packingQuantity: 24,
        unitVolume: 0.086,
      },
    }),
  ], ''),
  [
    {
      hbProductNo: 'HB002',
      productCode: 'P-HB002',
      quantity: 2,
      packingQuantity: 24,
      unitVolume: 0.086,
      domesticPrice: 3.2,
      oemPrice: 1.8,
      notes: '',
    },
  ],
  '发送货柜业务字段缺失时应使用匹配商品字段兜底',
)

assertDeepEqual(
  buildAssignContainerItems([
    createProduct({
      newProduct: {
        quantity: 5,
        productCode: 'HB004',
        productName: '零装箱数托底商品',
        domesticPrice: 0,
        oemPrice: 0,
        casePackQuantity: 0,
        volume: 0,
      },
      matchedProduct: {
        productCode: 'P-HB004',
        domesticPrice: 9.9,
        oemPrice: 8.8,
        packingQuantity: 36,
        unitVolume: 0.125,
      },
    }),
  ], undefined),
  [
    {
      hbProductNo: 'HB004',
      productCode: 'P-HB004',
      quantity: 5,
      packingQuantity: 36,
      unitVolume: 0.125,
      domesticPrice: 0,
      oemPrice: 0,
      notes: undefined,
    },
  ],
  '发送货柜装箱数和体积为 0 时应托底，价格为 0 时应保留当前值',
)

assertDeepEqual(
  buildAssignContainerItems([
    createProduct({
      newProduct: {
        quantity: 3,
        productCode: 'HB003',
        productName: '无价格商品',
      },
      matchedProduct: { productCode: 'P-HB003' },
    }),
  ], undefined),
  [
    {
      hbProductNo: 'HB003',
      productCode: 'P-HB003',
      quantity: 3,
      packingQuantity: undefined,
      unitVolume: undefined,
      domesticPrice: undefined,
      oemPrice: undefined,
      notes: undefined,
    },
  ],
  '发送货柜不应把缺失价格转成 0',
)

assertDeepEqual(
  findInvalidAssignContainerItems(buildAssignContainerItems([
    createProduct({
      newProduct: {
        quantity: 0,
        productCode: 'HB005',
        productName: '业务字段缺失商品',
        domesticPrice: 0,
        casePackQuantity: 0,
        volume: 0,
      },
      matchedProduct: {
        productCode: 'P-HB005',
      },
    }),
  ])),
  [
    {
      hbProductNo: 'HB005',
      productCode: 'P-HB005',
      fields: ['件数', '国内价格', '装箱数', '体积'],
      reasons: [],
    },
  ],
  '发送货柜应拦截件数、国内价格、装箱数、体积为空或为 0 的商品',
)

assertDeepEqual(
  findInvalidAssignContainerItems(buildAssignContainerItems([
    createProduct({
      newProduct: {
        quantity: 4,
        productCode: 'HB006',
        productName: '旧字段托底通过商品',
      },
      matchedProduct: {
        productCode: 'P-HB006',
        domesticPrice: 6.6,
        packingQuantity: 12,
        unitVolume: 0.08,
      },
    }),
  ])),
  [],
  '发送货柜应允许业务字段通过旧商品托底后发送',
)

assertDeepEqual(
  findInvalidAssignContainerItems(buildAssignContainerItems([
    createProduct({
      newProduct: {
        quantity: 6,
        productCode: 'HB007',
        productName: '未匹配本地编码商品',
        domesticPrice: 6.8,
        casePackQuantity: 20,
        volume: 0.09,
      },
      matchedProduct: undefined,
    }),
  ])),
  [
    {
      hbProductNo: 'HB007',
      productCode: undefined,
      fields: ['本地商品编码'],
      reasons: ['未匹配本地商品编码'],
    },
  ],
  '发送货柜应拦截未匹配本地商品编码的商品',
)

assertDeepEqual(
  summarizeAssignProductsResult(
    {
      success: true,
      data: {
        created: 2,
        updated: 1,
        failed: [],
      },
    },
    buildAssignContainerItems([
      createProduct({
        newProduct: {
          quantity: 2,
          productCode: 'HB008',
          productName: '全成功商品',
          domesticPrice: 9.1,
          casePackQuantity: 18,
          volume: 0.05,
        },
        matchedProduct: {
          productCode: 'P-HB008',
        },
      }),
    ]),
  ),
  {
    status: 'success',
    success: true,
    message: undefined,
    created: 2,
    updated: 1,
    succeeded: 3,
    failedCount: 0,
    failed: [],
  },
  'assign-products 全成功时应归纳为 success',
)

assertDeepEqual(
  summarizeAssignProductsResult(
    {
      success: true,
      data: {
        created: 1,
        updated: 1,
        failed: [
          { productCode: 'P-HB009', error: '国内价格缺失' },
        ],
      },
    },
    buildAssignContainerItems([
      createProduct({
        id: 'row-9',
        newProduct: {
          quantity: 4,
          productCode: 'HB009',
          productName: '部分失败商品',
          domesticPrice: 10.5,
          casePackQuantity: 16,
          volume: 0.07,
        },
        matchedProduct: {
          productCode: 'P-HB009',
        },
      }),
    ]),
  ),
  {
    status: 'partial',
    success: true,
    message: undefined,
    created: 1,
    updated: 1,
    succeeded: 2,
    failedCount: 1,
    failed: [
      {
        hbProductNo: 'HB009',
        productCode: 'P-HB009',
        reason: '国内价格缺失',
      },
    ],
  },
  'assign-products 部分失败时应保留成功统计并带出失败明细',
)

assertDeepEqual(
  summarizeAssignProductsResult(
    {
      success: true,
      data: {
        created: 0,
        updated: 0,
        failed: [
          { productCode: 'P-HB010', error: '商品不存在' },
        ],
      },
    },
    buildAssignContainerItems([
      createProduct({
        id: 'row-10',
        newProduct: {
          quantity: 5,
          productCode: 'HB010',
          productName: '全失败商品',
          domesticPrice: 8.8,
          casePackQuantity: 30,
          volume: 0.06,
        },
        matchedProduct: {
          productCode: 'P-HB010',
        },
      }),
    ]),
  ),
  {
    status: 'failed',
    success: false,
    message: undefined,
    created: 0,
    updated: 0,
    succeeded: 0,
    failedCount: 1,
    failed: [
      {
        hbProductNo: 'HB010',
        productCode: 'P-HB010',
        reason: '商品不存在',
      },
    ],
  },
  'assign-products 全失败时不应被视为成功',
)

assertDeepEqual(
  summarizeAssignProductsResult(
    {
      success: false,
      message: '发送失败',
      data: {
        created: 0,
        updated: 0,
        failed: [],
      },
    },
    [],
  ),
  {
    status: 'apiError',
    success: false,
    message: '发送失败',
    created: 0,
    updated: 0,
    succeeded: 0,
    failedCount: 0,
    failed: [],
  },
  'assign-products 接口级失败时应归纳为 apiError',
)
