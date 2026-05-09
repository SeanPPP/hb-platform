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
}

export interface ContainerProductInfo {
  商品编码?: string
  货号?: string
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
}

export interface ContainerDetail {
  id: number
  hguid: string
  主表GUID?: string
  商品编码?: string
  装柜类型?: string
  商品类型?: string
  套装数量?: number
  装柜件数?: number
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
  warehouseImportPrice?: number
  warehouseOEMPrice?: number
  warehouseIsActive?: boolean
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

export interface ComingSoonHomeProduct {
  id: number
  hguid: string
  productCode?: string
  itemNumber?: string
  productName?: string
  englishName?: string
  productImage?: string
  quantity?: number
  isNewProduct: boolean
  warehouseIsActive?: boolean
}

export interface ComingSoonHomeContainer extends ContainerMain {
  商品列表: ComingSoonHomeProduct[]
}
