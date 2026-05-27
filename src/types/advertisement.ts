export type AdvertisementMediaType = 'Image' | 'Video'

export interface AdvertisementStoreItemDto {
  storeCode: string
}

export interface AdvertisementListDto {
  id: string
  title: string
  description?: string
  mediaType: AdvertisementMediaType
  mediaUrl: string
  thumbnailUrl?: string
  objectKey?: string
  originalFileName?: string
  contentType?: string
  fileSize?: number
  effectiveStart: string
  effectiveEnd: string
  isEnabled: boolean
  sortOrder: number
  stores: AdvertisementStoreItemDto[]
}

export type AdvertisementDetailDto = AdvertisementListDto

export interface CreateAdvertisementDto {
  title: string
  description?: string
  mediaType: AdvertisementMediaType
  mediaUrl: string
  thumbnailUrl?: string
  objectKey?: string
  originalFileName?: string
  contentType?: string
  fileSize?: number
  effectiveStart: string
  effectiveEnd: string
  isEnabled: boolean
  sortOrder: number
  stores: AdvertisementStoreItemDto[]
}

export type UpdateAdvertisementDto = CreateAdvertisementDto

export interface AdvertisementGridResult {
  items: AdvertisementListDto[]
  total: number
}

export interface AdvertisementUploadSignatureRequest {
  fileName: string
  contentType: string
  fileSize: number
  mediaType: AdvertisementMediaType
}

export interface AdvertisementUploadSignatureResponse {
  url: string
  mediaUrl?: string
  uploadUrl?: string
  objectKey: string
  headers?: Record<string, string>
}

export interface AdvertisementPayloadInput {
  title: string
  description?: string | null
  mediaType: AdvertisementMediaType
  mediaUrl: string
  thumbnailUrl?: string | null
  objectKey?: string | null
  originalFileName?: string | null
  contentType?: string | null
  fileSize?: number | null
  effectiveStart: string | { toISOString: () => string }
  effectiveEnd: string | { toISOString: () => string }
  isEnabled?: boolean
  sortOrder?: number | null
  stores?: Array<string | AdvertisementStoreItemDto>
}
