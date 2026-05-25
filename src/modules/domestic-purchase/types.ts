export enum ProductCreationType {
  Normal = 0,
  Set = 1,
  SetSubItem = 2,
}

export interface DomesticSupplierOption {
  supplierCode: string;
  supplierName: string;
}

export interface DomesticProductListItem {
  productCode: string;
  supplierCode: string;
  supplierName: string;
  productName: string | null;
  englishProductName: string | null;
  hbProductNo: string | null;
  barcode: string | null;
  productSpecification: string | null;
  productType: number | null;
  domesticPrice: number | null;
  oemPrice: number | null;
  importPrice: number | null;
  packingQuantity: number | null;
  unitVolume: number | null;
  middlePackQuantity: number | null;
  productImage: string | null;
  isActive: boolean;
}

export interface DomesticProductListQuery {
  page?: number;
  pageSize?: number;
  supplierCode?: string | null;
  productNo?: string | null;
}

export interface DomesticProductListResult {
  items: DomesticProductListItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface UpdateDomesticProductRequest {
  productName?: string | null;
  englishProductName?: string | null;
  productSpecification?: string | null;
  productType?: number;
  domesticPrice?: number | null;
  oemPrice?: number | null;
  importPrice?: number | null;
  packingQuantity?: number | null;
  unitVolume?: number | null;
  middlePackQuantity?: number | null;
  productImage?: string | null;
  isActive?: boolean;
}

export interface DomesticProductEditDraft extends Omit<DomesticProductListItem, "isActive"> {
  isActive: boolean;
}

export interface ProductPrefixOption {
  prefixCode: string;
  prefixName: string;
  prefixDescription?: string | null;
}

export interface DomesticProductBatch {
  batchNumber: string;
  supplierCode: string;
  supplierName?: string | null;
  prefixCode?: string | null;
  normalCount: number;
  setCount: number;
  totalCount: number;
  createdAt: string;
  createdBy?: string | null;
}

export interface DomesticProductBatchItem {
  productCode: string;
  itemNumber: string;
  hbProductNo: string;
  barcode: string;
  productName: string;
  productType: ProductCreationType;
  privateLabelPrice?: number | null;
  setQuantity?: number | null;
  setPrice?: number | null;
  parentItemNumber?: string | null;
}

export interface DomesticProductBatchDetail extends DomesticProductBatch {
  items: DomesticProductBatchItem[];
}

export interface CreateDomesticProductBatchRequest {
  supplierCode: string;
  prefixCode?: string;
  prefixName?: string;
  items: Array<{
    productName?: string;
    productType: ProductCreationType;
    privateLabelPrice?: number | null;
  }>;
}

export interface UpdateDomesticProductBatchItemsRequest {
  items: Array<{
    productCode: string;
    productName?: string | null;
    privateLabelPrice?: number | null;
  }>;
}
