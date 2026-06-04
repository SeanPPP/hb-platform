import {
  buildSupplierImageUrl,
  getDefaultSupplierImageTemplate,
  validateSupplierImageTemplate,
} from './productImageTemplate'
import type { LocalSupplierDto } from '../../../types/localSupplier'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

const datsSupplier: LocalSupplierDto = {
  guid: 'supplier-dats',
  localSupplierCode: 'dats',
  name: 'Dats',
  status: 1,
  imageBaseUrl: 'https://saved.example.com/{itemNumber}.jpg',
}

assertEqual(
  getDefaultSupplierImageTemplate(datsSupplier, 'supplier'),
  'https://saved.example.com/{itemNumber}.jpg',
  '供应商已保存图片基础 URL 时应优先使用保存值',
)

assertEqual(
  getDefaultSupplierImageTemplate({ ...datsSupplier, imageBaseUrl: '' }, 'supplier'),
  'https://www.dats.com.au/images/ProductImages/500/{itemNumber}.jpg',
  '供应商图片基础 URL 为空时应按供应商代码使用预设模板',
)

assertEqual(
  getDefaultSupplierImageTemplate({ ...datsSupplier, imageBaseUrl: '' }, 'cos'),
  'https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/{supplierCode}/{itemNumber}.jpg',
  'COS 模式应使用供应商代码加货号模板',
)

assertEqual(
  buildSupplierImageUrl(
    'https://cdn.example.com/{supplierCode}/{itemNumber}.jpg',
    { localSupplierCode: 'dats', itemNumber: '72653' },
  ),
  'https://cdn.example.com/dats/72653.jpg',
  '图片模板应替换供应商代码和货号',
)

assertEqual(
  validateSupplierImageTemplate('https://cdn.example.com/{itemNumber}.jpg'),
  undefined,
  '包含货号占位符的模板应通过校验',
)

assert(
  validateSupplierImageTemplate('https://cdn.example.com/static.jpg')?.includes('{itemNumber}'),
  '缺少货号占位符时应返回校验错误',
)

assert(
  validateSupplierImageTemplate(`https://cdn.example.com/${'a'.repeat(180)}/{itemNumber}.jpg`)?.includes('200'),
  '图片基础 URL 超过商品图片列长度时应返回校验错误',
)

console.log('productImageTemplate.test: ok')
