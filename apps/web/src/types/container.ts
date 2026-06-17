export interface ContainerMain {
  id: number
  hguid: string
  货柜编号?: string
  装柜日期?: string
  预计到岸日期?: string
  实际到货日期?: string
  合计件数?: number
  合计数量?: number
  合计金额?: number
  总体积?: number
  成本浮率?: number
  汇率?: number
  运费?: number
  备注?: string
  状态?: number
  details?: ContainerDetail[]
}

export interface ContainerProductInfo {
  商品编码?: string
  货号?: string
  localSupplierCode?: string
  条形码?: string
  商品名称?: string
  英文名称?: string
  商品图片?: string
  零售价格?: number
  商品规格?: string
  单位?: string
  单件装箱数?: number
  单件体积?: number
  商品类型?: string
  套装数量?: number
  categoryName?: string
  CategoryName?: string
  productCategoryName?: string
  ProductCategoryName?: string
  categoryPath?: string
  CategoryPath?: string
  categoryFullPath?: string
  CategoryFullPath?: string
  warehouseCategoryGUID?: string
  WarehouseCategoryGUID?: string
  productCategoryGUID?: string
  ProductCategoryGUID?: string
}

export interface ContainerDetail {
  id: number
  hguid: string
  主表GUID?: string
  商品编码?: string
  localSupplierCode?: string
  商品名称?: string
  英文名称?: string
  商品图片?: string
  装柜类型?: string
  商品类型?: string
  套装数量?: number
  装柜件数?: number
  中包数?: number
  装柜数量?: number
  国内价格?: number
  调整浮率?: number
  进口价格?: number
  贴牌价格?: number
  单件装箱数?: number
  单件体积?: number
  合计装柜金额?: number
  合计装柜体积?: number
  运输成本?: number
  备注?: string
  商品信息?: ContainerProductInfo
  是否新商品?: boolean
  IsActive?: boolean
  lastImportPrice?: number
  LastImportPrice?: number
  lastOEMPrice?: number
  LastOEMPrice?: number
  warehouseImportPrice?: number
  WarehouseImportPrice?: number
  warehouseOEMPrice?: number
  WarehouseOEMPrice?: number
  warehouseIsActive?: boolean
  categoryName?: string
  CategoryName?: string
  productCategoryName?: string
  ProductCategoryName?: string
  categoryPath?: string
  CategoryPath?: string
  categoryFullPath?: string
  CategoryFullPath?: string
  warehouseCategoryGUID?: string
  WarehouseCategoryGUID?: string
  productCategoryGUID?: string
  ProductCategoryGUID?: string
  matchType?: 'productCode' | 'supplierItem' | 'unmatched'
  MatchType?: string
}

export type ContainerDetailQueryTag = 'all' | 'new' | 'existing' | 'noOemPrice' | 'abnormalImport' | 'active' | 'inactive'
export type ContainerDetailQueryProductType = 'normal' | 'set' | 'multi' | 'setChild'
export type ContainerDetailQueryNewProductState = 'new' | 'existing'
export type ContainerDetailQueryMatchType = 'productCode' | 'supplierItem' | 'unmatched'
export type ContainerDetailQueryWarehouseStatus = 'active' | 'inactive'
export type ContainerDetailQuerySortOrder = 'ascend' | 'descend'

export interface ContainerDetailQuery {
  containerGuid: string
  pageNumber: number
  pageSize: number
  itemNumber?: string
  barcode?: string
  productName?: string
  englishName?: string
  remark?: string
  productTypes?: ContainerDetailQueryProductType[]
  newProductStates?: ContainerDetailQueryNewProductState[]
  matchTypes?: ContainerDetailQueryMatchType[]
  warehouseStatus?: ContainerDetailQueryWarehouseStatus[]
  containerPiecesMin?: number
  containerPiecesMax?: number
  middlePackQuantityMin?: number
  middlePackQuantityMax?: number
  containerQuantityMin?: number
  containerQuantityMax?: number
  packingQuantityMin?: number
  packingQuantityMax?: number
  unitVolumeMin?: number
  unitVolumeMax?: number
  domesticPriceMin?: number
  domesticPriceMax?: number
  floatRateMin?: number
  floatRateMax?: number
  transportCostMin?: number
  transportCostMax?: number
  unitTransportCostMin?: number
  unitTransportCostMax?: number
  warehouseImportPriceMin?: number
  warehouseImportPriceMax?: number
  lastOEMPriceMin?: number
  lastOEMPriceMax?: number
  importPriceMin?: number
  importPriceMax?: number
  oemPriceMin?: number
  oemPriceMax?: number
  selectedTags?: ContainerDetailQueryTag[]
  sortBy?: string
  sortOrder?: ContainerDetailQuerySortOrder
}

