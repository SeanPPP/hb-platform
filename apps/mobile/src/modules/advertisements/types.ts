export type AdvertisementMediaType = "image" | "video";

export interface AdvertisementStoreScope {
  storeCode: string;
}

export interface AdvertisementItem {
  id: string;
  title: string;
  description: string;
  mediaType: AdvertisementMediaType;
  mediaUrl: string;
  thumbnailUrl: string;
  objectKey: string;
  originalFileName: string;
  contentType: string;
  fileSize: number | null;
  effectiveStart: string;
  effectiveEnd: string;
  isEnabled: boolean;
  sortOrder: number | null;
  stores: AdvertisementStoreScope[];
}

export interface AdvertisementListQuery {
  pageNumber?: number;
  pageSize?: number;
  title?: string;
  mediaType?: AdvertisementMediaType | "" | null;
  storeCode?: string;
  isEnabled?: boolean | null;
}

export interface AdvertisementUpsertPayload {
  title: string;
  description?: string | null;
  mediaType: AdvertisementMediaType;
  mediaUrl: string;
  thumbnailUrl?: string | null;
  objectKey?: string | null;
  originalFileName?: string | null;
  contentType?: string | null;
  fileSize?: number | null;
  effectiveStart: string;
  effectiveEnd: string;
  isEnabled: boolean;
  sortOrder?: number | null;
  stores: AdvertisementStoreScope[];
}

export interface AdvertisementUploadSignatureRequest {
  fileName: string;
  contentType: string;
  fileSize: number;
}

export interface AdvertisementUploadSignature {
  url: string;
  objectKey: string;
  mediaUrl?: string;
  uploadUrl?: string;
  headers: Record<string, string>;
}

export interface AdvertisementUploadedAsset {
  objectKey: string;
  mediaUrl: string;
}

export interface AdvertisementDraft {
  id?: string | null;
  title: string;
  description: string;
  mediaType: AdvertisementMediaType;
  mediaUrl: string;
  thumbnailUrl: string;
  objectKey: string;
  originalFileName: string;
  contentType: string;
  fileSize: string;
  effectiveStart: string;
  effectiveEnd: string;
  isEnabled: boolean;
  sortOrder: string;
  storeCodes: string[];
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  pageNumber: number;
  pageSize: number;
}
