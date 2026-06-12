export type SeasonalCardType = 1 | 2 | 3 | 4 | 5;
export type SeasonalCardPriceOption = 1 | 2 | 3 | 4;

export interface SeasonalCardCatalogItem {
  catalogGuid: string;
  cardType: SeasonalCardType | null;
  cardTypeName: string;
  priceOption: SeasonalCardPriceOption | null;
  priceOptionName: string;
  priceLabel: string;
  fixedUnitPrice: number | null;
  allowsCustomUnitPrice: boolean;
  isEnabled: boolean;
  sortOrder: number | null;
}

export interface SeasonalCardSubmissionRecord {
  submissionGuid: string;
  storeCode: string;
  catalogGuid: string;
  cardType: SeasonalCardType | null;
  cardTypeName: string;
  seasonYear: number | null;
  unitPrice: number | null;
  priceLabel: string;
  remainingQuantity: number | null;
  remark: string;
  submittedByName: string;
  submittedAt: string;
}

export interface SeasonalCardSubmissionQuery {
  storeCode?: string;
  cardType?: SeasonalCardType | number | string | null;
  seasonYear?: number | string | null;
  pageNumber?: number;
  pageSize?: number;
}

export interface SeasonalCardSubmissionPayload {
  storeCode: string;
  catalogGuid: string;
  seasonYear: number | string;
  remainingQuantity: number | string;
  customUnitPrice?: number | string | null;
  remark?: string | null;
}

export interface SeasonalCardSubmissionDraft {
  storeCode: string;
  seasonYear: string;
  cardType: string;
  catalogGuid: string;
  remainingQuantity: string;
  customUnitPrice: string;
  remark: string;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  pageNumber: number;
  pageSize: number;
}