export interface ContainerDetailTagStats {
  all: number
  new: number
  existing: number
  noOemPrice: number
  abnormalImport: number
  active: number
  inactive: number
}

export interface ContainerDetailQueryResult {
  items: ContainerDetail[]
  itemsTotal: number
  pageNumber: number
  pageSize: number
  hasMore: boolean
  tagStats: ContainerDetailTagStats
}

export interface ContainerDomesticSetCodeItem {
  productCode?: string
  itemNumber?: string
  productType?: number
  setProductCode?: string
  setItemNumber?: string
  barcode?: string
  retailPrice?: number
  purchasePrice?: number
}

export interface UpdateContainerDomesticSetCodePriceItem {
  setProductCode?: string
  retailPrice?: number | null
  purchasePrice?: number | null
}

export interface UpdateContainerDomesticSetCodePricesRequest {
  items: UpdateContainerDomesticSetCodePriceItem[]
}

export interface UpdateContainerDomesticSetCodePricesResult {
  updatedCount: number
}

export interface ContainerDetailBatchScope {
  selectedHguids?: string[]
  query?: ContainerDetailQuery
}

export interface ContainerDetailBatchActionResult {
  totalUpdated: number
  totalRequested?: number
}

export interface ContainerQueryRequest {
  dateType?: string
  startDate?: string
  endDate?: string
  page?: number
  pageSize?: number
  itemNumberFilter?: string
  sortBy?: string
  sortDirection?: string
}

export interface ContainerListResponse {
  containers: ContainerMain[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

export interface DateFilterOption {
  value: string
  label: string
}

export interface UpdateContainerRequest {
  实际到货日期?: string
  汇率?: number
  运费?: number
  备注?: string
  状态?: number
}

export interface UpdateContainerDetailRequest {
  hguid: string
  调整浮率?: number
  国内价格?: number
  进口价格?: number
  运输成本?: number
  商品名称?: string
  英文名称?: string
  ClearEnglishName?: boolean
  ProductCategoryGUID?: string
  贴牌价格?: number
  单件装箱数?: number
  中包数?: number
  单件体积?: number
  装柜数量?: number
  合计装柜体积?: number
  合计装柜金额?: number
  IsActive?: boolean
  SkipRelatedProductSync?: boolean
  matchType?: ContainerDetail['matchType']
  是否新商品?: boolean
}

export interface CreateContainerRequest {
  货柜编号: string
  装柜日期?: string
  预计到岸日期?: string
  汇率?: number
  运费?: number
  备注?: string
}

export interface SyncResult {
  isSuccess?: boolean
  IsSuccess?: boolean
  message?: string
  Message?: string
  details?: string
  Details?: string
  addedCount?: number
  AddedCount?: number
  updatedCount?: number
  UpdatedCount?: number
  deletedCount?: number
  DeletedCount?: number
  errorCount?: number
  ErrorCount?: number
}

export interface HqTranslationResult {
  TotalCandidates?: number
  TotalTranslated?: number
  TotalSkipped?: number
  TotalFailed?: number
  Samples?: Record<string, string>
}

export interface ComingSoonHomeProduct {
  id: number
  hguid: string
  productCode?: string
  itemNumber?: string
  barcode?: string
  productName?: string
  englishName?: string
  productImage?: string
  quantity?: number
  retailPrice?: number
  isNewProduct: boolean
  warehouseIsActive?: boolean
}

export type ComingSoonHomeContainerSummary = ContainerMain

export interface ComingSoonLoadedContainer extends ComingSoonHomeContainerSummary {
  商品列表: ComingSoonHomeProduct[]
}

export type ComingSoonHomeContainer = ComingSoonLoadedContainer
