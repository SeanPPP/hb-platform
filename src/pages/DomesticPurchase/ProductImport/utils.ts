import type { ProductImportItem, Statistics, DuplicateGroup } from './types'

export function generateImageUrl(productCode: string): string {
  if (!productCode) return ''
  return `https://hbimgoss.hbupplier.com/productimg/${productCode}.jpg`
}

export function createEmptyProduct(): ProductImportItem {
  return {
    id: `row_${Date.now()}_${Math.random().toString(36).substring(2, 9)}`,
    selected: false,
    imageUrl: '',
    customImage: false,
    imageLoadStatus: 'loading',
    newProduct: { quantity: 1, productCode: '', productName: '' },
    status: 'unchanged',
    isDuplicate: false,
    calculated: { totalProducts: 0, totalVolume: 0 },
  }
}

export function detectDuplicates(products: ProductImportItem[]): DuplicateGroup[] {
  const codeMap = new Map<string, ProductImportItem[]>()
  products.forEach((p) => {
    const code = p.newProduct.productCode?.trim()
    if (!code) return
    const existing = codeMap.get(code) || []
    existing.push(p)
    codeMap.set(code, existing)
  })
  const groups: DuplicateGroup[] = []
  codeMap.forEach((items, productCode) => {
    if (items.length > 1) {
      groups.push({
        productCode,
        count: items.length,
        rows: items.map((_, i) => i),
        items,
        merged: {
          quantity: items.reduce((sum, item) => sum + (item.newProduct.quantity || 0), 0),
          casePackQuantity: items[0]?.newProduct.casePackQuantity || 0,
          volume: items.reduce((sum, item) => sum + ((item.newProduct.volume || 0) * (item.newProduct.quantity || 0)), 0),
        },
      })
    }
  })
  return groups
}

export function mergeDuplicateProducts(products: ProductImportItem[]): ProductImportItem[] {
  const seen = new Map<string, ProductImportItem>()
  const result: ProductImportItem[] = []
  products.forEach((p) => {
    const code = p.newProduct.productCode?.trim()
    if (!code) { result.push(p); return }
    const existing = seen.get(code)
    if (existing) {
      existing.newProduct.quantity += p.newProduct.quantity || 0
      existing.mergedFrom = (existing.mergedFrom || 1) + 1
    } else {
      seen.set(code, { ...p })
      result.push(seen.get(code)!)
    }
  })
  return result
}

export function calculateStatistics(products: ProductImportItem[], selectedIds: string[]): Statistics {
  const selectedProducts = products.filter((p) => selectedIds.includes(p.id))
  return {
    total: products.length,
    duplicateCount: products.filter((p) => p.isDuplicate).length,
    newCount: products.filter((p) => p.status === 'new').length,
    updateCount: products.filter((p) => p.status === 'updated').length,
    unchangedCount: products.filter((p) => p.status === 'unchanged').length,
    errorCount: products.filter((p) => p.status === 'error').length,
    dbDuplicateCount: products.filter((p) => p.status === 'dbDuplicate').length,
    selectedCount: selectedProducts.length,
    totalQuantity: selectedProducts.reduce((sum, p) => sum + (p.newProduct.quantity || 0), 0),
    totalProducts: selectedProducts.reduce((sum, p) => sum + (p.newProduct.quantity || 0), 0),
    totalVolume: selectedProducts.reduce((sum, p) => sum + ((p.newProduct.volume || 0) * (p.newProduct.quantity || 0)), 0),
  }
}

export function updateCalculatedFields(product: ProductImportItem): ProductImportItem {
  return {
    ...product,
    calculated: {
      totalProducts: product.newProduct.quantity || 0,
      totalVolume: (product.newProduct.volume || 0) * (product.newProduct.quantity || 0),
    },
  }
}

export function validateProduct(product: ProductImportItem, mode: string): { [field: string]: string } {
  const errors: { [field: string]: string } = {}
  if (!product.newProduct.productCode?.trim()) errors.productCode = '货号不能为空'
  if (mode === 'import' && !product.newProduct.productName?.trim()) errors.productName = '商品名称不能为空'
  return errors
}
