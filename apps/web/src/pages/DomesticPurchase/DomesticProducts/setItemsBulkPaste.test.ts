import {
  applySetItemColumnPaste,
  buildSetProductPriceSyncPayload,
  calculateSetItemPriceTotals,
  parseSetItemPrice,
} from './setItemsBulkPaste'
import { ProductType, type DomesticProductItem, type DomesticProductSetItem } from '../../../types/domesticProduct'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

const baseItems: DomesticProductSetItem[] = [
  { id: 'row-1', productName: '旧商品1', setProductNo: 'NO1', setBarcode: 'BC1', domesticPrice: 1 },
  { id: 'row-2', productName: '旧商品2', setProductNo: 'NO2', setBarcode: 'BC2', oemPrice: 2 },
]

const productNameResult = applySetItemColumnPaste({
  items: baseItems,
  startRowId: 'row-2',
  field: 'productName',
  clipboardText: '新品A\n新品B\n新品C',
  createId: (index) => `temp-${index}`,
})

assertDeepEqual(
  productNameResult.items,
  [
    { id: 'row-1', productName: '旧商品1', setProductNo: 'NO1', setBarcode: 'BC1', domesticPrice: 1 },
    { id: 'row-2', productName: '新品A', setProductNo: 'NO2', setBarcode: 'BC2', oemPrice: 2 },
    { id: 'temp-2', productName: '新品B' },
    { id: 'temp-3', productName: '新品C' },
  ],
  '粘贴商品名称应从当前行写入，并在行数不足时新增空子项',
)
assertEqual(productNameResult.appliedCount, 3, '商品名称粘贴应统计成功写入数量')
assertEqual(productNameResult.skippedCount, 0, '商品名称粘贴不应跳过有效内容')

const emptyColumnResult = applySetItemColumnPaste({
  items: [],
  field: 'productName',
  clipboardText: '空表首行\n空表第二行',
  createId: (index) => `empty-${index}`,
})

assertDeepEqual(
  emptyColumnResult.items,
  [
    { id: 'empty-0', productName: '空表首行' },
    { id: 'empty-1', productName: '空表第二行' },
  ],
  '空表点击列头粘贴时应从第一行开始自动创建子项',
)

const skipBlankRowsResult = applySetItemColumnPaste({
  items: [],
  field: 'productName',
  clipboardText: '第一行\n\n\t\n第二行\n',
  createId: (index) => `skip-blank-${index}`,
})

assertDeepEqual(
  skipBlankRowsResult.items,
  [
    { id: 'skip-blank-0', productName: '第一行' },
    { id: 'skip-blank-1' },
    { id: 'skip-blank-2' },
    { id: 'skip-blank-3', productName: '第二行' },
  ],
  '粘贴时应保留 Excel 空单元格行位，避免后续数据错行',
)

assertEqual(skipBlankRowsResult.appliedCount, 2, '空单元格不应计入成功写入数量')

const priceResult = applySetItemColumnPaste({
  items: baseItems,
  startRowId: 'row-1',
  field: 'domesticPrice',
  clipboardText: '￥1,234.50\nabc\n$6',
  createId: (index) => `price-${index}`,
})

assertDeepEqual(
  priceResult.items,
  [
    { id: 'row-1', productName: '旧商品1', setProductNo: 'NO1', setBarcode: 'BC1', domesticPrice: 1234.5 },
    { id: 'row-2', productName: '旧商品2', setProductNo: 'NO2', setBarcode: 'BC2', oemPrice: 2 },
    { id: 'price-2', domesticPrice: 6 },
  ],
  '价格粘贴应清理货币符号和千分位，无效值跳过但保留行位',
)
assertEqual(priceResult.appliedCount, 2, '价格粘贴应只统计有效价格')
assertEqual(priceResult.skippedCount, 1, '价格粘贴应统计无效价格')

assertEqual(parseSetItemPrice('￥1,200.30'), 1200.3, '价格解析应移除人民币符号和千分位')
assertEqual(parseSetItemPrice('USD 8.5'), 8.5, '价格解析应移除字母货币标识')
assertEqual(parseSetItemPrice('abc'), undefined, '价格解析遇到无效内容应返回 undefined')

assertDeepEqual(
  calculateSetItemPriceTotals([
    { id: 'a', domesticPrice: 1.1 },
    { id: 'b', domesticPrice: 2.2, oemPrice: 0 },
    { id: 'c' },
  ]),
  {
    hasDomesticPrice: true,
    domesticPriceTotal: 3.3,
    hasOemPrice: true,
    oemPriceTotal: 0,
  },
  '合计应只在存在非空价格时返回对应合计，0 也应视为非空价格',
)

assertDeepEqual(
  calculateSetItemPriceTotals([{ id: 'a' }, { id: 'b' }]),
  {
    hasDomesticPrice: false,
    domesticPriceTotal: undefined,
    hasOemPrice: false,
    oemPriceTotal: undefined,
  },
  '全部价格为空时不应生成主码覆盖值',
)

const setProduct: DomesticProductItem = {
  id: 'set-1',
  supplierCode: 'SUP',
  supplierName: '供应商',
  name: '主套装',
  nameEn: 'Main Set',
  itemNumber: 'SET001',
  barcode: 'BAR001',
  specs: '规格',
  productType: ProductType.SET,
  domesticPrice: 9,
  labelPrice: 19,
  importPrice: 5,
  packingQty: 10,
  volume: 1.25,
  middlePackQty: 2,
  packingSize: '10x10',
  material: '纸',
  remark: '备注',
  productImage: 'https://example.com/a.png',
  isActive: true,
  createdAt: '2026-01-01T00:00:00.000Z',
}

assertDeepEqual(
  buildSetProductPriceSyncPayload(setProduct, {
    hasDomesticPrice: true,
    domesticPriceTotal: 12,
    hasOemPrice: false,
    oemPriceTotal: undefined,
  }),
  {
    productName: '主套装',
    englishProductName: 'Main Set',
    barcode: 'BAR001',
    productSpecification: '规格',
    productType: ProductType.SET,
    domesticPrice: 12,
    oemPrice: 19,
    importPrice: 5,
    packingQuantity: 10,
    unitVolume: 1.25,
    middlePackQuantity: 2,
    packingSize: '10x10',
    material: '纸',
    remarks: '备注',
    productImage: 'https://example.com/a.png',
    isActive: true,
  },
  '仅同步国内价时应保留主码当前零售价，避免覆盖为空',
)

assertDeepEqual(
  buildSetProductPriceSyncPayload(setProduct, {
    hasDomesticPrice: false,
    domesticPriceTotal: undefined,
    hasOemPrice: true,
    oemPriceTotal: 23,
  }),
  {
    productName: '主套装',
    englishProductName: 'Main Set',
    barcode: 'BAR001',
    productSpecification: '规格',
    productType: ProductType.SET,
    domesticPrice: 9,
    oemPrice: 23,
    importPrice: 5,
    packingQuantity: 10,
    unitVolume: 1.25,
    middlePackQuantity: 2,
    packingSize: '10x10',
    material: '纸',
    remarks: '备注',
    productImage: 'https://example.com/a.png',
    isActive: true,
  },
  '仅同步零售价时应保留主码当前国内价，避免覆盖为空',
)

assertEqual(
  buildSetProductPriceSyncPayload(setProduct, {
    hasDomesticPrice: false,
    domesticPriceTotal: undefined,
    hasOemPrice: false,
    oemPriceTotal: undefined,
  }),
  undefined,
  '子项价格全部为空时不应生成主码更新 payload',
)
