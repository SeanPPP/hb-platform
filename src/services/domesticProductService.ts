import type { ApiResponse } from '../types/api'
import type {
  CreateDomesticProductPayload,
  DomesticProductGridQuery,
  DomesticProductGridResult,
  DomesticProductItem,
  DomesticProductSetItem,
  ProductType,
  SupplierOption,
  UpdateDomesticProductPayload,
} from '../types/domesticProduct'
import request from '../utils/request'

const API_BASE = '/api/react/v1/domestic-products'

interface GridApiResult {
  items?: unknown[]
  total?: number
}

function toNumber(value: unknown) {
  if (typeof value === 'number') {
    return value
  }

  if (typeof value === 'string' && value.trim()) {
    const parsed = Number(value)
    return Number.isNaN(parsed) ? undefined : parsed
  }

  return undefined
}

function toBoolean(value: unknown, fallback = true) {
  if (typeof value === 'boolean') {
    return value
  }

  if (typeof value === 'string') {
    if (value.toLowerCase() === 'true') {
      return true
    }
    if (value.toLowerCase() === 'false') {
      return false
    }
  }

  return fallback
}

function transformSetItem(raw: Record<string, unknown>): DomesticProductSetItem {
  return {
    id: String(
      raw.setProductCode ??
        raw.SetProductCode ??
        raw.productCode ??
        raw.ProductCode ??
        `temp_${Date.now()}_${Math.random()}`,
    ),
    setProductCode: String(raw.setProductCode ?? raw.SetProductCode ?? '') || undefined,
    productCode: String(raw.productCode ?? raw.ProductCode ?? '') || undefined,
    productName: String(raw.productName ?? raw.ProductName ?? '') || undefined,
    setProductNo: String(raw.setProductNo ?? raw.SetProductNo ?? raw.productNo ?? raw.ProductNo ?? '') || undefined,
    setBarcode: String(raw.setBarcode ?? raw.SetBarcode ?? '') || undefined,
    domesticPrice: toNumber(raw.domesticPrice ?? raw.DomesticPrice),
    importPrice: toNumber(raw.importPrice ?? raw.ImportPrice),
    oemPrice: toNumber(raw.oemPrice ?? raw.OEMPrice),
  }
}

function transformProduct(raw: Record<string, unknown>): DomesticProductItem {
  const setProducts = raw.setProducts ?? raw.SetProducts ?? raw.domesticSetProducts ?? raw.DomesticSetProducts
  const productType = toNumber(raw.productType ?? raw.ProductType) ?? 0

  return {
    id: String(raw.productCode ?? raw.ProductCode ?? raw.id ?? `temp_${Date.now()}_${Math.random()}`),
    supplierCode: String(raw.supplierCode ?? raw.SupplierCode ?? ''),
    supplierName: String(raw.supplierName ?? raw.SupplierName ?? ''),
    name: String(raw.productName ?? raw.ProductName ?? raw.name ?? ''),
    nameEn: String(raw.englishProductName ?? raw.EnglishProductName ?? raw.nameEn ?? '') || undefined,
    itemNumber: String(raw.hbProductNo ?? raw.HBProductNo ?? raw.itemNumber ?? ''),
    barcode: String(raw.barcode ?? raw.Barcode ?? '') || undefined,
    specs: String(raw.productSpecification ?? raw.ProductSpecification ?? raw.specs ?? '') || undefined,
    productImage: String(raw.productImage ?? raw.ProductImage ?? '') || undefined,
    productType: productType as ProductType,
    domesticPrice: toNumber(raw.domesticPrice ?? raw.DomesticPrice),
    labelPrice: toNumber(raw.oemPrice ?? raw.OEMPrice ?? raw.labelPrice),
    importPrice: toNumber(raw.importPrice ?? raw.ImportPrice),
    packingQty: toNumber(raw.packingQuantity ?? raw.PackingQuantity ?? raw.packingQty),
    volume: toNumber(raw.unitVolume ?? raw.UnitVolume ?? raw.volume),
    middlePackQty: toNumber(raw.middlePackQuantity ?? raw.MiddlePackQuantity ?? raw.middlePackQty),
    packingSize: String(raw.packingSize ?? raw.PackingSize ?? '') || undefined,
    material: String(raw.material ?? raw.Material ?? '') || undefined,
    remark: String(raw.remarks ?? raw.Remarks ?? raw.remark ?? '') || undefined,
    isActive: toBoolean(raw.isActive ?? raw.IsActive),
    createdAt: String(raw.createdAt ?? raw.CreatedAt ?? new Date().toISOString()),
    updatedAt: String(raw.updatedAt ?? raw.UpdatedAt ?? '') || undefined,
    createdBy: String(raw.createdBy ?? raw.CreatedBy ?? '') || undefined,
    updatedBy: String(raw.updatedBy ?? raw.UpdatedBy ?? '') || undefined,
    setItems: Array.isArray(setProducts)
      ? setProducts.filter((item): item is Record<string, unknown> => !!item && typeof item === 'object').map(transformSetItem)
      : [],
  }
}

function createFilterModel(query: DomesticProductGridQuery) {
  const filterModel: Record<string, unknown> = {}

  if (query.supplierCode) {
    filterModel.supplierCode = {
      FilterType: 'text',
      Type: 'contains',
      Filter: query.supplierCode,
    }
  }

  if (query.productType !== undefined) {
    filterModel.productType = {
      FilterType: 'set',
      Values: [String(query.productType)],
    }
  }

  if (query.isActive !== undefined) {
    filterModel.isActive = {
      FilterType: 'set',
      Values: [String(query.isActive)],
    }
  }

  return Object.keys(filterModel).length ? filterModel : null
}

