import type { LocalSupplierDto } from '../../../types/localSupplier'

export type SupplierImageMode = 'cos' | 'supplier'

export const COS_SUPPLIER_IMAGE_TEMPLATE =
  'https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/{supplierCode}/{itemNumber}.jpg'

export const SUPPLIER_IMAGE_TEMPLATE_PRESETS: Record<string, string> = {
  malmar: 'https://www.malmar.com.au/img/products/thumb/{itemNumber}.jpg',
  yatsal: 'https://www.yatsal.com.au/Images/ProductImages/500/{itemNumber}.jpg',
  dats: 'https://www.dats.com.au/images/ProductImages/500/{itemNumber}.jpg',
}

export const PRODUCT_IMAGE_URL_MAX_LENGTH = 200

export function getDefaultSupplierImageTemplate(
  supplier: Pick<LocalSupplierDto, 'localSupplierCode' | 'imageBaseUrl'> | undefined,
  mode: SupplierImageMode,
) {
  if (mode === 'cos') {
    return COS_SUPPLIER_IMAGE_TEMPLATE
  }

  const savedTemplate = supplier?.imageBaseUrl?.trim()
  if (savedTemplate) {
    return savedTemplate
  }

  const supplierCode = supplier?.localSupplierCode?.trim().toLowerCase()
  return supplierCode ? SUPPLIER_IMAGE_TEMPLATE_PRESETS[supplierCode] ?? '' : ''
}

export function buildSupplierImageUrl(
  template: string,
  values: { localSupplierCode: string; itemNumber: string },
) {
  return template
    .split('{supplierCode}')
    .join(encodeURIComponent(values.localSupplierCode))
    .split('{itemNumber}')
    .join(encodeURIComponent(values.itemNumber))
}

export function validateSupplierImageTemplate(template: string) {
  const value = template.trim()
  if (!value) {
    return '请输入图片基础 URL'
  }
  if (!value.includes('{itemNumber}')) {
    return '图片基础 URL 必须包含 {itemNumber}'
  }
  if (value.length > PRODUCT_IMAGE_URL_MAX_LENGTH) {
    return `图片基础 URL 不能超过 ${PRODUCT_IMAGE_URL_MAX_LENGTH} 个字符`
  }
  return undefined
}