function createSortModel(query: DomesticProductGridQuery) {
  if (!query.sortField) {
    return [{ colId: 'createdAt', sort: 'desc' }]
  }

  return [
    {
      colId: query.sortField,
      sort: query.sortOrder === 'ascend' ? 'asc' : 'desc',
    },
  ]
}

function normalizeGridResponse(payload: unknown, page: number, pageSize: number): DomesticProductGridResult {
  const result = payload as
    | {
        success?: boolean
        Success?: boolean
        data?: GridApiResult
        Data?: { Items?: unknown[]; Total?: number }
      }
    | undefined

  const items =
    result?.data?.items ??
    result?.Data?.Items ??
    []
  const total =
    result?.data?.total ??
    result?.Data?.Total ??
    0

  const transformedItems = Array.isArray(items)
    ? items
        .filter((item): item is Record<string, unknown> => !!item && typeof item === 'object')
        .map(transformProduct)
        .map((item, index) => ({
          ...item,
          rowNumber: (page - 1) * pageSize + index + 1,
        }))
    : []

  return {
    items: transformedItems,
    total,
    page,
    pageSize,
  }
}

export async function getDomesticProductsGrid(query: DomesticProductGridQuery): Promise<DomesticProductGridResult> {
  const payload = await request<unknown>(`${API_BASE}/grid`, {
    method: 'POST',
    data: {
      StartRow: (query.page - 1) * query.pageSize,
      EndRow: query.page * query.pageSize,
      PageSize: query.pageSize,
      GlobalSearch: query.searchText || null,
      FilterModel: createFilterModel(query),
      SortModel: createSortModel(query),
    },
  })

  return normalizeGridResponse(payload, query.page, query.pageSize)
}

export async function createDomesticProduct(data: CreateDomesticProductPayload): Promise<DomesticProductItem> {
  const payload = await request<ApiResponse<Record<string, unknown>>>(API_BASE, {
    method: 'POST',
    data: {
      SupplierCode: data.supplierCode,
      ProductName: data.productName,
      EnglishProductName: data.englishProductName,
      HBProductNo: data.hbProductNo,
      Barcode: data.barcode,
      ProductSpecification: data.productSpecification,
      ProductType: data.productType,
      DomesticPrice: data.domesticPrice,
      OEMPrice: data.oemPrice,
      ImportPrice: data.importPrice,
      PackingQuantity: data.packingQuantity,
      UnitVolume: data.unitVolume,
      MiddlePackQuantity: data.middlePackQuantity,
      PackingSize: data.packingSize,
      Material: data.material,
      Remarks: data.remarks,
      ProductImage: data.productImage,
      IsActive: data.isActive ?? true,
    },
  })

  return transformProduct((payload.data ?? payload) as Record<string, unknown>)
}

export async function updateDomesticProduct(
  productCode: string,
  data: UpdateDomesticProductPayload,
): Promise<DomesticProductItem> {
  const payload = await request<ApiResponse<Record<string, unknown>>>(`${API_BASE}/${productCode}`, {
    method: 'PUT',
    data: {
      ProductName: data.productName,
      EnglishProductName: data.englishProductName,
      Barcode: data.barcode,
      ProductSpecification: data.productSpecification,
      ProductType: data.productType,
      DomesticPrice: data.domesticPrice,
      OEMPrice: data.oemPrice,
      ImportPrice: data.importPrice,
      PackingQuantity: data.packingQuantity,
      UnitVolume: data.unitVolume,
      MiddlePackQuantity: data.middlePackQuantity,
      PackingSize: data.packingSize,
      Material: data.material,
      Remarks: data.remarks,
      ProductImage: data.productImage,
      IsActive: data.isActive ?? true,
    },
  })

  return transformProduct((payload.data ?? payload) as Record<string, unknown>)
}

export async function batchDeleteDomesticProducts(productCodes: string[]): Promise<void> {
  await request<unknown>(`${API_BASE}/batch-delete`, {
    method: 'DELETE',
    data: { productCodes },
  })
}

export async function getSupplierOptions(): Promise<SupplierOption[]> {
  const payload = await request<ApiResponse<Array<Record<string, unknown>>>>('/api/react/v1/suppliers/list')
  const rawList = payload.data ?? []

  return Array.isArray(rawList)
    ? rawList
        .filter((item): item is Record<string, unknown> => !!item && typeof item === 'object')
        .map((item) => ({
          code: String(item.code ?? item.SupplierCode ?? ''),
          name: String(item.name ?? item.SupplierName ?? ''),
          shopNumber: String(item.shopNumber ?? item.ShopNumber ?? '') || undefined,
          contactPerson: String(item.contactPerson ?? item.ContactPerson ?? '') || undefined,
          phone: String(item.phone ?? item.Phone ?? '') || undefined,
        }))
    : []
}

export async function getDomesticProductSetItems(productCode: string): Promise<DomesticProductSetItem[]> {
  const payload = await request<ApiResponse<unknown[]>>(`${API_BASE}/${productCode}/set-items`)
  const rawList = payload.data ?? []

  return Array.isArray(rawList)
    ? rawList
        .filter((item): item is Record<string, unknown> => !!item && typeof item === 'object')
        .map(transformSetItem)
    : []
}

export async function updateDomesticProductSetItems(
  productCode: string,
  items: DomesticProductSetItem[],
): Promise<void> {
  await request<unknown>(`${API_BASE}/${productCode}/set-items`, {
    method: 'PUT',
    data: {
      items: items.map((item) => ({
        setProductCode: item.setProductCode,
        productName: item.productName,
        setProductNo: item.setProductNo,
        setBarcode: item.setBarcode,
        domesticPrice: item.domesticPrice,
        importPrice: item.importPrice,
        oemPrice: item.oemPrice,
      })),
    },
  })
}
